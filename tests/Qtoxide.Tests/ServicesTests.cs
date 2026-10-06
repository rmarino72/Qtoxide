using Qtoxide.Models;
using Qtoxide.Services;
using Qtoxide.ViewModels;
using Toxide;
using Toxide.State;

namespace Qtoxide.Tests;

public class ServicesTests
{
    [Fact]
    public void Profile_CreateThenOpen_KeepsIdentityAndName()
    {
        using var home = new TempHome();
        var manager = new ProfileManager(home.Paths);
        string address;
        using (var session = manager.Create("alice", null))
            address = session.Tox.Address.ToString();

        Assert.Equal(["alice"], manager.List());
        Assert.False(manager.IsEncrypted("alice"));
        using var reopened = manager.Open("alice", null);
        Assert.Equal(address, reopened.Tox.Address.ToString());
        Assert.Equal("alice", reopened.Tox.Name);
    }

    [Fact]
    public void Profile_WithPassword_IsEncryptedAndChecked()
    {
        using var home = new TempHome();
        var manager = new ProfileManager(home.Paths);
        manager.Create("bob", "hunter22").Dispose();

        Assert.True(manager.IsEncrypted("bob"));
        Assert.True(ToxEncryptSave.IsEncrypted(File.ReadAllBytes(home.Paths.ProfileFile("bob"))));
        Assert.Throws<ProfileException>(() => manager.Open("bob", null));
        Assert.Equal("Wrong password.", Assert.Throws<ProfileException>(() => manager.Open("bob", "wrong!!")).Message);
        manager.Open("bob", "hunter22").Dispose();
    }

    [Fact]
    public void ChangePassword_ReencryptsProfileAndHistory()
    {
        using var home = new TempHome();
        var manager = new ProfileManager(home.Paths);
        using (var session = manager.Create("carol", null))
        {
            session.History.Add("AA", new StoredMessage { Text = "secret history" });
            session.History.Flush();
            Assert.Contains("secret history", File.ReadAllText(home.Paths.HistoryFile("carol")));

            session.ChangePassword("newpass1");
        }

        Assert.True(manager.IsEncrypted("carol"));
        Assert.DoesNotContain("secret history", File.ReadAllText(home.Paths.HistoryFile("carol")));
        using var reopened = manager.Open("carol", "newpass1");
        Assert.Equal("secret history", reopened.History.Get("AA").Single().Text);
    }

    [Fact]
    public void Import_AcceptsToxcoreProfiles_AndRejectsOtherFiles()
    {
        using var home = new TempHome();
        var manager = new ProfileManager(home.Paths);
        using var tox = Tox.Create(new ToxOptions { StartPort = 0, EndPort = 0, LocalDiscoveryEnabled = false });
        var file = Path.Combine(home.Paths.Root, "qtox profile.tox");
        File.WriteAllBytes(file, tox.GetSaveData());

        var name = manager.Import(file);
        Assert.Equal("qtox profile", name);
        Assert.Equal("qtox profile 2", manager.Import(file));
        using (var session = manager.Open(name, null))
            Assert.Equal(tox.Address.ToString(), session.Tox.Address.ToString());

        var junk = Path.Combine(home.Paths.Root, "junk.tox");
        File.WriteAllText(junk, "not a profile");
        Assert.Throws<ProfileException>(() => manager.Import(junk));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    public void InvalidProfileNames_AreRejected(string name) => Assert.NotNull(ProfileManager.ValidateName(name));

    [Fact]
    public void LongMessages_AreSplitOnUtf8Boundaries()
    {
        var text = string.Concat(Enumerable.Repeat("héllo wörld 🙂 ", 300));
        var parts = MainViewModel.Split(text, Tox.MaxMessageLength).ToList();

        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.True(System.Text.Encoding.UTF8.GetByteCount(p) <= Tox.MaxMessageLength));
        Assert.Equal(text, string.Concat(parts));
    }

    [Fact]
    public void BootstrapList_IsParsed()
    {
        const string json = """
            {"nodes":[{"ipv4":"1.2.3.4","ipv6":"-","port":33445,"public_key":"AA","status_udp":true},
                      {"ipv4":"5.6.7.8","ipv6":"-","port":1,"public_key":"BB","status_udp":false},
                      {"ipv4":"-","ipv6":"2001:db8::1","port":2,"public_key":"CC","status_udp":true}]}
            """;
        var nodes = BootstrapNodes.Parse(json);
        Assert.Equal(["1.2.3.4", "2001:db8::1"], nodes.Select(n => n.Host));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..", "file..")]
    [InlineData("photo.jpg", "photo.jpg")]
    public void IncomingFileNames_CannotEscapeTheDownloadFolder(string name, string expected) =>
        Assert.Equal(expected, FileTransferManager.SafeName(name));
}
