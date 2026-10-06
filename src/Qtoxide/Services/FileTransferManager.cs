using Qtoxide.Models;
using Toxide;

namespace Qtoxide.Services;

public sealed class Transfer
{
    internal Transfer(uint friendNumber, string friendKey, uint fileNumber, bool incoming, string fileName, long size)
    {
        FriendNumber = friendNumber;
        FriendKey = friendKey;
        FileNumber = fileNumber;
        Incoming = incoming;
        FileName = fileName;
        Size = size;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public uint FriendNumber { get; }
    public string FriendKey { get; }
    public uint FileNumber { get; }
    public bool Incoming { get; }
    public string FileName { get; }
    public long Size { get; }
    public long Transferred { get; internal set; }
    public TransferState State { get; internal set; }
    public string? Path { get; internal set; }

    internal Stream? Stream { get; set; }
    internal bool PausedByUs { get; set; }
}

/// <summary>
/// File transfers and avatars on top of the Tox file API. Chunks are served and written on the
/// protocol thread; <see cref="Changed"/> reports progress and state changes (also on that thread).
/// Lock order: this class never calls into Tox while holding its own lock from the UI side.
/// </summary>
public sealed class FileTransferManager : IDisposable
{
    private readonly Tox _tox;
    private readonly AvatarStore _avatars;
    private readonly Func<string> _downloadDirectory;
    private readonly object _sync = new();
    private readonly Dictionary<(uint Friend, uint File), Transfer> _transfers = new();
    private readonly Dictionary<(uint Friend, uint File), byte[]> _avatarUploads = new();
    private readonly Dictionary<(uint Friend, uint File), (string Key, MemoryStream Data)> _avatarDownloads = new();

    public FileTransferManager(Tox tox, AvatarStore avatars, Func<string> downloadDirectory)
    {
        _tox = tox;
        _avatars = avatars;
        _downloadDirectory = downloadDirectory;
        tox.FileReceiveRequested += OnFileReceive;
        tox.FileChunkReceived += OnChunkReceived;
        tox.FileChunkRequested += OnChunkRequested;
        tox.FileControlReceived += OnControl;
        tox.FriendConnectionStatusChanged += OnFriendConnection;
    }

    /// <summary>A new incoming file waits for <see cref="Accept"/> or <see cref="Cancel"/>.</summary>
    public event Action<Transfer>? IncomingFile;

    /// <summary>Progress or state of a transfer changed.</summary>
    public event Action<Transfer>? Changed;

    /// <summary>A friend's avatar was received or removed (key in hex).</summary>
    public event Action<string>? AvatarChanged;

    private string KeyOf(uint friend) => Convert.ToHexString(_tox.GetFriendPublicKey(friend));

    // ---------------------------------------------------------------- outgoing

