# CognitiveCore Hybrid AI Workspace

A Windows desktop starter application with a .NET 8 WPF host and embedded CPython through Python.NET. C# owns execution: lifecycle, HTTPS requests, key handling, cancellation, SQLite and UI. Python owns intelligence helpers: reviewed agent prompts and small orchestration/context functions loaded through an allowlist. Groq is called by C# HttpClient over HTTPS. There is no local HTTP server.

## Structure

- `src/CognitiveCore.App` — WPF application, Python.NET runtime/bridge, Groq client, SQLite memory and append-only event ledger.
- `src/CognitiveCore.App/Python` — agent definitions, allowlisted module loader, orchestration helper, and optional arithmetic-only worker.
- `tests` — .NET architecture checks and Python standard-library unit tests.

## Requirements and first run

- Windows x64 and .NET 8 SDK.
- Matching CPython 3.11 x64 runtime distribution. Python.NET embeds native CPython; this project does not install or download Python.
- A Groq API key.

Set `COGNITIVECORE_PYTHON_DLL` to the full path of `python311.dll` and `COGNITIVECORE_PYTHON_HOME` to the CPython runtime directory. Ship a complete matching runtime including standard library and dependent native files. A DLL alone is insufficient. Check the chosen CPython embeddable/runtime layout and ensure imports work on a clean Windows machine. The managed project cannot bundle or infer a universal CPython layout.

```powershell
dotnet restore .\CognitiveCore.sln
dotnet build .\CognitiveCore.sln -c Release
$env:COGNITIVECORE_PYTHON_DLL = 'C:\runtime\python311.dll'
$env:COGNITIVECORE_PYTHON_HOME = 'C:\runtime'
dotnet run --project .\src\CognitiveCore.App\CognitiveCore.App.csproj
```

Databases are created under `%LOCALAPPDATA%\CognitiveCore`. Enter a key in the password field. The starter does not persist credentials. Request timeout is 60 seconds. Cancel propagates to the HTTPS request; Python bridge calls are short local functions and observe cancellation before they begin.

## Tests

```powershell
dotnet test .\CognitiveCore.sln -c Release
python -m unittest discover -s .\tests -p 'test_*.py'
```

## Optional worker and deployment

`safeworker.py` runs as a separate Python process with a JSON-line protocol and accepts only basic arithmetic expressions via a small AST allowlist. `PythonWorkerClient` imposes a deadline and kills the process tree on timeout. It is not arbitrary Python execution and is not a hostile-code sandbox. For untrusted code, use an OS-enforced boundary such as a locked-down Windows AppContainer or disposable VM with filesystem, network, CPU and memory limits; this sample does not implement that boundary.

```powershell
dotnet publish .\src\CognitiveCore.App\CognitiveCore.App.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
```

The publish output is not a single-file executable. Distribute the app files, Python.NET/native dependencies and matching CPython runtime sidecars. Validate on a clean x64 Windows machine without system Python installed using the exact runtime layout and environment settings.

## Architecture and limitations

WPF UI → C# coordinator → Python.NET bridge (GIL acquired per allowlisted call) → C# Groq `HttpClient` over HTTPS → local SQLite episode and event record. The runtime initializes once and calls `BeginAllowThreads`; Python calls run away from the UI and reacquire the GIL. Shutdown restores the initializing thread state. SQLite triggers reject event updates/deletes. Episodic memory is recent local conversation context, not vector search. Event records omit the key and conversation bodies. This is a production-oriented scaffold; installer/signing, retention controls and end-to-end verification of the chosen CPython layout remain deployment work.

## Corrections to the supplied sample

The FastAPI/local HTTP hop was removed. The pasted WPF sample used nonexistent `HttpResponseMessage.ContentTextAsync()`, lacked timeout/cancellation, sent the key in a local plain-HTTP JSON body, and did not safely isolate the claimed code execution. This implementation makes Groq calls directly from C# over HTTPS and confines the optional worker to arithmetic expressions. The starter model IDs are current replacements for the Llama models in the pasted sample; check Groq's model catalog before deploying because model availability changes.
