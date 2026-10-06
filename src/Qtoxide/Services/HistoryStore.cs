using System.Text.Json;
using Qtoxide.Models;
using Toxide.State;

namespace Qtoxide.Services;

/// <summary>
/// Chat histories of one profile, keyed by the friend's public key (hex). Stored as JSON, encrypted
/// with the profile's password when it has one. Saves are batched: <see cref="MarkDirty"/> schedules one.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private readonly string _path;
    private ToxPassKey? _key;
    private readonly Dictionary<string, List<StoredMessage>> _chats;
    private readonly object _sync = new();
    private readonly Timer _timer;
    private bool _dirty;

    public HistoryStore(string path, ToxPassKey? key)
    {
        _path = path;
        _key = key;
        var data = SecureFile.Read(path, key);
        _chats = data is null
            ? new Dictionary<string, List<StoredMessage>>()
            : JsonSerializer.Deserialize<Dictionary<string, List<StoredMessage>>>(data, JsonFiles.Options) ?? new();
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<StoredMessage> Get(string friendKey)
    {
        lock (_sync)
            return _chats.TryGetValue(friendKey, out var list) ? list.ToList() : [];
    }

    public void Add(string friendKey, StoredMessage message)
    {
        lock (_sync)
        {
            if (!_chats.TryGetValue(friendKey, out var list))
                _chats[friendKey] = list = [];
            list.Add(message);
        }
        MarkDirty();
    }

    public void Remove(string friendKey)
    {
        lock (_sync)
            _chats.Remove(friendKey);
        MarkDirty();
    }

    /// <summary>Call after changing a stored message in place.</summary>
    public void MarkDirty()
    {
        lock (_sync)
        {
            _dirty = true;
            _timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }
    }

    public void ChangeKey(ToxPassKey? key)
    {
        lock (_sync)
        {
            _key = key;
            _dirty = true;
        }
        Flush();
    }

    public void Flush()
    {
        byte[] data;
        ToxPassKey? key;
        lock (_sync)
        {
            if (!_dirty)
                return;
            _dirty = false;
            data = JsonSerializer.SerializeToUtf8Bytes(_chats, JsonFiles.Options);
            key = _key;
        }
        SecureFile.Write(_path, data, key);
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
