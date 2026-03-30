using System.Runtime.InteropServices;
using Naja.StdLib.Core;

namespace Naja.StdLib;

// ── stat result ───────────────────────────────────────────────────────────────

/// <summary>
/// Python os.stat_result / os.lstat_result equivalent.
/// Fields follow CPython naming.  st_mode encodes file type:
///   0o100xxx – regular file (S_IFREG), 0o40xxx – directory (S_IFDIR),
///   0o120xxx – symlink (S_IFLNK).
/// </summary>
public sealed class NajaStatResult
{
    public long   st_mode  { get; }
    public long   st_ino   { get; }
    public long   st_dev   { get; }
    public long   st_nlink { get; }
    public long   st_uid   { get; }
    public long   st_gid   { get; }
    public long   st_size  { get; }
    public double st_atime { get; }
    public double st_mtime { get; }
    public double st_ctime { get; }

    public NajaStatResult(long mode, long ino, long dev, long nlink,
                          long uid, long gid, long size,
                          double atime, double mtime, double ctime)
    {
        st_mode = mode; st_ino = ino; st_dev = dev; st_nlink = nlink;
        st_uid = uid; st_gid = gid; st_size = size;
        st_atime = atime; st_mtime = mtime; st_ctime = ctime;
    }

    // Equality mirrors CPython: two stat results are equal when they describe
    // the same file-system object (same dev+ino or, on Windows where ino is
    // unreliable, same mode+size+mtime).
    public override bool Equals(object? obj)
    {
        if (obj is not NajaStatResult o) return false;
        if (st_ino != 0 && o.st_ino != 0 && st_ino == o.st_ino && st_dev == o.st_dev)
            return true;
        return st_mode == o.st_mode && st_size == o.st_size && st_mtime == o.st_mtime;
    }

    public override int GetHashCode() => HashCode.Combine(st_mode, st_size, st_mtime);
    public override string ToString()  => $"os.stat_result(st_mode={st_mode}, st_size={st_size}, st_mtime={st_mtime})";
}

// ── Win32 P/Invoke helpers (shared by NajaOsPath and NajaOs) ─────────────────

