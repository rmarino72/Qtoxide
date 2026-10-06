using Toxide;
using Toxide.State;

namespace Qtoxide.Services;

/// <summary>
/// An open profile: the running Tox instance plus everything stored next to it (history,
/// settings, avatars). Saves the profile in the toxcore format, so the .tox file also opens in qTox.
/// </summary>
public sealed class ProfileSession : IDisposable
{
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromMinutes(1);

    private readonly AppPaths _paths;
    private readonly object _saveLock = new();
    private ToxPassKey? _key;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    internal ProfileSession(AppPaths paths, string name, Tox tox, ToxPassKey? key)
    {
        _paths = paths;
        Name = name;
        Tox = tox;
        _key = key;
        Settings = JsonFiles.Load<ProfileSettings>(paths.SettingsFile(name));
        History = new HistoryStore(paths.HistoryFile(name), key);
        Avatars = new AvatarStore(paths.AvatarsDirectory(name));
    }

    public string Name { get; }
    public Tox Tox { get; }
    public HistoryStore History { get; }
    public ProfileSettings Settings { get; }
    public AvatarStore Avatars { get; }
    public bool HasPassword => _key is not null;

    /// <summary>Writes the .tox profile (encrypted if it has a password) and the settings.</summary>
    public void Save()
    {
        lock (_saveLock)
        {
            SecureFile.Write(_paths.ProfileFile(Name), Tox.GetSaveData(), _key);
            JsonFiles.Save(_paths.SettingsFile(Name), Settings);
        }
    }

    public void SaveSettings()
    {
        lock (_saveLock)
            JsonFiles.Save(_paths.SettingsFile(Name), Settings);
    }

    /// <summary>Sets, changes or (with null) removes the password; profile and history are rewritten.</summary>
    public void ChangePassword(string? password)
    {
        var old = _key;
        _key = string.IsNullOrEmpty(password) ? null : ToxPassKey.Derive(password);
        Save();
        History.ChangeKey(_key);
        old?.Dispose();
    }

    /// <summary>Writes an unencrypted copy of the profile, e.g. to move it to another client.</summary>
    public void Export(string path) => File.WriteAllBytes(path, Tox.GetSaveData());

    /// <summary>Joins the network and runs the protocol loop in the background.</summary>
    public void Start(IReadOnlyList<BootstrapNode> nodes)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Bootstrap(nodes);
        _loop = Task.Run(async () =>
        {
            var loop = Tox.RunAsync(token);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(AutosaveInterval, token).ConfigureAwait(false);
                    Save(); // keeps the saved DHT nodes fresh for a fast next start
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // disk full or file locked: try again next time
                }
            }
            await loop.ConfigureAwait(false);
        }, token);
    }

    public void Bootstrap(IEnumerable<BootstrapNode> nodes)
    {
        foreach (var node in nodes.Concat(Settings.CustomNodes))
        {
            try
            {
                Tox.Bootstrap(node.Host, node.Port, node.PublicKey);
            }
            catch (ToxException)
            {
                // unresolvable host or malformed key: skip it
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }

        Save();
        History.Dispose();
        Tox.Dispose();
        _key?.Dispose();
        _cts?.Dispose();
    }
}
