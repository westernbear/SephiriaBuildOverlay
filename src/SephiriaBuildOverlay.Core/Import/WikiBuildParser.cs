using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Import;

public static class WikiBuildParser
{
    public static ImportedBuild Parse(string json, Guid expectedId)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new JsonException("응답이 비어 있습니다.");

        var root = JObject.Parse(json);
        var data = root["data"] as JObject ?? root;
        var idText = RequiredString(data, "postUuid");
        if (!Guid.TryParseExact(idText, "D", out var id) || id != expectedId)
            throw new JsonException("응답의 postUuid가 요청한 빌드와 일치하지 않습니다.");

        var content = data["content"] as JArray
            ?? throw new JsonException("필수 content 배열이 없습니다.");
        var sections = new List<ImportedSection>(content.Count);
        for (var sectionIndex = 0; sectionIndex < content.Count; sectionIndex++)
        {
            if (content[sectionIndex] is not JObject sectionObject)
                throw new JsonException($"content[{sectionIndex}]가 객체가 아닙니다.");
            var itemArray = sectionObject["items"] as JArray
                ?? throw new JsonException($"content[{sectionIndex}].items 배열이 없습니다.");
            var items = new List<ImportedItem>(itemArray.Count);
            for (var itemIndex = 0; itemIndex < itemArray.Count; itemIndex++)
            {
                if (itemArray[itemIndex] is not JObject itemObject)
                    throw new JsonException($"content[{sectionIndex}].items[{itemIndex}]가 객체가 아닙니다.");
                // Every occurrence is intentionally retained. artifact_values is never read.
                items.Add(new ImportedItem(
                    OptionalString(itemObject, "id") ?? $"{sectionIndex}:{itemIndex}",
                    RequiredString(itemObject, "value")));
            }

            sections.Add(new ImportedSection(
                sectionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OptionalString(sectionObject, "label") ?? $"구역 {sectionIndex + 1}",
                OptionalString(sectionObject, "description") ?? string.Empty,
                items));
        }

        var talents = new Dictionary<string, int>(StringComparer.Ordinal);
        if (data["ability"] is JObject ability)
        {
            foreach (var talent in TalentNames.All)
            {
                var token = ability[talent];
                talents[talent] = token?.Type == JTokenType.Integer ? token.Value<int>() : 0;
            }
        }
        else
        {
            foreach (var talent in TalentNames.All) talents[talent] = 0;
        }

        var combos = data["combo"] is JArray comboArray
            ? comboArray.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
            : Array.Empty<string>();

        return new ImportedBuild(
            id,
            OptionalString(data, "title") ?? id.ToString("D"),
            RequiredString(data, "version"),
            OptionalString(data, "weapon"),
            OptionalString(data, "miracle"),
            sections,
            talents,
            combos,
            OptionalString(data, "costume"));
    }

    private static string RequiredString(JObject obj, string property)
    {
        var value = OptionalString(obj, property);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new JsonException($"필수 문자열 '{property}'가 없습니다.");
    }

    private static string? OptionalString(JObject obj, string property)
    {
        var token = obj[property];
        return token?.Type == JTokenType.String ? token.Value<string>() : null;
    }
}
