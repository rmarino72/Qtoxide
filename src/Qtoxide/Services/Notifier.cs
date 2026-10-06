using System.Diagnostics;

namespace Qtoxide.Services;

/// <summary>Desktop notifications through the platform's own tool (macOS Notification Center, notify-send).</summary>
public interface INotifier
{
    void Notify(string title, string body);
}

public sealed class DesktopNotifier : INotifier
{
    public void Notify(string title, string body)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("osascript") { UseShellExecute = false };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add($"display notification {Quote(body)} with title {Quote(title)}");
                Process.Start(psi)?.Dispose();
            }
            else if (OperatingSystem.IsLinux())
            {
                var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false };
                psi.ArgumentList.Add(title);
                psi.ArgumentList.Add(body);
                Process.Start(psi)?.Dispose();
            }
            // Windows: unread counters and the window flash are used instead.
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // notification tool missing: not worth bothering the user
        }
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
