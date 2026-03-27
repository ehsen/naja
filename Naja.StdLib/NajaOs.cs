using Naja.StdLib.Core;

namespace Naja.StdLib;

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
    public void remove(object path)           => File.Delete(S(path));
    public void unlink(object path)           => File.Delete(S(path));
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
    public object stat(object path)
    {
        var fi = new FileInfo(S(path));
        return new { st_size = fi.Length, st_mtime = ((DateTimeOffset)fi.LastWriteTimeUtc).ToUnixTimeSeconds() };
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
}
