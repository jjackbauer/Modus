using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Wip.Sandbox.WorkerClient;

internal sealed record WindowsContainedProcess(Process Process, StreamWriter Input, StreamReader Output, IntPtr Job);

internal static class WindowsAppContainer
{
    private const string ProfileName = "WipSandboxWorker";
    private const uint TokenAllAccess = 0x000F01FF;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint ProcThreadAttributeSecurityCapabilities = 0x00020009;

    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WIP",
        "appcontainer.sid");

    public static bool IsAvailable()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(MarkerPath))
        {
            return false;
        }

        if (DeriveAppContainerSidFromAppContainerName(ProfileName, out var sid) != 0)
        {
            return false;
        }

        FreeSid(sid);
        return true;
    }

    public static WindowsContainedProcess Start(string workerPath, SandboxWorkerOptions options, string temp)
    {
        if (!File.Exists(MarkerPath) ||
            DeriveAppContainerSidFromAppContainerName(ProfileName, out var sid) != 0)
        {
            throw new PlatformNotSupportedException(
                "The WipSandboxWorker AppContainer profile is not installed. Run tools/sandbox/Install-WipAppContainer.ps1 as administrator.");
        }

        try
        {
            var dotnet = SandboxWorkerLauncher.ResolveDotnet();
            var environment = BuildEnvironment(temp, dotnet);
            var process = Launch(dotnet, workerPath, temp, environment, sid, out var input, out var output);
            var job = WindowsJobObject.Create(process, options.MaxMemoryBytes, options.MaxProcesses);
            return new WindowsContainedProcess(process, input, output, job);
        }
        finally
        {
            FreeSid(sid);
        }
    }

    private static Process Launch(string dotnet, string worker, string temp, string environment, IntPtr sid, out StreamWriter input, out StreamReader output)
    {
        var security = new SecurityAttributes
        {
            nLength = Marshal.SizeOf<SecurityAttributes>(),
            bInheritHandle = true
        };
        if (!CreatePipe(out var stdinRead, out var stdinWrite, ref security, 0) ||
            !CreatePipe(out var stdoutRead, out var stdoutWrite, ref security, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the AppContainer pipes.");
        }

        SetHandleInformation(stdinWrite, HandleFlagInherit, 0);
        SetHandleInformation(stdoutRead, HandleFlagInherit, 0);

        var capabilities = new SecurityCapabilities { AppContainerSid = sid };
        var startup = new StartupInfoEx();
        startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
        startup.StartupInfo.dwFlags = StartfUseStdHandles;
        startup.StartupInfo.hStdInput = stdinRead;
        startup.StartupInfo.hStdOutput = stdoutWrite;
        startup.StartupInfo.hStdError = stdoutWrite;

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        startup.lpAttributeList = Marshal.AllocHGlobal(size);
        try
        {
            if (!InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, ref size) ||
                !UpdateProcThreadAttribute(
                    startup.lpAttributeList,
                    0,
                    (IntPtr)ProcThreadAttributeSecurityCapabilities,
                    ref capabilities,
                    (IntPtr)Marshal.SizeOf<SecurityCapabilities>(),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach AppContainer capabilities.");
            }

            if (!OpenProcessToken(GetCurrentProcess(), TokenAllAccess, out var token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the current process token.");
            }

            try
            {
                var commandLine = $"\"{dotnet}\" \"{worker}\"";
                Process process;
                if (CreateProcessAsUser(
                        token,
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        true,
                        ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                        environment,
                        temp,
                        ref startup,
                        out var processInformation))
                {
                    process = AttachProcess(processInformation);
                }
                else
                {
                    var firstError = Marshal.GetLastWin32Error();
                    if (!CreateProcess(
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            true,
                            ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                            environment,
                            temp,
                            ref startup,
                            out processInformation))
                    {
                        throw new Win32Exception(firstError,
                            $"AppContainer CreateProcessAsUser failed ({firstError}); CreateProcess fallback failed ({Marshal.GetLastWin32Error()}).");
                    }

                    process = AttachProcess(processInformation);
                }

                CloseHandle(stdinRead);
                CloseHandle(stdoutWrite);
                input = new StreamWriter(new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(stdinWrite, true), FileAccess.Write, 1, false))
                {
                    AutoFlush = true
                };
                output = new StreamReader(new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(stdoutRead, true), FileAccess.Read, 1, false));
                return process;
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

    private static Process AttachProcess(ProcessInformation information)
    {
        var process = Process.GetProcessById((int)information.dwProcessId);
        CloseHandle(information.hThread);
        CloseHandle(information.hProcess);
        return process;
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

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SecurityAttributes attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        string environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        string environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr attributeList, int flags, IntPtr attribute, ref SecurityCapabilities value, IntPtr size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
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
