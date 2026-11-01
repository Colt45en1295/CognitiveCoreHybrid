using System.Diagnostics;
using Python.Runtime;

namespace CognitiveCore.App.Services;

public sealed class PythonWorkerClient
{
    public async Task<string> EvaluateAsync(string expression, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new InvalidOperationException("Expression cannot be empty.");
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "safe_worker.py");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("The safe worker script was not found.", scriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{scriptPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the safe Python worker process.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        var payload = System.Text.Json.JsonSerializer.Serialize(new { expression });

        try
        {
            await process.StandardInput.WriteLineAsync(payload).WaitAsync(timeoutCts.Token, cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var exitTask = process.WaitForExitAsync(timeoutCts.Token);
            await exitTask;

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Python worker exited with code {process.ExitCode}: {stderr}");
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                return "No result returned.";
            }

            using var document = System.Text.Json.JsonDocument.Parse(stdout.Trim());
            if (!document.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
            {
                var message = document.RootElement.TryGetProperty("error", out var errorValue)
                    ? errorValue.GetString() ?? "Unknown worker error"
                    : "Unknown worker error";
                throw new InvalidOperationException(message);
            }

            return document.RootElement.TryGetProperty("value", out var value) ? value.ToString() : "No value returned.";
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
    }
}

public sealed class PythonBridge : IDisposable
{
    private readonly object _syncRoot = new();
    private bool _initialized;

    public void EnsureInitialized()
    {
        lock (_syncRoot)
        {
            if (_initialized)
            {
                return;
            }

            var pythonDll = Environment.GetEnvironmentVariable("COGNITIVECORE_PYTHON_DLL");
            var pythonHome = Environment.GetEnvironmentVariable("COGNITIVECORE_PYTHON_HOME");

            if (string.IsNullOrWhiteSpace(pythonDll) || string.IsNullOrWhiteSpace(pythonHome))
            {
                throw new InvalidOperationException("Set COGNITIVECORE_PYTHON_DLL and COGNITIVECORE_PYTHON_HOME to a valid CPython 3.11 runtime before using the Python bridge.");
            }

            if (!File.Exists(pythonDll))
            {
                throw new FileNotFoundException("Python DLL not found.", pythonDll);
            }

            if (!Directory.Exists(pythonHome))
            {
                throw new DirectoryNotFoundException("Python runtime home not found.");
            }

            Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("PYTHONPATH", pythonHome, EnvironmentVariableTarget.Process);
            Runtime.PythonDLL = pythonDll;

            PythonEngine.Initialize();
            _initialized = true;
        }
    }

    public async Task<string> BuildContextAsync(string prompt, IReadOnlyCollection<string> history, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            EnsureInitialized();

            using (Py.GIL())
            {
                using var scope = Py.CreateScope();
                var pythonDirectory = Path.Combine(AppContext.BaseDirectory, "Python");
                scope.Exec($"import sys; sys.path.insert(0, r'{pythonDirectory.Replace("\\", "\\\\")}')");
                scope.Exec("import allowlist_loader");
                var loader = scope.Get("allowlist_loader");

                var historyList = new PyList();
                foreach (var item in history)
                {
                    historyList.Append(item);
                }

                var result = loader.InvokeMethod("build_context", prompt, historyList);
                return result.ToString();
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_initialized)
            {
                PythonEngine.Shutdown();
                _initialized = false;
            }
        }
    }
}
