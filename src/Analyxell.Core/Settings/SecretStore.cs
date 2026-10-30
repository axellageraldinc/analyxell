using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Analyxell.Core.Settings;

/// <summary>Stores small secrets (such as an API key) outside the settings file.</summary>
public interface ISecretStore
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

/// <summary>
/// Windows DPAPI storage scoped to the current Windows user. The encrypted blob is useless on another
/// account or machine, and the key never appears in settings, logs, captures or the repository.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("analyxell.v1");

    public static string DefaultDirectory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Analyxell", "secrets");

    public string? Get(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Set(string name, string value)
    {
        Directory.CreateDirectory(directory);
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(name), cipher);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string name)
    {
        var safe = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0)
        {
            throw new ArgumentException("Secret name must contain letters or digits.", nameof(name));
        }

        return System.IO.Path.Combine(directory, safe + ".bin");
    }
}
