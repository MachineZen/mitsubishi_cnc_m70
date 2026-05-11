param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$collectorOutputDir = Join-Path $repoRoot "build\collector"
$publishOutputDir = Join-Path $repoRoot "build\publish\MitsubishiCncMonitor"
$collectorExe = Join-Path $collectorOutputDir "MitsubishiCncCollector.exe"

New-Item -ItemType Directory -Force -Path $collectorOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $publishOutputDir | Out-Null

$vcVarsAll = @(
    "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvarsall.bat",
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $vcVarsAll) {
    throw "Visual Studio C++ build tools were not found. Install Build Tools or Visual Studio with Desktop development for C++."
}

$sourceRoot = Join-Path $repoRoot "mitsubishi_cnc_m70_ezsocket_net"
$tempCmd = Join-Path $collectorOutputDir "build-native-collector.cmd"
$compileScript = @'
@echo off
call "{0}" x64 >nul
if errorlevel 1 exit /b 1
cl /nologo /O2 /MT /TC /D_CRT_SECURE_NO_WARNINGS /D_WINSOCK_SECURE_NO_WARNINGS /I "{1}" /Fe:"{2}" "{3}\m80_smoke_test.c" "{1}\m70_error.c" "{1}\m70_ezsocket.c" "{1}\m70_giop.c" "{1}\m70_log.c" "{1}\socket.c" "{1}\utill.c" /link ws2_32.lib
'@ -f $vcVarsAll, $sourceRoot, $collectorExe, $repoRoot
Set-Content -Path $tempCmd -Value $compileScript -Encoding ASCII

Write-Host "Building native collector..." -ForegroundColor Cyan
cmd.exe /c $tempCmd
if ($LASTEXITCODE -ne 0) {
    throw "Native collector build failed."
}

Write-Host "Publishing Windows dashboard..." -ForegroundColor Cyan
dotnet publish `
    (Join-Path $repoRoot "MitsubishiCncMonitor\MitsubishiCncMonitor.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:CollectorExecutablePath="$collectorExe" `
    -o $publishOutputDir

if ($LASTEXITCODE -ne 0) {
    throw "Dashboard publish failed."
}

Copy-Item -Path $collectorExe -Destination (Join-Path $publishOutputDir "MitsubishiCncCollector.exe") -Force
Remove-Item -Path $tempCmd -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Package ready:" -ForegroundColor Green
Write-Host "  $publishOutputDir"
Write-Host ""
Write-Host "Run this file:" -ForegroundColor Green
Write-Host "  $(Join-Path $publishOutputDir 'MitsubishiCncMonitor.exe')"
