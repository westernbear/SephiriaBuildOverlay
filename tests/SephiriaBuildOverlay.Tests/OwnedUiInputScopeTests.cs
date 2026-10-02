using SephiriaBuildOverlay.Plugin;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public sealed class OwnedUiInputScopeTests
{
    private sealed class IdenticalNameAction
    {
        public override bool Equals(object? obj) => obj is IdenticalNameAction;
        public override int GetHashCode() => 1;
    }

    [Fact]
    public void AllowsOnlyPrivateModuleAndActionWhileVisible()
    {
        var module = new object(); var action = new object(); var scope = new OwnedUiInputScope(module, new[] { action });
        Assert.True(scope.AllowsModule(module, true)); Assert.True(scope.AllowsAction(action, true));
        Assert.False(scope.AllowsModule(new object(), true)); Assert.False(scope.AllowsAction(new object(), true));
        Assert.False(scope.AllowsModule(module, false)); Assert.False(scope.AllowsAction(action, false));
    }

    [Fact]
    public void GameActionWithEqualNameOrValueIsStillBlocked()
    {
        var action = new IdenticalNameAction(); var foreign = new IdenticalNameAction(); Assert.Equal(action, foreign);
        var scope = new OwnedUiInputScope(new object(), new[] { action }); Assert.False(scope.AllowsAction(foreign, true));
    }

    [Fact]
    public void LaterEditsCannotBroadenWhitelist()
    {
        var actions = new List<object> { new object() }; var scope = new OwnedUiInputScope(new object(), actions);
        var foreign = new object(); actions.Add(foreign); Assert.False(scope.AllowsAction(foreign, true));
    }
}
