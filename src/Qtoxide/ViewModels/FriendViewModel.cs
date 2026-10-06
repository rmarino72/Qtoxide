using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toxide;

namespace Qtoxide.ViewModels;

public sealed partial class FriendViewModel : ViewModelBase
{
    private readonly Action<FriendViewModel> _remove;

    public FriendViewModel(uint number, string publicKey, Action<FriendViewModel> remove)
    {
        Number = number;
        PublicKey = publicKey;
        _remove = remove;
    }

    public uint Number { get; }

    /// <summary>The friend's long-term public key, hex.</summary>
    public string PublicKey { get; }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(Initials))]
    private string _name = "";

    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Presence))]
    private ToxUserStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Presence), nameof(PresenceText))]
    private bool _isOnline;

    [ObservableProperty] private bool _isTyping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int _unread;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatar;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? PublicKey[..8] + "…" : Name;
    public string Initials => Presentation.Initials(DisplayName);
    public bool HasUnread => Unread > 0;
    public bool HasAvatar => Avatar is not null;
    public Presence Presence => IsOnline ? Presentation.FromStatus(Status) : Presence.Offline;
    public string PresenceText => IsOnline ? (string.IsNullOrEmpty(StatusMessage) ? "Online" : StatusMessage) : "Offline";

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(PresenceText));

    [RelayCommand]
    private void Remove() => _remove(this);
}

public enum Presence
{
    Offline,
    Online,
    Away,
    Busy,
}

internal static class Presentation
{
    public static Presence FromStatus(ToxUserStatus status) => status switch
    {
        ToxUserStatus.Away => Presence.Away,
        ToxUserStatus.Busy => Presence.Busy,
        _ => Presence.Online,
    };

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}",
        };
    }
}
