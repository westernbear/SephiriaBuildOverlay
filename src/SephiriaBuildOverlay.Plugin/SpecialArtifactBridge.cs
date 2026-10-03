using System.Collections;
using System.Globalization;
using System.Text;
using SephiriaBuildOverlay.Core.Solvers;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private readonly Dictionary<string, string[]> _observedArtifactCategories = new(StringComparer.Ordinal);
    private object NativeStaticField(string type, string field) => GameType(type)?.GetField(field,
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null)
        ?? throw new InvalidOperationException("게임 정적 필드 누락: " + type + "." + field);

    private static string[] NativeStrings(object? source) => source is IEnumerable sequence
        ? sequence.Cast<object>().Select(x => x as string ?? throw new InvalidOperationException("아티팩트 분류 형식 변경")).ToArray()
        : throw new InvalidOperationException("아티팩트 분류 누락");
    private static double[] NativeAmounts(object charm, string field)
    {
        if (ReadNamedObject(charm, field) is not Array table || table.Length == 0 || table.Length > 256)
            throw new InvalidOperationException("특수 아티팩트 효과 표 누락: " + field);
        var result = table.Cast<object>().Select(x => Convert.ToDouble(x, CultureInfo.InvariantCulture)).ToArray();
        if (result.Any(x => double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > 1000000))
            throw new InvalidOperationException("특수 아티팩트 효과 값 오류");
        return result;
    }
    private ArtifactPlacementEffect CapturePlacementEffect(object item, object charm, string id)
    {
        var type = charm.GetType();
        bool Is(string name) => GameType(name)?.IsInstanceOfType(charm) == true;
        for (var ancestor = type; ancestor is not null; ancestor = ancestor.BaseType)
            if (ArtifactPlacementEffect.RequiresManualPlacement(ancestor.Name))
                _optimizationUnavailable = "자동 시전·추가 마법탄의 배치 효과는 수동 확인 필요";
        var kind = type.Name switch
        {
            "Charm_UpCharmDamage" => ArtifactPlacementKind.Needle,
            "Charm_ReduceMPCost" => ArtifactPlacementKind.ManaSupport,
            "Charm_RightSpellCooldownHelper" => ArtifactPlacementKind.CooldownSupport,
            "Charm_NearLevelDamage" => ArtifactPlacementKind.NeighborLevels,
            "Charm_PlanetModule" => ArtifactPlacementKind.PlanetSupport,
            "Charm_CompanionChaos" => ArtifactPlacementKind.RowCompanions,
            "Charm_3Elemental_ByRow" => ArtifactPlacementKind.RowCategory,
            "Charm_WhitePaper" => ArtifactPlacementKind.WhitePaper,
            _ => ArtifactPlacementKind.Ordinary
        };
        // Only the native category callbacks whose exact semantics we model
        // may replace the basic implementation. Unknown/modded callbacks remain
        // fail-closed rather than silently becoming movable ordinary charms.
        var preSet = type.GetMethod("OnPreSetEffectRefreshed")?.DeclaringType?.Name;
        var categoryMethod = type.GetMethod("GetItemCategory");
        var categoryOwner = categoryMethod?.DeclaringType?.Name;
        if (!ArtifactPlacementEffect.SupportsCategoryCallback(preSet) || !ArtifactPlacementEffect.SupportsCategoryCallback(categoryOwner))
            _optimizationUnavailable = "미지원 특수 아티팩트 효과는 수동 확인 필요";
        var observed = NativeStrings(categoryMethod?.Invoke(charm, null));
        _observedArtifactCategories[id] = observed;
        var entity = ReadNamedObject(item, "Entity") ?? throw new InvalidOperationException("아티팩트 엔티티 누락");
        var categories = NativeStrings(ReadNamedObject(entity, "categories"));
        var attackableType = GameType("IAttackableCharm");
        var attackable = attackableType?.IsInstanceOfType(charm) == true &&
            attackableType.GetMethod("IsAttackableCharm")?.Invoke(charm, null) is true;
        var offset = kind switch
        {
            ArtifactPlacementKind.Needle => new GridPoint(ReadNamedNullableInt(charm, "xOffset") ?? throw new InvalidOperationException("침 X 방향 누락"),
                ReadNamedNullableInt(charm, "yOffset") ?? throw new InvalidOperationException("침 Y 방향 누락")),
            ArtifactPlacementKind.ManaSupport => new GridPoint(-1, 0),
            ArtifactPlacementKind.CooldownSupport => new GridPoint(1, 0),
            _ => default
        };
        var amounts = kind switch
        {
            ArtifactPlacementKind.Needle => NativeAmounts(charm, "damageBonusByLevel"),
            ArtifactPlacementKind.ManaSupport => NativeAmounts(charm, "reducePercentByLevel"),
            ArtifactPlacementKind.CooldownSupport => NativeAmounts(charm, "cooldownRecoveryByLevel"),
            ArtifactPlacementKind.NeighborLevels => NativeAmounts(charm, "allDamageBonusByLevel"),
            _ => Array.Empty<double>()
        };
        var extra = kind == ArtifactPlacementKind.Needle && ReadBool(charm, "hasDependencyCondition")
            ? NativeAmounts(charm, "dependencyDamageBonusByLevel") : Array.Empty<double>();
        var directions = kind == ArtifactPlacementKind.NeighborLevels
            ? (NativeStaticField("Charm_NearLevelDamage", "directions") as IEnumerable ?? throw new InvalidOperationException("조화의 수정 이웃 방향 누락"))
                .Cast<object>().Select(p => new GridPoint(ReadNamedNullableInt(p, "x") ?? throw new InvalidOperationException("이웃 X 누락"),
                    ReadNamedNullableInt(p, "y") ?? throw new InvalidOperationException("이웃 Y 누락"))).ToArray() : null;
        return new ArtifactPlacementEffect(kind, categories, attackable, Is("ICompanionCharm"), Is("Charm_SummonGreenBat"),
            Is("Charm_FireIce") || Is("Charm_FireIceWeapon"), offset, amounts, extra, ReadBool(charm, "hasDependencyCondition"),
            ReadNamedNullableInt(charm, "maxRarity") ?? 0, ReadNamedNullableInt(entity, "rarity") ?? 0,
            kind == ArtifactPlacementKind.RowCategory ? NativeStrings(ReadNamedObject(charm, "lineCategory")) : null,
            kind == ArtifactPlacementKind.WhitePaper ? ReadNamedNullableInt(charm, "match") ?? throw new InvalidOperationException("백지 분류 조건 누락") : 2, directions);
    }
    private BoardComboModel CaptureBoardCombos(BoardOptimizationInput input)
    {
        var current = JointBoardPlanner.Current(input);
        var state = new SpecialArtifactEvaluation(input, current, JointBoardPlanner.Cells(input, current).ToDictionary(x => x.Position));
        var raw = state.ComboCounts();
        if (!state.CategoriesReliable) throw new InvalidOperationException("서로 분류를 참조하는 백지는 수동 배치 필요");
        foreach (var artifact in input.Artifacts)
            if (!_observedArtifactCategories.TryGetValue(artifact.Id, out var observed) ||
                !new HashSet<string>(observed.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal).SetEquals(state.Categories(artifact)))
                throw new InvalidOperationException("특수 아티팩트 분류 갱신 대기 중");
        var native = new Dictionary<string, int>(StringComparer.Ordinal);
        if (ReadNamedObject(_boardInventory!, "currentSetEffectCount") is not IEnumerable pairs)
            throw new InvalidOperationException("현재 콤보 수량 누락");
        foreach (var pair in pairs.Cast<object>())
        {
            var key = ReadNamedString(pair, "Key") ?? throw new InvalidOperationException("콤보 ID 누락");
            native[key] = ReadNamedNullableInt(pair, "Value") ?? throw new InvalidOperationException("콤보 수량 누락");
        }
        var environment = ReadStatic("DungeonManager", "Instance");
        if (environment is not null && ReadNamedObject(environment, "hardModeEnvironment") is IEnumerable conditions)
            foreach (var pair in conditions.Cast<object>())
                if (ReadNamedString(pair, "Key") == "OVERLAPITEMCOMBO" && ReadNamedNullableInt(pair, "Value") > 0 &&
                    input.Artifacts.GroupBy(a => a.Key).Any(g => g.Count() > 1 && g.Any(a => a.PlacementEffect.DynamicCategory)))
                    throw new InvalidOperationException("중복 분류 제한이 있는 특수 아티팩트는 수동 배치 필요");
        var offsets = raw.Keys.Concat(native.Keys).Distinct().ToDictionary(k => k,
            k => SpecialArtifactRules.Count(native, k) - SpecialArtifactRules.Count(raw, k), StringComparer.Ordinal);
        var protectedCategories = new List<string>(); var goals = new List<string>();
        if (NativeStaticField("ItemDatabase", "itemCategories") is not IDictionary categories)
            throw new InvalidOperationException("게임 콤보 카탈로그 누락");
        foreach (DictionaryEntry entry in categories)
        {
            if (entry.Key is not string key || entry.Value is null) continue;
            var label = ReadNamedString(entry.Value, "categoryName");
            if (_placementPlan!.Combos.Any(g => string.Equals(g, key, StringComparison.OrdinalIgnoreCase) || g == label)) goals.Add(key);
            var prefab = ReadNamedObject(entry.Value, "comboEffectPrefab") as GameObject;
            var comboType = GameType("ComboEffectBase");
            var behavior = prefab == null || comboType is null ? null : prefab.GetComponent(comboType);
            // Mystic creates randomized inventory engravings. Unknown combo
            // implementations may also mutate matrices: preserve their count.
            if (behavior != null && behavior.GetType().Name is not ("ComboEffectBase" or "ComboEffect_Alchemy" or "ComboEffect_DarkCloud" or
                "ComboEffect_Debuff" or "ComboEffect_FlameSword" or "ComboEffect_Frost" or "ComboEffect_Guardian" or "ComboEffect_Planet"))
                protectedCategories.Add(key);
        }
        return new BoardComboModel(goals, offsets, protectedCategories);
    }

    private static void AppendPlacementInvariant(StringBuilder s, BoardArtifact artifact)
    {
        var e = artifact.PlacementEffect;
        s.Append(':').Append(e.Kind).Append(':').Append(e.Attackable).Append(':').Append(e.Companion).Append(':').Append(e.SummonPlanet)
            .Append(':').Append(e.PreserveSide).Append(':').Append(e.PreserveSide && artifact.Position.X <= 2).Append(':').Append(e.Offset)
            .Append(':').Append(e.Rarity).Append(':').Append(e.DependencyCondition).Append(':').Append(e.MaximumRarity).Append(':').Append(e.PaperMatch);
        foreach (var c in e.Categories) s.Append(":c=").Append(c);
        foreach (var c in e.RowCategories) s.Append(":r=").Append(c);
        foreach (var n in e.Amounts) s.Append(":n=").Append(n.ToString("R", CultureInfo.InvariantCulture));
        foreach (var n in e.DependencyAmounts) s.Append(":d=").Append(n.ToString("R", CultureInfo.InvariantCulture));
        if (e.Kind == ArtifactPlacementKind.NeighborLevels) foreach (var p in e.NeighborOffsets) s.Append(":o=").Append(p);
    }
}
