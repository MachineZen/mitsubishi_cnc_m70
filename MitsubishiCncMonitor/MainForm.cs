namespace MitsubishiCncMonitor;

public sealed partial class MainForm : Form
{
    private const int MachineCount = 3;
    private const int MaxHistoryRows = 120;

    private static readonly ControllerOption[] ControllerOptions =
    [
        new(8, "M80 / M800"),
        new(6, "M70 / M700")
    ];

    private readonly Dictionary<int, MachineCardView> _machineCards = [];
    private readonly Dictionary<int, CollectorSession> _sessions = [];
    private readonly List<MachineRuntimeState> _machineStates = [];
    private readonly TextBox[] _machineNameInputs = new TextBox[MachineCount];
    private readonly TextBox[] _machineIpInputs = new TextBox[MachineCount];
    private readonly Dictionary<string, Label> _detailValueLabels = [];

    private readonly string _collectorPath;

    private AppSettings _settings;
    private bool _monitoringActive;
    private bool _suppressSelectorEvents;
    private int _selectedSlotIndex;

    private NumericUpDown _portInput = null!;
    private NumericUpDown _intervalInput = null!;
    private ComboBox _controllerTypeInput = null!;
    private CheckBox _autoStartCheckbox = null!;
    private ComboBox _detailMachineSelector = null!;
    private DataGridView _axisGrid = null!;
    private DataGridView _historyGrid = null!;
    private Label _detailMachineTitle = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private Label _configuredSummaryValue = null!;
    private Label _liveSummaryValue = null!;
    private Label _oeeSummaryValue = null!;
    private Label _alarmSummaryValue = null!;
    private SplitContainer _contentSplit = null!;
    private SplitContainer _detailSplit = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;
    private Button _saveButton = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _saveMrrButton = null!;
    private NumericUpDown _widthOfCutInput = null!;
    private NumericUpDown _depthOfCutInput = null!;
    private NumericUpDown _targetMrrInput = null!;
    private NumericUpDown _qualityInput = null!;
    private GaugeDialControl _oeeGauge = null!;
    private GaugeDialControl _availabilityGauge = null!;
    private GaugeDialControl _performanceGauge = null!;
    private GaugeDialControl _mrrGauge = null!;

    public MainForm()
    {
        _settings = SettingsStore.LoadOrDefault();
        _collectorPath = ResolveCollectorPath();

        ConfigureWindow();
        BuildInterface();
        LoadSettingsIntoControls(_settings);
        RebuildRuntimeStates();
        RefreshSelectorItems();
        RefreshDashboard();
        RefreshDetailPanel();
        UpdateStatus(File.Exists(_collectorPath)
            ? "Dashboard ready. Enter machine IP addresses and start monitoring."
            : "Collector executable is missing. Build or publish the desktop package first.");

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshDashboard();
            RefreshDetailPanel();
        };
        _refreshTimer.Start();

        Shown += HandleShown;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refreshTimer.Stop();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        base.OnFormClosing(e);
    }

    private void ConfigureWindow()
    {
        Text = "Mitsubishi CNC Multi-Machine Dashboard";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 820);
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(243, 240, 233);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
    }
}
