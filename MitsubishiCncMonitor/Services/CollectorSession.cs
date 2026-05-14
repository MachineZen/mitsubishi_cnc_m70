using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace MitsubishiCncMonitor;

public sealed class CollectorSession : IDisposable
{
    private readonly string _collectorPath;
    private readonly int _slotIndex;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private CancellationTokenSource? _cts;
    private Process? _process;
    private bool _stopRequested;
    private string _lastErrorLine = string.Empty;

    public CollectorSession(int slotIndex, string collectorPath)
    {
        _slotIndex = slotIndex;
        _collectorPath = collectorPath;
    }

    public event EventHandler<MachineStateChangedEventArgs>? StateChanged;

    public event EventHandler<MachineSampleReceivedEventArgs>? SampleReceived;

    public void Start(MachineSettings machine, int port, int controllerType, int intervalMs)
    {
        StopInternal(false);

        if (string.IsNullOrWhiteSpace(machine.IpAddress))
        {
            PublishState(ConnectionState.NotConfigured, "Enter an IP address to monitor this machine.");
            return;
        }

        if (!File.Exists(_collectorPath))
        {
            PublishState(ConnectionState.Error, $"Collector executable was not found at {_collectorPath}");
            return;
        }

        var process = new Process
        {
            StartInfo = CreateStartInfo(machine, port, controllerType, intervalMs),
            EnableRaisingEvents = true
        };

        var cts = new CancellationTokenSource();
        _stopRequested = false;
        _lastErrorLine = string.Empty;

        process.Exited += HandleProcessExit;
        process.ErrorDataReceived += HandleErrorDataReceived;

        lock (_sync)
        {
            _process = process;
            _cts = cts;
        }

        PublishState(ConnectionState.Starting, $"Connecting to {machine.IpAddress}:{port}...");

        try
        {
            process.Start();
            process.BeginErrorReadLine();
            _ = Task.Run(() => ReadStandardOutputAsync(process, cts.Token));
        }
        catch (Exception ex)
        {
            CleanupProcess(process);
            lock (_sync)
            {
                _process = null;
                _cts?.Dispose();
                _cts = null;
            }

            PublishState(ConnectionState.Error, ex.Message);
        }
    }

    public void Stop()
    {
        StopInternal(true);
    }

    public void Dispose()
    {
        StopInternal(false);
    }

    private async Task ReadStandardOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    PublishState(ConnectionState.Starting, trimmed);
                    continue;
                }

                CollectorSample? sample;
                try
                {
                    sample = JsonSerializer.Deserialize<CollectorSample>(trimmed, _jsonOptions);
                }
                catch
                {
                    continue;
                }

                if (sample is null)
                {
                    continue;
                }

                PublishSample(sample);
                PublishState(ConnectionState.Running, $"Last update {sample.TimestampLocal:HH:mm:ss.fff}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (!_stopRequested)
            {
                PublishState(ConnectionState.Error, ex.Message);
            }
        }
    }

    private void HandleErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (_stopRequested || string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        _lastErrorLine = e.Data.Trim();
        PublishState(ConnectionState.Error, _lastErrorLine);
    }

    private void HandleProcessExit(object? sender, EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        if (_stopRequested)
        {
            CleanupProcess(process);
            return;
        }

        var exitCode = process.ExitCode;
        var message = string.IsNullOrWhiteSpace(_lastErrorLine)
            ? $"Collector exited with code {exitCode}."
            : _lastErrorLine;

        PublishState(exitCode == 0 ? ConnectionState.Stopped : ConnectionState.Error, message);

        lock (_sync)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        CleanupProcess(process);
    }

    private ProcessStartInfo CreateStartInfo(MachineSettings machine, int port, int controllerType, int intervalMs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _collectorPath,
            WorkingDirectory = Path.GetDirectoryName(_collectorPath) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(machine.IpAddress.Trim());
        startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(controllerType.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(intervalMs.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("0");

        return startInfo;
    }

    private void StopInternal(bool publishState)
    {
        Process? process;
        CancellationTokenSource? cts;

        lock (_sync)
        {
            _stopRequested = true;
            process = _process;
            cts = _cts;
            _process = null;
            _cts = null;
        }

        cts?.Cancel();

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
            }

            try
            {
                process.WaitForExit(2000);
            }
            catch
            {
            }

            CleanupProcess(process);
        }

        cts?.Dispose();

        if (publishState)
        {
            PublishState(ConnectionState.Stopped, "Monitoring stopped.");
        }
    }

    private void CleanupProcess(Process process)
    {
        process.Exited -= HandleProcessExit;
        process.ErrorDataReceived -= HandleErrorDataReceived;
        process.Dispose();
    }

    private void PublishState(ConnectionState state, string message)
    {
        StateChanged?.Invoke(this, new MachineStateChangedEventArgs(_slotIndex, state, message));
    }

    private void PublishSample(CollectorSample sample)
    {
        SampleReceived?.Invoke(this, new MachineSampleReceivedEventArgs(_slotIndex, sample));
    }
}

public sealed class MachineStateChangedEventArgs : EventArgs
{
    public MachineStateChangedEventArgs(int slotIndex, ConnectionState state, string message)
    {
        SlotIndex = slotIndex;
        State = state;
        Message = message;
    }

    public int SlotIndex { get; }

    public ConnectionState State { get; }

    public string Message { get; }
}

public sealed class MachineSampleReceivedEventArgs : EventArgs
{
    public MachineSampleReceivedEventArgs(int slotIndex, CollectorSample sample)
    {
        SlotIndex = slotIndex;
        Sample = sample;
    }

    public int SlotIndex { get; }

    public CollectorSample Sample { get; }
}