    public Transfer Send(uint friendNumber, string path)
    {
        var stream = File.OpenRead(path);
        uint file;
        try
        {
            file = _tox.FileSend(friendNumber, ToxFileKind.Data, (ulong)stream.Length, System.IO.Path.GetFileName(path));
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        var transfer = new Transfer(friendNumber, KeyOf(friendNumber), file, false, System.IO.Path.GetFileName(path), stream.Length)
        {
            State = TransferState.WaitingForFriend,
            Path = path,
            Stream = stream,
        };
        lock (_sync)
            _transfers[(friendNumber, file)] = transfer;
        return transfer;
    }

    /// <summary>Sends our avatar (or "no avatar") to a friend that just came online.</summary>
    public void SendAvatar(uint friendNumber)
    {
        var avatar = _avatars.Self;
        try
        {
            uint file = avatar is null
                ? _tox.FileSend(friendNumber, ToxFileKind.Avatar, 0, "")
                : _tox.FileSend(friendNumber, ToxFileKind.Avatar, (ulong)avatar.Length, "avatar.png", Tox.Hash(avatar));
            if (avatar is not null)
                lock (_sync)
                    _avatarUploads[(friendNumber, file)] = avatar;
        }
        catch (ToxException)
        {
            // friend went offline again, or too many transfers: it will get the avatar next time
        }
    }

    private void OnChunkRequested(object? sender, FileChunkRequestEventArgs e)
    {
        var id = (e.FriendNumber, e.FileNumber);
        byte[]? avatar;
        Transfer? transfer;
        lock (_sync)
        {
            _avatarUploads.TryGetValue(id, out avatar);
            _transfers.TryGetValue(id, out transfer);
        }

        if (avatar is not null)
        {
            if (e.Length == 0)
                lock (_sync) _avatarUploads.Remove(id);
            else
                _tox.FileSendChunk(e.FriendNumber, e.FileNumber, e.Position, avatar.AsSpan((int)e.Position, e.Length));
            return;
        }

        if (transfer is null)
            return;

        if (e.Length == 0)
        {
            Finish(transfer, TransferState.Completed);
            return;
        }

        var buffer = new byte[e.Length];
        lock (_sync)
        {
            if (transfer.Stream is null)
                return;
            transfer.Stream.Position = (long)e.Position;
            transfer.Stream.ReadExactly(buffer);
        }
        _tox.FileSendChunk(e.FriendNumber, e.FileNumber, e.Position, buffer);
        transfer.Transferred = (long)e.Position + e.Length;
        transfer.State = TransferState.Transferring;
        Changed?.Invoke(transfer);
    }

    // ---------------------------------------------------------------- incoming

    private void OnFileReceive(object? sender, FileReceiveEventArgs e)
    {
        var key = KeyOf(e.FriendNumber);
        if (e.Kind == ToxFileKind.Avatar)
        {
            ReceiveAvatar(e, key);
            return;
        }

        var transfer = new Transfer(e.FriendNumber, key, e.FileNumber, true, SafeName(e.FileName), (long)Math.Min(e.Size, long.MaxValue))
        {
            State = TransferState.Pending,
        };
        lock (_sync)
            _transfers[(e.FriendNumber, e.FileNumber)] = transfer;
        IncomingFile?.Invoke(transfer);
    }

    private void ReceiveAvatar(FileReceiveEventArgs e, string key)
    {
        if (e.Size == 0)
        {
            _avatars.Remove(key);
            Control(e.FriendNumber, e.FileNumber, ToxFileControl.Cancel);
            AvatarChanged?.Invoke(key);
            return;
        }

        // Same hash as the avatar we have: no need to download it again.
        var have = _avatars.GetHash(key);
        if (e.Size > AvatarStore.MaxSize || (have is not null && have.AsSpan().SequenceEqual(_tox.GetFileId(e.FriendNumber, e.FileNumber))))
        {
            Control(e.FriendNumber, e.FileNumber, ToxFileControl.Cancel);
            return;
        }

        lock (_sync)
            _avatarDownloads[(e.FriendNumber, e.FileNumber)] = (key, new MemoryStream());
        Control(e.FriendNumber, e.FileNumber, ToxFileControl.Resume);
    }

    /// <summary>Accepts an incoming file into the download folder.</summary>
    public void Accept(Transfer transfer)
    {
        lock (_sync)
        {
            if (transfer.State != TransferState.Pending)
                return;
            var directory = _downloadDirectory();
            Directory.CreateDirectory(directory);
            transfer.Path = UniquePath(directory, transfer.FileName);
            transfer.Stream = new FileStream(transfer.Path, FileMode.CreateNew, FileAccess.Write);
            transfer.State = TransferState.Transferring;
        }

        if (!Control(transfer.FriendNumber, transfer.FileNumber, ToxFileControl.Resume))
            Finish(transfer, TransferState.Cancelled);
        Changed?.Invoke(transfer);
    }

    public void Cancel(Transfer transfer)
    {
        if (transfer.State is TransferState.Completed or TransferState.Cancelled)
            return;
        Control(transfer.FriendNumber, transfer.FileNumber, ToxFileControl.Cancel);
        Finish(transfer, TransferState.Cancelled);
    }

    public void TogglePause(Transfer transfer)
    {
        if (transfer.State == TransferState.Transferring)
        {
            if (Control(transfer.FriendNumber, transfer.FileNumber, ToxFileControl.Pause))
            {
                transfer.PausedByUs = true;
                transfer.State = TransferState.Paused;
            }
        }
        else if (transfer.State == TransferState.Paused && transfer.PausedByUs)
        {
            if (Control(transfer.FriendNumber, transfer.FileNumber, ToxFileControl.Resume))
            {
                transfer.PausedByUs = false;
                transfer.State = TransferState.Transferring;
            }
        }
        Changed?.Invoke(transfer);
    }

    private void OnChunkReceived(object? sender, FileChunkEventArgs e)
    {
        var id = (e.FriendNumber, e.FileNumber);
        (string Key, MemoryStream Data) avatar;
        Transfer? transfer;
        bool isAvatar;
        lock (_sync)
        {
            isAvatar = _avatarDownloads.TryGetValue(id, out avatar);
            _transfers.TryGetValue(id, out transfer);
        }

        if (isAvatar)
        {
            if (e.Data.Length > 0)
            {
                avatar.Data.Position = (long)e.Position;
                avatar.Data.Write(e.Data);
                return;
            }
            lock (_sync) _avatarDownloads.Remove(id);
            _avatars.Set(avatar.Key, avatar.Data.ToArray());
            AvatarChanged?.Invoke(avatar.Key);
            return;
        }

        if (transfer is null)
            return;
        if (e.Data.Length == 0)
        {
            Finish(transfer, TransferState.Completed);
            return;
        }

        lock (_sync)
        {
            if (transfer.Stream is null)
                return;
            transfer.Stream.Position = (long)e.Position;
            transfer.Stream.Write(e.Data);
        }
        transfer.Transferred = (long)e.Position + e.Data.Length;
        Changed?.Invoke(transfer);
    }

    private void OnControl(object? sender, FileControlEventArgs e)
    {
        Transfer? transfer;
        lock (_sync)
        {
            _transfers.TryGetValue((e.FriendNumber, e.FileNumber), out transfer);
            if (e.Control == ToxFileControl.Cancel)
            {
                _avatarUploads.Remove((e.FriendNumber, e.FileNumber));
                _avatarDownloads.Remove((e.FriendNumber, e.FileNumber));
            }
        }
        if (transfer is null)
            return;

        switch (e.Control)
        {
            case ToxFileControl.Cancel:
                Finish(transfer, TransferState.Cancelled);
                return;
            case ToxFileControl.Pause:
                transfer.State = TransferState.Paused;
                break;
            case ToxFileControl.Resume:
                if (!transfer.PausedByUs)
                    transfer.State = TransferState.Transferring;
                break;
        }
        Changed?.Invoke(transfer);
    }

    /// <summary>toxcore drops every transfer when the friend goes offline, silently: mirror that.</summary>
    private void OnFriendConnection(object? sender, FriendConnectionEventArgs e)
    {
        if (e.Connection != ToxConnection.None)
            return;

        List<Transfer> broken;
        lock (_sync)
        {
            broken = _transfers.Values.Where(t => t.FriendNumber == e.FriendNumber).ToList();
            foreach (var k in _avatarUploads.Keys.Where(k => k.Friend == e.FriendNumber).ToList())
                _avatarUploads.Remove(k);
            foreach (var k in _avatarDownloads.Keys.Where(k => k.Friend == e.FriendNumber).ToList())
                _avatarDownloads.Remove(k);
        }
        foreach (var transfer in broken)
            Finish(transfer, TransferState.Cancelled);
    }

    private void Finish(Transfer transfer, TransferState state)
    {
        lock (_sync)
        {
            _transfers.Remove((transfer.FriendNumber, transfer.FileNumber));
            transfer.Stream?.Dispose();
            transfer.Stream = null;
            transfer.State = state;
            if (state == TransferState.Completed)
                transfer.Transferred = transfer.Size;
            if (state == TransferState.Cancelled && transfer.Incoming && transfer.Path is not null && File.Exists(transfer.Path))
                File.Delete(transfer.Path); // no half-written files left behind
        }
        Changed?.Invoke(transfer);
    }

    private bool Control(uint friend, uint file, ToxFileControl control)
    {
        try
        {
            _tox.FileControl(friend, file, control);
            return true;
        }
        catch (ToxException)
        {
            return false;
        }
    }

    internal static string SafeName(string name)
    {
        var clean = System.IO.Path.GetFileName(name.Replace('\\', '/'));
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            clean = clean.Replace(c, '_');
        return string.IsNullOrWhiteSpace(clean) || clean.StartsWith('.') ? "file" + clean : clean;
    }

    private static string UniquePath(string directory, string fileName)
    {
        var path = System.IO.Path.Combine(directory, fileName);
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var ext = System.IO.Path.GetExtension(fileName);
        for (int i = 1; File.Exists(path); i++)
            path = System.IO.Path.Combine(directory, $"{stem} ({i}){ext}");
        return path;
    }

    public void Dispose()
    {
        _tox.FileReceiveRequested -= OnFileReceive;
        _tox.FileChunkReceived -= OnChunkReceived;
        _tox.FileChunkRequested -= OnChunkRequested;
        _tox.FileControlReceived -= OnControl;
        _tox.FriendConnectionStatusChanged -= OnFriendConnection;
        lock (_sync)
        {
            foreach (var t in _transfers.Values)
                t.Stream?.Dispose();
            _transfers.Clear();
        }
    }
}
