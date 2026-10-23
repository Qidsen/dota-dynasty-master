using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dynasty.Core.Rulesets
{
    /// <summary>SHA-256 of a ruleset's canonical content; identifies an exact, immutable patch snapshot.</summary>
    public readonly struct RulesetFingerprint : IEquatable<RulesetFingerprint>
    {
        private RulesetFingerprint(string hex)
        {
            Hex = hex;
        }

        /// <summary>Lowercase hex digest; null for default (missing) fingerprint.</summary>
        public string Hex { get; }

        public bool IsEmpty => Hex == null;

        public static RulesetFingerprint Compute(byte[] canonicalContent)
        {
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(canonicalContent);
                var sb = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                    sb.Append(b.ToString("x2"));
                return new RulesetFingerprint(sb.ToString());
            }
        }

        public bool Equals(RulesetFingerprint other) => string.Equals(Hex, other.Hex, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is RulesetFingerprint other && Equals(other);

        public override int GetHashCode() => Hex == null ? 0 : StringComparer.Ordinal.GetHashCode(Hex);

        public override string ToString() => Hex ?? "<none>";

        public static bool operator ==(RulesetFingerprint a, RulesetFingerprint b) => a.Equals(b);
        public static bool operator !=(RulesetFingerprint a, RulesetFingerprint b) => !a.Equals(b);
    }

    /// <summary>
    /// Pins a simulation to one exact ruleset snapshot: stable ID, monotonically increasing
    /// version, and content fingerprint. Matches and replays store this, never "the current patch".
    /// </summary>
    public sealed class RulesetDescriptor : IEquatable<RulesetDescriptor>
    {
        private static readonly Regex IdPattern = new Regex("^[a-z0-9]+([._-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

        public RulesetDescriptor(string id, int version, RulesetFingerprint fingerprint)
        {
            if (id == null || !IdPattern.IsMatch(id))
                throw new ArgumentException("Ruleset ID must be a lowercase token like 'dynasty.base'.", nameof(id));
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version), "Ruleset version starts at 1.");
            if (fingerprint.IsEmpty)
                throw new ArgumentException("Ruleset fingerprint is required.", nameof(fingerprint));

            Id = id;
            Version = version;
            Fingerprint = fingerprint;
        }

        public string Id { get; }
        public int Version { get; }
        public RulesetFingerprint Fingerprint { get; }

        public bool Equals(RulesetDescriptor? other) =>
            other != null && Id == other.Id && Version == other.Version && Fingerprint == other.Fingerprint;

        public override bool Equals(object? obj) => Equals(obj as RulesetDescriptor);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Id) * 397 ^ Version) * 397 ^ Fingerprint.GetHashCode();
            }
        }

        public override string ToString() => Id + "@" + Version + " (" + Fingerprint.Hex.Substring(0, 12) + ")";
    }
}
