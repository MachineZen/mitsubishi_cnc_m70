namespace MitsubishiCncMonitor;

public sealed partial class MainForm
{
    private void LoadSettingsIntoControls(AppSettings settings)
    {
        _portInput.Value = settings.SharedPort;
        _intervalInput.Value = settings.PollIntervalMs;
        _controllerTypeInput.SelectedValue = settings.ControllerType;
        _autoStartCheckbox.Checked = settings.AutoStartMonitoring;

        for (var index = 0; index < MachineCount; index++)
        {
            var machine = settings.Machines[index];
            _machineNameInputs[index].Text = machine.Name;
            _machineIpInputs[index].Text = machine.IpAddress;
        }
    }

    private void RebuildRuntimeStates()
    {
        _machineStates.Clear();

        for (var index = 0; index < MachineCount; index++)
        {
            _machineStates.Add(new MachineRuntimeState(index, _settings.Machines[index].Clone()));
        }

        if (_selectedSlotIndex >= _machineStates.Count)
        {
            _selectedSlotIndex = 0;
        }

        RefreshSelectorItems();
    }

    private void RefreshSelectorItems()
    {
        _suppressSelectorEvents = true;
        _detailMachineSelector.Items.Clear();

        foreach (var state in _machineStates)
        {
            _detailMachineSelector.Items.Add(state.DisplayName);
        }

        if (_detailMachineSelector.Items.Count > 0)
        {
            _detailMachineSelector.SelectedIndex = Math.Clamp(_selectedSlotIndex, 0, _detailMachineSelector.Items.Count - 1);
        }

        _suppressSelectorEvents = false;
    }

    private void RefreshDashboard()
    {
        var configuredCount = 0;
        var liveCount = 0;
        var alarmCount = 0;
        var oeeTotal = 0.0;
        var oeeMachineCount = 0;

        for (var index = 0; index < _machineStates.Count; index++)
        {
            var state = _machineStates[index];
            var effectiveState = GetEffectiveState(state);
            _machineCards[index].Apply(state, _settings.SharedPort, effectiveState, index == _selectedSlotIndex);

            if (state.IsConfigured)
            {
                configuredCount++;
            }
            if (effectiveState == ConnectionState.Running)
            {
                liveCount++;
            }
            if (state.LastSample?.AlarmActive == true)
            {
                alarmCount++;
            }
            if (state.OeeMetrics.TargetMrrCm3PerMin > 0.0)
            {
                oeeTotal += state.OeeMetrics.OeePercent;
                oeeMachineCount++;
            }
        }

        _configuredSummaryValue.Text = configuredCount.ToString();
        _liveSummaryValue.Text = liveCount.ToString();
        _oeeSummaryValue.Text = oeeMachineCount > 0 ? $"{oeeTotal / oeeMachineCount:0.0}%" : "--";
        _alarmSummaryValue.Text = alarmCount.ToString();

        UpdateButtons();
    }

    private void RefreshDetailPanel()
    {
        if (_machineStates.Count == 0)
        {
            return;
        }

        var state = _machineStates[_selectedSlotIndex];
        var effectiveState = GetEffectiveState(state);
        var sample = state.LastSample;

        _detailMachineTitle.Text = state.DisplayName;

        SetDetailValue("endpoint", state.IsConfigured ? $"{state.Machine.IpAddress}:{_settings.SharedPort}" : "--");
        SetDetailValue("connection", $"{DescribeState(effectiveState)} | {state.StatusMessage}");
        SetDetailValue("status", sample?.StatusDisplayText ?? "--");
        SetDetailValue("mode", sample?.ModeText ?? "--");
        SetDetailValue("runstatus", sample?.RunStatusText ?? "--");
        SetDetailValue("spindle", sample?.SpindleText ?? "--");
        SetDetailValue("spindleload", sample?.SpindleLoadText ?? "--");
        SetDetailValue("feed", sample is null ? "--" : $"{sample.FeedText} mm/min");
        SetDetailValue("parts", sample?.PartsText ?? "--");
        SetDetailValue("tool", sample?.ToolText ?? "--");
        SetDetailValue("alarm", sample?.AlarmSummary ?? (string.IsNullOrWhiteSpace(state.LastError) ? "No active alarm" : state.LastError));
        SetDetailValue("updated", state.LastUpdated.HasValue ? state.LastUpdated.Value.ToString("dd-MMM-yyyy HH:mm:ss.fff") : "--");

        RefreshOeeGauges(state);
        LoadSelectedMachineProcessInputs(state);
        RefreshAxisGrid(sample);
        RefreshHistoryGrid(state.History);
    }

