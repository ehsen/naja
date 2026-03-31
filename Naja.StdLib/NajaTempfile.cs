using System;
using System.IO;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python tempfile module - temporary file and directory creation.
/// Provides utilities for creating temporary files and directories
/// with automatic cleanup support.
/// </summary>
public class NajaTempfile
{
    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    /// <summary>
    /// Create a temporary directory and return its path.
    /// Mirrors tempfile.mkdtemp().
    /// </summary>
    public static string mkdtemp(object? prefix = null, object? suffix = null, object? dir = null)
    {
        var prefixStr = prefix is not null ? S(prefix) : "tmp";
        var suffixStr = suffix is not null ? S(suffix) : "";
        var basePath = dir is not null ? S(dir) : Path.GetTempPath();

        // Create a unique directory name
        string tempDir;
        do
        {
            var randomPart = Guid.NewGuid().ToString("N").Substring(0, 8);
            tempDir = Path.Combine(basePath, $"{prefixStr}{randomPart}{suffixStr}");
        } while (Directory.Exists(tempDir));

        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Create a temporary file and return its path.
    /// Mirrors tempfile.mkstemp().
    /// Returns a tuple of (file descriptor, path) - descriptor is -1 for .NET.
    /// </summary>
    public static (int, string) mkstemp(object? prefix = null, object? suffix = null, object? dir = null)
    {
        var prefixStr = prefix is not null ? S(prefix) : "tmp";
        var suffixStr = suffix is not null ? S(suffix) : "";
        var basePath = dir is not null ? S(dir) : Path.GetTempPath();

        // Create a unique file name
        string tempFile;
        do
        {
            var randomPart = Guid.NewGuid().ToString("N").Substring(0, 8);
            tempFile = Path.Combine(basePath, $"{prefixStr}{randomPart}{suffixStr}");
        } while (File.Exists(tempFile));

        // Create the file
        File.WriteAllText(tempFile, "");
        
        // Return (-1, path) - file descriptor not supported in .NET
        return (-1, tempFile);
    }

    /// <summary>
    /// Get the directory used for temporary files.
    /// Mirrors tempfile.gettempdir().
    /// </summary>
    public static string gettempdir()
    {
        return Path.GetTempPath();
    }

    /// <summary>
    /// Get a temporary filename that is unique.
    /// Mirrors tempfile.mktemp().
    /// </summary>
    public static string mktemp(object? prefix = null, object? suffix = null, object? dir = null)
    {
        var prefixStr = prefix is not null ? S(prefix) : "tmp";
        var suffixStr = suffix is not null ? S(suffix) : "";
        var basePath = dir is not null ? S(dir) : Path.GetTempPath();

        // Return a unique file path (without creating it)
        string tempFile;
        do
        {
            var randomPart = Guid.NewGuid().ToString("N").Substring(0, 8);
            tempFile = Path.Combine(basePath, $"{prefixStr}{randomPart}{suffixStr}");
        } while (File.Exists(tempFile));

        return tempFile;
    }
}
