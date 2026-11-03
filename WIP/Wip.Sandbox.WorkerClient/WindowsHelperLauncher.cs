using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Wip.Sandbox.WorkerClient;

internal static class WindowsHelperLauncher
{
    private const string HelperPipe = "WipSandboxHelper";
    private const uint TokenQuery = 0x0008;
    private const int TokenIsAppContainer = 29;
    private const uint HandleFlagInherit = 0x00000001;

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WIP");

    public static bool IsAvailable()
    {
        return OperatingSystem.IsWindows() && File.Exists(Path.Combine(DataDirectory, "helper.token"));
    }

    public static WindowsContainedProcess Start(string workerPath, SandboxWorkerOptions options, string temp)
    {
        if (!IsAvailable())
        {
            throw new PlatformNotSupportedException(
                "The WIP sandbox helper is not installed. Run tools/sandbox/Install-WipAppContainer.ps1 as administrator.");
        }

        var attributes = new SecurityAttributes { nLength = Marshal.SizeOf<SecurityAttributes>(), bInheritHandle = true };
        if (!CreatePipe(out var stdinRead, out var stdinWrite, ref attributes, 0) ||
            !CreatePipe(out var stdoutRead, out var stdoutWrite, ref attributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create helper pipes.");
        }

        SetHandleInformation(stdinWrite, HandleFlagInherit, 0);
        SetHandleInformation(stdoutRead, HandleFlagInherit, 0);

        var request = JsonSerializer.Serialize(new
        {
            token = File.ReadAllText(Path.Combine(DataDirectory, "helper.token")).Trim(),
            workerPath,
            temp,
            callerPid = Environment.ProcessId,
            stdinHandle = stdinRead.ToInt64(),
            stdoutHandle = stdoutWrite.ToInt64()
        });

        int processId;
        try
        {
            using var helper = new NamedPipeClientStream(".", HelperPipe, PipeDirection.InOut, PipeOptions.None);
            helper.Connect(5000);
            using (var writer = new StreamWriter(helper, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true })
            {
                writer.WriteLine(request);
            }

            string response;
            using (var reader = new StreamReader(helper, Encoding.UTF8, false, 1024, leaveOpen: true))
            {
                response = reader.ReadLine() ?? "";
            }

            var result = JsonDocument.Parse(response).RootElement;
            if (result.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
            {
                var error = result.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "unknown";
                throw new SandboxWorkerException("containmentFailed", $"The sandbox helper rejected the launch: {error}.");
            }

            processId = result.GetProperty("processId").GetInt32();
        }
        finally
        {
            CloseHandle(stdinRead);
            CloseHandle(stdoutWrite);
        }

        var process = Process.GetProcessById(processId);
        if (!IsAppContainerToken(process))
        {
            process.Kill(entireProcessTree: true);
            throw new SandboxWorkerException("containmentFailed", "The helper launched the worker without an AppContainer token.");
        }

        var job = WindowsJobObject.Create(process, options.MaxMemoryBytes, options.MaxProcesses);
        var input = new StreamWriter(new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(stdinWrite, true), FileAccess.Write, 1, false)) { AutoFlush = true };
        var output = new StreamReader(new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(stdoutRead, true), FileAccess.Read, 1, false));
        return new WindowsContainedProcess(process, input, output, job);
    }

    private static bool IsAppContainerToken(Process process)
    {
        if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            return GetTokenInformation(token, TokenIsAppContainer, out var value, sizeof(int), out _) && value != 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);
}