    private void RefreshAxisGrid(CollectorSample? sample)
    {
        _axisGrid.Rows.Clear();

        if (sample is null)
        {
            return;
        }

        var axisCount = Math.Max(sample.AxisCount, Math.Max(sample.AxisTorque.Count, sample.AxisFeedRate.Count));
        for (var index = 0; index < axisCount; index++)
        {
            var torque = index < sample.AxisTorque.Count && sample.AxisTorque[index].HasValue
                ? sample.AxisTorque[index]!.Value.ToString()
                : "--";
            var feed = index < sample.AxisFeedRate.Count && sample.AxisFeedRate[index].HasValue
                ? sample.AxisFeedRate[index]!.Value.ToString("0.###")
                : "--";

            _axisGrid.Rows.Add(sample.GetAxisLabel(index), torque, feed);
        }
    }

    private void RefreshHistoryGrid(IEnumerable<CollectorSample> history)
    {
        _historyGrid.Rows.Clear();

        foreach (var sample in history.Take(60))
        {
            _historyGrid.Rows.Add(
                sample.TimestampLocal.ToString("dd-MMM HH:mm:ss.fff"),
                sample.StatusDisplayText,
                sample.ModeText,
                sample.RunStatusText,
                sample.SpindleSpeed,
                sample.FeedText,
                sample.PartsText,
                sample.ToolText,
                sample.AlarmActive ? sample.AlarmSummary : "No active alarm");
        }
    }

    private void HandleShown(object? sender, EventArgs e)
    {
        BeginInvoke(new Action(ApplyResponsiveLayout));

        if (_settings.AutoStartMonitoring && _settings.Machines.Any(machine => !string.IsNullOrWhiteSpace(machine.IpAddress)))
        {
            StartMonitoring();
        }
    }

    private void HandleSaveClicked(object? sender, EventArgs e)
    {
        _settings = CaptureSettingsFromControls();
        SettingsStore.Save(_settings);

        var restart = _monitoringActive;
        RebuildRuntimeStates();
        RefreshDashboard();
        RefreshDetailPanel();
        UpdateStatus("Settings saved.");

        if (restart)
        {
            StartMonitoring();
        }
    }

    private void HandleStartClicked(object? sender, EventArgs e)
    {
        StartMonitoring();
    }

    private void HandleSaveMrrSettingsClicked(object? sender, EventArgs e)
    {
        if (_machineStates.Count == 0)
        {
            return;
        }

        var runtimeState = _machineStates[_selectedSlotIndex];
        runtimeState.UpdateProcessSettings(
            (double)_widthOfCutInput.Value,
            (double)_depthOfCutInput.Value,
            (double)_targetMrrInput.Value,
            (double)_qualityInput.Value);

        _settings.Machines[_selectedSlotIndex].WidthOfCutMm = runtimeState.Machine.WidthOfCutMm;
        _settings.Machines[_selectedSlotIndex].DepthOfCutMm = runtimeState.Machine.DepthOfCutMm;
        _settings.Machines[_selectedSlotIndex].TargetMrrCm3PerMin = runtimeState.Machine.TargetMrrCm3PerMin;
        _settings.Machines[_selectedSlotIndex].QualityPercent = runtimeState.Machine.QualityPercent;
        SettingsStore.Save(_settings);

        runtimeState.RecalculateOeeMetricsFromHistory();

        RefreshDashboard();
        RefreshDetailPanel();
        UpdateStatus($"{runtimeState.DisplayName}: MRR/OEE settings saved.");
    }

    private void HandleStopClicked(object? sender, EventArgs e)
    {
        StopMonitoring();
    }

    private void HandleCardSelected(object? sender, int slotIndex)
    {
        _selectedSlotIndex = slotIndex;

        _suppressSelectorEvents = true;
        _detailMachineSelector.SelectedIndex = slotIndex;
        _suppressSelectorEvents = false;

        RefreshDashboard();
        RefreshDetailPanel();
    }

