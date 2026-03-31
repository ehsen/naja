using System.Runtime.InteropServices;
using System.Text;

namespace Naja.StdLib;

/// <summary>
/// Python '_winapi' module emulation.
/// Exposes Windows-specific APIs used by CPython's test suite and stdlib.
/// Includes CreateJunction, GetCurrentProcess, GetProcessHandleCount.
/// </summary>
public sealed class NajaWinapi
{
    public static readonly NajaWinapi Instance = new();

    // ── CreateJunction ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an NTFS junction point at <paramref name="junctionPath"/> that
    /// redirects to <paramref name="sourcePath"/>.
    ///
    /// Mirrors CPython _winapi.CreateJunction(source_path, junction_path).
    /// The junction reparse data stores the target as a \??\-prefixed NT path
    /// (SubstituteName) and the plain Win32 path (PrintName), matching the
    /// layout produced by CPython's own implementation so that os.readlink()
    /// returns a \\?\-prefixed path consistent with CPython behaviour.
    /// </summary>
    public void CreateJunction(object sourcePath, object junctionPath)
        => CreateJunctionCore(sourcePath?.ToString() ?? "", junctionPath?.ToString() ?? "");

    // ── Process handle management ──────────────────────────────────────────────

    /// <summary>
    /// Get a pseudo-handle to the current process.
    /// Returns a special value (-1) that can be used with other process functions.
    /// Mirrors CPython _winapi.GetCurrentProcess().
    /// </summary>
    public IntPtr GetCurrentProcess()
    {
        // Return the special process handle (same as Windows API)
        return System.Diagnostics.Process.GetCurrentProcess().Handle;
    }

    /// <summary>
    /// Get the number of open handles in a process.
    /// Used to detect handle leaks in tests.
    /// Mirrors CPython _winapi.GetProcessHandleCount(handle).
    /// </summary>
    public int GetProcessHandleCount(object processHandle)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("GetProcessHandleCount is Windows-only");

        try
        {
            var handle = Convert.ToInt64(processHandle);
            if (!GetProcessHandleCount_Internal(new IntPtr(handle), out int count))
            {
                throw new SystemError($"GetProcessHandleCount failed: error {Marshal.GetLastWin32Error()}");
            }
            return count;
        }
        catch (Exception ex)
        {
            throw new SystemError($"GetProcessHandleCount error: {ex.Message}", ex);
        }
    }

    // ── P/Invoke declarations ─────────────────────────────────────────────────

    private const uint GENERIC_WRITE          = 0x40000000;
    private const uint FILE_SHARE_READ        = 0x00000001;
    private const uint FILE_SHARE_WRITE       = 0x00000002;
    private const uint FILE_SHARE_DELETE      = 0x00000004;
    private const uint OPEN_EXISTING          = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS   = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FSCTL_SET_REPARSE_POINT      = 0x000900A4;
    private const uint IO_REPARSE_TAG_MOUNT_POINT   = 0xA0000003;

    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessHandleCount(
        IntPtr hProcess, out int pdwHandleCount);

    // ── Core implementation ───────────────────────────────────────────────────

    /// <summary>
    /// Internal P/Invoke wrapper for GetProcessHandleCount
    /// </summary>
    private static bool GetProcessHandleCount_Internal(IntPtr hProcess, out int count)
    {
        return GetProcessHandleCount(hProcess, out count);
    }

    private static void CreateJunctionCore(string sourcePath, string junctionPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("_winapi.CreateJunction is Windows-only");

        // Resolve to absolute paths.
        var targetFull = Path.GetFullPath(sourcePath);
        var junctionFull = Path.GetFullPath(junctionPath);

        // The junction must point to a directory.
        if (!Directory.Exists(targetFull))
            throw new DirectoryNotFoundException($"Junction target not found: '{targetFull}'");

        // Create the (empty) directory that will become the junction.
        Directory.CreateDirectory(junctionFull);

        // SubstituteName: NT namespace path  (\??\C:\...)
        // PrintName:      Win32 namespace path (C:\...)
        var substituteBytes = Encoding.Unicode.GetBytes(@"\??\" + targetFull);
        var printBytes      = Encoding.Unicode.GetBytes(targetFull);

        // REPARSE_DATA_BUFFER layout for mount-point (junction):
        //   ULONG  ReparseTag             (4)
        //   USHORT ReparseDataLength      (2)
        //   USHORT Reserved               (2)
        //   USHORT SubstituteNameOffset   (2)
        //   USHORT SubstituteNameLength   (2)
        //   USHORT PrintNameOffset        (2)
        //   USHORT PrintNameLength        (2)
        //   WCHAR  PathBuffer[...]
        int substituteOffset = 0;
        int printOffset      = substituteBytes.Length + 2; // +2 for null terminator
        int pathBufferSize   = printOffset + printBytes.Length + 2; // final null terminator
        int reparseDataLen   = 8 + pathBufferSize; // 8 = four USHORTs for offsets/lengths

        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(IO_REPARSE_TAG_MOUNT_POINT);       // ReparseTag
        bw.Write((ushort)reparseDataLen);           // ReparseDataLength
        bw.Write((ushort)0);                        // Reserved
        bw.Write((ushort)substituteOffset);         // SubstituteNameOffset
        bw.Write((ushort)substituteBytes.Length);   // SubstituteNameLength
        bw.Write((ushort)printOffset);              // PrintNameOffset
        bw.Write((ushort)printBytes.Length);        // PrintNameLength
        bw.Write(substituteBytes);
        bw.Write((ushort)0);                        // null terminator for SubstituteName
        bw.Write(printBytes);
        bw.Write((ushort)0);                        // null terminator for PrintName
        var reparseData = ms.ToArray();

        // Open the directory with backup semantics so we can set the reparse point.
        var hDir = CreateFileW(
            junctionFull, GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (hDir == INVALID_HANDLE_VALUE)
            throw new IOException($"CreateJunction: failed to open '{junctionFull}': " +
                                  $"error {Marshal.GetLastWin32Error()}");

        try
        {
            var gcHandle = GCHandle.Alloc(reparseData, GCHandleType.Pinned);
            try
            {
                bool ok = DeviceIoControl(
                    hDir, FSCTL_SET_REPARSE_POINT,
                    gcHandle.AddrOfPinnedObject(), reparseData.Length,
                    IntPtr.Zero, 0, out _, IntPtr.Zero);
                if (!ok)
                    throw new IOException($"CreateJunction: DeviceIoControl failed: " +
                                          $"error {Marshal.GetLastWin32Error()}");
            }
            finally { gcHandle.Free(); }
        }
        finally { CloseHandle(hDir); }
    }

    // ── Exception types ────────────────────────────────────────────────────────

    /// <summary>
    /// Custom exception for _winapi errors
    /// </summary>
    public sealed class SystemError : Exception
    {
        public SystemError(string message) : base(message) { }
        public SystemError(string message, Exception inner) : base(message, inner) { }
    }
}
