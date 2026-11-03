using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Wip.Sandbox.WorkerClient;

internal static class WindowsHelperLauncher
{
    private const string HelperPipe = "WipSandboxHelper";

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
        using var input = new NamedPipeServerStream(inputName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var output = new NamedPipeServerStream(outputName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
        var job = WindowsJobObject.Create(process, options.MaxMemoryBytes, options.MaxProcesses);
        var inputWriter = new StreamWriter(input) { AutoFlush = true };
        var outputReader = new StreamReader(output);
        return new WindowsContainedProcess(process, inputWriter, outputReader, job);
    }
}
