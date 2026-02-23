using System.Net.NetworkInformation;
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
    [ObservableProperty] private string _ipAddress        = "10.138.203.86";
    [ObservableProperty] private string _portText         = "1024";
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private IconState _connIconState = IconState.Red;

    // ── Configuration Message ────────────────────────────────────────────────
    [ObservableProperty] private bool   _pacFlight;
    [ObservableProperty] private bool   _pacMissile;
    [ObservableProperty] private bool   _termSafed;
    [ObservableProperty] private bool   _termArmed;
    [ObservableProperty] private bool   _dudOverride;
    [ObservableProperty] private bool   _resetFlag;
    [ObservableProperty] private string _configSendStatus = "—";

    // ── Loopback — sent values ────────────────────────────────────────────────
    [ObservableProperty] private string _lbCountText = "0";
    [ObservableProperty] private string _lbData0Text = "0";
    [ObservableProperty] private string _lbData1Text = "0";
    [ObservableProperty] private string _lbData2Text = "0";
    [ObservableProperty] private string _lbData3Text = "0";

    // ── Loopback — received values ────────────────────────────────────────────
    [ObservableProperty] private string _lbRxCount = "—";
    [ObservableProperty] private string _lbRxData0 = "—";
    [ObservableProperty] private string _lbRxData1 = "—";
    [ObservableProperty] private string _lbRxData2 = "—";
    [ObservableProperty] private string _lbRxData3 = "—";

    // ── Loopback — compare (icon per column) ─────────────────────────────────
    [ObservableProperty] private IconState _cmpCountIcon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData0Icon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData1Icon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData2Icon = IconState.Gray;
    [ObservableProperty] private IconState _cmpData3Icon = IconState.Gray;

    [ObservableProperty] private string _loopbackSendStatus = "—";

    // ── Status Request ────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _statusDataType;
    [ObservableProperty] private string _statusSendStatus = "—";

    // ── Firmware info (populated from StatusMessage) ──────────────────────────
    [ObservableProperty] private string _fwVersion = "-";
    [ObservableProperty] private string _fwSerial  = "-";
    [ObservableProperty] private string _fwBuild   = "-";
    [ObservableProperty] private string _fwDate    = "-";
    [ObservableProperty] private string _fwTime    = "-";

    // ── Ping ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _pingAddress = "10.138.129.16";
    [ObservableProperty] private string _pingStatus  = "—";
    [ObservableProperty] private IconState _pingIconState = IconState.Gray;

    // ── Debug output ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _debugOutput = string.Empty;

    public ConfigViewModel(TcpMessageClient client, MainViewModel mainVm)
    {
        _client = client;
        _mainVm = mainVm;

        // Subscribe to events forwarded from MainViewModel
        mainVm.LoopbackReceived += ApplyLoopback;
        mainVm.StatusReceived   += ApplyStatus;

        // Reflect main connection state
        IsConnected       = mainVm.IsConnected;
        ConnectionStatus  = mainVm.SystemState;
        ConnIconState     = mainVm.IsConnected ? IconState.Green : IconState.Red;
    }

    // ── Connect ───────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!int.TryParse(PortText, out int port)) { AppendDebug("Invalid port."); return; }
        try
        {
            await _mainVm.ConnectAsync($"{IpAddress}:{port}");
            IsConnected      = true;
            ConnectionStatus = "Connected";
            ConnIconState    = IconState.Green;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Failed";
            AppendDebug($"Connect error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _mainVm.DisconnectAsync();
        IsConnected      = false;
        ConnectionStatus = "Disconnected";
        ConnIconState    = IconState.Red;
    }

    // ── Loopback ──────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task SendLoopbackAsync()
    {
        if (!TryParseByte(LbCountText, out byte count) ||
            !TryParseByte(LbData0Text, out byte d0)    ||
            !TryParseByte(LbData1Text, out byte d1)    ||
            !TryParseByte(LbData2Text, out byte d2)    ||
            !TryParseByte(LbData3Text, out byte d3))
        {
            LoopbackSendStatus = "Bad values (0–255)";
            return;
        }
        try
        {
            await _client.SendAsync(new LoopbackMessage(count, d0, d1, d2, d3));
            LoopbackSendStatus = "Sent ✔";
        }
        catch (Exception ex) { LoopbackSendStatus = $"Error: {ex.Message}"; }
    }

    private void ApplyLoopback(LoopbackMessage lb)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LbRxCount = lb.Count.ToString();
            LbRxData0 = lb.Data0.ToString();
            LbRxData1 = lb.Data1.ToString();
            LbRxData2 = lb.Data2.ToString();
            LbRxData3 = lb.Data3.ToString();

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

            AppendDebug($"Loopback RX: cnt={lb.Count} d0={lb.Data0} d1={lb.Data1} d2={lb.Data2} d3={lb.Data3}");
        });
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
            AppendDebug($"Status received — FW: {s.FirmwareVersion} Mode: {s.PmLiteMode}");
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

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static bool TryParseByte(string text, out byte value) =>
        byte.TryParse(text?.Trim(), out value);

    private void AppendDebug(string line) =>
        DebugOutput += $"{DateTime.Now:HH:mm:ss.fff}  {line}\n";
}
