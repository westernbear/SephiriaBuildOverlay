namespace SephiriaBuildOverlay.Plugin;

internal static class StartingOptionEligibility
{
    // UI_CostumePanel.EquipSkin permits Default OR SkinPurchased_<skinID>.
    // Locked is content metadata, not proof that this profile does not own it.
    public static bool? SkinSelectable(bool exists, bool matchesCostume, string? unlockType, bool? purchased) =>
        !exists || !matchesCostume ? null : unlockType == "Default" ? true :
        unlockType is "Purchase" or "Locked" ? purchased : null;
}
