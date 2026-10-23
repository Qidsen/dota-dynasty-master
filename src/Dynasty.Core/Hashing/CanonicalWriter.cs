using System.IO;
using System.Text;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Rulesets;

namespace Dynasty.Core.Hashing
{
    /// <summary>
    /// Unambiguous byte encoding of content (little-endian integers, length-prefixed UTF-8,
    /// fixed-point as raw). The same logical content always produces the same bytes, which
    /// makes the SHA-256 fingerprint a stable identity for ruleset snapshots.
    /// </summary>
    public sealed class CanonicalWriter
    {
        private readonly MemoryStream _buffer = new MemoryStream();

        public void WriteInt64(long value)
        {
            ulong v = (ulong)value;
            for (int i = 0; i < 8; i++)
                _buffer.WriteByte((byte)(v >> (8 * i)));
        }

        public void WriteBool(bool value) => _buffer.WriteByte(value ? (byte)1 : (byte)0);

        public void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt64(bytes.Length);
            _buffer.Write(bytes, 0, bytes.Length);
        }

        public void WriteFixed(Fixed value) => WriteInt64(value.Raw);

        public void WriteVector(FixedVector2 value)
        {
            WriteFixed(value.X);
            WriteFixed(value.Y);
        }

        public byte[] ToArray() => _buffer.ToArray();

        public RulesetFingerprint ToFingerprint() => RulesetFingerprint.Compute(ToArray());
    }
}
