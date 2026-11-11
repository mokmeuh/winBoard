namespace WinBoard.Services.Settings;

/// <summary>Protection d'un secret (DPAPI côté application Windows).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>Sans protection : pour les tests uniquement.</summary>
public sealed class PassthroughProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => (byte[])data.Clone();
    public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
}

/// <summary>Stockage de la clé API, séparé de settings.json.</summary>
public sealed class SecretStore
{
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public SecretStore(string directory, ISecretProtector protector, string fileName = "gemini.key")
    {
        _path = Path.Combine(directory, fileName);
        _protector = protector;
    }

    public bool HasApiKey => File.Exists(_path);

    public void SetApiKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { Clear(); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, _protector.Protect(System.Text.Encoding.UTF8.GetBytes(key.Trim())));
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>Renvoie la clé, ou null si absente ou illisible.</summary>
    public string? GetApiKey()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return System.Text.Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
