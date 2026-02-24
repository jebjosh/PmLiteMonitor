using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PmLiteMonitor.Controls;
using PmLiteMonitor.Messages;
using PmLiteMonitor.Networking;

namespace PmLiteMonitor.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly TcpMessageClient _client;
    private readonly MainViewModel    _mainVm;

    // ── TCP ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string    _ipAddress        = "10.138.203.86";
    [ObservableProperty] private string    _portText         = "1024";
    [ObservableProperty] private bool      _isConnected;
    [ObservableProperty] private string    _connectionStatus = "Disconnected";
    [ObservableProperty] private IconState _connIconState    = IconState.Red;
    [ObservableProperty] private string    _ipError          = string.Empty;
    [ObservableProperty] private string    _portError        = string.Empty;

    // ── Configuration Message ─────────────────────────────────────────────────
    [ObservableProperty] private bool   _pacFlight;
    [ObservableProperty] private bool   _pacMissile;
    [ObservableProperty] private bool   _termSafed;
    [ObservableProperty] private bool   _termArmed;
    [ObservableProperty] private bool   _dudOverride;
    [ObservableProperty] private bool   _resetFlag;
    [ObservableProperty] private string _configSendStatus = "—";

    partial void OnPacFlightChanged(bool value)  { if (value) PacMissile = false; }
    partial void OnPacMissileChanged(bool value) { if (value) PacFlight  = false; }
    partial void OnTermSafedChanged(bool value)  { if (value) TermArmed  = false; }
    partial void OnTermArmedChanged(bool value)  { if (value) TermSafed  = false; }

    // ── Loopback — sent values ────────────────────────────────────────────────
    // Accepts decimal 0–255 OR hex with/without 0x prefix e.g. FF, 0xFF, 255
    [ObservableProperty] private string _lbCountText = "0";
    [ObservableProperty] private string _lbData0Text = "0";
    [ObservableProperty] private string _lbData1Text = "0";
    [ObservableProperty] private string _lbData2Text = "0";
    [ObservableProperty] private string _lbData3Text = "0";

    // Per-field validation errors — bind these to a red TextBlock under each TextBox
    [ObservableProperty] private string _lbCountError = string.Empty;
    [ObservableProperty] private string _lbData0Error = string.Empty;
    [ObservableProperty] private string _lbData1Error = string.Empty;
    [ObservableProperty] private string _lbData2Error = string.Empty;
    [ObservableProperty] private string _lbData3Error = string.Empty;

    // Countdown display — separate read-only field so we don't clobber user input
    [ObservableProperty] private string _lbCountRemaining  = string.Empty;
    [ObservableProperty] private bool   _isLoopbackRunning;
    [ObservableProperty] private string _loopbackSendStatus = "—";

    // ── Loopback — received values ────────────────────────────────────────────
    [ObservableProperty] private string    _lbRxCount = "—";
    [ObservableProperty] private string    _lbRxData0 = "—";
    [ObservableProperty] private string    _lbRxData1 = "—";
    [ObservableProperty] private string    _lbRxData2 = "—";
    [ObservableProperty] private string    _lbRxData3 = "—";
    [ObservableProperty] private IconState _cmpCountIcon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData0Icon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData1Icon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData2Icon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData3Icon = IconState.Gray;

    // ── Status Request ────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _statusDataType;
    [ObservableProperty] private string _statusSendStatus = "—";

    // ── Firmware info ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _fwVersion = "-";
    [ObservableProperty] private string _fwSerial  = "-";
    [ObservableProperty] private string _fwBuild   = "-";
    [ObservableProperty] private string _fwDate    = "-";
    [ObservableProperty] private string _fwTime    = "-";

    // ── Ping ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private string    _pingAddress   = "10.138.129.16";
    [ObservableProperty] private string    _pingStatus    = "—";
    [ObservableProperty] private IconState _pingIconState = IconState.Gray;

    // ── Debug output ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _debugOutput = string.Empty;

    // ── Loopback state ────────────────────────────────────────────────────────
    private CancellationTokenSource?               _loopbackCts;

    // TCS used to hand a received LoopbackMessage back to the waiting send loop.
    // The loop sets this before each send, then awaits its Task.
    // ApplyLoopback calls TrySetResult() to unblock the loop.
    private TaskCompletionSource<LoopbackMessage>? _loopbackResponseTcs;

    // Per-run statistics shown in the UI
    [ObservableProperty] private int    _lbSent;       // how many were sent
    [ObservableProperty] private int    _lbPassCount;  // how many echoed back correctly
    [ObservableProperty] private int    _lbFailCount;  // how many mismatched or timed out
    [ObservableProperty] private string _lbStats = string.Empty;

    // ── Constructor ───────────────────────────────────────────────────────────
    public ConfigViewModel(TcpMessageClient client, MainViewModel mainVm)
    {
        _client = client;
        _mainVm = mainVm;

        mainVm.LoopbackReceived += ApplyLoopback;
        mainVm.StatusReceived   += ApplyStatus;

        IsConnected      = mainVm.IsConnected;
        ConnectionStatus = mainVm.SystemState;
        ConnIconState    = mainVm.IsConnected ? IconState.Green : IconState.Red;

        // Run initial validation so errors show immediately on open
        ValidateIp(IpAddress);
        ValidatePort(PortText);
    }

    // ── Live validation — fires on every keystroke via OnXxxChanged ───────────

    partial void OnIpAddressChanged(string value)  => ValidateIp(value);
    partial void OnPortTextChanged(string value)   => ValidatePort(value);

    // Loopback fields — validate and re-evaluate Send button
    partial void OnLbCountTextChanged(string value)
    {
        LbCountError = ByteError(value, "Count");
        SendLoopbackCommand.NotifyCanExecuteChanged();
    }
    partial void OnLbData0TextChanged(string value)
    {
        LbData0Error = ByteError(value, "Data 0");
        SendLoopbackCommand.NotifyCanExecuteChanged();
    }
    partial void OnLbData1TextChanged(string value)
    {
        LbData1Error = ByteError(value, "Data 1");
        SendLoopbackCommand.NotifyCanExecuteChanged();
    }
    partial void OnLbData2TextChanged(string value)
    {
        LbData2Error = ByteError(value, "Data 2");
        SendLoopbackCommand.NotifyCanExecuteChanged();
    }
    partial void OnLbData3TextChanged(string value)
    {
        LbData3Error = ByteError(value, "Data 3");
        SendLoopbackCommand.NotifyCanExecuteChanged();
    }

    private void ValidateIp(string value)
    {
        // Valid IPv4: four octets each 0-255 separated by dots
        var parts = (value ?? string.Empty).Trim().Split('.');
        bool ok   = parts.Length == 4 &&
                    parts.All(p => int.TryParse(p, out int n) && n >= 0 && n <= 255);
        IpError = ok ? string.Empty : "Enter a valid IPv4 address  (e.g. 192.168.1.1)";
        ConnectCommand.NotifyCanExecuteChanged();
    }

    private void ValidatePort(string value)
    {
        bool ok = int.TryParse((value ?? string.Empty).Trim(), out int p) && p >= 1 && p <= 65535;
        PortError = ok ? string.Empty : "Port must be 1 – 65535";
        ConnectCommand.NotifyCanExecuteChanged();
    }

    // Returns an error string or empty if valid.
    // Used both for the error label binding and for CanExecute checks.
    private static string ByteError(string text, string field)
        => TryParseByte(text, out _) ? string.Empty : $"{field}: enter 0–255 or hex 00–FF";

    // ── Connect / Disconnect ──────────────────────────────────────────────────

    // CanExecute prevents the button activating with invalid input
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        int.TryParse(PortText.Trim(), out int port);
        try
        {
            await _mainVm.ConnectAsync($"{IpAddress.Trim()}:{port}");
            IsConnected      = true;
            ConnectionStatus = "Connected";
            ConnIconState    = IconState.Green;
        }
        catch (Exception ex)
        {
            ConnectionStatus = "Failed";
            AppendDebug($"Connect error: {ex.Message}");
        }
        ConnectCommand.NotifyCanExecuteChanged();
    }

    private bool CanConnect() =>
        !IsConnected &&
        string.IsNullOrEmpty(IpError) &&
        string.IsNullOrEmpty(PortError) &&
        !string.IsNullOrWhiteSpace(IpAddress) &&
        !string.IsNullOrWhiteSpace(PortText);

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        StopLoopback();   // cancel any running countdown first
        await _mainVm.DisconnectAsync();
        IsConnected      = false;
        ConnectionStatus = "Disconnected";
        ConnIconState    = IconState.Red;
        ConnectCommand.NotifyCanExecuteChanged();
    }

    // ── Loopback request-response loop ───────────────────────────────────────
    // Each iteration:
    //   1. Send LoopbackMessage with current remaining count
    //   2. Await the server's echo (up to ResponseTimeoutMs)
    //   3. Compare every field — count as pass or fail
    //   4. Decrement remaining and repeat
    //
    // ApplyLoopback is called by the MainViewModel event on whatever thread
    // the TCP receive loop uses. It calls TrySetResult on the TCS that the
    // send loop is awaiting — bridging the two threads safely.

    private const int ResponseTimeoutMs = 2000;  // how long to wait for each echo

    [RelayCommand(CanExecute = nameof(CanSendLoopback))]
    private async Task SendLoopbackAsync()
    {
        if (!TryParseByte(LbCountText, out byte totalCount) ||
            !TryParseByte(LbData0Text, out byte d0)         ||
            !TryParseByte(LbData1Text, out byte d1)         ||
            !TryParseByte(LbData2Text, out byte d2)         ||
            !TryParseByte(LbData3Text, out byte d3))
        {
            LoopbackSendStatus = "Fix errors above before sending";
            return;
        }

        // Reset statistics for this run
        LbSent      = 0;
        LbPassCount = 0;
        LbFailCount = 0;
        LbStats     = string.Empty;

        _loopbackCts      = new CancellationTokenSource();
        IsLoopbackRunning = true;
        LoopbackSendStatus = $"Waiting for echo…";
        SendLoopbackCommand.NotifyCanExecuteChanged();
        StopLoopbackCommand.NotifyCanExecuteChanged();

        try
        {
            for (byte remaining = totalCount; ; remaining--)
            {
                _loopbackCts.Token.ThrowIfCancellationRequested();

                // ── 1. Arm the TCS BEFORE sending so we never miss the response ──
                // If we armed it after sending, a very fast server could reply
                // before we're listening and the response would be lost.
                _loopbackResponseTcs = new TaskCompletionSource<LoopbackMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                // ── 2. Send ───────────────────────────────────────────────────────
                await _client.SendAsync(
                    new LoopbackMessage(remaining, d0, d1, d2, d3),
                    _loopbackCts.Token);
                LbSent++;

                LbCountRemaining   = remaining == 0 ? "Waiting for final echo…"
                                                    : $"{remaining} remaining — waiting for echo…";
                LoopbackSendStatus = $"Sent #{LbSent} (count={remaining}) — awaiting response";

                // ── 3. Await echo with timeout ────────────────────────────────────
                // Task.WhenAny lets us race the TCS against a timeout task.
                // We also register the cancellation token so Stop() unblocks us.
                using var timeoutCts = CancellationTokenSource
                    .CreateLinkedTokenSource(_loopbackCts.Token);
                timeoutCts.CancelAfter(ResponseTimeoutMs);

                LoopbackMessage? response = null;
                try
                {
                    // Register cancellation so the TCS task unblocks when stopped
                    timeoutCts.Token.Register(() =>
                        _loopbackResponseTcs?.TrySetCanceled());

                    response = await _loopbackResponseTcs.Task;
                }
                catch (OperationCanceledException)
                {
                    // Check if it was our main stop token or just the timeout
                    _loopbackCts.Token.ThrowIfCancellationRequested();

                    // Timeout — count as fail and continue
                    LbFailCount++;
                    LbStats = $"Pass: {LbPassCount}  Fail: {LbFailCount}  Sent: {LbSent}";
                    AppendDebug($"LB #{LbSent}: TIMEOUT (count={remaining})");
                    LoopbackSendStatus = $"#{LbSent} timed out after {ResponseTimeoutMs}ms";

                    if (remaining == 0) break;
                    continue;
                }
                finally
                {
                    _loopbackResponseTcs = null;
                }

                // ── 4. Compare echoed fields against what was sent ─────────────────
                // Count field echoes the remaining value we sent, not totalCount
                bool countMatch = response.Count == remaining;
                bool d0Match    = response.Data0 == d0;
                bool d1Match    = response.Data1 == d1;
                bool d2Match    = response.Data2 == d2;
                bool d3Match    = response.Data3 == d3;
                bool allMatch   = countMatch && d0Match && d1Match && d2Match && d3Match;

                if (allMatch) LbPassCount++;
                else          LbFailCount++;

                LbStats = $"Pass: {LbPassCount}  Fail: {LbFailCount}  Sent: {LbSent}";

                string result = allMatch ? "PASS ✔" : "FAIL ✗";
                LoopbackSendStatus = $"#{LbSent} {result}  |  {LbStats}";
                AppendDebug($"LB #{LbSent} {result}: cnt={response.Count}({(countMatch?"✔":"✗")}) " +
                            $"d0=0x{response.Data0:X2}({(d0Match?"✔":"✗")}) " +
                            $"d1=0x{response.Data1:X2}({(d1Match?"✔":"✗")}) " +
                            $"d2=0x{response.Data2:X2}({(d2Match?"✔":"✗")}) " +
                            $"d3=0x{response.Data3:X2}({(d3Match?"✔":"✗")})");

                if (remaining == 0) break;
            }

            // ── Final summary ─────────────────────────────────────────────────
            LbCountRemaining   = "Done";
            LoopbackSendStatus = $"Complete — {LbPassCount}/{LbSent} passed";
            LbStats            = $"Pass: {LbPassCount}  Fail: {LbFailCount}  Sent: {LbSent}";
            AppendDebug($"Loopback complete — {LbPassCount}/{LbSent} passed, {LbFailCount} failed");
        }
        catch (OperationCanceledException)
        {
            LoopbackSendStatus = $"Stopped — {LbPassCount}/{LbSent} passed";
            LbCountRemaining   = string.Empty;
        }
        catch (Exception ex)
        {
            LoopbackSendStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _loopbackResponseTcs = null;
            IsLoopbackRunning    = false;
            _loopbackCts?.Dispose();
            _loopbackCts = null;
            SendLoopbackCommand.NotifyCanExecuteChanged();
            StopLoopbackCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSendLoopback() =>
        !IsLoopbackRunning                &&
        TryParseByte(LbCountText, out _)  &&
        TryParseByte(LbData0Text, out _)  &&
        TryParseByte(LbData1Text, out _)  &&
        TryParseByte(LbData2Text, out _)  &&
        TryParseByte(LbData3Text, out _);

    [RelayCommand(CanExecute = nameof(CanStopLoopback))]
    private void StopLoopback() => _loopbackCts?.Cancel();

    private bool CanStopLoopback() => IsLoopbackRunning;

    private void ApplyLoopback(LoopbackMessage lb)
    {
        // ── Always update the received row and compare icons ──────────────────
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LbRxCount = lb.Count.ToString();
            LbRxData0 = $"0x{lb.Data0:X2}";
            LbRxData1 = $"0x{lb.Data1:X2}";
            LbRxData2 = $"0x{lb.Data2:X2}";
            LbRxData3 = $"0x{lb.Data3:X2}";

            TryParseByte(LbCountText, out byte sc);
            TryParseByte(LbData0Text, out byte s0);
            TryParseByte(LbData1Text, out byte s1);
            TryParseByte(LbData2Text, out byte s2);
            TryParseByte(LbData3Text, out byte s3);

            CmpCountIcon = lb.Count == sc ? IconState.Green : IconState.Red;
            CmpData0Icon = lb.Data0 == s0 ? IconState.Green : IconState.Red;
            CmpData1Icon = lb.Data1 == s1 ? IconState.Green : IconState.Red;
            CmpData2Icon = lb.Data2 == s2 ? IconState.Green : IconState.Red;
            CmpData3Icon = lb.Data3 == s3 ? IconState.Green : IconState.Red;
        });

        // ── Unblock the send loop if it is waiting for this response ──────────
        // TrySetResult is thread-safe and a no-op if the TCS is already completed.
        // This means stray loopback messages received outside a run are harmless.
        _loopbackResponseTcs?.TrySetResult(lb);
    }

    // ── Config Message ────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task SendConfigAsync()
    {
        try
        {
            await _client.SendAsync(new ConfigurationMessage(
                PacFlight, PacMissile, TermSafed, TermArmed, DudOverride, ResetFlag));
            ConfigSendStatus = "Sent ✔";
        }
        catch (Exception ex) { ConfigSendStatus = $"Error: {ex.Message}"; }
    }

    // ── Status Request ────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task SendStatusRequestAsync()
    {
        try
        {
            await _client.SendAsync(new RequestMessage(MessageTypes.Status));
            StatusSendStatus = "Sent ✔";
        }
        catch (Exception ex) { StatusSendStatus = $"Error: {ex.Message}"; }
    }

    private void ApplyStatus(StatusMessage s)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            FwVersion = s.FirmwareVersion;
            FwSerial  = s.FirmwareSerial;
            FwBuild   = s.FirmwareBuild;
            FwDate    = s.FirmwareDate;
            FwTime    = s.FirmwareTime;
            AppendDebug($"Status RX — FW: {s.FirmwareVersion} Mode: {s.PmLiteMode}");
        });
    }

    // ── Ping ─────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task PingAsync()
    {
        PingStatus    = "Pinging…";
        PingIconState = IconState.Gray;
        try
        {
            using var ping  = new Ping();
            var reply = await ping.SendPingAsync(PingAddress, 2000);
            bool ok   = reply.Status == IPStatus.Success;
            PingStatus    = ok ? $"OK  ({reply.RoundtripTime} ms)" : reply.Status.ToString();
            PingIconState = ok ? IconState.Green : IconState.Red;
        }
        catch (Exception ex)
        {
            PingStatus    = $"Error: {ex.Message}";
            PingIconState = IconState.Red;
        }
    }

    // ── Byte parser — decimal OR hex ─────────────────────────────────────────
    /// <summary>
    /// Accepts:
    ///   Decimal — "0", "255", "128"
    ///   Hex with prefix  — "0xFF", "0xA5", "0x0F"
    ///   Hex without prefix — "FF", "A5", "0F"  (any a-f character triggers hex mode)
    ///   All are validated to be within 0–255.
    /// </summary>
    public static bool TryParseByte(string? text, out byte value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        // 0x / 0X prefix — always hex
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(text[2..],
                System.Globalization.NumberStyles.HexNumber, null, out value);

        // Contains any a-f letter — treat whole string as hex
        if (text.Any(c => "abcdefABCDEF".Contains(c)))
            return byte.TryParse(text,
                System.Globalization.NumberStyles.HexNumber, null, out value);

        // Pure decimal
        return byte.TryParse(text, out value);
    }

    private void AppendDebug(string line) =>
        DebugOutput += $"{DateTime.Now:HH:mm:ss.fff}  {line}\n";
}
