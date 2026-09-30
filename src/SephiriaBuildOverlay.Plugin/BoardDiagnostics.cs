using System.Collections;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    public object ReadBoardDiagnostics()
    {
        FindLocalPlayerId(out var owned, out var player);
        PreparePlayerComponents(player);
        var inventory = LocalInventory();
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
        var statusPanel = registry?["UI_CharacterStatusPanel"] as Component;
        var tablets = inventory is null ? null : ReadNamedObject(inventory, "CurrentStoneTablets") as IEnumerable;
        return new
        {
            owned,
            inputBindings = ReadInputBindings(),
            width = inventory is null ? null : ReadNamedNullableInt(inventory, "Width"),
            height = inventory is null ? null : ReadNamedNullableInt(inventory, "Height"),
            statusPanel = statusPanel is not null && ReadBool(statusPanel, "IsOpened"),
            font = NativeFontDiagnostic(statusPanel),
            overlay = _nativeLayer?.Diagnostics(),
            inventoryMode = statusPanel is null ? null : ReadNamedString(statusPanel, "InventoryMode"),
            pointerRotation = _pointerRotation,
            ghostAssignments = _ghostAssignments.Select(x => new { x.InstanceId, x.From, x.To }).ToArray(),
            boardItems = _boardItems.Select(x => new { x.InstanceId, x.Key, x.Position, x.MaxLevel, x.CanRelocate }).ToArray(),
            levels = _slotLevels.Select(x => new { position = x.Key, value = x.Value }).ToArray(),
            icons = statusPanel is null ? null : (ReadNamedObject(statusPanel, "itemIcons") as IEnumerable)?.OfType<Component>().Select(x => new
            {
                x = ReadNamedNullableInt(x, "X"), y = ReadNamedNullableInt(x, "Y"),
                localInventory = ReferenceEquals(ReadNamedObject(x, "Inventory"), inventory),
                showing = ReadBool(x, "Showing"), active = x.gameObject.activeInHierarchy
            }).ToArray(),
            tablets = tablets?.Cast<object>().Select(x => new
            {
                id = ReadNamedNullableInt(x, "instanceID"), entity = ReadNamedNullableInt(x, "entityID"),
                x = ReadNamedNullableInt(x, "xIdx"), y = ReadNamedNullableInt(x, "yIdx"), rotation = ReadNamedNullableInt(x, "rotation"),
                rotatable = ReadBool(x, "isRotatable"),
                nativeRotatable = NativeCanRotate(x),
                query = ReadNamedString(x, "query"), condition = ReadNamedString(x, "conditionQuery"),
                effects = DiagnosticRange(ReadNamedObject(x, "EffectRange") as IEnumerable),
                criteria = DiagnosticRange(ReadNamedObject(x, "CriteriaRange") as IEnumerable)
            }).ToArray()
        };
    }

    private object[] ReadInputBindings()
    {
        var controller = ReadStatic("PlayerInputController", "Instance");
        var playerInput = controller is null ? null : ReadNamedObject(controller, "playerInput");
        var asset = playerInput is null ? null : ReadNamedObject(playerInput, "actions");
        var maps = asset is null ? null : ReadNamedObject(asset, "actionMaps") as IEnumerable;
        if (maps is null) return Array.Empty<object>();
        var result = new List<object>();
        foreach (var map in maps.Cast<object>())
        foreach (var action in (ReadNamedObject(map, "actions") as IEnumerable)?.Cast<object>() ?? Enumerable.Empty<object>())
        {
            var name = ReadNamedString(action, "name") ?? "";
            if (name.IndexOf("Open", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (var binding in (ReadNamedObject(action, "bindings") as IEnumerable)?.Cast<object>() ?? Enumerable.Empty<object>())
                result.Add(new { name, path = ReadNamedString(binding, "effectivePath", "path") });
        }
        return result.Take(100).ToArray();
    }

    private static object? NativeFontDiagnostic(Component? statusPanel)
    {
        var icon = statusPanel is null ? null : (ReadNamedObject(statusPanel, "itemIcons") as IEnumerable)?.OfType<Component>().FirstOrDefault();
        var text = icon is null ? null : ReadNamedObject(icon, "powerText");
        var font = text is null ? null : ReadNamedObject(text, "font");
        var source = font is null ? null : ReadNamedObject(font, "sourceFontFile");
        return text is null ? null : new { asset = (font as UnityEngine.Object)?.name, source = (source as UnityEngine.Object)?.name, size = ReadNamedString(text, "fontSize"), style = ReadNamedString(text, "fontStyle") };
    }

    private object? LocalInventory() => _playerComponents.Where(x => x != null)
        .Select(x => ReadNamedObject(x, "Inventory")).FirstOrDefault(x => x?.GetType().Name == "GridInventory");

    private static object[]? DiagnosticRange(IEnumerable? sequence) => sequence?.Cast<object>()
        .Select(x =>
        {
            var position = ReadNamedObject(x, "position");
            return (object)new
            {
                type = x.GetType().Name,
                x = position is null ? ReadNamedNullableInt(x, "x", "X") : ReadNamedNullableInt(position, "x"),
                y = position is null ? ReadNamedNullableInt(x, "y", "Y") : ReadNamedNullableInt(position, "y"),
                effect = ReadNamedString(x, "effectType"),
                value = ReadNamedString(x, "levelParam", "level", "value", "criteria")
            };
        }).ToArray();
}
