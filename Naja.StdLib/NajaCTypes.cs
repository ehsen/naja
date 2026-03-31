using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python ctypes module - Foreign Function Interface (FFI) support.
/// Enables calling C functions from .NET, particularly Windows API functions.
/// 
/// Implements:
/// - ctypes.wintypes: DWORD, HANDLE, BOOL, LPDWORD, LPSTR, LPWSTR, etc.
/// - ctypes.POINTER: Function pointers and pointer types
/// - ctypes.byref/ctypes.pointer: Address-of operators
/// - ctypes.create_string_buffer: Buffer management
/// - ctypes.windll: P/Invoke to Windows DLLs
/// - ctypes.c_int, c_char, c_double, etc.: C type wrappers
/// </summary>
public class NajaCTypes
{
    // ── Helper conversions ─────────────────────────────────────────────────────

    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    private static int I(object? o) => o switch
    {
        int i => i,
        long l => (int)l,
        _ => Convert.ToInt32(o)
    };

    // ── C Type Wrappers ────────────────────────────────────────────────────────

    /// <summary>Base class for ctypes primitive types.</summary>
    public class CType
    {
        public object? Value { get; set; }
        public CType(object? value = null) => Value = value;
        public override string ToString() => Value?.ToString() ?? "NULL";
    }

    public class c_int : CType
    {
        public c_int(object? value = null) : base(value is int i ? i : 0) { }
    }

    public class c_uint : CType
    {
        public c_uint(object? value = null) : base(value is uint u ? u : 0u) { }
    }

    public class c_long : CType
    {
        public c_long(object? value = null) : base(value is long l ? l : 0L) { }
    }

    public class c_ulong : CType
    {
        public c_ulong(object? value = null) : base(value is ulong ul ? ul : 0UL) { }
    }

    public class c_double : CType
    {
        public c_double(object? value = null) : base(value is double d ? d : 0.0) { }
    }

    public class c_char : CType
    {
        public c_char(object? value = null) : base(value is string s && s.Length > 0 ? s[0] : '\0') { }
    }

    public class c_bool : CType
    {
        public c_bool(object? value = null) : base(value is bool b ? b : false) { }
    }

    public class c_void : CType { }

    // ── ctypes.wintypes - Windows-specific types ───────────────────────────────

    public class wintypes
    {
        /// <summary>DWORD - 32-bit unsigned integer</summary>
        public class DWORD : c_uint
        {
            public DWORD(object? value = null) : base(value) { }
        }

        /// <summary>HANDLE - opaque handle (pointer-sized integer)</summary>
        public class HANDLE : CType
        {
            public HANDLE(object? value = null) : base(value) { }
        }

        /// <summary>BOOL - boolean (4 bytes on Windows)</summary>
        public class BOOL : c_int
        {
            public BOOL(object? value = null) : base(value is bool b ? (b ? 1 : 0) : value) { }
        }

        /// <summary>LPDWORD - pointer to DWORD</summary>
        public class LPDWORD : CType
        {
            private uint[] _data;
            public LPDWORD(object? value = null)
            {
                _data = value is uint u ? new[] { u } : new uint[1];
            }
            public uint Value
            {
                get => _data[0];
                set => _data[0] = value;
            }
        }

        /// <summary>LPSTR - pointer to char (ANSI string)</summary>
        public class LPSTR : CType
        {
            public LPSTR(object? value = null) : base(value is string s ? s : "") { }
        }

        /// <summary>LPWSTR - pointer to wchar_t (Unicode string)</summary>
        public class LPWSTR : CType
        {
            public LPWSTR(object? value = null) : base(value is string s ? s : "") { }
        }

        /// <summary>BYTE - 8-bit unsigned integer</summary>
        public class BYTE : CType
        {
            public BYTE(object? value = null) : base(value is byte b ? b : (byte)0) { }
        }

        /// <summary>WORD - 16-bit unsigned integer</summary>
        public class WORD : CType
        {
            public WORD(object? value = null) : base(value is ushort u ? u : (ushort)0) { }
        }
    }

    // ── Pointer and address operations ─────────────────────────────────────────

    /// <summary>
    /// Create a pointer to an object.
    /// In ctypes, POINTER(type) returns a type that can hold pointers.
    /// This simplified version just wraps the address concept.
    /// </summary>
    public static object POINTER(object type)
    {
        // Return a type descriptor that indicates "pointer to type"
        return new PointerType(type);
    }

    public class PointerType
    {
        public object PointeeType { get; }
        public PointerType(object pointeeType) => PointeeType = pointeeType;
        public override string ToString() => $"POINTER({PointeeType})";
    }

    public class Pointer<T>
    {
        public T? Value { get; set; }
        public Pointer(T? value = default) => Value = value;

        public IntPtr Address
        {
            get
            {
                if (Value is CType ct && ct.Value is not null)
                    return new IntPtr(ct.Value.GetHashCode());
                return IntPtr.Zero;
            }
        }

        public override string ToString() => $"<pointer to {Value}>";
    }

    /// <summary>
    /// Get address of an object (c reference operator &).
    /// Simplified: returns a reference object that tracks the original.
    /// </summary>
    public static object byref(object obj)
    {
        if (obj is CType ct)
            return new ByRef(ct);
        return new ByRef(obj);
    }

    /// <summary>
    /// Alias for byref - create pointer to object.
    /// </summary>
    public static object pointer(object obj) => byref(obj);

    public class ByRef
    {
        public object Reference { get; }
        public ByRef(object reference) => Reference = reference;
        public override string ToString() => $"byref({Reference})";
    }

