using Toxide.State;

namespace Qtoxide.Services;

/// <summary>Atomic file writes, optionally encrypted with the profile's password key (toxEsave format).</summary>
internal static class SecureFile
{
    public static void Write(string path, byte[] data, ToxPassKey? key)
    {
        var bytes = key is null ? data : key.Encrypt(data);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    public static byte[]? Read(string path, ToxPassKey? key)
    {
        if (!File.Exists(path))
            return null;
        var bytes = File.ReadAllBytes(path);
        if (key is null)
            return ToxEncryptSave.IsEncrypted(bytes) ? null : bytes;
        return ToxEncryptSave.IsEncrypted(bytes) ? key.Decrypt(bytes) : bytes;
    }
}
