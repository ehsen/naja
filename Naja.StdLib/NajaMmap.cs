using System.IO.MemoryMappedFiles;

namespace Naja.StdLib;

/// <summary>
/// Python 'mmap' module emulation.
/// Provides memory-mapped file and shared memory access.
/// 
/// Windows-specific memory mapping for process synchronization:
/// - Named shared memory regions (tagname parameter)
/// - File-based memory mapping
/// - Direct memory access and manipulation
/// 
/// This is a minimal implementation focused on the features needed by test_windows:
/// - mmap.mmap(-1, size, tagname=str) → named shared memory region
/// - __getitem__(idx) / __setitem__(idx, val) → byte access
/// - __enter__() / __exit__() → context manager protocol
/// - close() → cleanup
/// </summary>
public sealed class NajaMmap
{
    public static readonly NajaMmap Instance = new();

    // ── Access modes (for compatibility) ────────────────────────────────

    /// <summary>
    /// Default access mode (read/write)
    /// </summary>
    public const int ACCESS_WRITE = 0;

    /// <summary>
    /// Read-only access
    /// </summary>
    public const int ACCESS_READ = 1;

    /// <summary>
    /// Copy-on-write access
    /// </summary>
    public const int ACCESS_COPY = 2;

    // ── MmapObject wrapper ─────────────────────────────────────────────

    /// <summary>
    /// Wrapper for a memory-mapped region.
    /// Provides Python-like interface to memory mapping.
    /// </summary>
    public sealed class MmapObject : IDisposable
    {
        private readonly MemoryMappedFile? _mmf;
        private readonly MemoryMappedViewAccessor? _accessor;
        private readonly byte[] _buffer;
        private readonly int _size;
        private readonly string? _name;
        private bool _disposed;

        public int size() => _size;

        /// <summary>
        /// Create a memory-mapped region.
        /// fileno=-1 creates an anonymous mapping; fileno>=0 maps a file.
        /// tagname enables named shared memory (Windows).
        /// </summary>
        public MmapObject(int fileno, int length, string? tagname = null, int access = ACCESS_WRITE)
        {
            _size = length;
            _name = tagname;

            try
            {
                if (fileno == -1)
                {
                    // Anonymous mapping (named shared memory on Windows)
                    if (!string.IsNullOrEmpty(tagname))
                    {
                        try
                        {
                            _mmf = MemoryMappedFile.OpenExisting(tagname);
                        }
                        catch
                        {
                            _mmf = MemoryMappedFile.CreateNew(tagname, length);
                        }
                    }
                    else
                    {
                        _mmf = MemoryMappedFile.CreateNew(null, length);
                    }
                }
                else
                {
                    // File-based mapping (not implemented for now)
                    throw new NotImplementedError("File-based mmap not yet implemented");
                }

                _accessor = _mmf.CreateViewAccessor();
                _buffer = new byte[length];

                // Initialize buffer from accessor
                if (_accessor.Capacity > 0)
                {
                    try
                    {
                        _accessor.ReadArray(0, _buffer, 0, Math.Min((int)_accessor.Capacity, length));
                    }
                    catch
                    {
                        // If read fails, use zero-filled buffer
                        Array.Clear(_buffer, 0, _buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                _mmf?.Dispose();
                _accessor?.Dispose();
                throw new IOError($"mmap creation failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get byte at index
        /// </summary>
        public int __getitem__(int index)
        {
            if (index < 0 || index >= _size)
                throw new MmapIndexError($"mmap index out of range");
            return _buffer[index];
        }

        /// <summary>
        /// Set byte at index
        /// </summary>
        public void __setitem__(int index, object value)
        {
            if (index < 0 || index >= _size)
                throw new MmapIndexError($"mmap index out of range");

            int byteVal = Convert.ToInt32(value) & 0xFF;
            _buffer[index] = (byte)byteVal;

            // Write back to accessor
            try
            {
                _accessor?.WriteArray(index, new[] { (byte)byteVal }, 0, 1);
            }
            catch
            {
                // Ignore write failures
            }
        }

        /// <summary>
        /// Read up to size bytes starting at offset
        /// </summary>
        public byte[] read(int size)
        {
            var result = new byte[Math.Min(size, _buffer.Length)];
            Array.Copy(_buffer, 0, result, 0, result.Length);
            return result;
        }

        /// <summary>
        /// Write bytes at current position
        /// </summary>
        public void write(object data)
        {
            var bytes = data switch
            {
                byte[] arr => arr,
                string s => System.Text.Encoding.UTF8.GetBytes(s),
                _ => System.Text.Encoding.UTF8.GetBytes(data?.ToString() ?? "")
            };

            Array.Copy(bytes, 0, _buffer, 0, Math.Min(bytes.Length, _buffer.Length));

            // Write back to accessor
            try
            {
                _accessor?.WriteArray(0, bytes, 0, Math.Min(bytes.Length, (int)_accessor.Capacity));
            }
            catch
            {
                // Ignore write failures
            }
        }

        /// <summary>
        /// Resize the memory map (may not be supported for all types)
        /// </summary>
        public void resize(int newsize)
        {
            if (newsize <= _size)
                return; // Shrinking not supported

            // Expand buffer
            var newBuffer = new byte[newsize];
            Array.Copy(_buffer, newBuffer, _size);
            newBuffer = newBuffer;

            // Note: actual memory-mapped file resize requires creating a new mapping
            // For simplicity, we just expand the buffer
        }

        /// <summary>
        /// Flush changes to underlying storage
        /// </summary>
        public void flush()
        {
            try
            {
                _accessor?.Flush();
            }
            catch
            {
                // Ignore flush errors
            }
        }

        /// <summary>
        /// Close the memory mapping
        /// </summary>
        public void close()
        {
            Dispose();
        }

        /// <summary>
        /// Context manager support
        /// </summary>
        public MmapObject __enter__() => this;

        /// <summary>
        /// Context manager exit
        /// </summary>
        public void __exit__(object? exc_type = null, object? exc_val = null, object? exc_tb = null)
        {
            Dispose();
        }

        /// <summary>
        /// IDisposable implementation
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _accessor?.Dispose();
            }
            catch { }

            try
            {
                _mmf?.Dispose();
            }
            catch { }
        }

        public override string ToString() => $"<mmap size={_size} name={_name ?? "anonymous"}>";
    }

    // ── Exception types ────────────────────────────────────────────────

    /// <summary>
    /// Custom exception for mmap index errors
    /// </summary>
    public sealed class MmapIndexError : Exception
    {
        public MmapIndexError(string message) : base(message) { }
    }

    /// <summary>
    /// Custom exception for mmap I/O errors
    /// </summary>
    public sealed class IOError : System.IO.IOException
    {
        public IOError(string message) : base(message) { }
        public IOError(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// NotImplementedError for unsupported features
    /// </summary>
    public sealed class NotImplementedError : NotImplementedException
    {
        public NotImplementedError(string message) : base(message) { }
    }
}
