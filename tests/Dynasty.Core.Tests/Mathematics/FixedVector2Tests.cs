using Dynasty.Core.Mathematics;

namespace Dynasty.Core.Tests.Mathematics;

public class FixedVector2Tests
{
    private static FixedVector2 V(int x, int y) => FixedVector2.FromInts(x, y);

    [Fact]
    public void Arithmetic_IsComponentWise()
    {
        Assert.Equal(V(4, 6), V(1, 2) + V(3, 4));
        Assert.Equal(V(-2, -2), V(1, 2) - V(3, 4));
        Assert.Equal(V(3, 6), V(1, 2) * Fixed.FromInt(3));
    }

    [Fact]
    public void Length_UsesPythagoras()
    {
        Assert.Equal(Fixed.FromInt(25), V(3, 4).LengthSquared);
        Assert.Equal(Fixed.FromInt(5), V(3, 4).Length);
        Assert.Equal(Fixed.FromInt(5), FixedVector2.Distance(V(1, 1), V(4, 5)));
    }

    [Fact]
    public void Length_HandlesMapScaleDistances()
    {
        Assert.Equal(Fixed.FromInt(10000), V(6000, 8000).Length);
    }

    [Fact]
    public void MoveTowards_StepsByMaxDistanceAlongTheLine()
    {
        FixedVector2 result = FixedVector2.MoveTowards(V(0, 0), V(30, 40), Fixed.FromInt(10));

        Assert.Equal(V(6, 8), result);
    }

    [Fact]
    public void MoveTowards_StopsExactlyAtTargetWhenCloserThanStep()
    {
        FixedVector2 result = FixedVector2.MoveTowards(V(0, 0), V(3, 4), Fixed.FromInt(10));

        Assert.Equal(V(3, 4), result);
    }

    [Fact]
    public void Reflect_ThroughCenterMirrorsPoint()
    {
        Assert.Equal(V(9000, 4000), V(1000, 6000).ReflectThrough(V(5000, 5000)));
    }
}
