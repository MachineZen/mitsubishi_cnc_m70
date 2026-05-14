using System.Globalization;
using System.Text.Json.Serialization;

namespace MitsubishiCncMonitor;

public enum ConnectionState
{
    NotConfigured,
    Stopped,
    Starting,
    Running,
    Stale,
    Error
}

public sealed class AppSettings
{
    public int SharedPort { get; set; } = 683;
    public int PollIntervalMs { get; set; } = 1000;
    public int ControllerType { get; set; } = 8;
    public bool AutoStartMonitoring { get; set; }
    public List<MachineSettings> Machines { get; set; } = [];

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings
        {
            Machines =
            [
                new MachineSettings { Name = "Machine 1" },
                new MachineSettings { Name = "Machine 2" },
                new MachineSettings { Name = "Machine 3" }
            ]
        };

        settings.Normalize();
        return settings;
    }

    public void Normalize()
    {
        SharedPort = SharedPort <= 0 ? 683 : Math.Clamp(SharedPort, 1, 65535);
        PollIntervalMs = PollIntervalMs <= 0 ? 1000 : Math.Clamp(PollIntervalMs, 250, 60000);
        ControllerType = ControllerType is 6 or 8 ? ControllerType : 8;

        Machines ??= [];
        if (Machines.Count > 3)
        {
            Machines = Machines.Take(3).ToList();
        }

        while (Machines.Count < 3)
        {
            Machines.Add(new MachineSettings { Name = $"Machine {Machines.Count + 1}" });
        }

        for (var index = 0; index < Machines.Count; index++)
        {
            Machines[index].Name = string.IsNullOrWhiteSpace(Machines[index].Name)
                ? $"Machine {index + 1}"
                : Machines[index].Name.Trim();
            Machines[index].IpAddress = Machines[index].IpAddress?.Trim() ?? string.Empty;
            Machines[index].WidthOfCutMm = Math.Max(0.0, Machines[index].WidthOfCutMm);
            Machines[index].DepthOfCutMm = Math.Max(0.0, Machines[index].DepthOfCutMm);
            Machines[index].TargetMrrCm3PerMin = Math.Max(0.0, Machines[index].TargetMrrCm3PerMin);
            Machines[index].QualityPercent = Math.Clamp(Machines[index].QualityPercent, 0.0, 100.0);
        }
    }
}

public sealed class MachineSettings
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public double WidthOfCutMm { get; set; }
    public double DepthOfCutMm { get; set; }
    public double TargetMrrCm3PerMin { get; set; }
    public double QualityPercent { get; set; } = 100.0;

    public MachineSettings Clone()
    {
        return new MachineSettings
        {
            Name = Name,
            IpAddress = IpAddress,
            WidthOfCutMm = WidthOfCutMm,
            DepthOfCutMm = DepthOfCutMm,
            TargetMrrCm3PerMin = TargetMrrCm3PerMin,
            QualityPercent = QualityPercent
        };
    }
}

public sealed class CollectorSample
{
    [JsonPropertyName("ts")]
    public long UnixTimestamp { get; set; }

    [JsonPropertyName("ts_ms")]
    public long UnixTimestampMilliseconds { get; set; }

    [JsonPropertyName("sample")]
    public int SampleNumber { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("status_text")]
    public string StatusText { get; set; } = "UNKNOWN";

    [JsonPropertyName("mode")]
    public int Mode { get; set; }

    [JsonPropertyName("run_status")]
    public int RunStatus { get; set; }

    [JsonPropertyName("spindle_speed")]
    public int SpindleSpeed { get; set; }

    [JsonPropertyName("spindle_torque_load")]
    public int SpindleTorqueLoad { get; set; }

    [JsonPropertyName("feed_speed")]
    public double FeedSpeed { get; set; }

    [JsonPropertyName("counter")]
    public int Counter { get; set; }

    [JsonPropertyName("part_count")]
    public int PartCount { get; set; }

    [JsonPropertyName("tool_number")]
    public int? ToolNumber { get; set; }

    [JsonPropertyName("axis_count")]
    public int AxisCount { get; set; }

    [JsonPropertyName("axis_names")]
    public List<string> AxisNames { get; set; } = [];

    [JsonPropertyName("axis_torque")]
    public List<int?> AxisTorque { get; set; } = [];

    [JsonPropertyName("axis_feed_rate")]
    public List<double?> AxisFeedRate { get; set; } = [];

    [JsonPropertyName("alarm_active")]
    public bool AlarmActive { get; set; }

    [JsonPropertyName("alarm_no")]
    public int AlarmNumber { get; set; }

    [JsonPropertyName("alarm_text")]
    public string AlarmText { get; set; } = string.Empty;

    [JsonPropertyName("ret")]
    public CollectorReturnCodes? ReturnCodes { get; set; }

