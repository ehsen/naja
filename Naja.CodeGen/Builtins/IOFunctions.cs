namespace Naja.CodeGen.Builtins;

/// <summary>
/// Input/output and file operations.
/// Implements print(), input(), open() and related file I/O functions.
/// </summary>
public static class IOFunctions
{
    /// <summary>Print Python values to console, matching Python's print() behavior.</summary>
    public static void Print(object[] args)
    {
        var output = string.Join(" ", args.Select(arg => TypeConversion.ToStr(arg)));
        Console.WriteLine(output);
    }

    /// <summary>Read a line from standard input with optional prompt.</summary>
    public static string Input(string prompt = "")
    {
        if (!string.IsNullOrEmpty(prompt))
            Console.Write(prompt);
        return Console.ReadLine() ?? "";
    }

    /// <summary>
    /// Open a file, returning a file object. Mirrors Python's open():
    ///   open(path)                      → text read, UTF-8
    ///   open(path, mode)                → mode like "r", "w", "a", "x", "rb", "wb", ...
    ///   open(path, mode, encoding)      → text with explicit encoding
    /// </summary>
    public static object? Open(object[] args)
    {
        if (args.Length == 0 || args[0] is null)
            throw new ArgumentException("open() expects at least a file path");

        var path = args[0]?.ToString() ?? "";
        var mode = args.Length > 1 && args[1] is not null ? args[1].ToString()! : "r";
        var encoding = args.Length > 2 && args[2] is not null ? args[2].ToString()! : "utf-8";

        return new NajaFile(path, mode, encoding);
    }
}

/// <summary>
/// Python file object returned by open(). Follows the same conventions as
/// Naja.StdLib.NajaStringIO: object-typed parameters with null defaults,
/// long return for write sizes, __enter__/__exit__ for context managers.
/// Supports text and binary modes r/w/a/x with the b/+ suffixes.
/// </summary>
public sealed class NajaFile
{
    private readonly string _path;
    private readonly string _mode;
    private readonly bool _binary;
    private readonly bool _readable;
    private readonly bool _writable;
    private readonly bool _append;
    private readonly bool _exclusive;   // "x" — fail if file exists
    private readonly bool _createTrunc; // "w" — create/truncate
    private readonly System.Text.Encoding _encoding;

    private System.IO.FileStream? _stream;
    private System.IO.StreamReader? _reader;
    private System.IO.StreamWriter? _writer;
    private bool _closed;

    public NajaFile(string path, string mode = "r", string encoding = "utf-8")
    {
        _path = path;
        _mode = string.IsNullOrEmpty(mode) ? "r" : mode;
        _binary = _mode.Contains('b');
        _readable = _mode.Contains('r') || _mode.Contains('+');
        _writable = _mode.Contains('w') || _mode.Contains('a') || _mode.Contains('+');
        _append = _mode.Contains('a');
        _exclusive = _mode.Contains('x');
        _createTrunc = _mode.Contains('w');
        _encoding = System.Text.Encoding.GetEncoding(encoding);

        if (!_readable && !_writable)
            throw new ArgumentException($"invalid mode: '{mode}'");

        var access = _readable && _writable ? System.IO.FileAccess.ReadWrite
                    : _readable ? System.IO.FileAccess.Read
                    : System.IO.FileAccess.Write;

        if (_append)
        {
            _stream = new System.IO.FileStream(path,
                System.IO.FileMode.Append, access, System.IO.FileShare.Read);
        }
        else if (_exclusive)
        {
            _stream = new System.IO.FileStream(path,
                System.IO.FileMode.CreateNew, access, System.IO.FileShare.Read);
        }
        else if (_createTrunc)
        {
            _stream = new System.IO.FileStream(path,
                System.IO.FileMode.Create, access, System.IO.FileShare.Read);
        }
        else if (_writable)
        {
            _stream = new System.IO.FileStream(path,
                System.IO.FileMode.OpenOrCreate, access, System.IO.FileShare.Read);
        }
        else
        {
            _stream = new System.IO.FileStream(path,
                System.IO.FileMode.Open, access, System.IO.FileShare.ReadWrite);
        }

        if (_readable)
            _reader = new System.IO.StreamReader(_stream, _encoding);
        if (_writable)
            _writer = new System.IO.StreamWriter(_stream, _encoding) { AutoFlush = true };
    }

    // ── Properties ─────────────────────────────────────────────────────────────

