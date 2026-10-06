using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Qtoxide.Models;
using Qtoxide.Services;
using Qtoxide.ViewModels;
using Qtoxide.Views;
using Toxide;

[assembly: AvaloniaTestApplication(typeof(Qtoxide.Tests.TestAppBuilder))]

namespace Qtoxide.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Renders the main screens to screenshots/ (light and dark), as a visual check of the layout.</summary>
public class Screenshots
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots");

    private static void Capture(MainWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory(Folder);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(Folder, name + ".png"));
    }

    private static AppServices Services(TempHome home) => new(
        new ProfileManager(home.Paths), new AvaloniaDispatcher(), new FakePlatform(), new FakeNotifier(),
        _ => Task.FromResult(new List<BootstrapNode>()));

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Render(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var home = new TempHome();
        var services = Services(home);
        services.Profiles.Create("Rosario", "password1").Dispose();
        services.Profiles.Create("Work", null).Dispose();
        var shell = new MainWindowViewModel(services);
        var window = new MainWindow { DataContext = shell, Width = 1100, Height = 720 };
        window.Show();
        Capture(window, $"login-{theme}");

        var session = services.Profiles.Open("Work", null);
        var main = new MainViewModel(session, services, () => { }) { StatusMessage = "Coding a Tox client" };
        shell.Current = main;
        Populate(main);
        Capture(window, $"chat-{theme}");

        main.OpenSettingsCommand.Execute(null);
        Capture(window, $"settings-{theme}");
        main.Settings = null;

        main.OpenAddFriendCommand.Execute(null);
        main.AddFriendId = "not-a-valid-id";
        main.AddFriendCommand.Execute(null);
        Capture(window, $"addfriend-{theme}");

        window.Close();
        main.Dispose();
    }

    /// <summary>Fake friends and messages: enough to see every kind of entry.</summary>
    private static void Populate(MainViewModel main)
    {
        static string Key(char c) => new(c, 64);
        var anna = new FriendViewModel(0, Key('A'), _ => { })
            { Name = "Anna Bianchi", StatusMessage = "In Rome this week", IsOnline = true, IsTyping = true };
        var marco = new FriendViewModel(1, Key('B'), _ => { })
            { Name = "Marco", Status = ToxUserStatus.Away, IsOnline = true, Unread = 3 };
        var luca = new FriendViewModel(2, Key('C'), _ => { }) { Name = "Luca Verdi", StatusMessage = "qTox user" };
        var nameless = new FriendViewModel(3, Key('D'), _ => { }) { Status = ToxUserStatus.Busy, IsOnline = true };

        var t = DateTimeOffset.Now.AddMinutes(-30);
        void Add(StoredMessage m, string author) => anna.Messages.Add(new MessageViewModel(m, author));
        Add(new StoredMessage { Text = "Ciao! Did you get the photos from the trip?", Time = t }, "Anna Bianchi");
        Add(new StoredMessage { Text = "Not yet, can you send them again?", Outgoing = true, Delivered = true, Time = t.AddMinutes(1) }, "Me");
        Add(new StoredMessage { Kind = MessageKind.File, FileName = "colosseum.jpg", FileSize = 2_400_000, Time = t.AddMinutes(2),
            TransferState = TransferState.Completed, FilePath = "/nonexistent" }, "Anna Bianchi");
        var progress = new MessageViewModel(new StoredMessage { Kind = MessageKind.File, FileName = "trevi-fountain.mp4",
            FileSize = 48_000_000, Time = t.AddMinutes(3), TransferState = TransferState.Transferring }, "Anna Bianchi")
        {
            Progress = 42,
        };
        anna.Messages.Add(progress);
        Add(new StoredMessage { Kind = MessageKind.Action, Text = "is downloading the videos", Outgoing = true, Time = t.AddMinutes(4) }, "Rosario");
        Add(new StoredMessage { Text = "Great, thanks! Talk later 🙂", Outgoing = true, Delivered = false, Time = t.AddMinutes(5) }, "Me");

        foreach (var f in new[] { anna, marco, nameless, luca })
            main.Friends.Add(f);
        main.Requests.Add(new FriendRequestViewModel(Key('E'), "Hi, it's Giulia from the conference!", (_, _) => { }));
        main.SelectedFriend = anna;
    }
}
