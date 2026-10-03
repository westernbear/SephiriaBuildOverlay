using System.Reflection;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ShopPriceBindingTests
{
    public enum Stat { Negotiation }
    public class Unit
    {
        public int GetCustomStat(string key) => throw new InvalidOperationException("Wrong overload");
        public int GetCustomStat(Stat stat) => 7;
    }
    public sealed class Player : Unit { }
    public sealed class Vendor : Unit { }
    public sealed class SmallVendor : Unit { }
    public sealed class UnionVendor : Unit { }
    public sealed class PotionVendor : Unit { }
    public sealed class DuckVendor : Unit { }

    [Fact]
    public void NameOnlyLookupReproducesMerchantCandidateFailure()
    {
        Assert.Throws<AmbiguousMatchException>(() => typeof(Player).GetMethod("GetCustomStat"));
        var method = ShopPriceBinding.NegotiationMethod(typeof(Unit), typeof(Stat));
        Assert.NotNull(method); Assert.Equal(typeof(Stat), method.GetParameters()[0].ParameterType);
        Assert.Equal(7, method.Invoke(new Player(), new object[] { Stat.Negotiation }));
        foreach (var merchant in new Unit[] { new Vendor(), new SmallVendor(), new UnionVendor(), new PotionVendor(), new DuckVendor() })
            Assert.Equal(7, method.Invoke(merchant, new object[] { Stat.Negotiation }));
    }

    [Fact]
    public void MissingAndNonEnumSchemaDoesNotChooseStringOverload()
    {
        Assert.Null(ShopPriceBinding.NegotiationMethod(null, typeof(Stat)));
        Assert.Null(ShopPriceBinding.NegotiationMethod(typeof(Unit), null));
        Assert.Null(ShopPriceBinding.NegotiationMethod(typeof(Unit), typeof(string)));
        Assert.Null(ShopPriceBinding.NegotiationMethod(typeof(object), typeof(Stat)));
    }
}
