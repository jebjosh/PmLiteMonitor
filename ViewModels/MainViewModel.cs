using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PmLiteMonitor.Controls;
using PmLiteMonitor.Messages;
using PmLiteMonitor.Networking;
using PmLiteMonitor.Services;
using WinForms = System.Windows.Forms;

namespace PmLiteMonitor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Shared TCP client (also used by ConfigViewModel) ────────────────────
    public TcpMessageClient Client { get; } = new();

    private readonly TelemetryRecorder _recorder = new();

    /// <summary>Shared across MainWindow and AboutWindow.</summary>
    public AboutViewModel AboutVm { get; } = new();

    // ── Connection ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _systemState   = "Disconnected";
    [ObservableProperty] private string _pmLiteMode    = "Unknown";
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private string _endpoint      = "—";

    // ── Missile ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _missileType      = "Unknown";
    [ObservableProperty] private string _missileFrequency = "0";
    [ObservableProperty] private string _missileAddress   = "0";
    [ObservableProperty] private IconState _tcpIconState       = IconState.Red;
    [ObservableProperty] private IconState _missileContState   = IconState.Gray;
    [ObservableProperty] private IconState _mrrState           = IconState.Gray;

    // ── Power ────────────────────────────────────────────────────────────────
    [ObservableProperty] private float     _itmValue;
    [ObservableProperty] private float     _mbcValue;
    [ObservableProperty] private float     _clockValue;
    [ObservableProperty] private float     _twtValue;
    [ObservableProperty] private float     _gyroPowerValue;
    [ObservableProperty] private IconState _itmState   = IconState.Gray;
    [ObservableProperty] private IconState _mbcState   = IconState.Gray;
    [ObservableProperty] private IconState _clockState = IconState.Gray;
    [ObservableProperty] private IconState _twtState   = IconState.Gray;
    [ObservableProperty] private IconState _gyroState  = IconState.Gray;

    // ── Temperature ──────────────────────────────────────────────────────────
    [ObservableProperty] private float     _tvmTemp;
    [ObservableProperty] private float     _mmpTemp;
    [ObservableProperty] private float     _plloTemp;
    [ObservableProperty] private float     _delayTemp;
    [ObservableProperty] private float     _gyroTemp;
    [ObservableProperty] private float     _isaTemp;
    [ObservableProperty] private float     _casTemp;

    [ObservableProperty] private IconState _tvmTempState   = IconState.Gray;
    [ObservableProperty] private IconState _mmpTempState   = IconState.Gray;
    [ObservableProperty] private IconState _plloTempState  = IconState.Gray;
    [ObservableProperty] private IconState _delayTempState = IconState.Gray;
    [ObservableProperty] private IconState _gyroTempState  = IconState.Gray;
    [ObservableProperty] private IconState _isaTempState   = IconState.Gray;
    [ObservableProperty] private IconState _casTempState   = IconState.Gray;

    [ObservableProperty] private IconState _tvmHtrState   = IconState.Gray;
    [ObservableProperty] private IconState _mmpHtrState   = IconState.Gray;
    [ObservableProperty] private IconState _plloHtrState  = IconState.Gray;
    [ObservableProperty] private IconState _delayHtrState = IconState.Gray;
    [ObservableProperty] private IconState _gyroHtrState  = IconState.Gray;
    [ObservableProperty] private IconState _isaHtrState   = IconState.Gray;
    [ObservableProperty] private IconState _casHtrState   = IconState.Gray;

    [ObservableProperty] private string _tvmHtrText   = "OFF";
    [ObservableProperty] private string _mmpHtrText   = "OFF";
    [ObservableProperty] private string _plloHtrText  = "OFF";
    [ObservableProperty] private string _delayHtrText = "OFF";
    [ObservableProperty] private string _gyroHtrText  = "OFF";
    [ObservableProperty] private string _isaHtrText   = "OFF";
    [ObservableProperty] private string _casHtrText   = "OFF";

    // ── Launch Sequence ──────────────────────────────────────────────────────
    [ObservableProperty] private IconState _mrsIconState = IconState.Gray;
    [ObservableProperty] private IconState _itlIconState = IconState.Gray;
    [ObservableProperty] private IconState _slsIconState = IconState.Gray;
    [ObservableProperty] private IconState _mirIconState = IconState.Gray;
    [ObservableProperty] private IconState _awyIconState = IconState.Gray;
    [ObservableProperty] private IconState _lsfIconState = IconState.Gray;

    [ObservableProperty] private string _mrsText  = "OFF";
    [ObservableProperty] private string _itlText  = "OFF";
    [ObservableProperty] private string _slsText  = "OFF";
    [ObservableProperty] private string _mirText  = "OFF";
    [ObservableProperty] private string _awyText  = "OFF";
    [ObservableProperty] private string _lsfText  = "PASS";
    [ObservableProperty] private string _mdcText  = "MDC: 0 0 0 0 0 0 0 0 0 0 0 0 0, Validity: Invalid";
    [ObservableProperty] private string _octalText= "Octal: 0 0 0 0 0 0 0 0 0 0 0 0 0";

    // ── Safe & Arming ────────────────────────────────────────────────────────
    [ObservableProperty] private float     _chrgCmd;
    [ObservableProperty] private float     _pafu;
    [ObservableProperty] private float     _fiveVdc;
    [ObservableProperty] private float     _condition;
    [ObservableProperty] private float     _leftCap;
    [ObservableProperty] private float     _leftLatch;
    [ObservableProperty] private float     _rightCap;
    [ObservableProperty] private float     _right;
    [ObservableProperty] private IconState _saChrgState  = IconState.Gray;
    [ObservableProperty] private IconState _saPafuState  = IconState.Gray;
    [ObservableProperty] private IconState _sa5VdcState  = IconState.Gray;

    // ── One Shots ────────────────────────────────────────────────────────────
    [ObservableProperty] private float     _oneTvm;
    [ObservableProperty] private float     _oneMmp;
    [ObservableProperty] private float     _oneCas;
    [ObservableProperty] private float     _oneGas;
    [ObservableProperty] private float     _onePafu;
    [ObservableProperty] private IconState _oneTvmState  = IconState.Blue;
    [ObservableProperty] private IconState _oneMmpState  = IconState.Blue;
    [ObservableProperty] private IconState _oneCasState  = IconState.Blue;
    [ObservableProperty] private IconState _oneGasState  = IconState.Blue;
    [ObservableProperty] private IconState _onePafuState = IconState.Blue;

    // ── Recorder ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _recordOutputPath;
    [ObservableProperty] private bool   _isRecording;
    [ObservableProperty] private string _recordStatus = "Idle — waiting for MRS ON";

    // ── Log ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _eventLog = string.Empty;
    [ObservableProperty] private bool   _logRaw;

    // ── Events raised for ConfigWindow ────────────────────────────────────────
    public event Action<LoopbackMessage>? LoopbackReceived;
    public event Action<StatusMessage>?   StatusReceived;

    // ── Constructor ──────────────────────────────────────────────────────────
    public MainViewModel()
    {
        _recordOutputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PmLiteCaptures");

        _recorder.OutputPath = _recordOutputPath;

        _recorder.OnRecordingComplete += msg =>
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                IsRecording  = false;
                RecordStatus = $"✔ {msg}";
                AppendLog($"[RECORD] {msg}");
            });

        _recorder.OnStatusMessage += msg =>
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                IsRecording  = _recorder.IsRecording;
                RecordStatus = msg;
                AppendLog($"[RECORD] {msg}");
            });

        Client.OnMessageReceived += OnMessageReceived;
        Client.OnWarning         += msg => AppendLog($"[WARN]  {msg}");
        Client.OnError           += ex  => AppendLog($"[ERROR] {ex.Message}");
        Client.OnDisconnected    += ()  =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                SystemState  = "Disconnected";
                IsConnected  = false;
                TcpIconState = IconState.Red;
            });
        };
    }

    // ── Message dispatch ─────────────────────────────────────────────────────
    private void OnMessageReceived(IMessage message)
    {
        if (LogRaw)
            AppendLog($"[RAW]  type={message.MessageType} size={message.Size} " +
                      $"data={BitConverter.ToString(message.RawData)}");

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (message)
            {
                case TelemetryMessage t:
                    ApplyTelemetry(t);
                    _recorder.Feed(t);
                    IsRecording = _recorder.IsRecording;
                    break;
                case StatusMessage s:
                    ApplyStatus(s);
                    StatusReceived?.Invoke(s);
                    break;
                case DebugMessage d:
                    AppendLog($"[DEBUG] {d.Text}");
                    break;
                case LoopbackMessage lb:
                    LoopbackReceived?.Invoke(lb);
                    break;
                case InvalidMessage inv:
                    AppendLog($"[INVALID] {inv.Reason}");
                    break;
            }
        });
    }

    // ── Telemetry → ViewModel properties ────────────────────────────────────
    /// <summary>
    /// Map parsed TelemetryMessage fields to display properties.
    /// Add your own icon-color thresholds here (e.g. Itm > 0 → Green).
    /// </summary>
    private void ApplyTelemetry(TelemetryMessage t)
    {
        // Power
        ItmValue       = t.Itm;
        MbcValue       = t.Mbc;
        ClockValue     = t.Clock;
        TwtValue       = t.Twt;
        GyroPowerValue = t.Gyro;

        // --- Icon state examples (set your own thresholds) ---
        // ItmState = t.Itm > 0f ? IconState.Green : IconState.Red;

        // Temperature
        TvmTemp   = t.TvmTemp;
        MmpTemp   = t.MmpTemp;
        PlloTemp  = t.PlloTemp;
        DelayTemp = t.DelayTemp;
        GyroTemp  = t.GyroTemp;
        IsaTemp   = t.IsaTemp;
        CasTemp   = t.CasTemp;

        // HTR
        TvmHtrState   = t.TvmHtr   ? IconState.Green : IconState.Red;
        MmpHtrState   = t.MmpHtr   ? IconState.Green : IconState.Red;
        PlloHtrState  = t.PlloHtr  ? IconState.Green : IconState.Red;
        DelayHtrState = t.DelayHtr ? IconState.Green : IconState.Red;
        GyroHtrState  = t.GyroHtr  ? IconState.Green : IconState.Red;
        IsaHtrState   = t.IsaHtr   ? IconState.Green : IconState.Red;
        CasHtrState   = t.CasHtr   ? IconState.Green : IconState.Red;

        TvmHtrText   = t.TvmHtr   ? "ON" : "OFF";
        MmpHtrText   = t.MmpHtr   ? "ON" : "OFF";
        PlloHtrText  = t.PlloHtr  ? "ON" : "OFF";
        DelayHtrText = t.DelayHtr ? "ON" : "OFF";
        GyroHtrText  = t.GyroHtr  ? "ON" : "OFF";
        IsaHtrText   = t.IsaHtr   ? "ON" : "OFF";
        CasHtrText   = t.CasHtr   ? "ON" : "OFF";

        // Launch sequence
        MrsText     = t.MrsText;
        ItlText     = t.ItlText;
        SlsText     = t.SlsText;
        MirText     = t.MirText;
        AwyText     = t.AwyText;
        LsfText     = t.LsfText;

        MrsIconState = t.MrsOn ? IconState.Red  : IconState.Gray;
        ItlIconState = t.ItlOn ? IconState.Blue : IconState.Gray;
        SlsIconState = t.SlsOn ? IconState.Blue : IconState.Gray;
        MirIconState = t.MirOn ? IconState.Blue : IconState.Gray;
        AwyIconState = t.AwyOn ? IconState.Blue : IconState.Gray;
        LsfIconState = t.LsfOn ? IconState.Green : IconState.Gray;

        // Safe & Arming
        ChrgCmd   = t.ChrgCmd;
        Pafu      = t.Pafu;
        FiveVdc   = t.FiveVdc;
        Condition = t.Condition;
        LeftCap   = t.LeftCap;
        LeftLatch = t.LeftLatch;
        RightCap  = t.RightCap;
        Right     = t.Right;

        // One Shots
        OneTvm  = t.OneTvm;
        OneMmp  = t.OneMmp;
        OneCas  = t.OneCas;
        OneGas  = t.OneGas;
        OnePafu = t.OnePafu;
    }

    private void ApplyStatus(StatusMessage s)
    {
        PmLiteMode = s.PmLiteMode;
        AboutVm.ApplyStatus(s);
        AppendLog($"[STATUS] FW: {s.FirmwareVersion} | Build: {s.FirmwareBuild} | Mode: {s.PmLiteMode}");
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    public async Task ConnectAsync(string endpoint)
    {
        var parts = endpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int port))
        {
            AppendLog("[CONNECT] Bad format — use ip:port"); return;
        }
        try
        {
            await Client.ConnectAsync(parts[0].Trim(), port);
            SystemState  = "Connected";
            IsConnected  = true;
            Endpoint     = endpoint;
            TcpIconState = IconState.Green;
            AppendLog($"[CONNECT] Connected to {endpoint}");
        }
        catch (Exception ex)
        {
            AppendLog($"[CONNECT FAIL] {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        await Client.DisconnectAsync();
        SystemState  = "Disconnected";
        IsConnected  = false;
        TcpIconState = IconState.Red;
    }

    [RelayCommand]
    private void BrowseOutputPath()
    {
        var dlg = new WinForms.FolderBrowserDialog
        {
            Description  = "Select telemetry capture output folder",
            SelectedPath = RecordOutputPath
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            RecordOutputPath     = dlg.SelectedPath;
            _recorder.OutputPath = dlg.SelectedPath;
        }
    }

    [RelayCommand]
    private void ClearLog() => EventLog = string.Empty;

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void AppendLog(string line)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            EventLog += $"{DateTime.Now:HH:mm:ss.fff}  {line}\n");
    }

    // Partial property change — update recorder output path when it changes in the UI
    partial void OnRecordOutputPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _recorder.OutputPath = value;
    }
}
