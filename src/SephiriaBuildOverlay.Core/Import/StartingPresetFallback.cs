using System.Globalization;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Import;

// Build structured Wiki data into the game's own compact import format. No
// game objects, purchases, save slots, or native mutations occur here.
public static class StartingPresetFallback
{
    public static NativePreset Create(ImportedBuild build, string currentCompact, VersionedCatalog catalog,
        IReadOnlyList<GameEntityDescriptor> entities, Func<string, ulong?> passiveId, ICollection<string> warnings)
    {
        // Source version is metadata, not an import gate. Each option below
        // must still match the current game's observed entity descriptor.
        var preset = NativePreset.ParseCompact(currentCompact);
        string? Verified(string slug, CatalogKind kind)
        {
            var binding = catalog.Verify(slug, kind, entities);
            if (binding.AllowsAutomaticAction) return binding.GameKey;
            warnings.Add($"{slug}: 시작 세팅 매핑 미검증"); return null;
        }
        ApplyWeapon(preset, build.WeaponSlug, catalog, entities, warnings);
        if (!string.IsNullOrWhiteSpace(build.CostumeSlug) && Verified(build.CostumeSlug!, CatalogKind.Costume) is { } costume)
            preset.SetValidatedCostume(costume, preset.Costume == costume ? preset.Skin : "");
        if (build.HasTalentAllocation)
        {
            var passives = new List<(ulong Id, int Points)>();
            var valid = true;
            foreach (var talent in TalentNames.All)
            {
                var points = build.Talents.TryGetValue(talent, out var value) ? value : 0;
                if (points < 0 || points > 100) throw new FormatException("특성 목표 수치가 잘못되었습니다.");
                if (points == 0) continue;
                var id = passiveId(talent);
                if (!id.HasValue || passives.Any(x => x.Id == id.Value)) { valid = false; warnings.Add(talent + ": 특성 매핑 미검증"); }
                else passives.Add((id.Value, points));
            }
            if (valid) { preset.Passives.Clear(); preset.Passives.AddRange(passives); }
        }
        if (build.Sections.Count > 0)
        {
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in build.Sections.SelectMany(x => x.Items))
                if (!ids.ContainsKey(item.Slug) && Verified(item.Slug, CatalogKind.Artifact) is { } key &&
                    int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) ids.Add(item.Slug, id);
            preset.Favorites.Clear(); preset.Favorites.AddRange(ids.Values.Distinct());
            // Explicit starting sections take precedence; otherwise use the
            // original item order. Native unlock/type/capacity filters follow.
            var starting = build.Sections.Where(x => x.Label.Trim().StartsWith("시작 아티팩트", StringComparison.Ordinal)).ToArray();
            var source = starting.Length > 0 ? starting : build.Sections;
            if (ids.Count > 0)
            {
                preset.Pocket.Clear();
                foreach (var item in source.SelectMany(x => x.Items))
                    if (ids.TryGetValue(item.Slug, out var id)) preset.Pocket.Add((-1, id, 1));
            }
        }
        ApplyFruitSkewer(preset, build.FruitSkewer);
        // Run through the strict parser before allowing any native mutation.
        return NativePreset.ParseCompact(preset.Compact());
    }

    // Structured fruit settings are authoritative when present, including an
    // explicit empty list. A missing field preserves the native preset's data.
    public static void ApplyFruitSkewer(NativePreset preset, IReadOnlyList<ImportedFruit>? fruits)
    {
        if (fruits is not null)
        {
            preset.Fruits.Clear(); preset.SetAdaptive(false);
            foreach (var fruit in fruits)
            {
                if (fruit.Value < -16 || fruit.Value > 16) throw new FormatException("과일꼬치 수량 제한을 초과했습니다.");
                if (fruit.Key == "adaptive_drop_bonus") { preset.SetAdaptive(fruit.Value > 0); continue; }
                var category = Category(fruit.Key);
                for (var i = 0; i < Math.Abs(fruit.Value); i++) preset.Fruits.Add((category, Math.Sign(fruit.Value)));
                if (preset.Fruits.Count > 512) throw new FormatException("과일꼬치 항목 수 제한을 초과했습니다.");
            }
        }
    }

    // Wiki's weapon is the final target. The lobby may equip only the verified
    // root, never grant the final upgrade or rely on a stale embedded preset W.
    public static void ApplyWeapon(NativePreset preset, string? slug, VersionedCatalog catalog,
        IReadOnlyList<GameEntityDescriptor> entities, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(slug)) return;
        try
        {
            var path = catalog.BuildWeaponPath(slug!);
            var entries = path.Select(key => catalog.Entries.Single(x => x.Kind == CatalogKind.Weapon && x.GameKey == key)).ToArray();
            foreach (var entry in entries)
                if (!catalog.Verify(entry.Slug, CatalogKind.Weapon, entities).AllowsAutomaticAction)
                { warnings.Add(entry.Slug + ": 시작 무기 매핑 미검증"); return; }
            if (int.TryParse(path[0], NumberStyles.None, CultureInfo.InvariantCulture, out var root)) preset.SetWeapon(root);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        { warnings.Add("시작 무기 경로를 검증하지 못했습니다."); }
    }

    public static string Category(string slug) => slug switch
    {
        "extrium" => "darkcloud", "magic_engineering" => "magitech", "spring_song" => "windsong",
        "firmness" => "sturdy", "yinggalbul" => "ember", "bargaining" => "savvy", "colleague" => "companion",
        "element" => "elemental", "ice_weapon" => "frost", "mystery" => "mystic", "sun_sword" => "flamesword",
        _ => slug
    };
}
