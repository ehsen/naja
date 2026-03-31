using System.Runtime.InteropServices;

namespace Naja.StdLib;

/// <summary>
/// Python 'msvcrt' module emulation.
/// Provides Windows-specific I/O functions from the C runtime.
/// 
/// This is a minimal implementation focused on test_windows requirements:
/// - msvcrt.get_osfhandle(fd) → int (file descriptor to OS handle)
/// - Other functions stubbed or skipped
/// 
/// This module is Windows-specific and should only be used on Windows.
/// </summary>
public sealed class NajaMsvcrt
{
    public static readonly NajaMsvcrt Instance = new();

    // ── P/Invoke declarations ──────────────────────────────────────────

    /// <summary>
    /// Windows kernel32 function to get handle from file descriptor.
    /// This is an internal CRT function but we can access it via P/Invoke.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr _get_osfhandle(int fd);

    // ── Main functions ────────────────────────────────────────────────

    /// <summary>
    /// Return the file handle for file descriptor fd.
    /// 
    /// Converts a Python file descriptor (integer) to a Windows OS handle (HANDLE).
    /// Used to interface between Python file operations and Windows APIs.
    /// 
    /// On Windows, this retrieves the actual Win32 HANDLE from the C runtime.
    /// The returned value can be used with Windows API functions.
    /// </summary>
    public int get_osfhandle(int fd)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("msvcrt.get_osfhandle is Windows-only");

        try
        {
            // Use _get_osfhandle from kernel32
            IntPtr handle = _get_osfhandle(fd);
            
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new OSError($"Bad file descriptor: {fd}");

            return (int)handle;
        }
        catch (DllNotFoundException)
        {
            // Fallback if kernel32 not available (shouldn't happen on Windows)
            throw new OSError($"Cannot convert file descriptor {fd} to OS handle");
        }
        catch (Exception ex)
        {
            throw new OSError($"Error converting file descriptor {fd}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get a single character from input, no echo.
    /// Stub - returns empty string (not needed for test_windows)
    /// </summary>
    public string getch()
    {
        throw new NotImplementedError("msvcrt.getch() is not implemented");
    }

    /// <summary>
    /// Get a single character from input, with echo.
    /// Stub - returns empty string (not needed for test_windows)
    /// </summary>
    public string getche()
    {
        throw new NotImplementedError("msvcrt.getche() is not implemented");
    }

    /// <summary>
    /// Check if a keystroke is available without blocking.
    /// Stub - returns false (not needed for test_windows)
    /// </summary>
    public bool kbhit()
    {
        return false;
    }

    /// <summary>
    /// Custom exception for msvcrt errors
    /// </summary>
    public sealed class OSError : System.IO.IOException
    {
        public OSError(string message) : base(message) { }
        public OSError(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// NotImplementedError for stubbed functions
    /// </summary>
    public sealed class NotImplementedError : NotImplementedException
    {
        public NotImplementedError(string message) : base(message) { }
    }
}
