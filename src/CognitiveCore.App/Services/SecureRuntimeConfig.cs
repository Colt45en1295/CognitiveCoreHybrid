namespace CognitiveCore.App.Services;

public static class SecureRuntimeConfig
{
    public static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Environment variable '{name}' is required.");
        }

        return value;
    }

    public static string GetGroqApiKey()
    {
        var configured = Environment.GetEnvironmentVariable("COGNITIVECORE_GROQ_KEY");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var userSecret = Environment.GetEnvironmentVariable("COGNITIVECORE_SECRET_groq_api_key", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(userSecret))
        {
            return SecureKeyStorage.Unprotect(userSecret);
        }

        throw new InvalidOperationException("No Groq API key is configured. Use DPAPI-backed user secret storage or COGNITIVECORE_GROQ_KEY.");
    }
}
