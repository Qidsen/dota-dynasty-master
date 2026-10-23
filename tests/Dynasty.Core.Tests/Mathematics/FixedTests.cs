using Dynasty.Core.Mathematics;

namespace Dynasty.Core.Tests.Mathematics;

public class FixedTests
{
    [Fact]
    public void FromInt_RoundTripsWholeNumbers()
    {
        Assert.Equal(42, Fixed.FromInt(42).ToIntFloor());
        Assert.Equal(-7, Fixed.FromInt(-7).ToIntFloor());
    }

    [Fact]
    public void FromRatio_RepresentsFractions()
    {
        Assert.Equal(Fixed.FromInt(1), Fixed.FromRatio(1, 2) + Fixed.FromRatio(1, 2));
        Assert.Equal(Fixed.One.Raw / 4, Fixed.FromRatio(1, 4).Raw);
    }

    [Fact]
    public void Arithmetic_BehavesLikeRealNumbersForExactValues()
    {
        Fixed a = Fixed.FromRatio(3, 2);
        Fixed b = Fixed.FromInt(4);

        Assert.Equal(Fixed.FromRatio(11, 2), a + b);
        Assert.Equal(Fixed.FromRatio(-5, 2), a - b);
        Assert.Equal(Fixed.FromInt(6), a * b);
        Assert.Equal(Fixed.FromRatio(3, 8), a / b);
        Assert.Equal(Fixed.FromRatio(-3, 2), -a);
    }

    [Fact]
    public void Division_ByZeroThrows()
    {
        Assert.Throws<DivideByZeroException>(() => Fixed.One / Fixed.Zero);
    }

    [Fact]
    public void Multiplication_OverflowThrowsInsteadOfWrapping()
    {
        Fixed huge = Fixed.FromInt(1 << 22);

        Assert.Throws<OverflowException>(() => huge * huge);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(144, 12)]
    [InlineData(250000, 500)]
    public void Sqrt_IsExactForPerfectSquares(int value, int expected)
    {
        Assert.Equal(Fixed.FromInt(expected), Fixed.Sqrt(Fixed.FromInt(value)));
    }

    [Fact]
    public void Sqrt_OfTwoIsAccurateToFixedPrecision()
    {
        long expectedRaw = (long)(Math.Sqrt(2) * Fixed.One.Raw);

        Assert.InRange(Fixed.Sqrt(Fixed.FromInt(2)).Raw, expectedRaw - 1, expectedRaw + 1);
    }

    [Fact]
    public void Sqrt_OfNegativeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixed.Sqrt(Fixed.FromInt(-1)));
    }

    [Fact]
    public void Comparison_AndMinMaxAbs()
    {
        Fixed a = Fixed.FromInt(-3);
        Fixed b = Fixed.FromInt(2);

        Assert.True(a < b);
        Assert.True(b >= Fixed.FromInt(2));
        Assert.Equal(a, Fixed.Min(a, b));
        Assert.Equal(b, Fixed.Max(a, b));
        Assert.Equal(Fixed.FromInt(3), Fixed.Abs(a));
    }

    [Fact]
    public void ToIntFloor_RoundsTowardNegativeInfinity()
    {
        Assert.Equal(1, Fixed.FromRatio(7, 4).ToIntFloor());
        Assert.Equal(-2, Fixed.FromRatio(-7, 4).ToIntFloor());
    }
}
