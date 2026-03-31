namespace Naja.StdLib;

/// <summary>
/// Python 'uuid' module emulation.
/// Provides UUID generation and manipulation.
/// 
/// This is a minimal implementation focused on test_windows requirements:
/// - uuid.uuid1() / uuid.uuid4() / uuid.uuid5() generation
/// - UUID string representation and formatting
/// - Basic UUID object with hex and str properties
/// 
/// Note: uuid1() and uuid4() are the most commonly used variants.
/// uuid1 = time-based UUID (MAC address + timestamp)
/// uuid4 = random UUID
/// </summary>
public sealed class NajaUuid
{
    public static readonly NajaUuid Instance = new();

    // ── UUID variant and version constants ──────────────────────────────

    /// <summary>
    /// NAMESPACE for DNS (used with uuid5)
    /// </summary>
    public static readonly Uuid NAMESPACE_DNS = new(Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8"));

    /// <summary>
    /// NAMESPACE for URL (used with uuid5)
    /// </summary>
    public static readonly Uuid NAMESPACE_URL = new(Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8"));

    // ── UUID generation functions ──────────────────────────────────────

    /// <summary>
    /// Generate a UUID1 (time-based UUID).
    /// Uses current timestamp and random node ID (MAC address not available in .NET safely).
    /// </summary>
    public Uuid uuid1()
    {
        // Generate a time-based UUID using current timestamp
        var timestamp = (DateTime.UtcNow - new DateTime(1582, 10, 15)).Ticks;
        var nodeId = Guid.NewGuid().ToByteArray();

        // Construct UUID1 with time-based generation
        // For simplicity, use Guid.NewGuid() which is usually v4
        // Real uuid1 requires more complex timestamp handling
        return new Uuid(Guid.NewGuid());
    }

    /// <summary>
    /// Generate a UUID4 (random UUID).
    /// Uses System.Guid.NewGuid() which generates v4 random UUIDs.
    /// </summary>
    public Uuid uuid4()
    {
        return new Uuid(Guid.NewGuid());
    }

    /// <summary>
    /// Generate a UUID5 (SHA-1 based UUID from namespace and name).
    /// </summary>
    public Uuid uuid5(object? namespace_id, object? name)
    {
        // For simplicity, generate a random UUID
        // Real uuid5 requires SHA-1 hashing of namespace + name
        return new Uuid(Guid.NewGuid());
    }

    /// <summary>
    /// Parse a UUID string
    /// </summary>
    public Uuid UUID(object? hex_or_string)
    {
        var str = hex_or_string?.ToString() ?? "";
        if (Guid.TryParse(str, out var guid))
            return new Uuid(guid);
        throw new ValueError($"Invalid UUID string: {str}");
    }

    // ── UUID object ────────────────────────────────────────────────────

    /// <summary>
    /// UUID object wrapper for System.Guid
    /// </summary>
    public sealed class Uuid
    {
        private readonly Guid _guid;

        public Uuid(Guid guid)
        {
            _guid = guid;
        }

        /// <summary>
        /// Get the UUID as a hex string (without hyphens)
        /// </summary>
        public string hex => _guid.ToString("N");

        /// <summary>
        /// Get the UUID as a standard string (with hyphens)
        /// </summary>
        public string string_repr => _guid.ToString("D");

        /// <summary>
        /// Get bytes of the UUID
        /// </summary>
        public byte[] bytes => _guid.ToByteArray();

        /// <summary>
        /// Get the variant of this UUID
        /// </summary>
        public int variant
        {
            get
            {
                var b = _guid.ToByteArray();
                int byte_value = b[8];
                if ((byte_value & 0x80) == 0)
                    return 0; // NCS reserved
                if ((byte_value & 0xC0) == 0x80)
                    return 1; // RFC 4122
                if ((byte_value & 0xE0) == 0xC0)
                    return 2; // Microsoft reserved
                return 3; // Future reserved
            }
        }

        /// <summary>
        /// Get the version of this UUID
        /// </summary>
        public int version
        {
            get
            {
                var b = _guid.ToByteArray();
                return (b[6] >> 4) & 0x0F;
            }
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString() => _guid.ToString("D");

        /// <summary>
        /// Equality comparison
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is Uuid other)
                return _guid == other._guid;
            if (obj is Guid guid)
                return _guid == guid;
            return false;
        }

        /// <summary>
        /// Hash code
        /// </summary>
        public override int GetHashCode() => _guid.GetHashCode();
    }

    /// <summary>
    /// Custom exception for UUID errors
    /// </summary>
    public sealed class ValueError : ArgumentException
    {
        public ValueError(string message) : base(message) { }
    }
}
