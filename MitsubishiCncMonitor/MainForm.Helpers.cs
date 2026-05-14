namespace MitsubishiCncMonitor;

public sealed partial class MainForm
{
    private AppSettings CaptureSettingsFromControls()
    {
        var settings = new AppSettings
        {
            SharedPort = Decimal.ToInt32(_portInput.Value),
            PollIntervalMs = Decimal.ToInt32(_intervalInput.Value),
            ControllerType = _controllerTypeInput.SelectedValue is int selectedValue ? selectedValue : 8,
            AutoStartMonitoring = _autoStartCheckbox.Checked,
            Machines = []
        };

        for (var index = 0; index < MachineCount; index++)
        {
            var existingMachine = index < _settings.Machines.Count
                ? _settings.Machines[index]
                : new MachineSettings();

            var widthOfCut = existingMachine.WidthOfCutMm;
            var depthOfCut = existingMachine.DepthOfCutMm;
            var targetMrr = existingMachine.TargetMrrCm3PerMin;
            var qualityPercent = existingMachine.QualityPercent;

            if (_widthOfCutInput is not null && index == _selectedSlotIndex)
            {
                widthOfCut = (double)_widthOfCutInput.Value;
                depthOfCut = (double)_depthOfCutInput.Value;
                targetMrr = (double)_targetMrrInput.Value;
                qualityPercent = (double)_qualityInput.Value;
            }

            settings.Machines.Add(new MachineSettings
            {
                Name = string.IsNullOrWhiteSpace(_machineNameInputs[index].Text)
                    ? $"Machine {index + 1}"
                    : _machineNameInputs[index].Text.Trim(),
                IpAddress = _machineIpInputs[index].Text.Trim(),
                WidthOfCutMm = widthOfCut,
                DepthOfCutMm = depthOfCut,
                TargetMrrCm3PerMin = targetMrr,
                QualityPercent = qualityPercent
            });
        }

        settings.Normalize();
        return settings;
    }

    private bool EnsureCollectorAvailable()
    {
        if (File.Exists(_collectorPath))
        {
            return true;
        }

        MessageBox.Show(
            this,
            "The native collector executable was not found. Run the publish script to build the packaged desktop app first.",
            "Collector Missing",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);

        UpdateStatus("Collector executable is missing.");
        return false;
    }

    private ConnectionState GetEffectiveState(MachineRuntimeState state)
    {
        if (!state.IsConfigured)
        {
            return ConnectionState.NotConfigured;
        }

        if (!_monitoringActive && state.State == ConnectionState.Running)
        {
            return ConnectionState.Stopped;
        }

        if (state.State == ConnectionState.Running && state.LastUpdated.HasValue)
        {
            var staleAfter = TimeSpan.FromMilliseconds(Math.Max(3000, _settings.PollIntervalMs * 3));
            if (DateTimeOffset.Now - state.LastUpdated.Value > staleAfter)
            {
                return ConnectionState.Stale;
            }
        }

        return state.State;
    }

    private void UpdateButtons()
    {
        _startButton.Enabled = true;
        _saveButton.Enabled = true;
        _stopButton.Enabled = _monitoringActive;
    }

