using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qtoxide.Models;
using Qtoxide.Services;
using Toxide;
using Toxide.Crypto;

namespace Qtoxide.ViewModels;

/// <summary>
/// The logged-in page: our identity, friend list, friend requests and the selected conversation.
/// Tox events arrive on the protocol thread and are applied on the UI thread through the dispatcher.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan TypingTimeout = TimeSpan.FromSeconds(3);

    private readonly ProfileSession _session;
    private readonly AppServices _services;
    private readonly Action _logout;
    private readonly Tox _tox;
    private readonly FileTransferManager _files;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Messages handed to Tox and waiting for a read receipt, by (friend, message id).</summary>
    private readonly Dictionary<(uint Friend, uint Id), (FriendViewModel Friend, MessageViewModel Message)> _awaitingReceipt = new();
    private readonly Dictionary<Guid, MessageViewModel> _fileMessages = new();
    private readonly Timer _typingTimer;
    private FriendViewModel? _typingTo;

    public MainViewModel(ProfileSession session, AppServices services, Action logout)
    {
        _session = session;
        _services = services;
        _logout = logout;
        _tox = session.Tox;
        _files = new FileTransferManager(_tox, session.Avatars, () => session.Settings.DownloadDirectory);
        _typingTimer = new Timer(_ => services.Dispatcher.Post(StopTyping), null, Timeout.Infinite, Timeout.Infinite);

        _name = _tox.Name;
        _statusMessage = _tox.StatusMessage;
        _status = _tox.Status;
        _toxId = _tox.Address.ToString();
        _selfAvatar = Images.Load(session.Avatars.Self);

        foreach (uint number in _tox.FriendList)
            Friends.Add(CreateFriend(number));
        SortFriends();
        foreach (var request in session.Settings.PendingRequests)
            Requests.Add(new FriendRequestViewModel(request.PublicKey, request.Message, AnswerRequest));

        Subscribe();
        _ = StartAsync();
    }

    public string ProfileName => _session.Name;
    public ProfileSession Session => _session;
    public ObservableCollection<FriendViewModel> Friends { get; } = [];
    public ObservableCollection<FriendRequestViewModel> Requests { get; } = [];
    public bool HasRequests => Requests.Count > 0;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _statusMessage;
    [ObservableProperty] private ToxUserStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Presence), nameof(ConnectionText))]
    private ToxConnection _connection;

    [ObservableProperty] private string _toxId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelfAvatar))]
    private Bitmap? _selfAvatar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SendFileCommand))]
    private FriendViewModel? _selectedFriend;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _messageText = "";

    // Overlays
    [ObservableProperty] private bool _isAddFriendOpen;
    [ObservableProperty] private string _addFriendId = "";
    [ObservableProperty] private string _addFriendMessage = "";
    [ObservableProperty] private string? _addFriendError;
    [ObservableProperty] private SettingsViewModel? _settings;
    [ObservableProperty] private string? _notice;

    public bool HasSelection => SelectedFriend is not null;
    public bool HasSelfAvatar => SelfAvatar is not null;
    public string Initials => Presentation.Initials(string.IsNullOrWhiteSpace(Name) ? ProfileName : Name);
    public Presence Presence => Connection == ToxConnection.None ? Presence.Offline : Presentation.FromStatus(Status);
    public string ConnectionText => Connection == ToxConnection.None ? "Connecting…" : "Online";
    public IReadOnlyList<ToxUserStatus> Statuses { get; } = [ToxUserStatus.None, ToxUserStatus.Away, ToxUserStatus.Busy];

    private async Task StartAsync()
    {
        var nodes = await _services.LoadBootstrapNodes(_cts.Token).ConfigureAwait(false);
        if (!_cts.IsCancellationRequested)
            _session.Start(nodes);
    }

    // ================================================================ Tox events (protocol thread)

    private void Subscribe()
    {
        var ui = _services.Dispatcher;
        _tox.ConnectionStatusChanged += (_, e) => ui.Post(() => Connection = e.Connection);
        _tox.FriendRequestReceived += (_, e) =>
            ui.Post(() => OnFriendRequest(Convert.ToHexString(e.PublicKey), e.Message));
        _tox.FriendMessageReceived += (_, e) => ui.Post(() => OnMessage(e.FriendNumber, e.Type, e.Message));
        _tox.FriendNameChanged += (_, e) => ui.Post(() => WithFriend(e.FriendNumber, f => f.Name = e.Text));
        _tox.FriendStatusMessageChanged += (_, e) => ui.Post(() => WithFriend(e.FriendNumber, f => f.StatusMessage = e.Text));
        _tox.FriendStatusChanged += (_, e) => ui.Post(() => WithFriend(e.FriendNumber, f => f.Status = e.Status));
        _tox.FriendTypingChanged += (_, e) => ui.Post(() => WithFriend(e.FriendNumber, f => f.IsTyping = e.IsTyping));
        _tox.FriendReadReceipt += (_, e) => ui.Post(() => OnReceipt(e.FriendNumber, e.MessageId));
        _tox.FriendConnectionStatusChanged += (_, e) =>
        {
            // Avatars go out from the protocol thread right away; everything else on the UI thread.
            if (e.Connection != ToxConnection.None)
                _files.SendAvatar(e.FriendNumber);
            ui.Post(() => OnFriendConnection(e.FriendNumber, e.Connection));
        };

        _files.IncomingFile += t => ui.Post(() => OnIncomingFile(t));
        _files.Changed += t => ui.Post(() => OnTransferChanged(t));
        _files.AvatarChanged += key => ui.Post(() => OnAvatarChanged(key));
    }

    private FriendViewModel? FindFriend(uint number) => Friends.FirstOrDefault(f => f.Number == number);

    private void WithFriend(uint number, Action<FriendViewModel> action)
    {
        if (FindFriend(number) is { } friend)
            action(friend);
    }

    private void OnFriendConnection(uint number, ToxConnection connection)
    {
        if (FindFriend(number) is not { } friend)
            return;

        friend.IsOnline = connection != ToxConnection.None;
        if (!friend.IsOnline)
        {
            friend.IsTyping = false;
            foreach (var key in _awaitingReceipt.Keys.Where(k => k.Friend == number).ToList())
                _awaitingReceipt.Remove(key);
        }
        else
        {
            // Like qTox: whatever was not confirmed is (re)sent when the friend comes back.
            foreach (var message in friend.Messages.Where(m => m.Outgoing && !m.Delivered && !m.IsFile))
                TrySend(friend, message);
        }
        SortFriends();
    }

    private void OnMessage(uint number, ToxMessageType type, string text)
    {
        if (FindFriend(number) is not { } friend)
            return;

        var stored = new StoredMessage
        {
            Kind = type == ToxMessageType.Action ? MessageKind.Action : MessageKind.Text,
            Text = text,
            Outgoing = false,
        };
        _session.History.Add(friend.PublicKey, stored);
        friend.Messages.Add(new MessageViewModel(stored, friend.DisplayName));
        friend.IsTyping = false;
        Attention(friend, type == ToxMessageType.Action ? $"* {friend.DisplayName} {text}" : text);
    }

    private void OnReceipt(uint number, uint id)
    {
        if (!_awaitingReceipt.Remove((number, id), out var entry))
            return;
        entry.Message.Model.Delivered = true;
        entry.Message.Delivered = true;
        _session.History.MarkDirty();
    }

    private void OnFriendRequest(string publicKey, string message)
    {
        if (Requests.Any(r => r.PublicKey == publicKey))
            return;
        Requests.Add(new FriendRequestViewModel(publicKey, message, AnswerRequest));
        _session.Settings.PendingRequests.Add(new PendingFriendRequest
            { PublicKey = publicKey, Message = message, Received = DateTimeOffset.Now });
        _session.SaveSettings();
        OnPropertyChanged(nameof(HasRequests));
        Notify("Friend request", message);
    }

    private void OnAvatarChanged(string key)
    {
        if (Friends.FirstOrDefault(f => f.PublicKey == key) is { } friend)
            friend.Avatar = Images.Load(_session.Avatars.Get(key));
    }

    // ================================================================ friends

    private FriendViewModel CreateFriend(uint number)
    {
        var key = Convert.ToHexString(_tox.GetFriendPublicKey(number));
        var friend = new FriendViewModel(number, key, RemoveFriend)
        {
            Name = _tox.GetFriendName(number),
            StatusMessage = _tox.GetFriendStatusMessage(number),
            Status = _tox.GetFriendStatus(number),
            IsOnline = _tox.GetFriendConnectionStatus(number) != ToxConnection.None,
            Avatar = Images.Load(_session.Avatars.Get(key)),
        };
        foreach (var stored in _session.History.Get(key))
        {
            // Transfers do not survive a restart.
            if (stored.Kind == MessageKind.File && stored.TransferState is not (TransferState.Completed or TransferState.Cancelled))
                stored.TransferState = TransferState.Cancelled;
            friend.Messages.Add(new MessageViewModel(stored, stored.Outgoing ? "" : friend.DisplayName, OnTransferAction));
        }
        return friend;
    }

    /// <summary>Online friends first, then by name.</summary>
    private void SortFriends()
    {
        var sorted = Friends.OrderByDescending(f => f.IsOnline).ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = Friends.IndexOf(sorted[i]);
            if (current != i)
                Friends.Move(current, i);
        }
    }

    private void AnswerRequest(FriendRequestViewModel request, bool accept)
    {
        Requests.Remove(request);
        _session.Settings.PendingRequests.RemoveAll(r => r.PublicKey == request.PublicKey);
        OnPropertyChanged(nameof(HasRequests));

        if (accept)
        {
            try
            {
                uint number = _tox.AddFriendNoRequest(Convert.FromHexString(request.PublicKey));
                var friend = CreateFriend(number);
                Friends.Add(friend);
                SortFriends();
                SelectedFriend = friend;
            }
            catch (ToxException ex)
            {
                Notice = ex.Message;
            }
        }
        _session.Save();
    }

    [RelayCommand]
    private void OpenAddFriend()
    {
        AddFriendId = "";
        AddFriendMessage = $"Hi, I'm {(string.IsNullOrWhiteSpace(Name) ? ProfileName : Name)}. Let's chat on Tox!";
        AddFriendError = null;
        IsAddFriendOpen = true;
    }

    [RelayCommand]
    private void CloseAddFriend() => IsAddFriendOpen = false;

    [RelayCommand]
    private void AddFriend()
    {
        var id = AddFriendId.Trim();
        if (id.StartsWith("tox:", StringComparison.OrdinalIgnoreCase))
            id = id[4..];

        if (!Toxide.Crypto.ToxId.TryParse(id.ToUpperInvariant(), out var address))
        {
            AddFriendError = "That is not a valid Tox ID (76 hexadecimal characters).";
            return;
        }

        try
        {
            uint number = _tox.AddFriend(address, string.IsNullOrWhiteSpace(AddFriendMessage) ? "Hi!" : AddFriendMessage);
            var friend = CreateFriend(number);
            Friends.Add(friend);
            SortFriends();
            SelectedFriend = friend;
            IsAddFriendOpen = false;
            _session.Save();
        }
        catch (ToxException ex)
        {
            AddFriendError = ex.Code switch
            {
                ToxErrorCode.OwnKey => "That is your own Tox ID.",
                ToxErrorCode.FriendAlreadyAdded or ToxErrorCode.SetNewNoSpam => "This friend is already in your list.",
                ToxErrorCode.TooLong => "The message is too long.",
                _ => ex.Message,
            };
        }
    }

    private void RemoveFriend(FriendViewModel friend)
    {
        _tox.DeleteFriend(friend.Number);
        _session.History.Remove(friend.PublicKey);
        _session.Avatars.Remove(friend.PublicKey);
        Friends.Remove(friend);
        if (SelectedFriend == friend)
            SelectedFriend = null;
        _session.Save();
    }

    partial void OnSelectedFriendChanged(FriendViewModel? oldValue, FriendViewModel? newValue)
    {
        if (oldValue is not null && _typingTo == oldValue)
            StopTyping();
        if (newValue is not null)
            newValue.Unread = 0;
        MessageText = "";
    }

    // ================================================================ chat

    private bool CanSend() => SelectedFriend is not null && !string.IsNullOrWhiteSpace(MessageText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        var friend = SelectedFriend!;
        var text = MessageText.TrimEnd();
        MessageText = "";
        StopTyping();

        var kind = MessageKind.Text;
        if (text.StartsWith("/me ", StringComparison.Ordinal))
        {
            kind = MessageKind.Action;
            text = text[4..];
        }

        foreach (var part in Split(text, Tox.MaxMessageLength))
        {
            var stored = new StoredMessage { Kind = kind, Text = part, Outgoing = true };
            _session.History.Add(friend.PublicKey, stored);
            var message = new MessageViewModel(stored, string.IsNullOrWhiteSpace(Name) ? ProfileName : Name);
            friend.Messages.Add(message);
            TrySend(friend, message);
        }
    }

    /// <summary>Hands a message to Tox if the friend is online; otherwise it waits for the friend.</summary>
    private void TrySend(FriendViewModel friend, MessageViewModel message)
    {
        if (!friend.IsOnline)
            return;
        try
        {
            var type = message.IsAction ? ToxMessageType.Action : ToxMessageType.Normal;
            uint id = _tox.SendMessage(friend.Number, message.Text, type);
            _awaitingReceipt[(friend.Number, id)] = (friend, message);
        }
        catch (ToxException)
        {
            // offline in the meantime, or queue full: it is resent on the next reconnection
        }
    }

    /// <summary>Splits text into chunks of at most <paramref name="maxBytes"/> UTF-8 bytes, never inside a character.</summary>
    internal static IEnumerable<string> Split(string text, int maxBytes)
    {
        int start = 0;
        while (start < text.Length)
        {
            int length = 0, bytes = 0, lastSpace = -1;
            while (start + length < text.Length)
            {
                int charLength = char.IsSurrogatePair(text, start + length) ? 2 : 1;
                int charBytes = Encoding.UTF8.GetByteCount(text.AsSpan(start + length, charLength));
                if (bytes + charBytes > maxBytes)
                    break;
                if (text[start + length] == ' ')
                    lastSpace = length;
                bytes += charBytes;
                length += charLength;
            }

            if (start + length < text.Length && lastSpace > 0)
                length = lastSpace + 1; // break at a space when possible
            yield return text.Substring(start, length);
            start += length;
        }
    }

    partial void OnMessageTextChanged(string value)
    {
        if (!_session.Settings.SendTypingNotifications || SelectedFriend is not { IsOnline: true } friend)
            return;

        if (string.IsNullOrEmpty(value))
        {
            StopTyping();
            return;
        }

        if (_typingTo != friend)
        {
            StopTyping();
            _typingTo = friend;
            TrySetTyping(friend, true);
        }
        _typingTimer.Change(TypingTimeout, Timeout.InfiniteTimeSpan);
    }

    private void StopTyping()
    {
        if (_typingTo is { } friend)
            TrySetTyping(friend, false);
        _typingTo = null;
    }

    private void TrySetTyping(FriendViewModel friend, bool typing)
    {
        try { _tox.SetTyping(friend.Number, typing); }
        catch (ToxException) { }
    }

    private void Attention(FriendViewModel friend, string text)
    {
        bool active = _services.Platform.IsWindowActive;
        if (SelectedFriend != friend || !active)
            friend.Unread++;
        if (!active)
            Notify(friend.DisplayName, text);
    }

    private void Notify(string title, string body)
    {
        if (!_session.Settings.Notifications || _services.Platform.IsWindowActive)
            return;
        _services.Notifier.Notify(title, body.Length > 200 ? body[..200] + "…" : body);
        _services.Platform.RequestAttention();
    }

    // ================================================================ files

    private bool CanSendFile() => SelectedFriend is not null;

    [RelayCommand(CanExecute = nameof(CanSendFile))]
    private async Task SendFileAsync()
    {
        var friend = SelectedFriend!;
        var path = await _services.Platform.PickFileToSendAsync();
        if (path is null)
            return;
        if (!friend.IsOnline)
        {
            Notice = $"{friend.DisplayName} is offline: files can only be sent to online friends.";
            return;
        }

        try
        {
            var transfer = _files.Send(friend.Number, path);
            var stored = new StoredMessage
            {
                Kind = MessageKind.File, Outgoing = true, FileName = transfer.FileName, FileSize = transfer.Size,
                FilePath = path, TransferState = transfer.State,
            };
            AddFileMessage(friend, stored, transfer);
        }
        catch (Exception ex) when (ex is ToxException or IOException or UnauthorizedAccessException)
        {
            Notice = ex.Message;
        }
    }

    private void OnIncomingFile(Transfer transfer)
    {
        if (FindFriend(transfer.FriendNumber) is not { } friend)
            return;
        var stored = new StoredMessage
        {
            Kind = MessageKind.File, Outgoing = false, FileName = transfer.FileName, FileSize = transfer.Size,
            TransferState = TransferState.Pending,
        };
        AddFileMessage(friend, stored, transfer);
        Attention(friend, $"sent you a file: {transfer.FileName}");
    }

    private void AddFileMessage(FriendViewModel friend, StoredMessage stored, Transfer transfer)
    {
        _session.History.Add(friend.PublicKey, stored);
        var message = new MessageViewModel(stored, stored.Outgoing ? Name : friend.DisplayName, OnTransferAction)
        {
            Transfer = transfer,
        };
        message.Update(transfer);
        _fileMessages[transfer.Id] = message;
        friend.Messages.Add(message);
    }

    private void OnTransferChanged(Transfer transfer)
    {
        if (!_fileMessages.TryGetValue(transfer.Id, out var message))
            return;
        message.Update(transfer);
        if (transfer.State is TransferState.Completed or TransferState.Cancelled)
        {
            _fileMessages.Remove(transfer.Id);
            _session.History.MarkDirty();
        }
    }

    private void OnTransferAction(MessageViewModel message, string action)
    {
        if (action == "open")
        {
            if (message.Model.FilePath is { } path)
                _services.Platform.OpenFile(path);
            return;
        }

        if (message.Transfer is not { } transfer)
            return;
        switch (action)
        {
            case "accept":
                try
                {
                    _files.Accept(transfer);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Notice = $"Cannot save the file: {ex.Message}";
                    _files.Cancel(transfer);
                }
                break;
            case "cancel":
                _files.Cancel(transfer);
                break;
            case "pause":
                _files.TogglePause(transfer);
                break;
        }
        message.Update(transfer);
    }

    // ================================================================ self

    partial void OnNameChanged(string value)
    {
        try
        {
            _tox.Name = value;
            _session.Save();
        }
        catch (ToxException)
        {
            Notice = $"The name can be at most {Tox.MaxNameLength} bytes.";
        }
        OnPropertyChanged(nameof(Initials));
    }

    partial void OnStatusMessageChanged(string value)
    {
        try
        {
            _tox.StatusMessage = value;
            _session.Save();
        }
        catch (ToxException)
        {
            Notice = $"The status message can be at most {Tox.MaxStatusMessageLength} bytes.";
        }
    }

    partial void OnStatusChanged(ToxUserStatus value)
    {
        _tox.Status = value;
        _session.Save();
        OnPropertyChanged(nameof(Presence));
    }

    [RelayCommand]
    private async Task CopyToxIdAsync()
    {
        await _services.Platform.SetClipboardAsync(ToxId);
        Notice = "Tox ID copied to the clipboard.";
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    [RelayCommand]
    private void OpenSettings() => Settings = new SettingsViewModel(this, _session, _services, () => Settings = null);

    [RelayCommand]
    private void Logout() => _logout();

    internal async Task ChangeAvatarAsync()
    {
        var path = await _services.Platform.PickImageAsync();
        if (path is null)
            return;
        byte[]? png;
        try
        {
            png = Images.ToAvatarPng(path, AvatarStore.MaxSize);
        }
        catch (Exception)
        {
            png = null;
        }
        if (png is null)
        {
            Notice = "This image cannot be used as an avatar.";
            return;
        }
        SetAvatar(png);
    }

    internal void SetAvatar(byte[]? png)
    {
        _session.Avatars.Self = png;
        SelfAvatar = Images.Load(png);
        foreach (var friend in Friends.Where(f => f.IsOnline))
            _files.SendAvatar(friend.Number);
    }

    /// <summary>A new nospam makes the old Tox ID useless for new friend requests (fights spam).</summary>
    internal void NewNoSpam()
    {
        _tox.NoSpam = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        ToxId = _tox.Address.ToString();
        _session.Save();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _typingTimer.Dispose();
        _files.Dispose();
        _session.Dispose();
        _cts.Dispose();
    }
}
