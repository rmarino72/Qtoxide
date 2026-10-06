namespace Qtoxide.Services;

/// <summary>
/// Where Qtoxide keeps its data: %LOCALAPPDATA%\Qtoxide, ~/Library/Application Support/Qtoxide or
/// ~/.local/share/Qtoxide (override with QTOXIDE_HOME, e.g. for tests or portable installs).
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root
               ?? Environment.GetEnvironmentVariable("QTOXIDE_HOME")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qtoxide");
        Directory.CreateDirectory(ProfilesDirectory);
    }

    public string Root { get; }
    public string ProfilesDirectory => Path.Combine(Root, "profiles");
    public string NodesCacheFile => Path.Combine(Root, "nodes.json");
    public string AppSettingsFile => Path.Combine(Root, "app.json");

    public string ProfileFile(string profile) => Path.Combine(ProfilesDirectory, profile + ".tox");
    public string HistoryFile(string profile) => Path.Combine(ProfilesDirectory, profile + ".history");
    public string SettingsFile(string profile) => Path.Combine(ProfilesDirectory, profile + ".settings.json");
    public string AvatarsDirectory(string profile) => Path.Combine(ProfilesDirectory, profile + ".avatars");

    public static string DefaultDownloads =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
