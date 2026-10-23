using System.Text;

namespace Dynasty.Core.Hashing
{
    /// <summary>
    /// FNV-1a 64-bit hash: cheap, stable checksum for simulation state and event streams.
    /// Not cryptographic; ruleset fingerprints use SHA-256 instead.
    /// </summary>
    public struct Fnv1a64
    {
        private const ulong OffsetBasis = 0xCBF29CE484222325UL;
        private const ulong Prime = 0x100000001B3UL;

        // Stored XOR-ed with the offset basis so that default(Fnv1a64) is a valid empty hash.
        private ulong _state;

        public ulong Value => _state ^ OffsetBasis;

        public static ulong HashString(string text)
        {
            var hash = new Fnv1a64();
            hash.AddBytes(Encoding.UTF8.GetBytes(text));
            return hash.Value;
        }

        public void AddByte(byte value)
        {
            ulong h = Value;
            h ^= value;
            h *= Prime;
            _state = h ^ OffsetBasis;
        }

        public void AddBytes(byte[] bytes)
        {
            foreach (byte b in bytes)
                AddByte(b);
        }

        /// <summary>Little-endian, independent of machine byte order.</summary>
        public void AddInt64(long value)
        {
            ulong v = (ulong)value;
            for (int i = 0; i < 8; i++)
                AddByte((byte)(v >> (8 * i)));
        }

        /// <summary>Length-prefixed UTF-8, so consecutive strings cannot alias each other.</summary>
        public void AddString(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            AddInt64(bytes.Length);
            AddBytes(bytes);
        }
    }
}
