namespace Naja.StdLib;

/// <summary>
/// Python 'stat' module emulation.
/// Provides constants and functions for interpreting file mode bits.
/// 
/// Supports:
/// - S_ISDIR(), S_ISFILE(), S_ISLNK(), etc. to check file type
/// - S_IMODE() to extract permission bits
/// - Windows reparse point tag constants (IO_REPARSE_TAG_*)
/// - File mode constants (S_IFREG, S_IFDIR, S_IFLNK, etc.)
/// </summary>
public sealed class NajaStat
{
    public static readonly NajaStat Instance = new();

    // ── File type constants (in decimal, converted from octal) ─────────────────

    /// <summary>
    /// File mode: regular file (octal 0o100000 = 32768 decimal)
    /// </summary>
    public const int S_IFREG = 32768;

    /// <summary>
    /// File mode: directory (octal 0o040000 = 16384 decimal)
    /// </summary>
    public const int S_IFDIR = 16384;

    /// <summary>
    /// File mode: symbolic link (octal 0o120000 = 40960 decimal)
    /// </summary>
    public const int S_IFLNK = 40960;

    /// <summary>
    /// File mode: character device (octal 0o020000 = 8192 decimal)
    /// </summary>
    public const int S_IFCHR = 8192;

    /// <summary>
    /// File mode: block device (octal 0o060000 = 24576 decimal)
    /// </summary>
    public const int S_IFBLK = 24576;

    /// <summary>
    /// File mode: FIFO/named pipe (octal 0o010000 = 4096 decimal)
    /// </summary>
    public const int S_IFIFO = 4096;

    /// <summary>
    /// File mode: socket (octal 0o140000 = 49152 decimal)
    /// </summary>
    public const int S_IFSOCK = 49152;

    // ── Permission constants (in decimal, converted from octal) ────────────────

    /// <summary>
    /// Permission mask: all bits (octal 0o777 = 511 decimal)
    /// </summary>
    public const int S_IMODE_MASK = 511;

    /// <summary>
    /// Permission: read by owner (octal 0o400 = 256 decimal)
    /// </summary>
    public const int S_IRUSR = 256;

    /// <summary>
    /// Permission: write by owner (octal 0o200 = 128 decimal)
    /// </summary>
    public const int S_IWUSR = 128;

    /// <summary>
    /// Permission: execute by owner (octal 0o100 = 64 decimal)
    /// </summary>
    public const int S_IXUSR = 64;

    /// <summary>
    /// Permission: read by group (octal 0o040 = 32 decimal)
    /// </summary>
    public const int S_IRGRP = 32;

    /// <summary>
    /// Permission: write by group (octal 0o020 = 16 decimal)
    /// </summary>
    public const int S_IWGRP = 16;

    /// <summary>
    /// Permission: execute by group (octal 0o010 = 8 decimal)
    /// </summary>
    public const int S_IXGRP = 8;

    /// <summary>
    /// Permission: read by others (octal 0o004 = 4 decimal)
    /// </summary>
    public const int S_IROTH = 4;

    /// <summary>
    /// Permission: write by others (octal 0o002 = 2 decimal)
    /// </summary>
    public const int S_IWOTH = 2;

    /// <summary>
    /// Permission: execute by others (octal 0o001 = 1 decimal)
    /// </summary>
    public const int S_IXOTH = 1;

    // ── Windows reparse point tag constants ────────────────────────────────────

    /// <summary>
    /// Reparse tag: NTFS mount point / junction (IO_REPARSE_TAG_MOUNT_POINT)
    /// Value: 0xA0000003
    /// </summary>
    public const int IO_REPARSE_TAG_MOUNT_POINT = unchecked((int)0xA0000003);

    /// <summary>
    /// Reparse tag: Symbolic link (IO_REPARSE_TAG_SYMLINK)
    /// Value: 0xA000000C
    /// Used to identify symlinks (as opposed to junctions)
    /// </summary>
    public const int IO_REPARSE_TAG_SYMLINK = unchecked((int)0xA000000C);

    /// <summary>
    /// Reparse tag: HSM (Hierarchical Storage Management) (IO_REPARSE_TAG_HSM)
    /// Value: 0xC0000004
    /// </summary>
    public const int IO_REPARSE_TAG_HSM = unchecked((int)0xC0000004);

    /// <summary>
    /// Reparse tag: SIS (Single Instance Storage) (IO_REPARSE_TAG_SIS)
    /// Value: 0x80000007
    /// </summary>
    public const int IO_REPARSE_TAG_SIS = unchecked((int)0x80000007);

    /// <summary>
    /// Reparse tag: DFS (Distributed File System) (IO_REPARSE_TAG_DFS)
    /// Value: 0x8000000A
    /// </summary>
    public const int IO_REPARSE_TAG_DFS = unchecked((int)0x8000000A);

    /// <summary>
    /// Reparse tag: App Execution Link (IO_REPARSE_TAG_APPEXECLINK)
    /// Value: 0x8000001B
    /// Used by Windows for app alias shortcuts
    /// </summary>
    public const int IO_REPARSE_TAG_APPEXECLINK = unchecked((int)0x8000001B);

    // ── File type check functions ──────────────────────────────────────────────

    /// <summary>
    /// Check if mode represents a regular file
    /// </summary>
    public bool S_ISREG(int mode)
    {
        return (mode & 57344) == S_IFREG;  // 57344 = octal 0o170000
    }

    /// <summary>
    /// Check if mode represents a directory
    /// </summary>
    public bool S_ISDIR(int mode)
    {
        return (mode & 57344) == S_IFDIR;  // 57344 = octal 0o170000
    }

    /// <summary>
    /// Check if mode represents a symbolic link
    /// </summary>
    public bool S_ISLNK(int mode)
    {
        return (mode & 57344) == S_IFLNK;  // 57344 = octal 0o170000
    }

    /// <summary>
    /// Check if mode represents a character device
    /// </summary>
    public bool S_ISCHR(int mode)
    {
        return (mode & 57344) == S_IFCHR;  // 57344 = octal 0o170000
    }

    /// <summary>
    /// Check if mode represents a block device
    /// </summary>
    public bool S_ISBLK(int mode)
    {
        return (mode & 57344) == S_IFBLK;  // 57344 = octal 0o170000
    }

    /// <summary>
    /// Check if mode represents a FIFO/named pipe
    /// </summary>
    public bool S_ISFIFO(int mode)
    {
        return (mode & 57344) == S_IFIFO;  // 57344 = octal 0o170000
    }

    /// <summary>
    /// Check if mode represents a socket
    /// </summary>
    public bool S_ISSOCK(int mode)
    {
        return (mode & 57344) == S_IFSOCK;  // 57344 = octal 0o170000
    }

    /// <summary>
    /// Extract permission bits from mode
    /// </summary>
    public int S_IMODE(int mode)
    {
        return mode & S_IMODE_MASK;  // 511 = octal 0o777
    }
}