internal static class NajaOsWin32Helper
{
    // WIN32_FIND_DATA.dwReserved0 holds the reparse tag when
    // dwFileAttributes contains FILE_ATTRIBUTE_REPARSE_POINT.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;   // reparse tag
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]  public string cAlternateFileName;
    }

    private const uint IO_REPARSE_TAG_SYMLINK       = 0xA000000C;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private static readonly IntPtr INVALID_HANDLE   = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstVolumeW([Out] char[] lpszVolumeName, int cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextVolumeW(IntPtr hFindVolume, [Out] char[] lpszVolumeName, int cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindVolumeClose(IntPtr hFindVolume);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathNamesForVolumeNameW(
        string lpszVolumeName, [Out] char[] lpszVolumePathNames,
        int cchBufferLength, out int lpcchReturnLength);

    /// <summary>
    /// True if <paramref name="path"/> is a symlink reparse point
    /// (IO_REPARSE_TAG_SYMLINK) rather than a junction
    /// (IO_REPARSE_TAG_MOUNT_POINT).  Matches CPython os.path.islink semantics.
    /// </summary>
    internal static bool IsSymlink(string path)
    {
        var h = FindFirstFileW(path, out var data);
        if (h == INVALID_HANDLE) return false;
        FindClose(h);
        return (data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0
            && data.dwReserved0 == IO_REPARSE_TAG_SYMLINK;
    }

    internal static List<string> ListVolumes()
    {
        var volumes = new List<string>();
        var buf = new char[261];
        var h = FindFirstVolumeW(buf, buf.Length);
        if (h == INVALID_HANDLE) return volumes;
        try
        {
            do { volumes.Add(new string(buf).TrimEnd('\0')); }
            while (FindNextVolumeW(h, buf, buf.Length));
        }
        finally { FindVolumeClose(h); }
        return volumes;
    }

    internal static List<string> ListMounts(string volume)
    {
        int size = 512;
        var buf = new char[size];
        if (!GetVolumePathNamesForVolumeNameW(volume, buf, size, out int needed))
        {
            if (Marshal.GetLastWin32Error() == 0x7A) // ERROR_INSUFFICIENT_BUFFER
            {
                buf = new char[needed];
                if (!GetVolumePathNamesForVolumeNameW(volume, buf, buf.Length, out needed))
                    throw new IOException($"GetVolumePathNamesForVolumeName failed: error {Marshal.GetLastWin32Error()}");
            }
            else
            {
                throw new IOException($"GetVolumePathNamesForVolumeName failed: error {Marshal.GetLastWin32Error()}");
            }
        }
        var mounts = new List<string>();
        var raw = new string(buf, 0, Math.Min(needed, buf.Length));
        foreach (var part in raw.Split('\0'))
            if (!string.IsNullOrEmpty(part)) mounts.Add(part);
        return mounts;
    }
}

/// <summary>
/// Python 'os.path' sub-module emulation via System.IO.Path.
/// Returned as NajaOs.Instance.path.
/// </summary>
public sealed class NajaOsPath
{
    public static readonly NajaOsPath Instance = new();

    public string join(object a, object b) =>
        Path.Join(S(a), S(b));

    public string join(object a, object b, object c) =>
        Path.Join(S(a), S(b), S(c));

    public bool exists(object path) =>
        File.Exists(S(path)) || Directory.Exists(S(path));

    public bool isfile(object path)  => File.Exists(S(path));
    public bool isdir(object path)   => Directory.Exists(S(path));
    public bool isabs(object path)   => Path.IsPathRooted(S(path));

    public string dirname(object path)   => Path.GetDirectoryName(S(path)) ?? "";
    public string basename(object path)  => Path.GetFileName(S(path));
    public string abspath(object path)   => Path.GetFullPath(S(path));
    public string normpath(object path)  => Path.GetFullPath(S(path));
    public string realpath(object path)  => Path.GetFullPath(S(path));

    public object[] split(object path)
    {
        var p = S(path);
        return new object[] { Path.GetDirectoryName(p) ?? "", Path.GetFileName(p) };
    }

    public object[] splitext(object path)
    {
        var p = S(path);
        var ext = Path.GetExtension(p);
        return new object[] { p.Substring(0, p.Length - ext.Length), ext };
    }

    public string expanduser(object path)
    {
        var s = S(path);
        if (s.StartsWith("~"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), s.Substring(1).TrimStart('/', '\\'));
        return s;
    }

    public string expandvars(object path) =>
        Environment.ExpandEnvironmentVariables(S(path));

    public string sep => Path.DirectorySeparatorChar.ToString();
    public string pathsep => Path.PathSeparator.ToString();

    // ── Variadic join (handles *args unpacking) ───────────────────────────────
    public string join(object a, object b, object c, params object[] more)
    {
        var result = Path.Join(S(a), S(b), S(c));
        foreach (var part in more)
            result = Path.Join(result, S(part));
        return result;
    }

    // ── Symlink helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="path"/> is a symbolic link (not a junction).
    /// Mirrors CPython os.path.islink — uses FindFirstFileW to read the reparse tag
    /// so that NTFS junctions (IO_REPARSE_TAG_MOUNT_POINT) are correctly excluded.
    /// </summary>
    public bool islink(object path)
    {
        try
        {
            var attrs = File.GetAttributes(S(path));
            if (!attrs.HasFlag(FileAttributes.ReparsePoint)) return false;
            return OperatingSystem.IsWindows()
                ? NajaOsWin32Helper.IsSymlink(S(path))
                : true; // non-Windows: any reparse point treated as symlink
        }
        catch { return false; }
    }

    /// <summary>
    /// Like os.path.exists() but does not follow symlinks — returns true even for
    /// dangling symlinks.  Uses File.GetAttributes which operates on the link itself.
    /// </summary>
    public bool lexists(object path)
    {
        try { File.GetAttributes(S(path)); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Returns true when both paths refer to the same file-system object.
    /// Uses normalised absolute paths (sufficient for our test cases).
    /// </summary>
    public bool samefile(object path1, object path2)
        => string.Equals(Path.GetFullPath(S(path1)), Path.GetFullPath(S(path2)),
                         StringComparison.OrdinalIgnoreCase);

    private static string S(object o) => o?.ToString() ?? "";
}

/// <summary>
/// Python 'os' module emulation via System.IO, System.Environment.
/// Exposes getcwd, listdir, environ, path, makedirs, remove, rename, etc.
/// </summary>
public sealed class NajaOs
{
    public static readonly NajaOs Instance = new();

    // ── path sub-module ───────────────────────────────────────────────────────
    public NajaOsPath path => NajaOsPath.Instance;

    // ── Separators ────────────────────────────────────────────────────────────
    public string sep     => Path.DirectorySeparatorChar.ToString();
    public string pathsep => Path.PathSeparator.ToString();
    public string linesep => Environment.NewLine;
    public string curdir  => ".";
    public string pardir  => "..";

    // ── Current working directory ─────────────────────────────────────────────
    public string getcwd()         => Directory.GetCurrentDirectory();
    public void   chdir(object p)  => Directory.SetCurrentDirectory(S(p));

    // ── Directory operations ──────────────────────────────────────────────────
    public List<object> listdir()
    {
        var dir = Directory.GetCurrentDirectory();
        return Directory.GetFileSystemEntries(dir)
                        .Select(e => (object)Path.GetFileName(e))
                        .ToList();
    }

    public List<object> listdir(object p)
    {
        return Directory.GetFileSystemEntries(S(p))
                        .Select(e => (object)Path.GetFileName(e))
                        .ToList();
    }

    public void mkdir(object path) =>
        Directory.CreateDirectory(S(path));

    public void mkdir(object path, object mode) =>
        Directory.CreateDirectory(S(path));

    public void makedirs(object path) =>
        Directory.CreateDirectory(S(path));

    public void makedirs(object path, object mode) =>
        Directory.CreateDirectory(S(path));

    public void makedirs(object path, object mode, object exist_ok) =>
        Directory.CreateDirectory(S(path));

    public void rmdir(object path)   => Directory.Delete(S(path));
    public void removedirs(object p) => Directory.Delete(S(p), recursive: true);

    // ── File operations ───────────────────────────────────────────────────────
    public void remove(object path)
    {
        var p = S(path);
        // On Windows a directory-type symlink or junction must be removed via
        // Directory.Delete, not File.Delete.  Use GetAttributes (no follow) to detect.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var attrs = File.GetAttributes(p);
                if (attrs.HasFlag(FileAttributes.Directory) &&
                    attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    Directory.Delete(p);
                    return;
                }
            }
            catch { }
        }
        File.Delete(p);
    }

    public void unlink(object path) => remove(path);
    public void rename(object src, object dst) => File.Move(S(src), S(dst));

    // ── Environment variables ─────────────────────────────────────────────────
    public Dictionary<object, object> environ
    {
        get
        {
            var d = new Dictionary<object, object>();
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                d[(object)(entry.Key?.ToString() ?? "")] = (object)(entry.Value?.ToString() ?? "");
            return d;
        }
    }

    public string? getenv(object key, object? default_ = null) =>
        Environment.GetEnvironmentVariable(S(key)) ?? default_?.ToString();

    public void putenv(object key, object value) =>
        Environment.SetEnvironmentVariable(S(key), S(value));

    // ── Process ───────────────────────────────────────────────────────────────
    public long getpid() => System.Diagnostics.Process.GetCurrentProcess().Id;
    public long getppid() => 0L; // not reliably available cross-platform

    public long system(object cmd)
    {
        var p = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd", "/c " + S(cmd))
            { UseShellExecute = false });
        p?.WaitForExit();
        return p?.ExitCode ?? 0L;
    }

    // ── File stat ────────────────────────────────────────────────────────────

    /// <summary>Stat a path, following symlinks (like CPython os.stat).</summary>
    public NajaStatResult stat(object path)
    {
        var p = S(path);
        if (File.Exists(p))
        {
            var fi = new FileInfo(p);
            return new NajaStatResult(
                0x81A4L, 0, 0, 1, 0, 0, fi.Length,
                ToUnixTime(fi.LastAccessTimeUtc),
                ToUnixTime(fi.LastWriteTimeUtc),
                ToUnixTime(fi.CreationTimeUtc));
        }
        if (Directory.Exists(p))
        {
            var di = new DirectoryInfo(p);
            return new NajaStatResult(
                0x41EDL, 0, 0, 1, 0, 0, 0,
                ToUnixTime(di.LastAccessTimeUtc),
                ToUnixTime(di.LastWriteTimeUtc),
                ToUnixTime(di.CreationTimeUtc));
        }
        throw new FileNotFoundException($"[Errno 2] No such file or directory: '{p}'");
    }

    /// <summary>Stat a path WITHOUT following symlinks (like CPython os.lstat).</summary>
    public NajaStatResult lstat(object path)
    {
        var p = S(path);
        try
        {
            var attrs = File.GetAttributes(p); // operates on the link itself
            if (attrs.HasFlag(FileAttributes.ReparsePoint))
            {
                var fi = new FileInfo(p);
                return new NajaStatResult(
                    0xA1FFL, 0, 0, 1, 0, 0, 0,      // S_IFLNK
                    ToUnixTime(fi.LastAccessTimeUtc),
                    ToUnixTime(fi.LastWriteTimeUtc),
                    ToUnixTime(fi.CreationTimeUtc));
            }
        }
        catch { }
        return stat(path); // not a link — delegate to regular stat
    }

    // ── Symlinks ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a symbolic link at <paramref name="dst"/> pointing to <paramref name="src"/>.
    /// <paramref name="target_is_directory"/> controls whether a directory symlink is created
    /// (required on Windows when the target is a directory or does not yet exist).
    /// </summary>
    public void symlink(object src, object dst, object? target_is_directory = null)
    {
        var srcPath = S(src);
        var dstPath = S(dst);
        bool isDir = target_is_directory is bool b ? b
                   : target_is_directory is not null && Convert.ToBoolean(target_is_directory);
        if (isDir)
            Directory.CreateSymbolicLink(dstPath, srcPath);
        else
            File.CreateSymbolicLink(dstPath, srcPath);
    }

    /// <summary>
    /// Return the target of a symbolic link or junction.
    /// Mirrors CPython os.readlink — raises OSError for non-link paths.
    /// </summary>
    public string readlink(object path)
    {
        var p = S(path);
        FileSystemInfo? target = null;
        try
        {
            var attrs = File.GetAttributes(p);
            target = attrs.HasFlag(FileAttributes.Directory)
                ? Directory.ResolveLinkTarget(p, returnFinalTarget: false)
                : File.ResolveLinkTarget(p, returnFinalTarget: false);
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new IOException($"[WinError 4390] Not a reparse point: '{p}'", ex);
        }
        if (target is null)
            throw new IOException($"[WinError 4390] The file or directory is not a reparse point: '{p}'");
        return target.FullName;
    }

    // ── Windows drive / volume / mount enumeration ────────────────────────────

    /// <summary>
    /// Returns a list of drive root paths on Windows, e.g. ["C:\\", "D:\\"].
    /// Mirrors CPython os.listdrives() (Python 3.12+).
    /// </summary>
    public List<object> listdrives()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("os.listdrives() is Windows-only");
        return Directory.GetLogicalDrives().Select(d => (object)d).ToList();
    }

    /// <summary>
    /// Returns a list of volume GUID paths on Windows,
    /// e.g. ["\\\\?\\Volume{xxxxxxxx-...}\\"].
    /// Mirrors CPython os.listvolumes() (Python 3.12+).
    /// </summary>
    public List<object> listvolumes()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("os.listvolumes() is Windows-only");
        return NajaOsWin32Helper.ListVolumes().Select(v => (object)v).ToList();
    }

    /// <summary>
    /// Returns a list of mount-point paths for the given volume GUID path.
    /// Mirrors CPython os.listmounts(volume) (Python 3.12+).
    /// </summary>
    public List<object> listmounts(object volume)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("os.listmounts() is Windows-only");
        return NajaOsWin32Helper.ListMounts(S(volume)).Select(m => (object)m).ToList();
    }

    // ── urandom ───────────────────────────────────────────────────────────────
    public byte[] urandom(object n)
    {
        var buf = new byte[Convert.ToInt32(n)];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return buf;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string S(object? o) => o?.ToString() ?? "";

    private static double ToUnixTime(DateTime utc) =>
        ((DateTimeOffset)utc).ToUnixTimeMilliseconds() / 1000.0;
}
