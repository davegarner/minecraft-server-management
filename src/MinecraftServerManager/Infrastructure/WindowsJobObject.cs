using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MinecraftServerManager.Services;

/// <summary>
/// Places the Minecraft server in a Windows job object with KILL_ON_JOB_CLOSE, so that if the manager
/// process dies unexpectedly the server is terminated too instead of being orphaned (holding the world
/// lock and port, with no way to send it commands).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private nint _handle;

    private WindowsJobObject(nint handle) => _handle = handle;

    public static WindowsJobObject? TryAttach(Process process, ILogger logger)
    {
        var handle = CreateJobObject(0, null);
        if (handle == 0)
        {
            logger.LogWarning("CreateJobObject failed: {Error}", new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            return null;
        }

        var info = new JobObjectExtendedLimitInformationData();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformationData>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)size)
                || !AssignProcessToJobObject(handle, process.Handle))
            {
                logger.LogWarning("Could not attach the server process to a job object: {Error}",
                    new Win32Exception(Marshal.GetLastPInvokeError()).Message);
                CloseHandle(handle);
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return new WindowsJobObject(handle);
    }

    /// <summary>Closing the job terminates any process still in it, so only dispose once the server has exited.</summary>
    public void Dispose()
    {
        if (_handle == 0) return;
        CloseHandle(_handle);
        _handle = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationData
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint hJob, int jobObjectInfoClass, nint lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
