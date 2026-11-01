using System.Security.Cryptography;
using System.Text;

namespace CognitiveCore.App.Services;

public static class SecureKeyStorage
{
    public static string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            throw new ArgumentException("Secret cannot be empty.", nameof(plainText));
        }

        var data = Encoding.UTF8.GetBytes(plainText);
        var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedData);
    }

    public static string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            throw new ArgumentException("Protected secret cannot be empty.", nameof(protectedValue));
        }

        var encrypted = Convert.FromBase64String(protectedValue);
        var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }

    public static void Save(string secretName, string plainText)
    {
        var protectedValue = Protect(plainText);
        Environment.SetEnvironmentVariable($"COGNITIVECORE_SECRET_{secretName}", protectedValue, EnvironmentVariableTarget.User);
    }

    public static string Load(string secretName)
    {
        var protectedValue = Environment.GetEnvironmentVariable($"COGNITIVECORE_SECRET_{secretName}", EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            throw new InvalidOperationException($"Secret '{secretName}' not found.");
        }

        return Unprotect(protectedValue);
    }
}
