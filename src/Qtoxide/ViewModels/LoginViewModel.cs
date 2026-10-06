using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qtoxide.Services;

namespace Qtoxide.ViewModels;

/// <summary>Choose, unlock, create or import a profile.</summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<ProfileSession> _loggedIn;

    public LoginViewModel(AppServices services, Action<ProfileSession> loggedIn, string? lastProfile = null)
    {
        _services = services;
        _loggedIn = loggedIn;
        Refresh();
        SelectedProfile = Profiles.FirstOrDefault(p => p == lastProfile) ?? Profiles.FirstOrDefault();
        IsCreating = Profiles.Count == 0;
    }

    public ObservableCollection<string> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedIsEncrypted))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private string? _selectedProfile;

    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(CreateCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newPasswordConfirm = "";

    public bool SelectedIsEncrypted => SelectedProfile is not null && _services.Profiles.IsEncrypted(SelectedProfile);
    public bool HasProfiles => Profiles.Count > 0;

    private void Refresh()
    {
        Profiles.Clear();
        foreach (var name in _services.Profiles.List())
            Profiles.Add(name);
        OnPropertyChanged(nameof(HasProfiles));
    }

    private bool CanOpen() => SelectedProfile is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        var name = SelectedProfile!;
        var password = Password;
        await RunBusy(() => _services.Profiles.Open(name, password));
    }

    [RelayCommand]
    private void ShowCreate() => (IsCreating, Error) = (true, null);

    [RelayCommand]
    private void ShowOpen() => (IsCreating, Error) = (false, null);

    private bool CanCreate() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        var name = NewName.Trim();
        if (ProfileManager.ValidateName(name) is { } invalid)
        {
            Error = invalid;
            return;
        }
        if (NewPassword != NewPasswordConfirm)
        {
            Error = "The passwords do not match.";
            return;
        }
        if (NewPassword.Length is > 0 and < 6)
        {
            Error = "Use at least 6 characters, or no password at all.";
            return;
        }

        var password = NewPassword;
        await RunBusy(() => _services.Profiles.Create(name, password));
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await _services.Platform.PickProfileToImportAsync();
        if (file is null)
            return;
        try
        {
            var name = _services.Profiles.Import(file);
            Refresh();
            SelectedProfile = name;
            IsCreating = false;
            Error = null;
        }
        catch (Exception ex) when (ex is ProfileException or IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    /// <summary>Opening derives the password key (scrypt) and binds a socket: keep it off the UI thread.</summary>
    private async Task RunBusy(Func<ProfileSession> open)
    {
        IsBusy = true;
        Error = null;
        try
        {
            var session = await Task.Run(open);
            Password = NewPassword = NewPasswordConfirm = "";
            _loggedIn(session);
        }
        catch (Exception ex) when (ex is ProfileException or Toxide.ToxException or IOException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
