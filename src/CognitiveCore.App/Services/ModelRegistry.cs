namespace CognitiveCore.App.Services;

public sealed record ModelDefinition(
    string Name,
    string Provider,
    string Version,
    bool Enabled = true,
    string? Description = null);

public sealed class ModelRegistry
{
    private readonly Dictionary<string, ModelDefinition> _models;

    public ModelRegistry()
    {
        _models = new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["llama-3.3-70b-versatile"] = new("llama-3.3-70b-versatile", "groq", "3.3", true, "Main production reasoning model."),
            ["llama-3.1-8b-instant"] = new("llama-3.1-8b-instant", "groq", "3.1", true, "Compact, fast model."),
            ["mixtral-8x7b-32768"] = new("mixtral-8x7b-32768", "groq", "8x7b", true, "General-purpose model."),
        };
    }

    public ModelDefinition Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Model name is required.", nameof(name));
        }

        if (_models.TryGetValue(name, out var model) && model.Enabled)
        {
            return model;
        }

        throw new InvalidOperationException($"Model '{name}' is not in the approved registry.");
    }

    public IReadOnlyCollection<ModelDefinition> ApprovedModels => _models.Values.Where(x => x.Enabled).ToArray();
}
