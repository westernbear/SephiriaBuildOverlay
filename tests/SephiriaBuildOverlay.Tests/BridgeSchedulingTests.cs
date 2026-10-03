using Mono.Cecil;

namespace SephiriaBuildOverlay.Tests;

// Read compiled bridge IL without loading Unity, starting the game or buying
// anything. These contract checks complement pure recommendation/action tests.
public sealed class BridgeSchedulingTests
{
    private static AssemblyDefinition Plugin() => AssemblyDefinition.ReadAssembly(Path.Combine(AppContext.BaseDirectory, "SephiriaBuildOverlay.Plugin.dll"));
    private static string[] Operations(MethodDefinition method) => method.Body.Instructions.Select(i => i.Operand?.ToString() ?? "").ToArray();

    [Fact]
    public void PassiveRiftCapturePrecedesNativeInteractionGate()
    {
        using var plugin = Plugin();
        var gateway = plugin.MainModule.Types.Single(t => t.Name == "UnityGameGateway");
        var operations = Operations(gateway.Methods.Single(m => m.Name == "CaptureWorldCandidates"));
        var capture = Array.FindIndex(operations, s => s.Contains("::CaptureRiftShop("));
        var interactionGate = Array.FindIndex(operations, s => s == "canDoInteractive");
        Assert.InRange(capture, 0, interactionGate - 1);
        var rift = gateway.Methods.Single(m => m.Name == "CaptureRiftShop");
        Assert.Single(rift.Parameters);
        var riftOperations = Operations(rift);
        Assert.Contains(riftOperations, s => s.Contains("::IsLocalInventory("));
        Assert.Contains("arms", riftOperations); Assert.Contains("isPaid", riftOperations);
        Assert.DoesNotContain(riftOperations, s => s.Contains("::_actions"));
        Assert.DoesNotContain("canDoInteractive", riftOperations);
        Assert.DoesNotContain(riftOperations, s => s.Contains("Resources::FindObjects"));
    }

    [Fact]
    public void SpawnedRiftRegistrationHasNoNativePurchaseOrInputCalls()
    {
        using var plugin = Plugin();
        var type = plugin.MainModule.Types.Single(t => t.Name == "SephiriaBuildOverlayPlugin");
        var operations = Operations(type.Methods.Single(m => m.Name == "OnRiftShopStarted"));
        Assert.Contains(operations, s => s.Contains("::RegisterRiftShop("));
        Assert.DoesNotContain(operations, s => s.Contains("::HandleInteraction(") || s.Contains("::ConfirmOnceAsync("));
    }

    [Fact]
    public void MissingFontDoesNotHideCandidateFrames()
    {
        using var plugin = Plugin();
        var gateway = plugin.MainModule.Types.Single(t => t.Name == "UnityGameGateway");
        var operations = Operations(gateway.Methods.Single(m => m.Name == "BeginNativeOverlay"));
        Assert.Contains(operations, s => s.Contains("::BeginGraphics()"));
        Assert.Contains(operations, s => s.Contains("::_worldCandidateTextTemplate"));
    }

    [Fact]
    public void RewardMapWorkHasFrameTickAndOneSharedSliceBudget()
    {
        using var plugin = Plugin();
        var gateway = plugin.MainModule.Types.Single(t => t.Name == "UnityGameGateway");
        var type = plugin.MainModule.Types.Single(t => t.Name == "SephiriaBuildOverlayPlugin");
        Assert.Contains(Operations(type.Methods.Single(m => m.Name == "Update")), s => s.Contains("::TickTabletRewardCalculation()"));
        foreach (var name in new[] { "CaptureTabletRewards", "CaptureOptimizationInput" })
            Assert.Contains(Operations(gateway.Methods.Single(m => m.Name == name)), s => s.Contains("::BeginOptionCaptureSlice()"));
        Assert.Contains(Operations(gateway.Methods.Single(m => m.Name == "BeginOptionCaptureSlice")), s => s.Contains("Time::get_frameCount()"));
    }

    [Fact]
    public void PassiveBoardModelRemainsInSnapshotRevisionAfterSkippingGhostSolve()
    {
        using var plugin = Plugin();
        var gateway = plugin.MainModule.Types.Single(t => t.Name == "UnityGameGateway");
        var operations = Operations(gateway.Methods.Single(m => m.Name == "CaptureBoard"));
        Assert.Contains("|model:", operations);
        Assert.Contains(operations, s => s.Contains("::OptimizationInvariant("));
        Assert.Equal("System.Boolean", gateway.Methods.Single(m => m.Name == "TickTabletRewardCalculation").ReturnType.FullName);
    }

    [Fact]
    public void NoticesCannotGateConfirmationOrStartingPresets()
    {
        using var plugin = Plugin();
        var type = plugin.MainModule.Types.Single(t => t.Name == "SephiriaBuildOverlayPlugin");
        foreach (var name in new[] { "TickPlacement", "TickStartingPreset", "StartPlacement" })
            Assert.DoesNotContain(Operations(type.Methods.Single(m => m.Name == name)), s => s.Contains("NotificationQueue::"));
        var queue = plugin.MainModule.Types.Single(t => t.Name == "NotificationQueue");
        Assert.DoesNotContain(queue.Fields, f => f.Name == "_pending");
    }
}
