using System.Globalization;
using System.Text;

namespace MitsubishiCncMonitor;

public sealed class CsvSampleLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly string _machineName;
    private readonly string _directory;

    private StreamWriter? _writer;
    private List<string>? _axisLabels;

    public CsvSampleLogger(string machineName, string directory)
    {
        _machineName = string.IsNullOrWhiteSpace(machineName) ? "machine" : machineName.Trim();
        _directory = directory;
    }

    public string? FilePath { get; private set; }

    public static string ResolveDefaultLogDirectory()
    {
        var localPath = Path.Combine(Application.LocalUserAppDataPath, "logs");

        try
        {
            var appPath = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(appPath);
            return appPath;
        }
        catch
        {
            Directory.CreateDirectory(localPath);
            return localPath;
        }
    }

    public void LogSample(CollectorSample sample)
    {
        lock (_sync)
        {
            EnsureWriter(sample);
            var row = BuildRow(sample, _axisLabels!);
            _writer!.WriteLine(string.Join(",", row.Select(EscapeCsv)));
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void EnsureWriter(CollectorSample sample)
    {
        if (_writer is not null)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        _axisLabels = BuildAxisLabels(sample);
        FilePath = Path.Combine(
            _directory,
            $"{SanitizeFileComponent(_machineName)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");

        _writer = new StreamWriter(
            new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false));

        _writer.WriteLine(string.Join(",", BuildHeaders(_axisLabels).Select(EscapeCsv)));
    }

    private static List<string> BuildAxisLabels(CollectorSample sample)
    {
        var labels = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var axisCount = Math.Max(sample.AxisCount, Math.Max(sample.AxisNames.Count, Math.Max(sample.AxisTorque.Count, sample.AxisFeedRate.Count)));

        for (var index = 0; index < axisCount; index++)
        {
            var fallback = $"axis{index + 1}";
            var source = index < sample.AxisNames.Count && !string.IsNullOrWhiteSpace(sample.AxisNames[index])
                ? sample.AxisNames[index]
                : fallback;

            var label = SanitizeAxisLabel(source, fallback);
            if (seen.TryGetValue(label, out var count))
            {
                count++;
                seen[label] = count;
                label = $"{label}_{count}";
            }
            else
            {
                seen[label] = 1;
            }

            labels.Add(label);
        }

        return labels;
    }

    private static IReadOnlyList<string> BuildHeaders(IReadOnlyList<string> axisLabels)
    {
        var headers = new List<string>
        {
            "machine",
            "timestamp_local",
            "timestamp_utc",
            "unix_ms",
            "sample",
            "status_text",
            "mode_text",
            "run_status_text",
            "spindle_rpm",
            "spindle_torque_load",
            "feed_rate",
            "part_count",
            "tool_number",
            "alarm_active",
            "alarm_no",
            "alarm_text"
        };

        foreach (var label in axisLabels)
        {
            headers.Add(AxisMetricColumn(label, "servo_torque"));
        }

        foreach (var label in axisLabels)
        {
            headers.Add(AxisMetricColumn(label, "axis_feed_rate"));
        }

        return headers;
    }

    private List<string> BuildRow(CollectorSample sample, IReadOnlyList<string> axisLabels)
    {
        var localTimestamp = sample.TimestampLocal;
        var utcTimestamp = localTimestamp.ToUniversalTime();

        var row = new List<string>
        {
            _machineName,
            localTimestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            utcTimestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            sample.EffectiveUnixTimestampMilliseconds.ToString(CultureInfo.InvariantCulture),
            sample.SampleNumber.ToString(CultureInfo.InvariantCulture),
            sample.StatusDisplayText,
            sample.ModeText,
            sample.RunStatusText,
            sample.SpindleSpeed.ToString(CultureInfo.InvariantCulture),
            sample.SpindleTorqueLoad.ToString(CultureInfo.InvariantCulture),
            sample.FeedText,
            sample.PartCount.ToString(CultureInfo.InvariantCulture),
            sample.ToolNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            sample.AlarmActive ? "true" : "false",
            sample.AlarmNumber.ToString(CultureInfo.InvariantCulture),
            sample.AlarmText
        };

        for (var index = 0; index < axisLabels.Count; index++)
        {
            row.Add(index < sample.AxisTorque.Count && sample.AxisTorque[index].HasValue
                ? sample.AxisTorque[index]!.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty);
        }

        for (var index = 0; index < axisLabels.Count; index++)
        {
            row.Add(index < sample.AxisFeedRate.Count && sample.AxisFeedRate[index].HasValue
                ? sample.AxisFeedRate[index]!.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty);
        }

        return row;
    }

    private static string SanitizeAxisLabel(string name, string fallback)
    {
        var cleaned = new string(name
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
            .ToArray())
            .Trim('_');

        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string AxisMetricColumn(string label, string suffix)
    {
        if (suffix.StartsWith("axis_", StringComparison.Ordinal) && label.EndsWith("_axis", StringComparison.Ordinal))
        {
            return $"{label}_{suffix["axis_".Length..]}";
        }

        return $"{label}_{suffix}";
    }

    private static string SanitizeFileComponent(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(text
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "machine" : cleaned;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value}\"" : value;
    }
}
