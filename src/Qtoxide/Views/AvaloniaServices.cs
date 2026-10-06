using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Qtoxide.Services;

namespace Qtoxide.Views;

public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

public sealed class AvaloniaPlatformServices(Window window) : IPlatformServices
{
    private static readonly FilePickerFileType ToxProfiles = new("Tox profiles") { Patterns = ["*.tox"] };

    public async Task<string?> PickFileToSendAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Send a file" });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickProfileToImportAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a Tox profile (qTox, uTox, Toxic…)",
            FileTypeFilter = [ToxProfiles, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickExportPathAsync(string suggestedName)
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export profile",
            SuggestedFileName = suggestedName,
            FileTypeChoices = [ToxProfiles],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Download folder" });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickImageAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an avatar",
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task SetClipboardAsync(string text)
    {
        if (window.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public bool IsWindowActive => window.IsActive;

    public void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no application registered for this file type
        }
    }

    public void RequestAttention()
    {
        // Avalonia has no cross-platform "flash the taskbar" API; bring the title to the user's attention instead.
        if (!window.IsActive)
            window.Title = "● " + (window.Title ?? "").TrimStart('●', ' ');
    }
}
