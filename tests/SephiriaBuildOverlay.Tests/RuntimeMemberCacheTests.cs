using System.Reflection;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class RuntimeMemberCacheTests
{
    private class Parent
    {
        private readonly int hidden = 17;
        public int ReadHidden() => hidden;
        public int Fallback => 2;
    }
    private sealed class Child : Parent
    {
        public int Preferred => 1;
        public int this[int index] => index;
    }

    [Fact]
    public void RepeatedAndMissingLookupsInspectEachTypeOnlyOnce()
    {
        var cache = new RuntimeMemberCache();
        var member = cache.Find(typeof(Child), "Preferred");
        for (var i = 0; i < 10000; i++)
        {
            Assert.Same(member, cache.Find(typeof(Child), "Preferred"));
            Assert.Null(cache.Find(typeof(Child), "absent", "Item"));
        }
        Assert.Equal(1, cache.TypesInspected);
    }

    [Fact]
    public void LookupPreservesNamePriorityCaseAndPrivateInheritedFields()
    {
        var cache = new RuntimeMemberCache();
        Assert.Equal("Preferred", cache.Find(typeof(Child), "preferred", "Fallback")!.Name);
        Assert.Equal("Fallback", cache.Find(typeof(Child), "missing", "fallback")!.Name);
        var field = Assert.IsAssignableFrom<FieldInfo>(cache.Find(typeof(Child), "HIDDEN"));
        Assert.Equal(17, field.GetValue(new Child()));
    }
}
