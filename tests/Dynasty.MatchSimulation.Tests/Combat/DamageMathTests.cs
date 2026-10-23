using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Tests.Combat;

public class DamageMathTests
{
    private static readonly Fixed Factor = Fixed.FromRatio(6, 100);

    [Fact]
    public void Physical_IsReducedByArmorOnly()
    {
        Fixed result = DamageMath.Mitigate(Fixed.FromInt(100), DamageType.Physical, Fixed.FromInt(2), magicResistPercent: 50, Factor);

        Assert.InRange(result.ToDouble(), 89.28, 89.29);
    }

    [Fact]
    public void Magical_IsReducedByMagicResistOnly()
    {
        Fixed result = DamageMath.Mitigate(Fixed.FromInt(100), DamageType.Magical, Fixed.FromInt(20), magicResistPercent: 25, Factor);

        Assert.Equal(Fixed.FromInt(75), result);
    }

    [Fact]
    public void Pure_IgnoresAllMitigation()
    {
        Fixed result = DamageMath.Mitigate(Fixed.FromInt(100), DamageType.Pure, Fixed.FromInt(20), magicResistPercent: 90, Factor);

        Assert.Equal(Fixed.FromInt(100), result);
    }
}
