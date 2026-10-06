namespace Qtoxide.Services;

public sealed class AppSettings
{
    public string? LastProfile { get; set; }
}

public sealed class BootstrapNode
{
    public string Host { get; set; } = "";
    public ushort Port { get; set; }
    public string PublicKey { get; set; } = "";

    public override string ToString() => $"{Host} {Port} {PublicKey}";

    /// <summary>"host port key", as typed in the settings page.</summary>
    public static BootstrapNode? Parse(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !ushort.TryParse(parts[1], out var port) || parts[2].Length != 64)
            return null;
        return new BootstrapNode { Host = parts[0], Port = port, PublicKey = parts[2] };
    }
}

public sealed class PendingFriendRequest
{
    public string PublicKey { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset Received { get; set; }
}

/// <summary>Per-profile preferences (not secret, stored in clear next to the profile).</summary>
public sealed class ProfileSettings
{
    public bool Notifications { get; set; } = true;
    public bool SendTypingNotifications { get; set; } = true;
    public string DownloadDirectory { get; set; } = AppPaths.DefaultDownloads;
    public bool UseLanDiscovery { get; set; } = true;
    public bool UseIpv6 { get; set; } = true;
    public List<BootstrapNode> CustomNodes { get; set; } = [];
    public List<PendingFriendRequest> PendingRequests { get; set; } = [];
}
