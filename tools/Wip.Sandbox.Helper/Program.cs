using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string PipeName = "WipSandboxHelper";
    private const string ProfileName = "WipSandboxWorker";
    private const uint TokenAllAccess = 0x000F01FF;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint ProcThreadAttributeSecurityCapabilities = 0x00020009;

    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WIP");
    private static readonly string TokenPath = Path.Combine(DataDirectory, "helper.token");
    private static readonly string RootPath = Path.Combine(DataDirectory, "allowed-root.txt");
    private static readonly string DotnetPath = Path.Combine(DataDirectory, "dotnet-path.txt");
    private static readonly string TempRootPath = Path.Combine(DataDirectory, "temp-root.txt");

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length == 0 || args[0] != "--serve")
        {
            Console.Error.WriteLine("Wip.Sandbox.Helper --serve is Windows-only.");
            return 2;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("unhandled: " + e.ExceptionObject);
        Serve();
        return 0;
    }

    private static void Serve()
    {
        while (true)
        {
            using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
            server.WaitForConnection();
            try
            {
                Handle(server);
            }
            catch (Exception exception)
            {
                Log("request failed: " + exception);
            }
        }
    }

    private static void Handle(NamedPipeServerStream server)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        var line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var request = JsonSerializer.Deserialize<LaunchRequest>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (request is null || !IsAuthorized(request.Token))
        {
            writer.WriteLine(JsonSerializer.Serialize(new { ok = false, error = "unauthorized" }));
            return;
        }

        var error = Validate(request);
        if (error is not null)
        {
            writer.WriteLine(JsonSerializer.Serialize(new { ok = false, error }));
            return;
        }

        var processId = Launch(request);
        writer.WriteLine(JsonSerializer.Serialize(new { ok = true, processId }));
    }

    private static string? Validate(LaunchRequest request)
    {
        var root = File.ReadAllText(RootPath).Trim();
        var worker = Path.GetFullPath(request.WorkerPath);
        var temp = Path.GetFullPath(request.Temp);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!worker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return "worker path is outside the allowed root";
        }

        var tempRoot = File.ReadAllText(TempRootPath).Trim().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!temp.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(temp))
        {
            return "temp path is outside the allowed temp root";
        }

        if (!File.Exists(worker))
        {
            return "worker not found";
        }

        if (!request.InputPipe.StartsWith("wip-sandbox-in-", StringComparison.Ordinal) ||
            !request.OutputPipe.StartsWith("wip-sandbox-out-", StringComparison.Ordinal))
        {
            return "invalid pipe names";
        }

        return null;
    }

    private static bool IsAuthorized(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied) || !File.Exists(TokenPath))
        {
            return false;
        }

        var expected = File.ReadAllText(TokenPath).Trim();
        return CryptographicEquals(expected, supplied);
    }

    private static bool CryptographicEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var diff = 0;
        for (var index = 0; index < left.Length; index++)
        {
            diff |= left[index] ^ right[index];
        }

        return diff == 0;
    }

    private static int Launch(LaunchRequest request)
    {
        using var input = new NamedPipeClientStream(".", request.InputPipe, PipeDirection.Out, PipeOptions.None);
        using var output = new NamedPipeClientStream(".", request.OutputPipe, PipeDirection.In, PipeOptions.None);
        input.Connect(5000);
        output.Connect(5000);
        SetHandleInformation(input.SafePipeHandle.DangerousGetHandle(), HandleFlagInherit, HandleFlagInherit);
        SetHandleInformation(output.SafePipeHandle.DangerousGetHandle(), HandleFlagInherit, HandleFlagInherit);

        var sid = EnsureProfile();
        try
        {
            var dotnet = File.ReadAllText(DotnetPath).Trim();
            var environment = BuildEnvironment(request.Temp, dotnet);
            var capabilities = new SecurityCapabilities { AppContainerSid = sid };
            var startup = new StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            startup.StartupInfo.dwFlags = StartfUseStdHandles;
            startup.StartupInfo.hStdInput = input.SafePipeHandle.DangerousGetHandle();
            startup.StartupInfo.hStdOutput = output.SafePipeHandle.DangerousGetHandle();
            startup.StartupInfo.hStdError = output.SafePipeHandle.DangerousGetHandle();

            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            startup.lpAttributeList = Marshal.AllocHGlobal(size);
            try
            {
                if (!InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, ref size) ||
                    !UpdateProcThreadAttribute(startup.lpAttributeList, 0, (IntPtr)ProcThreadAttributeSecurityCapabilities,
                        ref capabilities, (IntPtr)Marshal.SizeOf<SecurityCapabilities>(), IntPtr.Zero, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "AppContainer attributes failed.");
                }

                if (!OpenProcessToken(GetCurrentProcess(), TokenAllAccess, out var token))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed.");
                }

                try
                {
                    var commandLine = $"\"{dotnet}\" \"{request.WorkerPath}\"";
                    if (!CreateProcessAsUser(token, null, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                            ExtendedStartupInfoPresent | CreateUnicodeEnvironment, environment, request.Temp, ref startup, out var information))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed.");
                    }

                    var processId = (int)information.dwProcessId;
                    CloseHandle(information.hThread);
                    CloseHandle(information.hProcess);
                    Log("launched pid=" + processId);
                    return processId;
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                DeleteProcThreadAttributeList(startup.lpAttributeList);
                Marshal.FreeHGlobal(startup.lpAttributeList);
            }
        }
        finally
        {
            FreeSid(sid);
        }
    }

    private static IntPtr EnsureProfile()
    {
        var hr = CreateAppContainerProfile(ProfileName, "WIP Sandbox Worker", "WIP AppContainer worker profile", IntPtr.Zero, 0, out var sid);
        if (hr == 0)
        {
            return sid;
        }

        hr = DeriveAppContainerSidFromAppContainerName(ProfileName, out sid);
        if (hr == 0)
        {
            return sid;
        }

        throw new Win32Exception(hr, "Could not create or derive the AppContainer profile.");
    }

    private static string BuildEnvironment(string temp, string dotnet)
    {
        var builder = new StringBuilder();
        void Add(string key, string value) => builder.Append(key).Append('=').Append(value).Append('\0');
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Add("DOTNET_ROOT", Path.GetDirectoryName(dotnet)!);
        Add("DOTNET_CLI_HOME", temp);
        Add("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
        Add("TMP", temp);
        Add("TEMP", temp);
        Add("TMPDIR", temp);
        Add("SystemRoot", windows);
        Add("windir", windows);
        Add("PATH", Path.GetDirectoryName(dotnet) + ";" + Path.Combine(windows, "System32"));
        builder.Append('\0');
        return builder.ToString();
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(Path.Combine(DataDirectory, "helper.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }
        catch
        {
            // Logging must not terminate the helper.
        }
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CreateAppContainerProfile(string name, string displayName, string description, IntPtr capabilities, uint capabilityCount, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? applicationName, string commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, string environment, string currentDirectory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr attributeList, int flags, IntPtr attribute, ref SecurityCapabilities value, IntPtr size, IntPtr previousValue, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    private sealed class LaunchRequest
    {
        public string? Token { get; init; }
        public string WorkerPath { get; init; } = "";
        public string Temp { get; init; } = "";
        public string InputPipe { get; init; } = "";
        public string OutputPipe { get; init; } = "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }
}
