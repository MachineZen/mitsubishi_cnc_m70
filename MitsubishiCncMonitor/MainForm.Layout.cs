using System.Drawing.Drawing2D;

namespace MitsubishiCncMonitor;

public sealed partial class MainForm
{
    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 18, 18, 10),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        root.Controls.Add(BuildHeroPanel(), 0, 0);
        root.Controls.Add(BuildConnectionPanel(), 0, 1);

        _contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 8,
            BackColor = BackColor
        };
        _contentSplit.Panel1MinSize = 240;
        _contentSplit.Panel2MinSize = 280;
        _contentSplit.Panel1.AutoScroll = true;
        _contentSplit.Panel2.AutoScroll = true;
        _contentSplit.Panel1.Padding = new Padding(0, 0, 0, 10);
        _contentSplit.Panel2.Padding = new Padding(0);
        _contentSplit.Panel1.Controls.Add(BuildDashboardPanel());
        _contentSplit.Panel2.Controls.Add(BuildDetailsPanel());

        root.Controls.Add(_contentSplit, 0, 2);
        Controls.Add(root);

        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = Color.White
        };
        _statusLabel = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusStrip.Items.Add(_statusLabel);
        Controls.Add(statusStrip);
    }

    private Control BuildHeroPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 18, 28, 18)
        };
        panel.Paint += (_, e) =>
        {
            using var brush = new LinearGradientBrush(
                panel.ClientRectangle,
                Color.FromArgb(15, 77, 89),
                Color.FromArgb(180, 130, 52),
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, panel.ClientRectangle);

            using var overlay = new SolidBrush(Color.FromArgb(30, Color.White));
            e.Graphics.FillEllipse(overlay, new Rectangle(panel.Width - 240, -90, 280, 220));
            e.Graphics.FillEllipse(overlay, new Rectangle(panel.Width - 480, 28, 180, 140));
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500F));
        panel.Controls.Add(layout);

        var textHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        textHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        textHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            Font = new Font("Bahnschrift SemiBold", 21F, FontStyle.Regular),
            ForeColor = Color.White,
            Text = "Single-screen monitoring for 3 Mitsubishi controllers",
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subtitle = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            ForeColor = Color.FromArgb(242, 247, 250),
            Text = "Professional desktop monitoring with one shared port, three machine IPs, and one clean operator dashboard for live status, cycle state, MRR-based OEE, feed, alarms, and axis activity.",
            TextAlign = ContentAlignment.MiddleLeft
        };
        textHost.Controls.Add(title, 0, 0);
        textHost.Controls.Add(subtitle, 0, 1);

        var statsHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Color.Transparent,
            Padding = new Padding(12, 8, 0, 8)
        };
        statsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        statsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        statsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        statsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

        statsHost.Controls.Add(CreateHeroStatCard("Configured", "0", out _configuredSummaryValue), 0, 0);
        statsHost.Controls.Add(CreateHeroStatCard("Live", "0", out _liveSummaryValue), 1, 0);
        statsHost.Controls.Add(CreateHeroStatCard("Avg OEE", "--", out _oeeSummaryValue), 2, 0);
        statsHost.Controls.Add(CreateHeroStatCard("Alarms", "0", out _alarmSummaryValue), 3, 0);

        layout.Controls.Add(textHost, 0, 0);
        layout.Controls.Add(statsHost, 1, 0);
        return panel;
    }

    private Control BuildConnectionPanel()
    {
        var panel = CreateSurfacePanel();
        panel.Padding = new Padding(18, 12, 18, 12);
        panel.AutoScroll = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(root);

        var titleHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        titleHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        titleHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));

        titleHost.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Connection Setup",
            Font = new Font("Bahnschrift SemiBold", 15F, FontStyle.Regular),
            ForeColor = Color.FromArgb(23, 37, 84)
        }, 0, 0);
        titleHost.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Set the common network values once, then assign each controller its own machine name and IP address.",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 1);
        root.Controls.Add(titleHost, 0, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));

        var controlsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        _portInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Width = 128,
            Value = 683,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular)
        };
        _intervalInput = new NumericUpDown
        {
            Minimum = 250,
            Maximum = 60000,
            Increment = 250,
            Width = 128,
            Value = 1000,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular)
        };
        _controllerTypeInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 184,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            DisplayMember = nameof(ControllerOption.Label),
            ValueMember = nameof(ControllerOption.Value),
            DataSource = ControllerOptions
        };
        _autoStartCheckbox = new CheckBox
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Margin = new Padding(10, 30, 18, 0),
            Text = "Auto start on launch"
        };

        _saveButton = CreateActionButton("Save Settings", Color.FromArgb(14, 116, 144), HandleSaveClicked);
        _startButton = CreateActionButton("Start Monitoring", Color.FromArgb(22, 101, 52), HandleStartClicked);
        _stopButton = CreateActionButton("Stop Monitoring", Color.FromArgb(185, 28, 28), HandleStopClicked);

        controlsFlow.Controls.Add(CreateFieldShell("Shared Port", _portInput));
        controlsFlow.Controls.Add(CreateFieldShell("Poll Interval (ms)", _intervalInput));
        controlsFlow.Controls.Add(CreateFieldShell("Controller Series", _controllerTypeInput));
        controlsFlow.Controls.Add(_autoStartCheckbox);
        actions.Controls.Add(controlsFlow, 0, 0);

        var buttonHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(10, 0, 0, 0)
        };
        buttonHost.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        buttonHost.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        buttonHost.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        ConfigureActionButtonForColumn(_saveButton);
        ConfigureActionButtonForColumn(_startButton);
        ConfigureActionButtonForColumn(_stopButton);
        buttonHost.Controls.Add(_saveButton, 0, 0);
        buttonHost.Controls.Add(_startButton, 0, 1);
        buttonHost.Controls.Add(_stopButton, 0, 2);
        actions.Controls.Add(buttonHost, 1, 0);

        root.Controls.Add(actions, 0, 1);

        var endpointsPanel = CreateInsetPanel();
        endpointsPanel.Padding = new Padding(18, 12, 18, 12);
        endpointsPanel.Margin = new Padding(0, 6, 0, 0);
        endpointsPanel.AutoScroll = true;

        var endpointsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            RowCount = 3,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        endpointsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        endpointsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        endpointsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        endpointsPanel.Controls.Add(endpointsRoot);

        endpointsRoot.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Machine Endpoints",
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59)
        }, 0, 0);
        endpointsRoot.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Use one row per controller. Every machine has its own IP address.",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 1);

        var editors = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = MachineCount + 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        editors.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

        editors.Controls.Add(CreateEndpointHeader("Machine"), 0, 0);
        editors.Controls.Add(CreateEndpointHeader("Display Name"), 1, 0);
        editors.Controls.Add(CreateEndpointHeader("IP Address"), 2, 0);

        for (var index = 0; index < MachineCount; index++)
        {
            editors.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            editors.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = $"Machine {index + 1}",
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, index + 1);

            var nameBox = CreateInputBox();
            nameBox.PlaceholderText = $"Machine {index + 1}";
            _machineNameInputs[index] = nameBox;
            editors.Controls.Add(nameBox, 1, index + 1);

            var ipBox = CreateInputBox();
            ipBox.PlaceholderText = $"Enter IP for machine {index + 1}";
            _machineIpInputs[index] = ipBox;
            editors.Controls.Add(ipBox, 2, index + 1);
        }

        endpointsRoot.Controls.Add(editors, 0, 2);
        root.Controls.Add(endpointsPanel, 0, 2);
        return panel;
    }

    private Control BuildDashboardPanel()
    {
        var panel = CreateSurfacePanel();
        panel.Padding = new Padding(18, 12, 18, 12);
        panel.AutoScroll = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            RowCount = 3,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Live Dashboard",
            Font = new Font("Bahnschrift SemiBold", 16F, FontStyle.Regular),
            ForeColor = Color.FromArgb(23, 37, 84)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Click a machine card to inspect its live detail panel below.",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 1);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 0),
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 220)
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        for (var index = 0; index < MachineCount; index++)
        {
            var card = new MachineCardView(index)
            {
                Dock = DockStyle.Fill
            };
            card.CardSelected += HandleCardSelected;
            _machineCards[index] = card;
            cards.Controls.Add(card, index, 0);
        }

        root.Controls.Add(cards, 0, 2);
        return panel;
    }

    private Control BuildDetailsPanel()
    {
        var panel = CreateSurfacePanel();
        panel.Padding = new Padding(18, 12, 18, 12);
        panel.AutoScroll = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            RowCount = 2,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));

        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Machine Detail",
            Font = new Font("Bahnschrift SemiBold", 16F, FontStyle.Regular),
            ForeColor = Color.FromArgb(23, 37, 84)
        }, 0, 0);

        _detailMachineSelector = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular)
        };
        _detailMachineSelector.SelectedIndexChanged += HandleDetailSelectorChanged;
        header.Controls.Add(_detailMachineSelector, 1, 0);
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 0, 0),
            MinimumSize = new Size(0, 520),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        body.Controls.Add(BuildSelectedMachinePanel(), 0, 0);
        body.Controls.Add(BuildDetailDataPanel(), 1, 0);
        root.Controls.Add(body, 0, 1);
        return panel;
    }

    private Control BuildSelectedMachinePanel()
    {
        var panel = CreateInsetPanel();
        panel.Padding = new Padding(18);
        panel.Margin = new Padding(0, 0, 12, 0);
        panel.AutoScroll = true;
        panel.MinimumSize = new Size(380, 520);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            RowCount = 4,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 204F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(root);

        _detailMachineTitle = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Bahnschrift SemiBold", 15F, FontStyle.Regular),
            ForeColor = Color.FromArgb(30, 41, 59)
        };
        root.Controls.Add(_detailMachineTitle, 0, 0);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Operational snapshot",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 1);

        var facts = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 0, 0)
        };
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        facts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        facts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        facts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        facts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

        facts.Controls.Add(CreateFactTile("Endpoint", "endpoint"), 0, 0);
        facts.Controls.Add(CreateFactTile("Connection", "connection"), 1, 0);
        facts.Controls.Add(CreateFactTile("CNC Status", "status"), 2, 0);
        facts.Controls.Add(CreateFactTile("Mode", "mode"), 0, 1);
        facts.Controls.Add(CreateFactTile("Run Status", "runstatus"), 1, 1);
        facts.Controls.Add(CreateFactTile("Spindle RPM", "spindle"), 2, 1);
        facts.Controls.Add(CreateFactTile("Spindle Load", "spindleload"), 0, 2);
        facts.Controls.Add(CreateFactTile("Feed Rate", "feed"), 1, 2);
        facts.Controls.Add(CreateFactTile("Part Count", "parts"), 2, 2);
        facts.Controls.Add(CreateFactTile("Tool No", "tool"), 0, 3);
        facts.Controls.Add(CreateFactTile("Alarm", "alarm"), 1, 3);
        facts.Controls.Add(CreateFactTile("Last Update", "updated"), 2, 3);
        root.Controls.Add(facts, 0, 2);

        root.Controls.Add(BuildMrrSettingsPanel(), 0, 3);

        return panel;
    }

    private Control BuildDetailDataPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            RowCount = 2,
            BackColor = Color.Transparent,
            MinimumSize = new Size(520, 520),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360F));

        root.Controls.Add(BuildGaugePanel(), 0, 0);

        _detailSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8,
            BackColor = BackColor
        };
        _detailSplit.Panel1MinSize = 280;
        _detailSplit.Panel2MinSize = 280;
        _detailSplit.Panel1.Padding = new Padding(12, 10, 6, 0);
        _detailSplit.Panel2.Padding = new Padding(6, 10, 0, 0);

        _detailSplit.Panel1.Controls.Add(BuildHistoryPanel());
        _detailSplit.Panel2.Controls.Add(BuildAxisPanel());
        root.Controls.Add(_detailSplit, 0, 1);
        return root;
    }

    private Control BuildGaugePanel()
    {
        var panel = CreateInsetPanel();
        panel.Padding = new Padding(16);
        panel.Margin = new Padding(12, 0, 0, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "MRR-Based OEE Gauges",
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85)
        }, 0, 0);

        var gauges = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0)
        };
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

        _oeeGauge = CreateGauge("OEE", Color.FromArgb(14, 165, 233));
        _availabilityGauge = CreateGauge("Availability", Color.FromArgb(59, 130, 246));
        _performanceGauge = CreateGauge("Performance", Color.FromArgb(245, 158, 11));
        _mrrGauge = CreateGauge("Live MRR", Color.FromArgb(34, 197, 94));

        gauges.Controls.Add(_oeeGauge, 0, 0);
        gauges.Controls.Add(_availabilityGauge, 1, 0);
        gauges.Controls.Add(_performanceGauge, 2, 0);
        gauges.Controls.Add(_mrrGauge, 3, 0);
        root.Controls.Add(gauges, 0, 1);

        return panel;
    }

    private Control BuildHistoryPanel()
    {
        var panel = CreateInsetPanel();
        panel.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Recent Samples",
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85)
        }, 0, 0);

        _historyGrid = CreateReadOnlyGrid();
        _historyGrid.Columns.Add("time", "Time");
        _historyGrid.Columns.Add("status", "Status");
        _historyGrid.Columns.Add("mode", "Mode");
        _historyGrid.Columns.Add("run", "Run");
        _historyGrid.Columns.Add("spindle", "RPM");
        _historyGrid.Columns.Add("feed", "Feed");
        _historyGrid.Columns.Add("parts", "Parts");
        _historyGrid.Columns.Add("tool", "Tool No");
        _historyGrid.Columns.Add("alarm", "Alarm");
        _historyGrid.Columns[0].Width = 130;
        _historyGrid.Columns[1].Width = 84;
        _historyGrid.Columns[2].Width = 74;
        _historyGrid.Columns[3].Width = 84;
        _historyGrid.Columns[4].Width = 82;
        _historyGrid.Columns[5].Width = 82;
        _historyGrid.Columns[6].Width = 74;
        _historyGrid.Columns[7].Width = 64;
        _historyGrid.Columns[8].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        root.Controls.Add(_historyGrid, 0, 1);

        return panel;
    }

    private Control BuildAxisPanel()
    {
        var panel = CreateInsetPanel();
        panel.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Axis Torque And Feed Rate",
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85)
        }, 0, 0);

        _axisGrid = CreateReadOnlyGrid();
        _axisGrid.Columns.Add("axis", "Axis");
        _axisGrid.Columns.Add("torque", "Servo Torque");
        _axisGrid.Columns.Add("feed", "Axis Feed Rate");
        _axisGrid.Columns[0].Width = 120;
        _axisGrid.Columns[1].Width = 120;
        _axisGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        root.Controls.Add(_axisGrid, 0, 1);

        return panel;
    }

    private Control BuildMrrSettingsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(14)
        };
        panel.Paint += (_, e) => DrawInnerBorder(e.Graphics, panel.ClientRectangle);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        panel.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "MRR / OEE Setup",
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Assumes feed is mm/min: MRR = feed x width of cut x depth of cut / 1000",
            Font = new Font("Segoe UI", 8F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 1);

        var firstRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        firstRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        firstRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        _widthOfCutInput = CreateDecimalInput(2);
        _depthOfCutInput = CreateDecimalInput(2);
        firstRow.Controls.Add(CreateFieldShell("Width Of Cut (mm)", _widthOfCutInput), 0, 0);
        firstRow.Controls.Add(CreateFieldShell("Depth Of Cut (mm)", _depthOfCutInput), 1, 0);
        root.Controls.Add(firstRow, 0, 2);

        var secondRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Color.Transparent
        };
        secondRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        secondRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        secondRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

        _targetMrrInput = CreateDecimalInput(1);
        _targetMrrInput.Maximum = 100000;
        _qualityInput = CreateDecimalInput(1);
        _qualityInput.Maximum = 100;
        _qualityInput.Value = 100;
        _saveMrrButton = CreateActionButton("Save OEE Settings", Color.FromArgb(21, 94, 117), HandleSaveMrrSettingsClicked);
        ConfigureActionButtonForColumn(_saveMrrButton);
        _saveMrrButton.Margin = new Padding(8, 22, 0, 0);

        secondRow.Controls.Add(CreateFieldShell("Target MRR (cm3/min)", _targetMrrInput), 0, 0);
        secondRow.Controls.Add(CreateFieldShell("Quality %", _qualityInput), 1, 0);
        secondRow.Controls.Add(_saveMrrButton, 2, 0);
        root.Controls.Add(secondRow, 0, 3);

        return panel;
    }

    private Panel CreateSurfacePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        panel.Paint += (_, e) => DrawSurfaceBorder(e.Graphics, panel.ClientRectangle);
        return panel;
    }

    private Panel CreateInsetPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 249, 246)
        };
        panel.Paint += (_, e) => DrawInnerBorder(e.Graphics, panel.ClientRectangle);
        return panel;
    }

    private DataGridView CreateReadOnlyGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            GridColor = Color.FromArgb(226, 232, 240),
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false
        };

        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeight = 34;
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(226, 232, 240),
            ForeColor = Color.FromArgb(15, 23, 42),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            SelectionBackColor = Color.FromArgb(226, 232, 240),
            SelectionForeColor = Color.FromArgb(15, 23, 42)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(30, 41, 59),
            SelectionBackColor = Color.FromArgb(220, 252, 231),
            SelectionForeColor = Color.FromArgb(22, 101, 52)
        };

        return grid;
    }

    private Control CreateHeroStatCard(string caption, string initialValue, out Label valueLabel)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(44, 255, 255, 255),
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(14, 14, 14, 14)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Text = caption,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(234, 242, 247)
        }, 0, 0);

        valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Text = initialValue,
            Font = new Font("Bahnschrift SemiBold", 21F, FontStyle.Regular),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(valueLabel, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static Label CreateEndpointHeader(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 116, 139),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }
}
