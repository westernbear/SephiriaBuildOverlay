namespace SephiriaBuildOverlay.Plugin;

internal static class StartingWeaponState
{
    public static bool Matches(int acceptedSelection, int? costumeWeapon, int? storedSelection, int? equippedWeapon) =>
        storedSelection == acceptedSelection && equippedWeapon == (costumeWeapon ?? acceptedSelection);
}
