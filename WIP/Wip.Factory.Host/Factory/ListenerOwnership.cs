using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// Proof that the loopback port a candidate answers on is owned by the process tree the factory launched. A
/// successful TCP connect plus <c>Process.HasExited</c> is <b>not</b> identity: an unrelated listener can occupy the
/// port (v0.4 FAC-P1-01).
/// </summary>
public sealed record ListenerIdentity(bool Verified, int ListenerProcessId, string Proof, string Detail)
{
    public const string UnverifiedCategory = "candidate-identity-unverified";
    public const string UnsupportedPlatform = "unsupported-platform";
    public const string NoListener = "no-listener";
    public const string ForeignListener = "foreign-listener";

    public static ListenerIdentity Unverified(int listenerProcessId, string proof, string detail)
        => new(false, listenerProcessId, proof, detail);
}

/// <summary>Platform-specific listener ownership proof. Tests substitute nothing: the Windows prover is exercised directly.</summary>
public interface IListenerIdentityProver
{
    ListenerIdentity Prove(int port, int launcherProcessId);
}

/// <summary>
/// Windows proof via <c>GetExtendedTcpTable(TCP_TABLE_OWNER_PID_LISTENER)</c> plus an ancestry walk over the
/// Toolhelp process snapshot. The listener PID must be the launcher itself or one of its descendants.
/// Other platforms return the named <c>unsupported-platform</c> refusal rather than a guess.
/// </summary>
public sealed class WindowsTcpListenerIdentityProver : IListenerIdentityProver
{
    public const string ProofKind = "windows-tcp-owner-pid+process-tree@1";

    public ListenerIdentity Prove(int port, int launcherProcessId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ListenerIdentity.Unverified(0, ListenerIdentity.UnsupportedPlatform, "listener ownership is not implemented on this platform");
        }

        if (!TryGetOwningProcessId(port, out var listenerProcessId))
        {
            return ListenerIdentity.Unverified(0, ListenerIdentity.NoListener, $"no listener owns loopback port {port}");
        }

        if (listenerProcessId == launcherProcessId)
        {
            return new ListenerIdentity(true, listenerProcessId, ProofKind, $"port {port} is owned by launcher pid {launcherProcessId}");
        }

        if (IsInProcessTree(listenerProcessId, launcherProcessId))
        {
            return new ListenerIdentity(true, listenerProcessId, ProofKind, $"port {port} is owned by pid {listenerProcessId}, a descendant of launcher pid {launcherProcessId}");
        }

        return ListenerIdentity.Unverified(
            listenerProcessId,
            ListenerIdentity.ForeignListener,
            $"port {port} is owned by pid {listenerProcessId}, which is not the launcher {launcherProcessId} or its descendant");
    }

    /// <summary>The owning PID of a listening TCP socket, or false when no row owns the port.</summary>
    public static bool TryGetOwningProcessId(int port, out int processId)
    {
        processId = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
        if (result != ErrorInsufficientBuffer && result != 0)
        {
            return false;
        }

        if (size <= 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
            if (result != 0)
            {
                return false;
            }

            var count = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, 4);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(rowPointer, index * rowSize));
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)(row.LocalPort & 0xFFFF));
                if (localPort == port)
                {
                    processId = unchecked((int)row.OwningPid);
                    return processId > 0;
                }
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>True when <paramref name="processId"/> is <paramref name="rootProcessId"/> or one of its descendants.</summary>
    public static bool IsInProcessTree(int processId, int rootProcessId)
    {
        if (processId == rootProcessId)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows() || processId <= 0 || rootProcessId <= 0)
        {
            return false;
        }

        var parents = SnapshotParents();
        var current = processId;
        for (var depth = 0; depth < 64; depth++)
        {
            if (!parents.TryGetValue(current, out var parent) || parent <= 0)
            {
                return false;
            }

            if (parent == rootProcessId)
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static Dictionary<int, int> SnapshotParents()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == IntPtr.Zero)
        {
            return parents;
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return parents;
            }

            do
            {
                parents[unchecked((int)entry.ProcessId)] = unchecked((int)entry.ParentProcessId);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return parents;
    }

    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint Th32csSnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool sort,
        int addressFamily,
        int tableClass,
        uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>Reads the launcher's exit state without throwing after disposal.</summary>
internal static class CandidateProcess
{
    public static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