    private void SetDetailValue(string key, string value)
    {
        _detailValueLabels[key].Text = value;
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private void UpdateStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private void LoadSelectedMachineProcessInputs(MachineRuntimeState state)
    {
        if (_widthOfCutInput is null)
        {
            return;
        }

        if (_widthOfCutInput.Focused || _depthOfCutInput.Focused || _targetMrrInput.Focused || _qualityInput.Focused)
        {
            return;
        }

        _widthOfCutInput.Value = ClampDecimal(state.Machine.WidthOfCutMm, _widthOfCutInput.Minimum, _widthOfCutInput.Maximum);
        _depthOfCutInput.Value = ClampDecimal(state.Machine.DepthOfCutMm, _depthOfCutInput.Minimum, _depthOfCutInput.Maximum);
        _targetMrrInput.Value = ClampDecimal(state.Machine.TargetMrrCm3PerMin, _targetMrrInput.Minimum, _targetMrrInput.Maximum);
        _qualityInput.Value = ClampDecimal(state.Machine.QualityPercent, _qualityInput.Minimum, _qualityInput.Maximum);
    }

    private void RefreshOeeGauges(MachineRuntimeState state)
    {
        var metrics = state.OeeMetrics;
        var hasGeometry = state.Machine.WidthOfCutMm > 0.0 && state.Machine.DepthOfCutMm > 0.0;
        var hasTarget = metrics.TargetMrrCm3PerMin > 0.0;

        _oeeGauge.Value = metrics.OeePercent;
        _oeeGauge.ValueText = hasTarget ? $"{metrics.OeePercent:0.0}%" : "--";
        _oeeGauge.Subtitle = hasTarget ? "MRR-based OEE" : "Set target MRR";

        _availabilityGauge.Value = metrics.AvailabilityPercent;
        _availabilityGauge.ValueText = state.History.Count > 0 ? $"{metrics.AvailabilityPercent:0.0}%" : "--";
        _availabilityGauge.Subtitle = state.History.Count > 0 ? $"Run {metrics.RunSeconds / 60.0:0.0} min" : "Waiting for samples";

        _performanceGauge.Value = metrics.PerformancePercent;
        _performanceGauge.ValueText = hasTarget ? $"{metrics.PerformancePercent:0.0}%" : "--";
        _performanceGauge.Subtitle = hasTarget
            ? $"Target {metrics.TargetMrrCm3PerMin:0.0} cm3/min"
            : "Set target MRR";

        _mrrGauge.Maximum = Math.Max(100.0, hasTarget ? metrics.TargetMrrCm3PerMin : metrics.ActualMrrCm3PerMin);
        _mrrGauge.Value = Math.Min(metrics.ActualMrrCm3PerMin, _mrrGauge.Maximum);
        _mrrGauge.ValueText = hasGeometry ? $"{metrics.ActualMrrCm3PerMin:0.0}" : "--";
        _mrrGauge.Subtitle = hasGeometry
            ? hasTarget
                ? $"Target {metrics.TargetMrrCm3PerMin:0.0} cm3/min"
                : "Actual cm3/min"
            : "Set cut width and depth";
    }

    private void ApplyResponsiveLayout()
    {
        if (_contentSplit is not null && _contentSplit.Height > 0)
        {
            var target = Math.Clamp((_contentSplit.Height * 40) / 100, 250, 300);
            if (target > 80 && target < _contentSplit.Height - 40)
            {
                _contentSplit.SplitterDistance = target;
            }
        }

        if (_detailSplit is not null && _detailSplit.Height > 0)
        {
            var target = Math.Clamp(_detailSplit.Width / 2, 260, 520);
            if (target > 120 && target < _detailSplit.Width - 120)
            {
                _detailSplit.SplitterDistance = target;
            }
        }
    }

    private string ResolveCollectorPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "MitsubishiCncCollector.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "build", "collector", "MitsubishiCncCollector.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "m80_smoke_test.exe"))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static Control CreateFieldShell(string caption, Control input)
    {
        var panel = new Panel
        {
            Width = input.Width + 4,
            Height = 68,
            Margin = new Padding(0, 0, 16, 0)
        };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = caption,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(71, 85, 105)
        };

        input.Top = 26;
        input.Left = 0;
        panel.Controls.Add(input);
        panel.Controls.Add(label);
        return panel;
    }

    private static TextBox CreateInputBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 6),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static NumericUpDown CreateDecimalInput(int decimalPlaces)
    {
        return new NumericUpDown
        {
            DecimalPlaces = decimalPlaces,
            Minimum = 0,
            Maximum = 100000,
            Increment = decimalPlaces == 0 ? 1 : 0.1M,
            Width = 140,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular)
        };
    }

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(71, 85, 105)
        };
    }

    private static Label CreateInlineLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85)
        };
    }

    private Control CreateFactTile(string caption, string key)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 6, 6)
        };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240), 1F);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(6, 4, 6, 4)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = caption,
            Font = new Font("Segoe UI", 7F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 0);

        var valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "--"
        };
        _detailValueLabels[key] = valueLabel;
        layout.Controls.Add(valueLabel, 0, 1);

        panel.Controls.Add(layout);
        return panel;
    }

    private static GaugeDialControl CreateGauge(string title, Color accentColor)
    {
        return new GaugeDialControl
        {
            Dock = DockStyle.Fill,
            Title = title,
            AccentColor = accentColor,
            Maximum = 100.0,
            Value = 0.0,
            ValueText = "--",
            Subtitle = "Waiting for data"
        };
    }

    private static decimal ClampDecimal(double value, decimal minimum, decimal maximum)
    {
        var converted = decimal.TryParse(value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), out var result)
            ? result
            : 0M;
        if (converted < minimum)
        {
            return minimum;
        }

        if (converted > maximum)
        {
            return maximum;
        }

        return converted;
    }

    private Button CreateActionButton(string text, Color color, EventHandler onClick)
    {
        var button = new Button
        {
            AutoSize = false,
            BackColor = color,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            Margin = new Padding(0, 18, 12, 0),
            Padding = new Padding(16, 8, 16, 8),
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.08f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, 0.08f);
        button.Click += onClick;
        return button;
    }

    private static void ConfigureActionButtonForColumn(Button button)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, 0, 8);
        button.MinimumSize = new Size(150, 0);
        button.Height = 34;
    }

    private void AddFactRow(TableLayoutPanel table, int rowIndex, string caption, string key)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 8.33F));

        var captionLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = caption,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(71, 85, 105),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(15, 23, 42),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "--"
        };

        _detailValueLabels[key] = valueLabel;
        table.Controls.Add(captionLabel, 0, rowIndex);
        table.Controls.Add(valueLabel, 1, rowIndex);
    }

    private static void DrawSurfaceBorder(Graphics graphics, Rectangle bounds)
    {
        using var pen = new Pen(Color.FromArgb(218, 223, 233), 1F);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private static void DrawInnerBorder(Graphics graphics, Rectangle bounds)
    {
        using var pen = new Pen(Color.FromArgb(226, 232, 240), 1F);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private static string DescribeState(ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Running => "Live",
            ConnectionState.Starting => "Connecting",
            ConnectionState.Stale => "Stale",
            ConnectionState.Error => "Error",
            ConnectionState.NotConfigured => "Not configured",
            _ => "Stopped"
        };
    }

    private sealed record ControllerOption(int Value, string Label);
}
