using Toxide;

namespace Qtoxide.Services;

/// <summary>Avatars as PNG files, one per public key (qTox keeps them the same way), plus our own.</summary>
public sealed class AvatarStore
{
    /// <summary>qTox refuses bigger avatars; so do we.</summary>
    public const int MaxSize = 64 * 1024;

    private const string SelfName = "self";
    private readonly string _directory;

    public AvatarStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    private string PathOf(string key) => Path.Combine(_directory, key + ".png");

    public byte[]? Get(string publicKeyHex) => File.Exists(PathOf(publicKeyHex)) ? File.ReadAllBytes(PathOf(publicKeyHex)) : null;

    public byte[]? GetHash(string publicKeyHex) => Get(publicKeyHex) is { } data ? Tox.Hash(data) : null;

    public void Set(string publicKeyHex, byte[] png) => File.WriteAllBytes(PathOf(publicKeyHex), png);

    public void Remove(string publicKeyHex) => File.Delete(PathOf(publicKeyHex));

    public byte[]? Self
    {
        get => Get(SelfName);
        set
        {
            if (value is null)
                Remove(SelfName);
            else
                Set(SelfName, value);
        }
    }
}
