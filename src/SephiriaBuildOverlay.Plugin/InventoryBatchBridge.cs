using System.Collections;
using System.Reflection;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    internal bool PlacementBatchEnabled { get; set; }
    internal bool BatchBoardReady => _boardVisible && _boardPanel != null && _boardPanel.gameObject.activeInHierarchy &&
        ReadBool(_boardPanel, "IsOpened") && ReadNamedString(_boardPanel, "InventoryMode") == "None" && !NativePickerBusy();
    internal RecommendedAction? NextPlacementBatch(InventoryPlacementBatch batch, Guid build, RunSnapshot snapshot, bool uiAllowed, double now) =>
        batch.Next(build, snapshot, uiAllowed && BatchBoardReady && snapshot.Screen is ScreenKind.Inventory or ScreenKind.ArtifactReward,
            _optimizationModel, _optimizationInput is null ? null : JointBoardPlanner.Current(_optimizationInput),
            _optimizationResult?.Layout, _optimizationAction is null ? null : new RecommendedAction(_optimizationAction.Kind, _optimizationAction.TargetToken,
                _optimizationAction.Reason, snapshot.Identity, expectedResult: _optimizationAction.ExpectedResult),
            _optimizationStep?.Expected, _ghostTask is not null, _optimizationUnavailable ?? _optimizationResult?.Unavailable, now);

    private bool NativePickerBusy()
    {
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
        // CurrentAny is an INSTANCE boolean, not a static object reference.
        // Unknown UI contracts fail closed instead of interpreting null as idle.
        return registry?["UI_NewItemPicker"] is not object mouse || ReadNamedObject(mouse, "CurrentAny") is not false ||
            registry["UI_NewItemPicker_Controller"] is not object pad || ReadNamedObject(pad, "CurrentAny") is not false;
    }

    // Same UIBase.Open / ControlCombine used by the native inventory shortcut,
    // without fabricating InputAction callbacks or changing inventory contents.
    internal bool TryOpenInventoryAfterReward()
    {
        if (_lastSnapshot?.IsLocalPlayerOwned != true || _requestPending || NativePickerBusy()) return false;
        var input = ReadStatic("PlayerInputController", "Instance");
        if (input is null || ReadNamedObject(input, "avatar") is not Component avatar || ReadBool(avatar, "IsInBattle")) return false;
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
        if (registry?["UI_CharacterStatusPanel"] is not Component panel || ReadNamedString(panel, "InventoryMode") != "None") return false;
        if (registry["UI_MessageBoxHolder"] is object holder && ReadBool(holder, "HasOpenedBox")) return false;
        if (registry["UI_MessageBox_InputYesNo"] is object textBox && ReadBool(textBox, "IsOpened")) return false;
        if (_lastSnapshot.Screen is not (ScreenKind.None or ScreenKind.Inventory or ScreenKind.ArtifactReward)) return false;
        if (ReadBool(panel, "IsOpened")) return true;
        var stack = ReadNamedObject(manager!, "CurrentControlStack") as IEnumerable;
        if (stack?.Cast<object>().Any(x => x.GetType().Name != "UI_SephiriteRewardPanel") == true) return false;
        var open = panel.GetType().GetMethod("Open", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (open is null) return false;
        if (registry["UI_SephiriteRewardPanel"] is Component reward && ReadBool(reward, "IsOpened"))
        {
            var combine = panel.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(x => x.Name == "ControlCombine" && x.GetParameters().Length == 2);
            if (combine is null) return false;
            combine.Invoke(panel, new object[] { reward, false });
        }
        open.Invoke(panel, null);
        return true;
    }
}
