using System.Collections;
using System.Reflection;
using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Core.Solvers;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private Vector2 _boardPointer;
    private TabletRotationTarget? _pointerRotation;
    private bool _manualRotationRequested;
    private MethodInfo? _nativeRotateRequest;
    private MethodInfo? _nativeCanRotate;

    public void SetBoardPointer(Vector2 position, bool manualRotation = false) { _boardPointer = position; _manualRotationRequested = manualRotation; }

    private bool NativeCanRotate(object tablet)
    {
        _nativeCanRotate ??= GameType("DungeonManager")?.GetMethod("IsTabletRotatable",
            BindingFlags.Static | BindingFlags.Public, null, new[] { tablet.GetType() }, null);
        try { return _nativeCanRotate?.Invoke(null, new[] { tablet }) is true; }
        catch { return false; }
    }

    private bool RotationMatches(TabletRotationTarget expected, Component visual, bool forRequest, bool requirePointer = true)
    {
        if (_boardInventory is null || _boardPanel == null || visual == null) return false;
        var item = ReadNamedObject(visual, "Item");
        var tablet = item is null ? null : ReadNamedObject(item, "StoneTablet");
        var entity = item is null ? null : ReadNamedObject(item, "Entity");
        if (tablet is null || entity is null || ReadNamedNullableInt(entity, "type") != 6) return false;
        var x = ReadNamedNullableInt(tablet, "xIdx"); var y = ReadNamedNullableInt(tablet, "yIdx");
        if (!x.HasValue || !y.HasValue) return false;
        var sameInventory = ReferenceEquals(ReadNamedObject(tablet, "Inventory"), _boardInventory) &&
            ReferenceEquals(ReadNamedObject(visual, "Inventory"), _boardInventory);
        var id = ReadNamedString(tablet, "instanceID"); var key = ReadNamedString(tablet, "entityID");
        var position = new GridPoint(x.Value, y.Value); var rotation = ReadNamedNullableInt(tablet, "rotation");
        if (!forRequest) return TabletRotationGuard.IsObserved(expected, id, key, position, rotation, sameInventory);
        var zone = ReadNamedObject(_boardPanel, "inventoryZone") as RectTransform;
        var shown = zone != null && visual.transform is RectTransform iconRect && ScreenRect(zone).Contains(ScreenRect(iconRect).center);
        return TabletRotationGuard.CanRequest(expected, id, key, position, rotation, sameInventory,
            _boardVisible && ReadNamedString(_boardPanel, "InventoryMode") == "None", NativeCanRotate(tablet),
            shown && visual.gameObject.activeInHierarchy && (!requirePointer || ReadBool(visual, "Showing")),
            !requirePointer || zone != null && ScreenRect(zone).Contains(_boardPointer) &&
            visual.transform is RectTransform rect && ScreenRect(rect).Contains(_boardPointer));
    }

    private void CapturePointerRotation(ScreenKind screen, List<ScreenCandidate> candidates)
    {
        _pointerRotation = null;
        if (!_manualRotationRequested || screen != ScreenKind.Inventory || _placementPlan is null || !_boardVisible || _boardPanel == null) return;
        foreach (var pair in _slotVisuals)
        {
            var visual = pair.Value;
            if (visual == null || visual.transform is not RectTransform rect || !ScreenRect(rect).Contains(_boardPointer)) continue;
            var item = ReadNamedObject(visual, "Item");
            var tablet = item is null ? null : ReadNamedObject(item, "StoneTablet");
            if (tablet is null) continue;
            var id = ReadNamedString(tablet, "instanceID"); var key = ReadNamedString(tablet, "entityID");
            var rotation = ReadNamedNullableInt(tablet, "rotation");
            if (id is null || key is null || !rotation.HasValue) continue;
            var expected = new TabletRotationTarget(id, key, pair.Key, rotation.Value);
            _nativeRotateRequest ??= _boardPanel.GetType().GetMethod("OnTabletRotate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { visual.GetType() }, null);
            if (_nativeRotateRequest is null || !RotationMatches(expected, visual, forRequest: true)) continue;
            var panel = _boardPanel;
            candidates.Add(new ScreenCandidate(expected.Token, CandidateKind.Item, null));
            _actions[expected.Token] = () =>
            {
                if (!ReferenceEquals(panel, _boardPanel) || !RotationMatches(expected, visual, forRequest: true))
                    throw new InvalidOperationException("석판 또는 포인터/회전 권한이 바뀌어 회전 요청을 중단했습니다.");
                // Exactly the native UI handler: inventory.DoClickAction -> normal
                // server/command route. Never call StoneTablet.Rotate directly.
                _nativeRotateRequest.Invoke(panel, new object[] { visual });
            };
            _rectangles[expected.Token] = rect;
            _actionOutcomes[expected.Token] = _ => RotationMatches(expected, visual, forRequest: false);
            _pointerRotation = expected;
            break;
        }
    }

    private static void AppendTabletState(System.Text.StringBuilder signature, IEnumerable? tablets)
    {
        if (tablets is null) return;
        foreach (var tablet in tablets.Cast<object>().OrderBy(x => ReadNamedString(x, "instanceID"), StringComparer.Ordinal))
        {
            signature.Append("|tablet:").Append(ReadNamedString(tablet, "instanceID"))
                .Append(':').Append(ReadNamedString(tablet, "entityID")).Append(':').Append(ReadNamedString(tablet, "rotation"))
                .Append(':').Append(ReadNamedString(tablet, "xIdx")).Append(',').Append(ReadNamedString(tablet, "yIdx"))
                .Append(':').Append(ReadNamedString(tablet, "query")).Append(':').Append(ReadNamedString(tablet, "conditionQuery"));
        }
    }
}
