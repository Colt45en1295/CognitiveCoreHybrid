using System.Windows;
using CognitiveCore.App.Services;

namespace CognitiveCore.App;

public partial class MainWindow : Window
{
    private readonly HybridRuntime _runtime;

    public MainWindow()
    {
        InitializeComponent();

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CognitiveCore");
        Directory.CreateDirectory(appData);

        var memoryStore = new SqliteMemoryStore(Path.Combine(appData, "memory.db"));
        var ledger = new AppendOnlyEventLedger(Path.Combine(appData, "events.db"));
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
            BaseAddress = new Uri("https://api.groq.com/openai/v1/")
        };

        var groqClient = new GroqClient(httpClient);
        var pythonBridge = new PythonBridge();
        var worker = new PythonWorkerClient();
        _runtime = new HybridRuntime(pythonBridge, groqClient, memoryStore, ledger, worker);

        ModelSelector.ItemsSource = new[]
        {
            "llama-3.3-70b-versatile",
            "llama-3.1-8b-instant",
            "mixtral-8x7b-32768"
        };

        ModelSelector.SelectedIndex = 0;
        PromptBox.Text = "Generate a short architecture plan for a hybrid IDE runtime using C# orchestration and Python intelligence.";
    }

    private async void OnExecuteClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var apiKey = ApiKeyBox.Password;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ResponseBox.Text = "Please enter a Groq API key.";
                return;
            }

            var prompt = PromptBox.Text;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                ResponseBox.Text = "Please enter a prompt.";
                return;
            }

            ExecuteButton.IsEnabled = false;
            WorkerButton.IsEnabled = false;

            var model = ModelSelector.SelectedItem?.ToString() ?? "llama-3.3-70b-versatile";
            var response = await _runtime.ExecutePromptAsync(prompt, model, apiKey, CancellationToken.None);
            ResponseBox.Text = response;
        }
        catch (Exception ex)
        {
            ResponseBox.Text = $"Execution failed: {ex.Message}";
        }
        finally
        {
            ExecuteButton.IsEnabled = true;
            WorkerButton.IsEnabled = true;
        }
    }

    private async void OnWorkerClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ExecuteButton.IsEnabled = false;
            WorkerButton.IsEnabled = false;

            var expression = PromptBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(expression))
            {
                expression = "2 + 3 * 4";
            }

            var result = await _runtime.SafeEvaluateAsync(expression, CancellationToken.None);
            ResponseBox.Text = result;
        }
        catch (Exception ex)
        {
            ResponseBox.Text = $"Worker evaluation failed: {ex.Message}";
        }
        finally
        {
            ExecuteButton.IsEnabled = true;
            WorkerButton.IsEnabled = true;
        }
    }
}
