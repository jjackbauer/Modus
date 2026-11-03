using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Wip.Sandbox.WorkerClient;

internal static class WindowsHelperLauncher
{
    private const string HelperPipe = "WipSandboxHelper";
    private const uint TokenQuery = 0x0008;
    private const int TokenIsAppContainer = 29;

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WIP");

    public static bool IsAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return File.Exists(Path.Combine(DataDirectory, "helper.token"));
    }

    public static WindowsContainedProcess Start(string workerPath, SandboxWorkerOptions options, string temp)
    {
        if (!IsAvailable())
        {
            throw new PlatformNotSupportedException(
                "The WIP sandbox helper is not installed. Run tools/sandbox/Install-WipAppContainer.ps1 as administrator.");
        }

        var suffix = Guid.NewGuid().ToString("N");
        var inputName = "wip-sandbox-in-" + suffix;
        var outputName = "wip-sandbox-out-" + suffix;
        using var input = CreateServerPipe(inputName, PipeDirection.Out);
        using var output = CreateServerPipe(outputName, PipeDirection.In);
        var inputWait = input.WaitForConnectionAsync();
        var outputWait = output.WaitForConnectionAsync();

        var request = JsonSerializer.Serialize(new
        {
            token = File.ReadAllText(Path.Combine(DataDirectory, "helper.token")).Trim(),
            workerPath,
            temp,
            inputPipe = inputName,
            outputPipe = outputName
        });

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

        var processId = result.GetProperty("processId").GetInt32();
        if (!inputWait.Wait(5000) || !outputWait.Wait(5000))
        {
            throw new SandboxWorkerException("containmentFailed", "The sandbox pipes did not connect.");
        }

        var process = Process.GetProcessById(processId);
        if (!IsAppContainerToken(process))
        {
            process.Kill(entireProcessTree: true);
            throw new SandboxWorkerException("containmentFailed", "The helper launched the worker without an AppContainer token.");
        }

        var job = WindowsJobObject.Create(process, options.MaxMemoryBytes, options.MaxProcesses);
        var inputWriter = new StreamWriter(input) { AutoFlush = true };
        var outputReader = new StreamReader(output);
        return new WindowsContainedProcess(process, inputWriter, outputReader, job);
    }

    private static NamedPipeServerStream CreateServerPipe(string name, PipeDirection direction)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, direction, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
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

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
