using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python shutil module - high-level file operations.
/// Implements core functionality needed for test infrastructure:
/// rmtree (recursive directory deletion), copy/copy2 (file copying),
/// copytree (directory tree copying), move (rename/move).
/// </summary>
public class NajaShutil
{
    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    /// <summary>
    /// Delete a directory tree recursively, mirroring shutil.rmtree().
    /// Removes all files and directories within the path.
    /// </summary>
    /// <param name="path">Root directory to remove</param>
    /// <param name="ignore_errors">If True, errors are silently ignored</param>
    /// <param name="onexc">Error handler callable (not fully implemented)</param>
    public void rmtree(object path, object? ignore_errors = null, object? onexc = null)
    {
        var dir = S(path);
        bool ignoreErrors = ignore_errors is bool b ? b : false;

        try
        {
            if (!Directory.Exists(dir))
                return;

            // Handle symbolic links specially - don't traverse into them
            var attrs = File.GetAttributes(dir);
            if (OperatingSystem.IsWindows() &&
                (attrs & FileAttributes.ReparsePoint) != 0)
            {
                // It's a junction or symlink - just delete it
                Directory.Delete(dir);
                return;
            }

            // Recursively remove all files and subdirectories
            RemoveTreeRecursive(dir, ignoreErrors);
        }
        catch when (ignoreErrors)
        {
            // Silently ignore errors
        }
    }

    /// <summary>
    /// Copy a file, preserving modification time if preserve_times=True.
    /// Mirrors shutil.copy2() or shutil.copy() depending on parameters.
    /// </summary>
    public string copy2(object src, object dst)
    {
        var source = S(src);
        var dest = S(dst);

        // If dst is a directory, copy file into it with same name
        if (Directory.Exists(dest))
            dest = Path.Combine(dest, Path.GetFileName(source));

        File.Copy(source, dest, overwrite: true);

        try
        {
            // Preserve modification time
            var srcInfo = new FileInfo(source);
            var dstInfo = new FileInfo(dest);
            dstInfo.LastWriteTime = srcInfo.LastWriteTime;
        }
        catch
        {
            // If we can't preserve time, continue anyway
        }

        return dest;
    }

    /// <summary>
    /// Copy a file. Mirrors shutil.copy().
    /// </summary>
    public string copy(object src, object dst)
    {
        var source = S(src);
        var dest = S(dst);

        // If dst is a directory, copy file into it with same name
        if (Directory.Exists(dest))
            dest = Path.Combine(dest, Path.GetFileName(source));

        File.Copy(source, dest, overwrite: true);
        return dest;
    }

    /// <summary>
    /// Copy an entire directory tree, mirroring shutil.copytree().
    /// </summary>
    public string copytree(object src, object dst, object? symlinks = null, 
                          object? ignore = null, object? copy_function = null,
                          object? ignore_dangling_symlinks = null)
    {
        var source = S(src);
        var dest = S(dst);
        bool copySymlinks = symlinks is bool b ? b : false;

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        if (Directory.Exists(dest))
            throw new IOException($"Destination directory already exists: {dest}");

        CopyTreeRecursive(source, dest, copySymlinks);
        return dest;
    }

    /// <summary>
    /// Move a file or directory tree. Mirrors shutil.move().
    /// </summary>
    public string move(object src, object dst)
    {
        var source = S(src);
        var dest = S(dst);

        // If dst is a directory, move source into it
        if (Directory.Exists(dest) && !File.Exists(dest))
            dest = Path.Combine(dest, Path.GetFileName(source));

        if (File.Exists(source))
        {
            File.Move(source, dest, overwrite: true);
        }
        else if (Directory.Exists(source))
        {
            try
            {
                Directory.Move(source, dest);
            }
            catch (IOException)
            {
                // If destination exists, try to delete it first
                if (Directory.Exists(dest))
                    Directory.Delete(dest, recursive: true);
                Directory.Move(source, dest);
            }
        }
        else
        {
            throw new FileNotFoundException($"Source not found: {source}");
        }

        return dest;
    }

    // ── Private Helpers ────────────────────────────────────────────────────────

    private void RemoveTreeRecursive(string dir, bool ignoreErrors)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                try
                {
                    File.Delete(file);
                }
                catch when (ignoreErrors) { }
            }

            foreach (var subdir in Directory.GetDirectories(dir))
            {
                try
                {
                    var attrs = File.GetAttributes(subdir);
                    if (OperatingSystem.IsWindows() &&
                        (attrs & FileAttributes.ReparsePoint) != 0)
                    {
                        // It's a junction or symlink
                        Directory.Delete(subdir);
                    }
                    else
                    {
                        RemoveTreeRecursive(subdir, ignoreErrors);
                    }
                }
                catch when (ignoreErrors) { }
            }

            Directory.Delete(dir);
        }
        catch when (ignoreErrors) { }
    }

    private void CopyTreeRecursive(string src, string dst, bool copySymlinks)
    {
        Directory.CreateDirectory(dst);

        foreach (var file in Directory.GetFiles(src))
        {
            var destFile = Path.Combine(dst, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subdir in Directory.GetDirectories(src))
        {
            var dirName = Path.GetFileName(subdir);
            var destSubdir = Path.Combine(dst, dirName);

            var attrs = File.GetAttributes(subdir);
            if (OperatingSystem.IsWindows() &&
                (attrs & FileAttributes.ReparsePoint) != 0)
            {
                if (copySymlinks)
                {
                    try
                    {
                        var target = Directory.ResolveLinkTarget(subdir, returnFinalTarget: false);
                        if (target != null)
                            Directory.CreateSymbolicLink(destSubdir, target.FullName);
                    }
                    catch { }
                }
            }
            else
            {
                CopyTreeRecursive(subdir, destSubdir, copySymlinks);
            }
        }
    }
}
