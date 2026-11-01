using System.Security.Cryptography;
using System.Text;

namespace CognitiveCore.App.Security;

public sealed class PythonAllowlist
{
    private readonly Dictionary<string, string> _moduleHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["allowlist_loader"] = string.Empty,
        ["safe_worker"] = string.Empty,
        ["runtime_bootstrap"] = string.Empty,
        ["planner"] = string.Empty,
        ["executor"] = string.Empty,
    };

    public void ValidateModule(string moduleName, string modulePath)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new ArgumentException("Module name is required.", nameof(moduleName));
        }

        if (!_moduleHashes.ContainsKey(moduleName))
        {
            throw new InvalidOperationException($"Module '{moduleName}' is not in the trusted allowlist.");
        }

        if (!File.Exists(modulePath))
        {
            throw new FileNotFoundException($"Module '{moduleName}' was not found.", modulePath);
        }

        var actualHash = ComputeSha256(modulePath);
        var expected = _moduleHashes[moduleName];

        if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(actualHash, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Hash mismatch for module '{moduleName}'.");
        }
    }

    public void RegisterTrustedModule(string moduleName, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new ArgumentException("Module name is required.", nameof(moduleName));
        }

        _moduleHashes[moduleName] = expectedHash;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
