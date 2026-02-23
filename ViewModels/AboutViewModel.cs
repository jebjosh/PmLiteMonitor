using CommunityToolkit.Mvvm.ComponentModel;
using PmLiteMonitor.Messages;

namespace PmLiteMonitor.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private string _firmwareVersion = "-";
    [ObservableProperty] private string _firmwareSerial  = "-";
    [ObservableProperty] private string _firmwareBuild   = "-";
    [ObservableProperty] private string _firmwareDate    = "-";
    [ObservableProperty] private string _firmwareTime    = "-";

    /// <summary>
    /// Called by MainViewModel whenever a StatusMessage is received.
    /// Updates the About window live if it's open.
    /// </summary>
    public void ApplyStatus(StatusMessage s)
    {
        FirmwareVersion = string.IsNullOrWhiteSpace(s.FirmwareVersion) ? "-" : s.FirmwareVersion;
        FirmwareSerial  = string.IsNullOrWhiteSpace(s.FirmwareSerial)  ? "-" : s.FirmwareSerial;
        FirmwareBuild   = string.IsNullOrWhiteSpace(s.FirmwareBuild)   ? "-" : s.FirmwareBuild;
        FirmwareDate    = string.IsNullOrWhiteSpace(s.FirmwareDate)    ? "-" : s.FirmwareDate;
        FirmwareTime    = string.IsNullOrWhiteSpace(s.FirmwareTime)    ? "-" : s.FirmwareTime;
    }
}