    public long EffectiveUnixTimestampMilliseconds =>
        UnixTimestampMilliseconds > 0 ? UnixTimestampMilliseconds : UnixTimestamp > 0 ? UnixTimestamp * 1000 : 0;

    public DateTimeOffset TimestampLocal =>
        EffectiveUnixTimestampMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(EffectiveUnixTimestampMilliseconds).ToLocalTime()
            : DateTimeOffset.Now;

    public string ModeText => Lookup(Mode, ModeMap);

    public string RunStatusText => Lookup(RunStatus, RunStatusMap);

    public string StatusDisplayText =>
        string.IsNullOrWhiteSpace(StatusText) ? Status.ToString(CultureInfo.InvariantCulture) : StatusText;

    public string ToolText => ToolNumber.HasValue ? ToolNumber.Value.ToString(CultureInfo.InvariantCulture) : "--";

    public string PartsText => PartCount.ToString(CultureInfo.InvariantCulture);

    public string SpindleText => $"{SpindleSpeed.ToString(CultureInfo.InvariantCulture)} rpm";

    public string SpindleLoadText => SpindleTorqueLoad.ToString(CultureInfo.InvariantCulture);

    public string FeedText => FeedSpeed.ToString("0.###", CultureInfo.InvariantCulture);

    public string AlarmBadgeText
    {
        get
        {
            if (!AlarmActive)
            {
                return "OK";
            }

            if (AlarmNumber > 0)
            {
                return AlarmNumber.ToString(CultureInfo.InvariantCulture);
            }

            return "ALM";
        }
    }

    public string GetAxisLabel(int axisIndex)
    {
        if (axisIndex >= 0 && axisIndex < AxisNames.Count)
        {
            var name = AxisNames[axisIndex]?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return $"Axis {axisIndex + 1}";
    }

    public string AlarmSummary
    {
        get
        {
            if (!AlarmActive)
            {
                return "No active alarm";
            }

            if (AlarmNumber > 0 && !string.IsNullOrWhiteSpace(AlarmText))
            {
                return $"{AlarmNumber}: {AlarmText}";
            }

            return string.IsNullOrWhiteSpace(AlarmText) ? "Alarm active" : AlarmText;
        }
    }

    private static readonly Dictionary<int, string> ModeMap = new()
    {
        [0] = "MEM",
        [1] = "DNC",
        [2] = "LNK",
        [3] = "MDI",
        [4] = "PC",
        [5] = "MNL",
        [6] = "JOG",
        [7] = "J+H",
        [8] = "R+H",
        [9] = "HDL",
        [10] = "STP",
        [11] = "STP1",
        [12] = "REF",
        [13] = "DRT",
        [14] = "INI",
        [15] = "NON",
        [16] = "LIN"
    };

    private static readonly Dictionary<int, string> RunStatusMap = new()
    {
        [0] = "RST",
        [1] = "EMG",
        [2] = "RDY",
        [3] = "AUT",
        [4] = "SYN",
        [5] = "CRS",
        [6] = "BST",
        [7] = "HLD"
    };

    private static string Lookup(int value, IReadOnlyDictionary<int, string> map)
    {
        return map.TryGetValue(value, out var text) ? text : "UNKNOWN";
    }
}

public sealed class CollectorReturnCodes
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("spindle")]
    public int Spindle { get; set; }

    [JsonPropertyName("spindle_torque")]
    public int SpindleTorque { get; set; }

    [JsonPropertyName("feed")]
    public int Feed { get; set; }

    [JsonPropertyName("counter")]
    public int Counter { get; set; }

    [JsonPropertyName("tool")]
    public int Tool { get; set; }

    [JsonPropertyName("alarm")]
    public int Alarm { get; set; }

    [JsonPropertyName("axis_count")]
    public int AxisCount { get; set; }

    [JsonPropertyName("axis_pos")]
    public int AxisPosition { get; set; }

    [JsonPropertyName("axis_torque_fail_count")]
    public int AxisTorqueFailCount { get; set; }
}

public sealed class MachineRuntimeState
{
    private static readonly HashSet<int> RunningStates = [3, 4, 5, 6];

    public MachineRuntimeState(int slotIndex, MachineSettings machine)
    {
        SlotIndex = slotIndex;
        Machine = machine;
        State = IsConfigured ? ConnectionState.Stopped : ConnectionState.NotConfigured;
        StatusMessage = IsConfigured ? "Ready to start." : "Enter an IP address to enable monitoring.";
    }

    public int SlotIndex { get; }

    public MachineSettings Machine { get; }

    public ConnectionState State { get; set; }

    public string StatusMessage { get; set; }

    public string LastError { get; set; } = string.Empty;

    public CollectorSample? LastSample { get; set; }

    public List<CollectorSample> History { get; } = [];

