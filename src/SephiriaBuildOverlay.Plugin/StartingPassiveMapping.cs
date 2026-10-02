namespace SephiriaBuildOverlay.Plugin;

internal static class StartingPassiveMapping
{
    public static (ulong Id, string Key, string Name) Expected(string slug) => slug switch
    {
        "anger" => (4UL, "Passive_Crit_Name", "분노"), "rapid" => (5UL, "Passive_Swiftness_Name", "신속"),
        "survival" => (6UL, "Passive_Survive_Name", "생존"), "patience" => (7UL, "Passive_Patience_Name", "인내"),
        "wisdom" => (8UL, "Passive_MPRegen_Name", "지혜"), "will" => (9UL, "Passive_Item_Name", "의지"),
        "base" => (11UL, "Passive_Ingenuity_Name", "기지"), _ => (0UL, "", "")
    };

    public static bool Matches(string slug, ulong id, string? key, string? name)
    {
        var expected = Expected(slug);
        return expected.Id != 0 && expected.Id == id && expected.Key == key && expected.Name == name;
    }
}
