using System.Text.Json;

namespace CognitiveCore.App.Services;

public sealed class HybridRuntime
{
    private readonly PythonBridge _pythonBridge;
    private readonly GroqClient _groqClient;
    private readonly SqliteMemoryStore _memoryStore;
    private readonly AppendOnlyEventLedger _eventLedger;
    private readonly PythonWorkerClient _workerClient;

    public HybridRuntime(
        PythonBridge pythonBridge,
        GroqClient groqClient,
        SqliteMemoryStore memoryStore,
        AppendOnlyEventLedger eventLedger,
        PythonWorkerClient workerClient)
    {
        _pythonBridge = pythonBridge ?? throw new ArgumentNullException(nameof(pythonBridge));
        _groqClient = groqClient ?? throw new ArgumentNullException(nameof(groqClient));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _eventLedger = eventLedger ?? throw new ArgumentNullException(nameof(eventLedger));
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
    }

    public async Task<string> ExecutePromptAsync(string prompt, string model, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("Prompt cannot be empty.");
        }

        var history = await _memoryStore.GetRecentAsync(8, cancellationToken);
        var context = await _pythonBridge.BuildContextAsync(prompt, history, cancellationToken);
        var response = await _groqClient.SendChatCompletionAsync(apiKey, model, prompt, context, cancellationToken);

        await _memoryStore.AddAsync(prompt, response, cancellationToken);
        await _eventLedger.AppendAsync("conversation.completed", new
        {
            model,
            promptLength = prompt.Length,
            responseLength = response.Length,
            timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        return response;
    }

    public async Task<string> SafeEvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new InvalidOperationException("Expression cannot be empty.");
        }

        var result = await _workerClient.EvaluateAsync(expression, cancellationToken);

        await _eventLedger.AppendAsync("worker.evaluated", new
        {
            expression,
            result,
            timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        return result;
    }
}