    private void HandleDetailSelectorChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectorEvents || _detailMachineSelector.SelectedIndex < 0)
        {
            return;
        }

        _selectedSlotIndex = _detailMachineSelector.SelectedIndex;
        RefreshDashboard();
        RefreshDetailPanel();
    }

    private void StartMonitoring()
    {
        _settings = CaptureSettingsFromControls();
        SettingsStore.Save(_settings);
        RebuildRuntimeStates();
        ResetCsvLoggers();

        if (!EnsureCollectorAvailable())
        {
            RefreshDashboard();
            RefreshDetailPanel();
            return;
        }

        EnsureSessions();
        EnsureCsvLoggers();
        _monitoringActive = true;

        for (var index = 0; index < _machineStates.Count; index++)
        {
            _sessions[index].Start(
                _machineStates[index].Machine,
                _settings.SharedPort,
                _settings.ControllerType,
                _settings.PollIntervalMs);
        }

        RefreshDashboard();
        RefreshDetailPanel();
        var logPath = _csvLoggers.Values.Select(logger => logger.FilePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            ?? CsvSampleLogger.ResolveDefaultLogDirectory();
        UpdateStatus($"Monitoring started. CSV logs will be written under {logPath}.");
    }

    private void StopMonitoring()
    {
        _monitoringActive = false;

        foreach (var session in _sessions.Values)
        {
            session.Stop();
        }

        ResetCsvLoggers();

        foreach (var state in _machineStates)
        {
            state.State = state.IsConfigured ? ConnectionState.Stopped : ConnectionState.NotConfigured;
            state.StatusMessage = state.IsConfigured ? "Monitoring stopped." : "Enter an IP address to enable monitoring.";
        }

        RefreshDashboard();
        RefreshDetailPanel();
        UpdateStatus("Monitoring stopped.");
    }

    private void EnsureSessions()
    {
        for (var index = 0; index < MachineCount; index++)
        {
            if (_sessions.ContainsKey(index))
            {
                continue;
            }

            var session = new CollectorSession(index, _collectorPath);
            session.StateChanged += HandleSessionStateChanged;
            session.SampleReceived += HandleSessionSampleReceived;
            _sessions[index] = session;
        }
    }

    private void EnsureCsvLoggers()
    {
        var logDirectory = CsvSampleLogger.ResolveDefaultLogDirectory();

        for (var index = 0; index < _machineStates.Count; index++)
        {
            if (_csvLoggers.ContainsKey(index) || !_machineStates[index].IsConfigured)
            {
                continue;
            }

            _csvLoggers[index] = new CsvSampleLogger(_machineStates[index].DisplayName, logDirectory);
        }
    }

    private void ResetCsvLoggers()
    {
        foreach (var logger in _csvLoggers.Values)
        {
            logger.Dispose();
        }

        _csvLoggers.Clear();
    }

    private void HandleSessionStateChanged(object? sender, MachineStateChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            var state = _machineStates[e.SlotIndex];
            state.State = e.State;
            state.StatusMessage = e.Message;

            if (e.State == ConnectionState.Error)
            {
                state.LastError = e.Message;
            }
            else if (e.State == ConnectionState.Running)
            {
                state.LastError = string.Empty;
            }

            RefreshDashboard();
            if (_selectedSlotIndex == e.SlotIndex)
            {
                RefreshDetailPanel();
            }

            UpdateStatus($"{state.DisplayName}: {e.Message}");
        });
    }

    private void HandleSessionSampleReceived(object? sender, MachineSampleReceivedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            var state = _machineStates[e.SlotIndex];
            if (_csvLoggers.TryGetValue(e.SlotIndex, out var logger))
            {
                try
                {
                    logger.LogSample(e.Sample);
                }
                catch (Exception ex)
                {
                    state.LastError = $"CSV logging failed: {ex.Message}";
                }
            }

            state.AddSample(e.Sample, MaxHistoryRows);
            state.State = ConnectionState.Running;
            state.StatusMessage = "Receiving live data.";
            if (!state.LastError.StartsWith("CSV logging failed:", StringComparison.Ordinal))
            {
                state.LastError = string.Empty;
            }

            RefreshDashboard();
            if (_selectedSlotIndex == e.SlotIndex)
            {
                RefreshDetailPanel();
            }
        });
    }
}
