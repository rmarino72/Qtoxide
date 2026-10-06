using System.Text.Json;

namespace Qtoxide.Services;

/// <summary>
/// Bootstrap nodes from https://nodes.tox.chat (the list qTox uses), cached on disk for offline
/// starts, plus the user's own nodes.
/// </summary>
public static class BootstrapNodes
{
    private const string ListUrl = "https://nodes.tox.chat/json";

    /// <summary>Used only when the list was never downloaded.</summary>
    private static readonly BootstrapNode[] Fallback =
    [
        new() { Host = "144.217.167.73", Port = 33445, PublicKey = "7E5668E0EE09E19F320AD47902419331FFEE147BB3606769CFBE921A2A2FD34C" },
    ];

    public static async Task<List<BootstrapNode>> GetAsync(AppPaths paths, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync(ListUrl, cancellationToken).ConfigureAwait(false);
            var nodes = Parse(json);
            if (nodes.Count > 0)
            {
                JsonFiles.Save(paths.NodesCacheFile, nodes);
                return nodes;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // offline, or the service is down: use the cache
        }

        var cached = JsonFiles.Load<List<BootstrapNode>>(paths.NodesCacheFile);
        return cached.Count > 0 ? cached : Fallback.ToList();
    }

    internal static List<BootstrapNode> Parse(string json)
    {
        var result = new List<BootstrapNode>();
        using var doc = JsonDocument.Parse(json);
        foreach (var node in doc.RootElement.GetProperty("nodes").EnumerateArray())
        {
            if (node.TryGetProperty("status_udp", out var udp) && !udp.GetBoolean())
                continue;
            var host = node.GetProperty("ipv4").GetString();
            if (string.IsNullOrEmpty(host) || host == "-")
                host = node.GetProperty("ipv6").GetString();
            if (string.IsNullOrEmpty(host) || host == "-")
                continue;
            result.Add(new BootstrapNode
            {
                Host = host,
                Port = node.GetProperty("port").GetUInt16(),
                PublicKey = node.GetProperty("public_key").GetString() ?? "",
            });
        }
        return result;
    }
}
