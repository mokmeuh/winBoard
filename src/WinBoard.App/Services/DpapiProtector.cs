using System.Security.Cryptography;
using WinBoard.Services.Settings;

namespace WinBoard.App.Services;

/// <summary>Protection de la clé API par DPAPI, liée au compte Windows courant.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("WinBoard.v1");

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
