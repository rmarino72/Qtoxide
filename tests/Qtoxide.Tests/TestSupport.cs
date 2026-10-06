using System.Collections.Concurrent;
using Qtoxide.Services;

namespace Qtoxide.Tests;

/// <summary>Collects actions posted from the Tox thread; the test runs them on its own thread with <see cref="Pump"/>.</summary>
public sealed class QueueDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _queue = new();

    public void Post(Action action) => _queue.Enqueue(action);

    public void Pump()
    {
        while (_queue.TryDequeue(out var action))
            action();
    }
}

public sealed class FakePlatform : IPlatformServices
{
    public string? NextFile { get; set; }
    public string? Clipboard { get; private set; }
    public bool Active { get; set; } = true;
    public List<string> Opened { get; } = [];

    public Task<string?> PickFileToSendAsync() => Task.FromResult(NextFile);
    public Task<string?> PickProfileToImportAsync() => Task.FromResult(NextFile);
    public Task<string?> PickExportPathAsync(string suggestedName) => Task.FromResult(NextFile);
    public Task<string?> PickFolderAsync() => Task.FromResult(NextFile);
    public Task<string?> PickImageAsync() => Task.FromResult(NextFile);

    public Task SetClipboardAsync(string text)
    {
        Clipboard = text;
        return Task.CompletedTask;
    }

    public bool IsWindowActive => Active;
    public void OpenFile(string path) => Opened.Add(path);
    public void RequestAttention() { }
}

public sealed class FakeNotifier : INotifier
{
    public List<(string Title, string Body)> Sent { get; } = [];
    public void Notify(string title, string body) => Sent.Add((title, body));
}

/// <summary>A temporary Qtoxide home folder.</summary>
public sealed class TempHome : IDisposable
{
    public TempHome() => Paths = new AppPaths(Directory.CreateTempSubdirectory("qtoxide-test").FullName);

    public AppPaths Paths { get; }

    public void Dispose()
    {
        try { Directory.Delete(Paths.Root, recursive: true); }
        catch (IOException) { }
    }
}
