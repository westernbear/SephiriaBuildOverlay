using UnityEngine;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;

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
    }

    internal object ReadStartingPresetCatalog() => new
    {
        passives = Resources.LoadAll("Passive", GameType("PassiveEntity")).Select(x => new
        { id = ReadNamedString(x, "id"), key = ReadNamedString(ReadNamedObject(x, "aName")!, "key"), name = ReadNamedString(x, "aName"), max = ReadNamedNullableInt(x, "maxLevel") }).ToArray(),
        costumes = Resources.LoadAll("Costume", GameType("CostumeEntity")).Select(x => new
        { id = ReadNamedString(x, "id"), name = ReadNamedString(x, "aName"), key = ReadNamedString(ReadNamedObject(x, "aName")!, "key") }).ToArray(),
        gameActionsAllowed = false
    };
}
