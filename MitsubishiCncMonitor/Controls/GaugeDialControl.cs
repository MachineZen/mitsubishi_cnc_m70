using System.Drawing.Drawing2D;

namespace MitsubishiCncMonitor;

public sealed class GaugeDialControl : Control
{
    private double _value;
    private double _maximum = 100.0;
    private string _title = string.Empty;
    private string _valueText = "--";
    private string _subtitle = string.Empty;
    private Color _accentColor = Color.FromArgb(14, 165, 233);

    public GaugeDialControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        Size = new Size(180, 180);
        Margin = new Padding(0, 0, 12, 0);
    }

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            Invalidate();
        }
    }

    public string ValueText
    {
        get => _valueText;
        set
        {
            _valueText = value;
            Invalidate();
        }
    }

    public string Subtitle
    {
        get => _subtitle;
        set
        {
            _subtitle = value;
            Invalidate();
        }
    }

    public Color AccentColor
    {
        get => _accentColor;
        set
        {
            _accentColor = value;
            Invalidate();
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value <= 0.0 ? 100.0 : value;
            Invalidate();
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0.0, Maximum);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = ClientRectangle;
        using var borderPen = new Pen(Color.FromArgb(226, 232, 240), 1F);
        e.Graphics.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);

        var titleRect = new Rectangle(0, 10, Width, 20);
        TextRenderer.DrawText(
            e.Graphics,
            Title,
            new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            titleRect,
            Color.FromArgb(51, 65, 85),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var size = Math.Min(Width, Height) - 44;
        var dialRect = new Rectangle((Width - size) / 2, 34, size, size);
        using var backgroundPen = new Pen(Color.FromArgb(226, 232, 240), 12F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawArc(backgroundPen, dialRect, 135, 270);

        var sweep = (float)(270.0 * (Value / Maximum));
        using var valuePen = new Pen(AccentColor, 12F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawArc(valuePen, dialRect, 135, sweep);

        var valueRect = new Rectangle(20, dialRect.Top + 42, Width - 40, 34);
        TextRenderer.DrawText(
            e.Graphics,
            ValueText,
            new Font("Bahnschrift SemiBold", 20F, FontStyle.Regular),
            valueRect,
            Color.FromArgb(15, 23, 42),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var subtitleRect = new Rectangle(18, dialRect.Bottom - 8, Width - 36, 32);
        TextRenderer.DrawText(
            e.Graphics,
            Subtitle,
            new Font("Segoe UI", 8.5F, FontStyle.Regular),
            subtitleRect,
            Color.FromArgb(100, 116, 139),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
    }
}