    public MachineOeeMetrics OeeMetrics { get; private set; } = MachineOeeMetrics.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Machine.IpAddress);

    public string DisplayName => string.IsNullOrWhiteSpace(Machine.Name) ? $"Machine {SlotIndex + 1}" : Machine.Name;

    public DateTimeOffset? LastUpdated => LastSample?.TimestampLocal;

    public void UpdateProcessSettings(double widthOfCutMm, double depthOfCutMm, double targetMrrCm3PerMin, double qualityPercent)
    {
        Machine.WidthOfCutMm = Math.Max(0.0, widthOfCutMm);
        Machine.DepthOfCutMm = Math.Max(0.0, depthOfCutMm);
        Machine.TargetMrrCm3PerMin = Math.Max(0.0, targetMrrCm3PerMin);
        Machine.QualityPercent = Math.Clamp(qualityPercent, 0.0, 100.0);
    }

    public void AddSample(CollectorSample sample, int maxHistory)
    {
        var previousSample = LastSample;
        LastSample = sample;
        History.Insert(0, sample);

        if (History.Count > maxHistory)
        {
            History.RemoveRange(maxHistory, History.Count - maxHistory);
        }

        UpdateOeeMetrics(previousSample, sample);
    }

    public void ResetOeeMetrics()
    {
        OeeMetrics = MachineOeeMetrics.Empty;
    }

    public void RecalculateOeeMetricsFromHistory()
    {
        OeeMetrics = MachineOeeMetrics.Empty;

        CollectorSample? previousSample = null;
        foreach (var sample in History.AsEnumerable().Reverse())
        {
            UpdateOeeMetrics(previousSample, sample);
            previousSample = sample;
        }
    }

    private void UpdateOeeMetrics(CollectorSample? previousSample, CollectorSample currentSample)
    {
        var tracker = OeeMetrics;
        var currentTimestamp = currentSample.TimestampLocal;
        var previousTimestamp = previousSample?.TimestampLocal;

        if (previousTimestamp.HasValue)
        {
            var elapsedSeconds = Math.Max(0.0, (currentTimestamp - previousTimestamp.Value).TotalSeconds);
            tracker.PlannedSeconds += elapsedSeconds;

            var running = IsRunning(currentSample);
            if (running)
            {
                tracker.RunSeconds += elapsedSeconds;

                var actualMrr = CalculateActualMrr(currentSample);
                tracker.CumulativeRemovedVolumeCm3 += actualMrr * (elapsedSeconds / 60.0);
            }
        }

        tracker.ActualMrrCm3PerMin = CalculateActualMrr(currentSample);
        tracker.TargetMrrCm3PerMin = Math.Max(0.0, Machine.TargetMrrCm3PerMin);
        tracker.QualityPercent = Math.Clamp(Machine.QualityPercent, 0.0, 100.0);

        var availability = tracker.PlannedSeconds > 0.0
            ? tracker.RunSeconds / tracker.PlannedSeconds
            : 0.0;

        var performance = tracker.TargetMrrCm3PerMin > 0.0 && tracker.RunSeconds > 0.0
            ? tracker.CumulativeRemovedVolumeCm3 / (tracker.TargetMrrCm3PerMin * (tracker.RunSeconds / 60.0))
            : 0.0;

        availability = Math.Clamp(availability, 0.0, 1.0);
        performance = Math.Clamp(performance, 0.0, 1.0);

        var quality = Math.Clamp(tracker.QualityPercent / 100.0, 0.0, 1.0);

        tracker.AvailabilityPercent = availability * 100.0;
        tracker.PerformancePercent = performance * 100.0;
        tracker.OeePercent = availability * performance * quality * 100.0;

        OeeMetrics = tracker;
    }

    private bool IsRunning(CollectorSample sample)
    {
        if (RunningStates.Contains(sample.RunStatus))
        {
            return true;
        }

        if (sample.SpindleSpeed > 0)
        {
            return true;
        }

        return sample.FeedSpeed > 0.0;
    }

    private double CalculateActualMrr(CollectorSample sample)
    {
        if (Machine.WidthOfCutMm <= 0.0 || Machine.DepthOfCutMm <= 0.0)
        {
            return 0.0;
        }

        var feedMmPerMin = Math.Max(0.0, sample.FeedSpeed);
        var mrrMm3PerMin = feedMmPerMin * Machine.WidthOfCutMm * Machine.DepthOfCutMm;
        return mrrMm3PerMin / 1000.0;
    }
}

public sealed class MachineOeeMetrics
{
    public static MachineOeeMetrics Empty => new();

    public double PlannedSeconds { get; set; }

    public double RunSeconds { get; set; }

    public double CumulativeRemovedVolumeCm3 { get; set; }

    public double ActualMrrCm3PerMin { get; set; }

    public double TargetMrrCm3PerMin { get; set; }

    public double QualityPercent { get; set; }

    public double AvailabilityPercent { get; set; }

    public double PerformancePercent { get; set; }

    public double OeePercent { get; set; }
}
