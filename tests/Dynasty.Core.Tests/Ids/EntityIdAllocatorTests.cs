using Dynasty.Core.Ids;

namespace Dynasty.Core.Tests.Ids;

public class EntityIdAllocatorTests
{
    [Fact]
    public void None_IsNotValid()
    {
        Assert.False(EntityId.None.IsValid);
    }

    [Fact]
    public void Allocate_ReturnsSequentialValidIdsStartingAtOne()
    {
        var allocator = new EntityIdAllocator();

        Assert.Equal(new EntityId(1), allocator.Allocate());
        Assert.Equal(new EntityId(2), allocator.Allocate());
        Assert.True(new EntityId(1).IsValid);
    }

    [Fact]
    public void RestoredAllocator_ContinuesWithoutReusingIds()
    {
        var original = new EntityIdAllocator();
        original.Allocate();
        original.Allocate();

        var restored = new EntityIdAllocator(original.NextValue);

        Assert.Equal(new EntityId(3), restored.Allocate());
    }

    [Fact]
    public void RestoredAllocator_RejectsValueThatWouldIssueNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityIdAllocator(0));
    }
}
