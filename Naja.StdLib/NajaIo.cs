using System.Text;
using System.Diagnostics.CodeAnalysis;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python 'io' module emulation.
/// 
/// Implements the core CPython io API:
///   io.StringIO([initial_value]) -> StringIO
///   io.BytesIO([initial_bytes])  -> BytesIO
///   io.TextIOWrapper(buffer, encoding='utf-8', ...) -> TextIOWrapper
///   io.DEFAULT_BUFFER_SIZE       -> int
/// </summary>
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class NajaIo
{
    public static readonly NajaIo Instance = new();

    // ── Constants ─────────────────────────────────────────────────────────────
    public long DEFAULT_BUFFER_SIZE => 8192;

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>io.StringIO(initial_value='', newline='\n') -> StringIO</summary>
    public NajaStringIO StringIO(object initial_value = null!, object newline = null!)
    {
        string init = initial_value != null ? Str(initial_value) : "";
        return new NajaStringIO(init);
    }

    /// <summary>io.BytesIO(initial_bytes=b'') -> BytesIO</summary>
    public NajaBytesIO BytesIO(object initial_bytes = null!)
    {
        byte[] init = initial_bytes is byte[] b ? b : Array.Empty<byte>();
        return new NajaBytesIO(init);
    }

    /// <summary>io.TextIOWrapper(buffer, encoding='utf-8') -> TextIOWrapper</summary>
    public NajaTextIOWrapper TextIOWrapper(object buffer, object encoding = null!, object errors = null!)
    {
        string enc = encoding != null ? Str(encoding) : "utf-8";
        return new NajaTextIOWrapper(buffer, enc);
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    internal static string Str(object o) => o switch
    {
        null   => "",
        string s => s,
        _      => o.ToString() ?? ""
    };
}

// ── StringIO ──────────────────────────────────────────────────────────────────

/// <summary>
/// Python io.StringIO — an in-memory text I/O stream.
/// </summary>
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class NajaStringIO
{
    private readonly StringBuilder _buf;
    private int _pos;
    private bool _closed;

    public NajaStringIO(string initial = "")
    {
        _buf = new StringBuilder(initial);
        _pos = 0;
    }

    // ── Python io.StringIO properties ────────────────────────────────────────
    public bool closed => _closed;

    // ── Core I/O methods ─────────────────────────────────────────────────────

    /// <summary>Write text to the buffer; returns number of characters written.</summary>
    public long write(object text)
    {
        CheckNotClosed();
        string s = NajaIo.Str(text);
        if (_pos < _buf.Length)
        {
            // Overwrite at current position
            for (int i = 0; i < s.Length; i++)
            {
                if (_pos + i < _buf.Length)
                    _buf[_pos + i] = s[i];
                else
                    _buf.Append(s[i]);
            }
        }
        else
        {
            // Past end: pad with nulls then append
            while (_buf.Length < _pos)
                _buf.Append('\0');
            _buf.Append(s);
        }
        _pos += s.Length;
        return (long)s.Length;
    }

    /// <summary>Read up to size characters (-1 = all).</summary>
    public string read(object size = null!)
    {
        CheckNotClosed();
        int sz = size != null ? (int)TypeCoercion.ToLong(size) : -1;
        if (_pos >= _buf.Length) return "";
        if (sz < 0)
        {
            string result = _buf.ToString(_pos, _buf.Length - _pos);
            _pos = _buf.Length;
            return result;
        }
        int count = Math.Min(sz, _buf.Length - _pos);
        string s = _buf.ToString(_pos, count);
        _pos += count;
        return s;
    }

    /// <summary>Read one line (up to and including newline or EOF).</summary>
    public string readline(object size = null!)
    {
        CheckNotClosed();
        int limit = size != null ? (int)TypeCoercion.ToLong(size) : -1;
        if (_pos >= _buf.Length) return "";

        var sb = new StringBuilder();
        while (_pos < _buf.Length)
        {
            char c = _buf[_pos++];
            sb.Append(c);
            if (c == '\n') break;
            if (limit > 0 && sb.Length >= limit) break;
        }
        return sb.ToString();
    }

    /// <summary>Read all lines as a list.</summary>
    public List<object> readlines(object hint = null!)
    {
        CheckNotClosed();
        var result = new List<object>();
        string line;
        while ((line = readline()) != "")
            result.Add(line);
        return result;
    }

    /// <summary>Write a list of strings to the stream.</summary>
    public void writelines(object lines)
    {
        CheckNotClosed();
        if (lines is List<object> lst)
        {
            foreach (var item in lst)
                write(item);
        }
        else if (lines is object[] arr)
        {
            foreach (var item in arr)
                write(item);
        }
    }

    /// <summary>Return the full string contents (regardless of current position).</summary>
    public string getvalue()
    {
        CheckNotClosed();
        return _buf.ToString();
    }

    /// <summary>Seek to a position: seek(pos, whence=0). Returns new position.</summary>
    public long seek(object pos, object whence = null!)
    {
        CheckNotClosed();
        int p = (int)TypeCoercion.ToLong(pos);
        int w = whence != null ? (int)TypeCoercion.ToLong(whence) : 0;
        _pos = w switch
        {
            0 => p,                          // SEEK_SET
            1 => _pos + p,                   // SEEK_CUR
            2 => _buf.Length + p,            // SEEK_END
            _ => throw PythonException.ValueError($"invalid whence ({w})")
        };
        _pos = Math.Max(0, _pos);
        return (long)_pos;
    }

    /// <summary>Return the current stream position.</summary>
    public long tell()
    {
        CheckNotClosed();
        return (long)_pos;
    }

    /// <summary>Truncate the stream to at most size characters.</summary>
    public long truncate(object size = null!)
    {
        CheckNotClosed();
        int sz = size != null ? (int)TypeCoercion.ToLong(size) : _pos;
        if (sz < _buf.Length)
            _buf.Remove(sz, _buf.Length - sz);
        return (long)sz;
    }

    /// <summary>Flush — no-op for in-memory streams.</summary>
    public void flush() { }

    /// <summary>Close the stream.</summary>
    public void close() => _closed = true;

    // ── Context manager support ───────────────────────────────────────────────
    public NajaStringIO __enter__() { CheckNotClosed(); return this; }
    public void __exit__(object exc_type, object exc_val, object exc_tb) => close();

    public override string ToString() => getvalue();

    private void CheckNotClosed()
    {
        if (_closed)
            throw PythonException.ValueError("I/O operation on closed file");
    }
}

// ── BytesIO ───────────────────────────────────────────────────────────────────

/// <summary>
/// Python io.BytesIO — an in-memory binary I/O stream.
/// </summary>
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class NajaBytesIO
{
    private readonly MemoryStream _ms;
    private bool _closed;

    public NajaBytesIO(byte[]? initial = null)
    {
        _ms = initial is { Length: > 0 } ? new MemoryStream(initial.ToArray()) : new MemoryStream();
        _ms.Position = 0;
    }

    // ── Properties ────────────────────────────────────────────────────────────
    public bool closed => _closed;

    // ── Core I/O methods ─────────────────────────────────────────────────────

    /// <summary>Write bytes; returns number of bytes written.</summary>
    public long write(object data)
    {
        CheckNotClosed();
        byte[] bytes = ToBytes(data);
        _ms.Write(bytes, 0, bytes.Length);
        return (long)bytes.Length;
    }

    /// <summary>Read up to size bytes (-1 = all).</summary>
    public byte[] read(object size = null!)
    {
        CheckNotClosed();
        int sz = size != null ? (int)TypeCoercion.ToLong(size) : -1;
        if (sz < 0)
            return ReadFully();
        var buf = new byte[sz];
        int n = _ms.Read(buf, 0, sz);
        if (n < sz) Array.Resize(ref buf, n);
        return buf;
    }

    /// <summary>Read one line ending in b'\n'.</summary>
    public byte[] readline(object size = null!)
    {
        CheckNotClosed();
        int limit = size != null ? (int)TypeCoercion.ToLong(size) : -1;
        var result = new List<byte>();
        int b;
        while ((b = _ms.ReadByte()) != -1)
        {
            result.Add((byte)b);
            if (b == '\n') break;
            if (limit > 0 && result.Count >= limit) break;
        }
        return result.ToArray();
    }

    /// <summary>Return the full bytes contents (regardless of current position).</summary>
    public byte[] getvalue()
    {
        CheckNotClosed();
        return _ms.ToArray();
    }

    /// <summary>Seek to a position. Returns new position.</summary>
    public long seek(object pos, object whence = null!)
    {
        CheckNotClosed();
        long p = TypeCoercion.ToLong(pos);
        int w = whence != null ? (int)TypeCoercion.ToLong(whence) : 0;
        return _ms.Seek(p, w switch
        {
            0 => SeekOrigin.Begin,
            1 => SeekOrigin.Current,
            2 => SeekOrigin.End,
            _ => throw PythonException.ValueError($"invalid whence ({w})")
        });
    }

    /// <summary>Return the current stream position.</summary>
    public long tell()
    {
        CheckNotClosed();
        return _ms.Position;
    }

    /// <summary>Truncate the stream to at most size bytes.</summary>
    public long truncate(object size = null!)
    {
        CheckNotClosed();
        long sz = size != null ? TypeCoercion.ToLong(size) : _ms.Position;
        _ms.SetLength(sz);
        return sz;
    }

    /// <summary>Flush — no-op for in-memory streams.</summary>
    public void flush() { }

    /// <summary>Close the stream.</summary>
    public void close()
    {
        _closed = true;
        _ms.Dispose();
    }

    // ── Context manager support ───────────────────────────────────────────────
    public NajaBytesIO __enter__() { CheckNotClosed(); return this; }
    public void __exit__(object exc_type, object exc_val, object exc_tb) => close();

    private byte[] ReadFully()
    {
        long rem = _ms.Length - _ms.Position;
        var buf = new byte[rem];
        _ms.Read(buf, 0, (int)rem);
        return buf;
    }

    private static byte[] ToBytes(object data) => data switch
    {
        byte[] arr => arr,
        string s   => Encoding.UTF8.GetBytes(s),
        _          => throw PythonException.TypeError($"a bytes-like object is required, not '{data?.GetType().Name ?? "NoneType"}'")
    };

    private void CheckNotClosed()
    {
        if (_closed)
            throw PythonException.ValueError("I/O operation on closed file");
    }
}

// ── TextIOWrapper ─────────────────────────────────────────────────────────────

/// <summary>
/// Python io.TextIOWrapper — wraps a BytesIO (or file stream) with text encoding.
/// </summary>
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class NajaTextIOWrapper
{
    private readonly StreamWriter? _writer;
    private readonly StreamReader? _reader;
    private readonly Encoding _encoding;
    private bool _closed;

    public NajaTextIOWrapper(object buffer, string encoding = "utf-8")
    {
        _encoding = TryGetEncoding(encoding);

        if (buffer is NajaBytesIO bytesIo)
        {
            // Wrap the underlying MemoryStream of the BytesIO
            var ms = new MemoryStream(bytesIo.getvalue());
            _reader = new StreamReader(ms, _encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            _writer = new StreamWriter(ms, _encoding, leaveOpen: true);
        }
        else if (buffer is Stream stream)
        {
            _reader = new StreamReader(stream, _encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            _writer = new StreamWriter(stream, _encoding, leaveOpen: true);
        }
        // else: unsupported buffer type — reads/writes will throw
    }

    public string encoding => _encoding.WebName;
    public bool   closed  => _closed;

    public long write(object text)
    {
        CheckNotClosed();
        if (_writer == null)
            throw PythonException.TypeError("write() not supported: buffer is not writable");
        string s = NajaIo.Str(text);
        _writer.Write(s);
        return (long)s.Length;
    }

    public string read(object size = null!)
    {
        CheckNotClosed();
        if (_reader == null)
            throw PythonException.TypeError("read() not supported: buffer is not readable");
        int sz = size != null ? (int)TypeCoercion.ToLong(size) : -1;
        if (sz < 0) return _reader.ReadToEnd();
        var buf = new char[sz];
        int n = _reader.Read(buf, 0, sz);
        return new string(buf, 0, n);
    }

    public string readline(object size = null!)
    {
        CheckNotClosed();
        if (_reader == null)
            throw PythonException.TypeError("readline() not supported: buffer is not readable");
        return _reader.ReadLine() ?? "";
    }

    public void flush() => _writer?.Flush();

    public void close()
    {
        _closed = true;
        _writer?.Flush();
        _writer?.Dispose();
        _reader?.Dispose();
    }

    public NajaTextIOWrapper __enter__() { CheckNotClosed(); return this; }
    public void __exit__(object exc_type, object exc_val, object exc_tb) => close();

    private void CheckNotClosed()
    {
        if (_closed)
            throw PythonException.ValueError("I/O operation on closed file");
    }

    private static Encoding TryGetEncoding(string name) =>
        name.ToLowerInvariant() switch
        {
            "utf-8" or "utf8"   => Encoding.UTF8,
            "ascii"             => Encoding.ASCII,
            "latin-1" or "latin1" or "iso-8859-1" => Encoding.Latin1,
            "utf-16" or "utf16" => Encoding.Unicode,
            _                   => Encoding.GetEncoding(name)
        };
}
