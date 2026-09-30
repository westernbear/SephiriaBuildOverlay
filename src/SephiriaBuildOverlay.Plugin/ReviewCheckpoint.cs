using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Review;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class ReviewCheckpoint
{
    public int Schema { get; set; } = 1;
    public ImportedBuild Build { get; set; } = null!;
    public bool WasActivated { get; set; }
    public List<SectionCheckpoint> Sections { get; set; } = new();

    public static ReviewCheckpoint Capture(BuildReviewSession review, bool activated) => new()
    {
        Build = review.Build, WasActivated = activated,
        Sections = review.Sections.Select(section => new SectionCheckpoint
        {
            Id = section.Source.Id, Role = section.Role, Priority = section.Priority,
            Items = section.Items.Select(item => new ItemCheckpoint
            {
                Id = item.Source.InstanceId, Slug = item.Source.Slug, Role = item.RoleOverride,
                Priority = item.PriorityOverride, Desired = item.DesiredAcquisitions, Mapping = item.ManualCatalogKey
            }).ToList()
        }).ToList()
    };

    public BuildReviewSession Restore(VersionedCatalog catalog)
    {
        if (Schema != 1 || Build is null || Sections is null || Sections.Count != Build.Sections.Count)
            throw new InvalidDataException("Invalid review checkpoint schema.");
        var review = new BuildReviewSession(Build, catalog);
        for (var sectionIndex = 0; sectionIndex < Sections.Count; sectionIndex++)
        {
            var saved = Sections[sectionIndex]; var section = review.Sections[sectionIndex];
            if (saved.Id != section.Source.Id || !Enum.IsDefined(typeof(TargetRole), saved.Role) || saved.Items.Count != section.Items.Count)
                throw new InvalidDataException("Review sections no longer match the saved source.");
            section.Role = saved.Role; section.Priority = saved.Priority;
            for (var itemIndex = 0; itemIndex < saved.Items.Count; itemIndex++)
            {
                var item = section.Items[itemIndex]; var value = saved.Items[itemIndex];
                if (value.Id != item.Source.InstanceId || value.Slug != item.Source.Slug || value.Desired < 1 ||
                    (value.Role.HasValue && !Enum.IsDefined(typeof(TargetRole), value.Role.Value)))
                    throw new InvalidDataException("Invalid saved item classification.");
                item.RoleOverride = value.Role; item.PriorityOverride = value.Priority;
                item.DesiredAcquisitions = value.Desired; item.ManualCatalogKey = value.Mapping;
            }
        }
        // Bindings are deliberately not stored: every restart/reload revalidates
        // current game metadata before restoring the activated plan.
        return review;
    }
}

internal sealed class SectionCheckpoint
{
    public string Id { get; set; } = "";
    public TargetRole Role { get; set; }
    public int Priority { get; set; }
    public List<ItemCheckpoint> Items { get; set; } = new();
}
internal sealed class ItemCheckpoint
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public TargetRole? Role { get; set; }
    public int? Priority { get; set; }
    public int Desired { get; set; }
    public string? Mapping { get; set; }
}

internal sealed class ReviewCheckpointStore
{
    private readonly string _path;
    internal string CheckpointPath => _path;
    public ReviewCheckpointStore(string? directory = null) => _path = Path.Combine(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SephiriaBuildOverlay"), "review.json");
    public ReviewCheckpoint? Load()
    {
        if (!File.Exists(_path)) return null;
        if (new FileInfo(_path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Review checkpoint exceeds 2 MiB.");
        return JsonConvert.DeserializeObject<ReviewCheckpoint>(File.ReadAllText(_path));
    }
    public void Save(ReviewCheckpoint checkpoint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(checkpoint), new System.Text.UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(temporary, _path, null); else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
