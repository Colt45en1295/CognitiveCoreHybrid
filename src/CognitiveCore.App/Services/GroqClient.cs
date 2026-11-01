using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CognitiveCore.App.Services;

public sealed class GroqClient
{
    private readonly HttpClient _httpClient;

    public GroqClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> SendChatCompletionAsync(
        string apiKey,
        string model,
        string prompt,
        string context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("A Groq API key is required.");
        }

        var requestBody = new
        {
            model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = context },
                new { role = "user", content = prompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Groq API request failed ({(int)response.StatusCode}): {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Groq API response did not contain a valid choice.");
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message))
        {
            throw new InvalidOperationException("Groq API response did not contain a message.");
        }

        if (!message.TryGetProperty("content", out var content))
        {
            throw new InvalidOperationException("Groq API response did not contain content.");
        }

        return content.ValueKind == JsonValueKind.String ? content.GetString() ?? string.Empty : content.ToString();
    }
}
