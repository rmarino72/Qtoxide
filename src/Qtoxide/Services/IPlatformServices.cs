namespace Qtoxide.Services;

/// <summary>What view models need from the windowing system (file pickers, clipboard...), mockable in tests.</summary>
public interface IPlatformServices
{
    Task<string?> PickFileToSendAsync();
    Task<string?> PickProfileToImportAsync();
    Task<string?> PickExportPathAsync(string suggestedName);
    Task<string?> PickFolderAsync();
    Task<string?> PickImageAsync();
    Task SetClipboardAsync(string text);
    bool IsWindowActive { get; }
    void OpenFile(string path);
    void RequestAttention();
}
