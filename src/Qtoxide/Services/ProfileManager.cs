using System.Security.Cryptography;
using Toxide;
using Toxide.State;

namespace Qtoxide.Services;

public sealed class ProfileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lists, creates, opens, imports and deletes profiles (one .tox file each).</summary>
public sealed class ProfileManager
{
    private readonly AppPaths _paths;

    public ProfileManager(AppPaths paths) => _paths = paths;

    public AppPaths Paths => _paths;

    public IReadOnlyList<string> List() =>
        Directory.GetFiles(_paths.ProfilesDirectory, "*.tox")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool IsEncrypted(string name)
    {
        var path = _paths.ProfileFile(name);
        if (!File.Exists(path))
            return false;
        Span<byte> header = stackalloc byte[8];
        using var stream = File.OpenRead(path);
        return stream.Read(header) == 8 && ToxEncryptSave.IsEncrypted(header);
    }

    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Choose a profile name.";
        if (name.Length > 64)
            return "The name is too long.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name.StartsWith('.'))
            return "The name contains characters not allowed in file names.";
        return null;
    }

    public ProfileSession Create(string name, string? password, ProfileSettings? settings = null)
    {
        if (ValidateName(name) is { } error)
            throw new ProfileException(error);
        if (File.Exists(_paths.ProfileFile(name)))
            throw new ProfileException($"A profile named \"{name}\" already exists.");

        var key = string.IsNullOrEmpty(password) ? null : ToxPassKey.Derive(password);
        settings ??= new ProfileSettings();
        JsonFiles.Save(_paths.SettingsFile(name), settings);

        var tox = Tox.Create(Options(settings, null));
        tox.Name = name;
        var session = new ProfileSession(_paths, name, tox, key);
        session.Save();
        return session;
    }

    /// <summary>Opens a profile; <paramref name="password"/> is needed only for encrypted ones.</summary>
    public ProfileSession Open(string name, string? password)
    {
        var path = _paths.ProfileFile(name);
        if (!File.Exists(path))
            throw new ProfileException($"Profile \"{name}\" not found.");

        var data = File.ReadAllBytes(path);
        ToxPassKey? key = null;
        if (ToxEncryptSave.IsEncrypted(data))
        {
            if (string.IsNullOrEmpty(password))
                throw new ProfileException("This profile is protected by a password.");
            key = ToxPassKey.Derive(password, ToxPassKey.GetSalt(data)!);
            try
            {
                data = key.Decrypt(data);
            }
            catch (CryptographicException ex)
            {
                key.Dispose();
                throw new ProfileException("Wrong password.", ex);
            }
        }

        var settings = JsonFiles.Load<ProfileSettings>(_paths.SettingsFile(name));
        try
        {
            var tox = Tox.Create(Options(settings, data));
            return new ProfileSession(_paths, name, tox, key);
        }
        catch (ToxException ex)
        {
            key?.Dispose();
            throw new ProfileException(ex.Code == ToxErrorCode.BadSaveData ? "The profile file is damaged." : ex.Message, ex);
        }
    }

    /// <summary>Copies a .tox file (e.g. from qTox) into the profile folder; returns the new profile name.</summary>
    public string Import(string file)
    {
        var data = File.ReadAllBytes(file);
        // A plain profile starts with [u32 0][u32 0x15ed1b1f], an encrypted one with "toxEsave".
        bool plain = data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 0, 0, 0, 0, 0x1f, 0x1b, 0xed, 0x15 });
        if (!plain && !ToxEncryptSave.IsEncrypted(data))
            throw new ProfileException("This is not a Tox profile.");

        var baseName = Path.GetFileNameWithoutExtension(file);
        if (ValidateName(baseName) is not null)
            baseName = "imported";
        var name = baseName;
        for (int i = 2; File.Exists(_paths.ProfileFile(name)); i++)
            name = $"{baseName} {i}";

        File.Copy(file, _paths.ProfileFile(name));
        return name;
    }

    public void Delete(string name)
    {
        File.Delete(_paths.ProfileFile(name));
        File.Delete(_paths.HistoryFile(name));
        File.Delete(_paths.SettingsFile(name));
        if (Directory.Exists(_paths.AvatarsDirectory(name)))
            Directory.Delete(_paths.AvatarsDirectory(name), recursive: true);
    }

    private static ToxOptions Options(ProfileSettings settings, byte[]? saveData) => new()
    {
        Ipv6Enabled = settings.UseIpv6,
        LocalDiscoveryEnabled = settings.UseLanDiscovery,
        SaveDataType = saveData is null ? ToxSaveDataType.None : ToxSaveDataType.ToxSave,
        SaveData = saveData,
    };
}
