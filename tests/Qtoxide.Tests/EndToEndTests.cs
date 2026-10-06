using System.Security.Cryptography;
using Qtoxide.Models;
using Qtoxide.Services;
using Qtoxide.ViewModels;
using Toxide;

namespace Qtoxide.Tests;

/// <summary>Two Qtoxide clients and a few relay nodes, over real UDP on localhost.</summary>
public sealed class EndToEndTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private static int _nextPort = 37000;

    private readonly List<Tox> _relays = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly List<BootstrapNode> _nodes = [];
    private readonly List<IDisposable> _disposables = [];

    public EndToEndTests()
    {
        for (int i = 0; i < 4; i++)
        {
            ushort port = (ushort)Interlocked.Increment(ref _nextPort);
            var relay = Tox.Create(new ToxOptions { StartPort = port, EndPort = port, LocalDiscoveryEnabled = false });
            _relays.Add(relay);
            _nodes.Add(new BootstrapNode { Host = "127.0.0.1", Port = port, PublicKey = Convert.ToHexString(relay.DhtId) });
            _ = Task.Run(() => relay.RunAsync(_cts.Token));
        }
        foreach (var relay in _relays.Skip(1))
            relay.Bootstrap("127.0.0.1", _nodes[0].Port, _nodes[0].PublicKey);
    }

    private sealed record Client(MainViewModel Vm, QueueDispatcher Ui, FakePlatform Platform, FakeNotifier Notifier,
        ProfileManager Profiles, AppServices Services);

    private Client Start(TempHome home, string name, bool create = true)
    {
        var profiles = new ProfileManager(home.Paths);
        var ui = new QueueDispatcher();
        var platform = new FakePlatform();
        var notifier = new FakeNotifier();
        var services = new AppServices(profiles, ui, platform, notifier, _ => Task.FromResult(_nodes.ToList()));
        var settings = new ProfileSettings
        {
            UseLanDiscovery = false,
            DownloadDirectory = Path.Combine(home.Paths.Root, "downloads"),
        };
        var session = create ? profiles.Create(name, null, settings) : profiles.Open(name, null);
        var vm = new MainViewModel(session, services, () => { });
        return new Client(vm, ui, platform, notifier, profiles, services);
    }

    private static void WaitUntil(Func<bool> condition, string what, params Client[] clients)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            foreach (var c in clients)
                c.Ui.Pump();
            if (condition())
                return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(what);
            Thread.Sleep(50);
        }
    }

    [Fact]
    public async Task TwoClients_BecomeFriends_ChatAndSendFiles()
    {
        var aliceHome = new TempHome();
        var bobHome = new TempHome();
        var alice = Start(aliceHome, "Alice");
        var bob = Start(bobHome, "Bob");
        _disposables.Add(alice.Vm);

        WaitUntil(() => alice.Vm.Connection != ToxConnection.None && bob.Vm.Connection != ToxConnection.None,
            "clients did not come online", alice, bob);

        // Alice adds Bob by Tox ID (with the qTox-style "tox:" prefix).
        alice.Vm.OpenAddFriendCommand.Execute(null);
        alice.Vm.AddFriendId = "tox:" + bob.Vm.ToxId;
        alice.Vm.AddFriendCommand.Execute(null);
        Assert.Null(alice.Vm.AddFriendError);
        Assert.Single(alice.Vm.Friends);

        bob.Platform.Active = false;
        WaitUntil(() => bob.Vm.Requests.Count == 1, "no friend request", alice, bob);
        Assert.Contains(bob.Notifier.Sent, n => n.Title == "Friend request");
        bob.Vm.Requests[0].AcceptCommand.Execute(null);
        Assert.Empty(bob.Vm.Requests);

        var bobAtAlice = alice.Vm.Friends[0];
        var aliceAtBob = bob.Vm.Friends[0];
        WaitUntil(() => bobAtAlice.IsOnline && aliceAtBob.IsOnline, "friends did not connect", alice, bob);
        WaitUntil(() => bobAtAlice.Name == "Bob" && aliceAtBob.Name == "Alice", "names not exchanged", alice, bob);

        // Messages, read receipts, unread counters and notifications.
        bob.Vm.SelectedFriend = null;
        alice.Vm.SelectedFriend = bobAtAlice;
        alice.Vm.MessageText = "ciao Bob!";
        alice.Vm.SendCommand.Execute(null);
        var sent = bobAtAlice.Messages.Last();
        WaitUntil(() => aliceAtBob.Messages.Any(m => m.Text == "ciao Bob!") && sent.Delivered, "message not delivered", alice, bob);
        Assert.Equal(1, aliceAtBob.Unread);
        Assert.Contains(bob.Notifier.Sent, n => n.Title == "Alice" && n.Body == "ciao Bob!");

        bob.Vm.SelectedFriend = aliceAtBob;
        Assert.Equal(0, aliceAtBob.Unread);
        bob.Vm.MessageText = "/me waves";
        bob.Vm.SendCommand.Execute(null);
        WaitUntil(() => bobAtAlice.Messages.Any(m => m.IsAction && m.ActionText == "* Bob waves"), "action not received", alice, bob);

        // File transfer: Alice sends, Bob accepts.
        var content = RandomNumberGenerator.GetBytes(150_000);
        var path = Path.Combine(aliceHome.Paths.Root, "holiday.jpg");
        File.WriteAllBytes(path, content);
        alice.Platform.NextFile = path;
        await alice.Vm.SendFileCommand.ExecuteAsync(null);

        WaitUntil(() => aliceAtBob.Messages.Any(m => m.IsFile && m.CanAccept), "file offer not received", alice, bob);
        var offer = aliceAtBob.Messages.Last(m => m.IsFile);
        Assert.Equal("holiday.jpg", offer.FileName);
        offer.AcceptCommand.Execute(null);
        var outgoing = bobAtAlice.Messages.Last(m => m.IsFile);
        WaitUntil(() => offer.State == TransferState.Completed && outgoing.State == TransferState.Completed,
            "file transfer did not complete", alice, bob);
        Assert.Equal(content, File.ReadAllBytes(offer.Model.FilePath!));
        Assert.StartsWith(Path.Combine(bobHome.Paths.Root, "downloads"), offer.Model.FilePath);

        // Avatar: sent to online friends, stored by public key.
        var avatar = RandomNumberGenerator.GetBytes(3000);
        alice.Vm.SetAvatar(avatar);
        string aliceKey = Convert.ToHexString(alice.Vm.Session.Tox.PublicKey);
        WaitUntil(() => bob.Vm.Session.Avatars.Get(aliceKey) is { } a && a.SequenceEqual(avatar), "avatar not received", alice, bob);

        // Bob logs out; Alice's message waits and is delivered when Bob is back.
        bob.Vm.Dispose();
        WaitUntil(() => !bobAtAlice.IsOnline, "Bob still online", alice);
        alice.Vm.MessageText = "are you there?";
        alice.Vm.SendCommand.Execute(null);
        var queued = bobAtAlice.Messages.Last();
        Assert.False(queued.Delivered);

        var bob2 = Start(bobHome, "Bob", create: false);
        _disposables.Add(bob2.Vm);
        _disposables.Add(aliceHome); // after the clients, which save on shutdown
        _disposables.Add(bobHome);
        var aliceAtBob2 = bob2.Vm.Friends.Single();
        Assert.Contains(aliceAtBob2.Messages, m => m.Text == "ciao Bob!"); // history survived the restart
        WaitUntil(() => queued.Delivered && aliceAtBob2.Messages.Any(m => m.Text == "are you there?"),
            "queued message not delivered after reconnection", alice, bob2);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            try { d.Dispose(); }
            catch (ObjectDisposedException) { }
        }
        _cts.Cancel();
        foreach (var relay in _relays)
            relay.Dispose();
        _cts.Dispose();
    }
}