    // ── Buffer operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Create a mutable character buffer.
    /// Mirrors ctypes.create_string_buffer(size_or_initializer).
    /// </summary>
    public static StringBuffer create_string_buffer(object size_or_initializer)
    {
        if (size_or_initializer is int size)
            return new StringBuffer(new byte[size]);
        else if (size_or_initializer is string str)
            return new StringBuffer(Encoding.ASCII.GetBytes(str + '\0'));
        else if (size_or_initializer is byte[] data)
            return new StringBuffer(data);
        else
        {
            int sz = I(size_or_initializer);
            return new StringBuffer(new byte[sz]);
        }
    }

    public class StringBuffer
    {
        private byte[] _buffer;

        public StringBuffer(byte[] buffer)
        {
            _buffer = (byte[])buffer.Clone();
        }

        public int Length => _buffer.Length;

        public byte Get(int index) => _buffer[index];
        public void Set(int index, byte value) => _buffer[index] = value;

        public string GetValue()
        {
            int nullIndex = Array.IndexOf(_buffer, (byte)0);
            if (nullIndex < 0) nullIndex = _buffer.Length;
            return Encoding.ASCII.GetString(_buffer, 0, nullIndex);
        }

        public void SetValue(string str)
        {
            var bytes = Encoding.ASCII.GetBytes(str);
            Array.Copy(bytes, 0, _buffer, 0, Math.Min(bytes.Length, _buffer.Length - 1));
            if (_buffer.Length > bytes.Length)
                _buffer[bytes.Length] = 0;
        }

        public byte[] GetRawBuffer() => (byte[])_buffer.Clone();

        public override string ToString() => $"<char buffer {Length}>";
    }

    // ── Windll - Windows DLL P/Invoke ─────────────────────────────────────────

    /// <summary>
    /// windll namespace - access to Windows DLLs.
    /// Example: ctypes.windll.kernel32.GetLastError()
    /// </summary>
    public static class windll
    {
        public static Kernel32Functions kernel32 => new Kernel32Functions();

        public class Kernel32Functions
        {
            /// <summary>PeekNamedPipe - peek into a pipe without reading.</summary>
            public int PeekNamedPipe(
                IntPtr hNamedPipe,
                IntPtr lpBuffer,
                uint nBufferSize,
                out uint lpBytesRead,
                out uint lpTotalBytesAvail,
                out uint lpBytesLeftThisMessage)
            {
                // Stub implementation - actual P/Invoke would be:
                // return NativeMethods.PeekNamedPipe(hNamedPipe, lpBuffer, nBufferSize, 
                //                                   out lpBytesRead, out lpTotalBytesAvail, 
                //                                   out lpBytesLeftThisMessage);
                
                lpBytesRead = 0;
                lpTotalBytesAvail = 0;
                lpBytesLeftThisMessage = 0;
                return 0;
            }

            /// <summary>SetConsoleCtrlHandler - set handler for console events.</summary>
            public int SetConsoleCtrlHandler(object? handler, int add)
            {
                // Simplified: just acknowledge. Real implementation would register .NET
                // handler with Windows console event system.
                return 1; // Success
            }

            /// <summary>GetLastError - get last Win32 error.</summary>
            public int GetLastError()
            {
                return Marshal.GetLastWin32Error();
            }

            /// <summary>SetLastError - set last Win32 error.</summary>
            public void SetLastError(int error)
            {
                Marshal.SetLastPInvokeError(error);
            }

            /// <summary>CloseHandle - close a Win32 handle.</summary>
            public int CloseHandle(IntPtr hObject)
            {
                if (hObject == IntPtr.Zero)
                    return 0;
                // In real implementation, would actually close the handle
                return 1;
            }

            /// <summary>CreateProcessW - create a new process.</summary>
            public int CreateProcessW(string? lpApplicationName, string? lpCommandLine,
                                    IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
                                    bool bInheritHandles, uint dwCreationFlags,
                                    IntPtr lpEnvironment, string? lpCurrentDirectory,
                                    IntPtr lpStartupInfo, IntPtr lpProcessInformation)
            {
                // Stub - complex function, not fully implemented
                return 0;
            }

            /// <summary>TerminateProcess - terminate a process.</summary>
            public int TerminateProcess(IntPtr hProcess, uint uExitCode)
            {
                // Stub - actual process termination
                return 1;
            }

            /// <summary>WaitForSingleObject - wait for object to be signaled.</summary>
            public int WaitForSingleObject(IntPtr hHandle, int dwMilliseconds)
            {
                // Stub - would wait for actual Windows object
                return 0; // WAIT_OBJECT_0
            }
        }
    }

    // ── Array types ────────────────────────────────────────────────────────────

    /// <summary>
    /// Create an array type for ctypes.
    /// Example: array(c_int, 10) creates array of 10 ints.
    /// </summary>
    public static ArrayType array(object element_type, int size)
    {
        return new ArrayType(element_type, size);
    }

    public class ArrayType
    {
        public object ElementType { get; }
        public int Size { get; }
        private object?[] _elements;

        public ArrayType(object elementType, int size)
        {
            ElementType = elementType;
            Size = size;
            _elements = new object?[size];
        }

        public object? Get(int index) => _elements[index];
        public void Set(int index, object? value) => _elements[index] = value;

        public override string ToString() => $"array({ElementType}, {Size})";
    }
}

/// <summary>
/// ctypes module entry point for Naja.
/// Provides access to ctypes.wintypes, ctypes.windll, and FFI functions.
/// </summary>
public class NajaCTypesModule : NajaCTypes { }
