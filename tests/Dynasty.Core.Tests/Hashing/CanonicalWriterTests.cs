using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.Core.Tests.Hashing;

public class CanonicalWriterTests
{
    [Fact]
    public void Int64_IsWrittenLittleEndian()
    {
        var writer = new CanonicalWriter();
        writer.WriteInt64(0x0102030405060708);

        Assert.Equal(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, writer.ToArray());
    }

    [Fact]
    public void String_IsLengthPrefixedUtf8()
    {
        var writer = new CanonicalWriter();
        writer.WriteString("ab");

        Assert.Equal(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, (byte)'a', (byte)'b' }, writer.ToArray());
    }

    [Fact]
    public void Fixed_IsWrittenAsItsRawValue()
    {
        var a = new CanonicalWriter();
        a.WriteFixed(Fixed.FromRatio(3, 2));
        var b = new CanonicalWriter();
        b.WriteInt64(Fixed.FromRatio(3, 2).Raw);

        Assert.Equal(b.ToArray(), a.ToArray());
    }

    [Fact]
    public void Fingerprint_DependsOnEveryWrittenValue()
    {
        var a = new CanonicalWriter();
        a.WriteString("tower");
        a.WriteInt64(1800);
        var b = new CanonicalWriter();
        b.WriteString("tower");
        b.WriteInt64(1801);

        Assert.NotEqual(a.ToFingerprint(), b.ToFingerprint());
    }
}
