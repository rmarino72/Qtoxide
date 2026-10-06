using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qtoxide.Services;

namespace Qtoxide.ViewModels;

/// <summary>Profile, privacy, notifications, files and network settings.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly ProfileSession _session;
    private readonly AppServices _services;
    private readonly Action _close;

    public SettingsViewModel(MainViewModel main, ProfileSession session, AppServices services, Action close)
    {
        _main = main;
        _session = session;
        _services = services;
        _close = close;

        var s = session.Settings;
        _notifications = s.Notifications;
        _sendTyping = s.SendTypingNotifications;
        _downloadDirectory = s.DownloadDirectory;
        _useLanDiscovery = s.UseLanDiscovery;
        _useIpv6 = s.UseIpv6;
        _customNodes = string.Join(Environment.NewLine, s.CustomNodes);
        _qrCode = Images.QrCode("tox:" + main.ToxId);
    }

    public MainViewModel Main => _main;
    public bool HasPassword => _session.HasPassword;

    [ObservableProperty] private bool _notifications;
    [ObservableProperty] private bool _sendTyping;
    [ObservableProperty] private string _downloadDirectory;
    [ObservableProperty] private bool _useLanDiscovery;
    [ObservableProperty] private bool _useIpv6;
    [ObservableProperty] private string _customNodes;
    [ObservableProperty] private Bitmap _qrCode;
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newPasswordConfirm = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _isBusy;

    partial void OnNotificationsChanged(bool value) => Apply(s => s.Notifications = value);
    partial void OnSendTypingChanged(bool value) => Apply(s => s.SendTypingNotifications = value);
    partial void OnDownloadDirectoryChanged(string value) => Apply(s => s.DownloadDirectory = value);
    partial void OnUseLanDiscoveryChanged(bool value) => Apply(s => s.UseLanDiscovery = value, restart: true);
    partial void OnUseIpv6Changed(bool value) => Apply(s => s.UseIpv6 = value, restart: true);

    private void Apply(Action<ProfileSettings> change, bool restart = false)
    {
        change(_session.Settings);
        _session.SaveSettings();
        if (restart)
            Message = "Network changes take effect the next time you log in.";
    }

    [RelayCommand]
    private void SaveNodes()
    {
        var nodes = new List<BootstrapNode>();
        foreach (var line in CustomNodes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (BootstrapNode.Parse(line) is not { } node)
            {
                Message = $"Invalid node line: \"{line}\" (expected: host port public-key).";
                return;
            }
            nodes.Add(node);
        }
        _session.Settings.CustomNodes = nodes;
        _session.SaveSettings();
        _session.Bootstrap(nodes);
        Message = nodes.Count == 0 ? "Using only the public node list." : $"{nodes.Count} custom node(s) saved.";
    }

    [RelayCommand]
    private async Task BrowseDownloadsAsync()
    {
        if (await _services.Platform.PickFolderAsync() is { } folder)
            DownloadDirectory = folder;
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (NewPassword != NewPasswordConfirm)
        {
            Message = "The passwords do not match.";
            return;
        }
        if (NewPassword.Length is > 0 and < 6)
        {
            Message = "Use at least 6 characters.";
            return;
        }

        var password = NewPassword;
        IsBusy = true;
        try
        {
            await Task.Run(() => _session.ChangePassword(password));
            Message = password.Length == 0 ? "Password removed: the profile is no longer encrypted." : "Password changed.";
            NewPassword = NewPasswordConfirm = "";
            OnPropertyChanged(nameof(HasPassword));
        }
        catch (IOException ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ChangeAvatar() => _main.ChangeAvatarAsync();

    [RelayCommand]
    private void RemoveAvatar() => _main.SetAvatar(null);

    [RelayCommand]
    private void NewNoSpam()
    {
        _main.NewNoSpam();
        QrCode = Images.QrCode("tox:" + _main.ToxId);
        Message = "New Tox ID generated: friends you already have keep working; give the new ID to new contacts.";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = await _services.Platform.PickExportPathAsync(_session.Name + ".tox");
        if (path is null)
            return;
        _session.Export(path);
        Message = "Profile exported (unencrypted): it opens in qTox and other Tox clients.";
    }

    [RelayCommand]
    private void Close() => _close();
}
