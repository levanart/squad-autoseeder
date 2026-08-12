using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.Json;
using Autoseeder.Client.Models;

namespace Autoseeder.Client.Services;

internal sealed class TokenStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "5thMR", "Autoseeder", "session.dat");

    public void Save(AuthState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var clear = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state));
        File.WriteAllBytes(_path, ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser));
    }

    public AuthState? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var clear = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AuthState>(clear);
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
