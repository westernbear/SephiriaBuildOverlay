using UnityEngine;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Import;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    internal void VerifyStartingPresetCatalog()
    {
        var passives = Resources.LoadAll("Passive", GameType("PassiveEntity")).Cast<object>().ToArray();
        foreach (var slug in TalentNames.All)
        {
            var expected = StartingPassiveMapping.Expected(slug);
            var matches = passives.Where(x => Convert.ToUInt64(ReadNamedObject(x, "id")) == expected.Id).ToArray();
            if (matches.Length != 1 || ReadNamedObject(matches[0], "aName") is not { } name ||
                !StartingPassiveMapping.Matches(slug, expected.Id, ReadNamedString(name, "key"), ReadNamedString(matches[0], "aName")))
                throw new InvalidOperationException("Native passive mismatch: " + slug);
        }
        var costumes = Resources.LoadAll("Costume", GameType("CostumeEntity")).Select(x =>
            new GameEntityDescriptor(ReadNamedString(x, "id")!, CatalogKind.Costume, ReadNamedString(x, "aName")!)).ToArray();
        foreach (var entry in _catalog.Entries.Where(x => x.Kind == CatalogKind.Costume))
            if (!_catalog.Verify(entry.Slug, CatalogKind.Costume, costumes).AllowsAutomaticAction)
                throw new InvalidOperationException("Native costume mismatch: " + entry.Slug);
        _log.LogInfo("Starting catalog PASS: 7 verified passive IDs/localization keys/names, 27 verified costume mappings; no game actions.");
        var fruitIds = ReadStartingFruitCategoryIds();
        if (fruitIds.Count == 0) throw new InvalidOperationException("Native fruit category catalog is empty.");
        var fruitPreset = NativePreset.ParseCompact("AAP1\nW:0\nC:PinkRabbit\nS:\nB:0\nR:\n");
        fruitPreset.Fruits.AddRange(fruitIds.Select(x => (x.ToLowerInvariant(), 1)));
        fruitPreset.LimitLoadout(_ => null, 0, fruitIds, fruitIds.Count, 1, 1);
        if (!fruitPreset.Fruits.Select(x => x.Category).SequenceEqual(fruitIds))
            throw new InvalidOperationException("Wiki/native fruit category binding mismatch.");
        _log.LogInfo("Starting fruit catalog PASS: exact native IDs=" + string.Join(",", fruitIds) + "; no game actions.");
    }

    // ItemDatabase is initialized only after entering the lobby. At the title,
    // inspect the same resources its Initialize method loads, without invoking
    // initialization or modifying any game/database state.
    private HashSet<string> ReadStartingFruitCategoryIds() => Resources.LoadAll("ItemCategory", GameType("ItemCategoryEntity"))
        .Select(x => ReadNamedString(x, "id")).Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!).ToHashSet(StringComparer.Ordinal);

    internal object ReadStartingPresetCatalog() => new
    {
        passives = Resources.LoadAll("Passive", GameType("PassiveEntity")).Select(x => new
        { id = ReadNamedString(x, "id"), key = ReadNamedString(ReadNamedObject(x, "aName")!, "key"), name = ReadNamedString(x, "aName"), max = ReadNamedNullableInt(x, "maxLevel") }).ToArray(),
        costumes = Resources.LoadAll("Costume", GameType("CostumeEntity")).Select(x => new
        { id = ReadNamedString(x, "id"), name = ReadNamedString(x, "aName"), key = ReadNamedString(ReadNamedObject(x, "aName")!, "key"),
            fixedWeapon = ReadNamedObject(x, "defaultWeapon") is { } weapon ? ReadNamedNullableInt(weapon, "id") : null }).ToArray(),
        fruitCategories = ReadStartingFruitCategoryIds().ToArray(),
        gameActionsAllowed = false
    };
}
