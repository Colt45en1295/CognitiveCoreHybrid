using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CognitiveCore.App.Services;

public sealed class SandboxBroker
{
    private readonly string _workspaceRoot;
    private readonly string _inputDir;
    private readonly string _outputDir;

    public SandboxBroker(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Path.Combine(Path.GetTempPath(), $"cognitivecore-sandbox-{Guid.NewGuid():N}");
        _inputDir = Path.Combine(_workspaceRoot, "input");
        _outputDir = Path.Combine(_workspaceRoot, "output");
        Directory.CreateDirectory(_inputDir);
        Directory.CreateDirectory(_outputDir);
    }

    public async Task<string> ExecuteUntrustedAsync(string code, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Sandbox broker requires Windows Job Objects.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code cannot be empty.", nameof(code));
        }

        var inputFile = Path.Combine(_inputDir, $"input-{Guid.NewGuid():N}.py");
        var outputFile = Path.Combine(_outputDir, $"output-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(inputFile, code, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{inputFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = _workspaceRoot,
        };

        using var process = new Process { StartInfo = startInfo };
        using var sandbox = new WindowsJobObjectSandbox();

        sandbox.Configure(new WorkerQuotaPolicy
        {
            MemoryInMb = 256,
            CpuSeconds = 5,
            WallClockSeconds = 30,
            OutputLimitBytes = 50 * 1024,
            NetworkBlocked = true,
            AllowChildProcesses = false,
        });

        process.Start();
        sandbox.Assign(process);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Untrusted code execution failed: {stderr}");
        }

        await File.WriteAllTextAsync(outputFile, stdout, cancellationToken);
        return stdout;
    }
}

public sealed class WorkerQuotaPolicy
{
    public int MemoryInMb { get; set; } = 256;
    public int CpuSeconds { get; set; } = 5;
    public int WallClockSeconds { get; set; } = 30;
    public long OutputLimitBytes { get; set; } = 50 * 1024;
    public bool NetworkBlocked { get; set; } = true;
    public bool AllowChildProcesses { get; set; } = false;
}

public sealed class WindowsJobObjectSandbox : IDisposable
{
    private readonly SafeHandle _jobHandle;

    public WindowsJobObjectSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Job Objects are only available on Windows.");
        }

        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle.IsInvalid)
        {
            throw new InvalidOperationException("Failed to create Windows Job Object.");
        }
    }

    public void Configure(WorkerQuotaPolicy policy)
    {
        if (_jobHandle == null || _jobHandle.IsClosed || _jobHandle.IsInvalid)
        {
            throw new InvalidOperationException("Sandbox job handle is invalid.");
        }

        var limitInfo = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOBOBJECTLIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                        JOBOBJECTLIMIT.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION |
                        JOBOBJECTLIMIT.JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK,
            ActiveProcessLimit = 1,
            PerJobUserTimeLimit = 0,
            MaximumWorkingSetSize = UIntPtr.Zero,
            MinimumWorkingSetSize = UIntPtr.Zero,
        };

        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = limitInfo,
        };

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(extendedInfo, pointer, false);
            if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, pointer, (uint)size))
            {
                throw new InvalidOperationException("Failed to configure Windows Job Object sandbox limits.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void Assign(Process process)
    {
        if (process == null)
        {
            throw new ArgumentNullException(nameof(process));
        }

        if (!AssignProcessToJobObject(_jobHandle, process.SafeHandle.DangerousGetHandle()))
        {
            throw new InvalidOperationException("Failed to assign process to the sandbox Job Object.");
        }
    }

    public void Dispose()
    {
        _jobHandle?.Dispose();
    }

    private static SafeHandle CreateJobObject(IntPtr lpJobAttributes, string? name)
    {
        return new SafeJobHandle(CreateJobObjectW(lpJobAttributes, name), ownsHandle: true);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeHandle hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeHandle hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    private sealed class SafeJobHandle : SafeHandle
    {
        public SafeJobHandle(IntPtr handle, bool ownsHandle) : base(IntPtr.Zero, ownsHandle)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }

    private enum JobObjectInfoType : uint
    {
        ExtendedLimitInformation = 9,
    }

    [Flags]
    private enum JOBOBJECTLIMIT : uint
    {
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000,
        JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400,
        JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x00001000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerJobUserTimeLimit;
        public long MaximumWorkingSetSize;
        public long MinimumWorkingSetSize;
        public uint LimitFlags;
        public long ActiveProcessLimit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    }
}
