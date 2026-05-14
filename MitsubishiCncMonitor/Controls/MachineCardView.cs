using System.Drawing.Drawing2D;

namespace MitsubishiCncMonitor;

public sealed class MachineCardView : Panel
{
    private readonly Label _nameLabel;
    private readonly Label _endpointLabel;
    private readonly Label _stateBadge;
    private readonly Label _statusValue;
    private readonly Label _modeValue;
    private readonly Label _runValue;
    private readonly Label _toolValue;
    private readonly Label _spindleValue;
    private readonly Label _feedValue;
    private readonly Label _loadValue;
    private readonly Label _alarmValue;
    private readonly Label _messageValue;
    private readonly Label _updatedValue;

    private bool _selected;
    private Color _accentColor = Color.FromArgb(34, 128, 108);

    public MachineCardView(int slotIndex)
    {
        SlotIndex = slotIndex;
        DoubleBuffered = true;
        Margin = new Padding(0, 0, 0, 0);
        Padding = new Padding(16, 14, 16, 12);
        MinimumSize = new Size(320, 210);
        BackColor = Color.White;
        Cursor = Cursors.Hand;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106F));

        _nameLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Bahnschrift SemiBold", 14F, FontStyle.Regular),
            ForeColor = Color.FromArgb(23, 37, 84),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        _stateBadge = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Margin = new Padding(10, 2, 0, 2)
        };
        header.Controls.Add(_nameLabel, 0, 0);
        header.Controls.Add(_stateBadge, 1, 0);
        root.Controls.Add(header, 0, 0);

        _endpointLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.Controls.Add(_endpointLabel, 0, 1);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 6)
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        metrics.Controls.Add(CreateMetricTile("Status", out _statusValue), 0, 0);
        metrics.Controls.Add(CreateMetricTile("Mode", out _modeValue), 1, 0);
        metrics.Controls.Add(CreateMetricTile("Run", out _runValue), 2, 0);
        metrics.Controls.Add(CreateMetricTile("Tool No", out _toolValue), 3, 0);
        metrics.Controls.Add(CreateMetricTile("RPM", out _spindleValue), 0, 1);
        metrics.Controls.Add(CreateMetricTile("Feed", out _feedValue), 1, 1);
        metrics.Controls.Add(CreateMetricTile("S.Load", out _loadValue), 2, 1);
        metrics.Controls.Add(CreateMetricTile("Alarm", out _alarmValue), 3, 1);
        root.Controls.Add(metrics, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));

        _messageValue = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8F, FontStyle.Regular),
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoEllipsis = true
        };
        _updatedValue = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139),
            AutoEllipsis = true
        };

        footer.Controls.Add(_messageValue, 0, 0);
        footer.Controls.Add(_updatedValue, 0, 1);
        root.Controls.Add(footer, 0, 3);

        AttachClickHandlers(this);
    }

    public int SlotIndex { get; }

    public event EventHandler<int>? CardSelected;

    public void Apply(MachineRuntimeState runtimeState, int port, ConnectionState effectiveState, bool selected)
    {
        _selected = selected;
        _accentColor = GetAccentColor(effectiveState);
        BackColor = selected ? Color.FromArgb(255, 252, 245) : Color.White;
        var hasMrrGeometry = runtimeState.Machine.WidthOfCutMm > 0.0 && runtimeState.Machine.DepthOfCutMm > 0.0;
        var hasTargetMrr = runtimeState.OeeMetrics.TargetMrrCm3PerMin > 0.0;

        var sample = runtimeState.LastSample;
        _nameLabel.Text = runtimeState.DisplayName;
        _endpointLabel.Text = runtimeState.IsConfigured
            ? $"{runtimeState.Machine.IpAddress}:{port}"
            : "IP address not configured";
        _stateBadge.Text = GetBadgeText(effectiveState);
        _stateBadge.BackColor = _accentColor;

        _statusValue.Text = sample?.StatusDisplayText ?? "--";
        _modeValue.Text = sample?.ModeText ?? "--";
        _runValue.Text = sample?.RunStatusText ?? "--";
        _toolValue.Text = sample?.ToolText ?? "--";
        _spindleValue.Text = sample?.SpindleSpeed.ToString() ?? "--";
        _feedValue.Text = sample?.FeedText ?? "--";
        _loadValue.Text = sample?.SpindleLoadText ?? "--";
        _alarmValue.Text = sample?.AlarmBadgeText ?? "--";

        if (sample?.AlarmActive == true)
        {
            _messageValue.Text = sample.AlarmSummary;
            _messageValue.ForeColor = Color.FromArgb(180, 83, 9);
        }
        else if (!string.IsNullOrWhiteSpace(runtimeState.LastError) && effectiveState == ConnectionState.Error)
        {
            _messageValue.Text = runtimeState.LastError;
            _messageValue.ForeColor = Color.FromArgb(185, 28, 28);
        }
        else
        {
            _messageValue.Text = sample is not null
                ? $"Parts {sample.PartsText} | Axes {sample.AxisCount} | {(hasTargetMrr ? $"OEE {runtimeState.OeeMetrics.OeePercent:0.0}%" : (hasMrrGeometry ? "Set target MRR" : runtimeState.StatusMessage))}"
                : runtimeState.StatusMessage;
            _messageValue.ForeColor = Color.FromArgb(71, 85, 105);
        }

        _updatedValue.Text = runtimeState.LastUpdated.HasValue
            ? $"Updated {runtimeState.LastUpdated.Value:dd-MMM HH:mm:ss.fff}"
            : "Waiting for first sample";

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        using var borderPath = CreateRoundedRectangle(bounds, 20);
        using var borderPen = new Pen(_selected ? _accentColor : Color.FromArgb(214, 219, 229), _selected ? 2F : 1F);
        using var accentBrush = new SolidBrush(_accentColor);

        e.Graphics.DrawPath(borderPen, borderPath);
        e.Graphics.FillRectangle(accentBrush, new Rectangle(18, 0, Width - 36, 6));
    }

    private Control CreateMetricTile(string caption, out Label valueLabel)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Margin = new Padding(0, 0, 8, 8)
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
            Padding = new Padding(8, 5, 8, 5)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = caption,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 0);

        valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "--"
        };
        layout.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private void AttachClickHandlers(Control control)
    {
        control.Click += (_, _) => CardSelected?.Invoke(this, SlotIndex);

        foreach (Control child in control.Controls)
        {
            AttachClickHandlers(child);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static Color GetAccentColor(ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Running => Color.FromArgb(34, 128, 108),
            ConnectionState.Starting => Color.FromArgb(217, 119, 6),
            ConnectionState.Stale => Color.FromArgb(8, 145, 178),
            ConnectionState.Error => Color.FromArgb(220, 38, 38),
            ConnectionState.NotConfigured => Color.FromArgb(100, 116, 139),
            _ => Color.FromArgb(71, 85, 105)
        };
    }

    private static string GetBadgeText(ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Running => "Live",
            ConnectionState.Starting => "Connecting",
            ConnectionState.Stale => "Stale",
            ConnectionState.Error => "Error",
            ConnectionState.NotConfigured => "No IP",
            _ => "Stopped"
        };
    }
}
