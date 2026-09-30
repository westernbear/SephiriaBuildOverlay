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
            width = inventory is null ? null : ReadNamedNullableInt(inventory, "Width"),
            height = inventory is null ? null : ReadNamedNullableInt(inventory, "Height"),
            statusPanel = statusPanel is not null && ReadBool(statusPanel, "IsOpened"),
            font = NativeFontDiagnostic(statusPanel),
            overlay = _nativeLayer?.Diagnostics(),
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
                query = ReadNamedString(x, "query"), condition = ReadNamedString(x, "conditionQuery"),
                effects = DiagnosticRange(ReadNamedObject(x, "EffectRange") as IEnumerable),
                criteria = DiagnosticRange(ReadNamedObject(x, "CriteriaRange") as IEnumerable)
            }).ToArray()
        };
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
        .Select(x => (object)new { type = x.GetType().Name, x = ReadNamedNullableInt(x, "x", "X"), y = ReadNamedNullableInt(x, "y", "Y"), value = ReadNamedString(x, "level", "value", "criteria") }).ToArray();
}
