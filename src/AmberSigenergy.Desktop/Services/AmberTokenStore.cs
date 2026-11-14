using System.Security.Cryptography;
using System.Text;

namespace AmberSigenergy.Desktop.Services;

/// <summary>Stores the Amber API token encrypted with Windows DPAPI for the current user.</summary>
public static class AmberTokenStore
{
    private static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AmberSigenergy", "amber-token.dat");

    public static void Save(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token cannot be empty.", nameof(token));
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        File.WriteAllBytes(TokenPath, protectedBytes);
    }

    public static string? Load()
    {
        if (!File.Exists(TokenPath)) return null;
        var protectedBytes = File.ReadAllBytes(TokenPath);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
    }

    public static void Delete()
    {
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
    }
}
