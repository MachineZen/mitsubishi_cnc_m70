# Mitsubishi CNC Desktop Dashboard

This Windows desktop app lets you:

- enter up to 3 machine IP addresses
- keep one shared port for all controllers
- choose the controller family once (`M70 / M700` or `M80 / M800`)
- monitor live status, mode, run state, spindle speed, feed speed, part count, tool number, alarms, and axis load/feed data

## Build the packaged EXE

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-dashboard.ps1
```

The packaged app is created in:

```text
build\publish\MitsubishiCncMonitor
```

Run:

```text
build\publish\MitsubishiCncMonitor\MitsubishiCncMonitor.exe
```

## First use

1. Enter the 3 machine names and IP addresses.
2. Keep the shared port value common for all machines.
3. Choose the controller series.
4. Click `Start Monitoring`.
5. Optional: enable `Auto start on launch` so the app starts monitoring automatically next time.