    public bool closed => _closed;
    public string mode => _mode;
    public string name => _path;

    // ── Write ──────────────────────────────────────────────────────────────────

    public long write(object text)
    {
        CheckOpen();
        if (_writer is null)
            throw new System.IO.IOException($"File not open for writing: '{_path}'");

        if (_binary)
        {
            var bytes = ToBytes(text);
            _stream!.Write(bytes, 0, bytes.Length);
            return bytes.Length;
        }
        var s = text?.ToString() ?? "";
        _writer.Write(s);
        return s.Length;
    }

    public long writelines(object lines)
    {
        CheckOpen();
        long n = 0;
        if (lines is System.Collections.IEnumerable seq && lines is not string)
        {
            foreach (var item in seq)
            {
                n += write(item is null ? "" : item.ToString()!);
            }
        }
        else
        {
            n = write(lines);
        }
        return n;
    }

    // ── Read ───────────────────────────────────────────────────────────────────

    public object read(object size = null!)
    {
        CheckOpen();
        if (_reader is null)
            throw new System.IO.IOException($"File not open for reading: '{_path}'");

        if (_binary)
        {
            return size is null
                ? _reader.BaseStream.ReadAllBytes()
                : _reader.BaseStream.ReadExactly(Convert.ToInt64(size));
        }

        if (size is null)
            return _reader.ReadToEnd();

        var buffer = new char[Convert.ToInt32(size)];
        var n = _reader.Read(buffer, 0, buffer.Length);
        return new string(buffer, 0, n);
    }

    public object readline(object size = null!)
    {
        CheckOpen();
        if (_reader is null)
            throw new System.IO.IOException($"File not open for reading: '{_path}'");

        if (size is null)
            return _reader.ReadLine() ?? "";

        var buffer = new char[Convert.ToInt32(size)];
        var n = 0;
        int ch;
        while (n < buffer.Length && (ch = _reader.Read()) != -1)
        {
            buffer[n++] = (char)ch;
            if (ch == '\n') break;
        }
        return new string(buffer, 0, n);
    }

    public System.Collections.Generic.List<object> readlines(object hint = null!)
    {
        var lines = new System.Collections.Generic.List<object>();
        if (_binary)
        {
            foreach (var line in read().ToString()!.Split('\n'))
                lines.Add(line);
        }
        else
        {
            string? line;
            while ((line = _reader!.ReadLine()) is not null)
                lines.Add(line);
        }
        return lines;
    }

    // ── Position ───────────────────────────────────────────────────────────────

    public long tell()
    {
        CheckOpen();
        return _binary ? _stream!.Position
             : _reader is not null ? _reader is var _ ? _stream!.Position : 0
             : _stream!.Position;
    }

    public long seek(object offset, object whence = null!)
    {
        CheckOpen();
        var origin = whence is null ? System.IO.SeekOrigin.Begin
                   : (System.IO.SeekOrigin)Convert.ToInt32(whence);
        _reader?.DiscardBufferedData();
        _stream!.Seek(Convert.ToInt64(offset), origin);
        return _stream.Position;
    }

    public void flush()
    {
        CheckOpen();
        _writer?.Flush();
        _stream?.Flush();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public void close()
    {
        if (_closed) return;
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _closed = true;
    }

    public NajaFile __enter__()
    {
        CheckOpen();
        return this;
    }

    public void __exit__(object exc_type, object exc_val, object exc_tb) => close();

    public override string ToString() =>
        $"<_io.TextIOWrapper name='{_path}' mode='{_mode}' encoding='{_encoding.WebName}'>";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void CheckOpen()
    {
        if (_closed)
            throw new System.IO.IOException($"I/O operation on closed file: '{_path}'");
    }

    private static byte[] ToBytes(object value) => value switch
    {
        byte[] b => b,
        string s => System.Text.Encoding.UTF8.GetBytes(s),
        _ => System.Text.Encoding.UTF8.GetBytes(value?.ToString() ?? "")
    };
}

internal static class FileStreamReadExtensions
{
    public static byte[] ReadAllBytes(this System.IO.Stream stream)
    {
        using var ms = new System.IO.MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static byte[] ReadExactly(this System.IO.Stream stream, long count)
    {
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            var n = stream.Read(buf, read, (int)(count - read));
            if (n <= 0) break;
            read += n;
        }
        if (read < count) System.Array.Resize(ref buf, read);
        return buf;
    }
}