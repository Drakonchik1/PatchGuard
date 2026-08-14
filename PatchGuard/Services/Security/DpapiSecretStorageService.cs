using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PatchGuard.Services.Security;

/// <summary>
/// Windows DPAPI (CurrentUser) secret files under %LocalAppData%/PatchGuard/secrets.
/// </summary>
public sealed class DpapiSecretStorageService : ISecretStorageService
{
    private readonly string _directory;
    private readonly object _gate = new();

    public DpapiSecretStorageService()
        : this(ResolveDefaultDirectory())
    {
    }

    public DpapiSecretStorageService(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string? GetSecret(string key)
    {
        var path = ResolvePath(key);
        lock (_gate)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var protectedBytes = File.ReadAllBytes(path);
                if (protectedBytes.Length == 0)
                {
                    return null;
                }

                var plain = ProtectedData.Unprotect(
                    protectedBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public void SetSecret(string key, string? value)
    {
        var path = ResolvePath(key);
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            var plain = Encoding.UTF8.GetBytes(value.Trim());
            try
            {
                var protectedBytes = ProtectedData.Protect(
                    plain,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);
                AtomicWriteAllBytes(path, protectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    public bool HasSecret(string key)
    {
        return !string.IsNullOrEmpty(GetSecret(key));
    }

    private string ResolvePath(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var safe = SanitizeKey(key);
        return Path.Combine(_directory, $"{safe}.bin");
    }

    private static string SanitizeKey(string key)
    {
        var chars = key.Trim().ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(chars[i]) && chars[i] is not '-' and not '_')
            {
                chars[i] = '-';
            }
        }

        return new string(chars);
    }

    private static void AtomicWriteAllBytes(string path, ReadOnlySpan<byte> contents)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The secret path has no parent directory.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best effort after a failed replacement; preserve the original exception.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort after a failed replacement; preserve the original exception.
            }
        }
    }

    private static string ResolveDefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PatchGuard",
            "secrets");
}
