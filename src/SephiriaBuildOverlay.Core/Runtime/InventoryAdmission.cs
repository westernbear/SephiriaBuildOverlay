namespace SephiriaBuildOverlay.Core.Runtime;

public enum InventoryAdmission { Available, Merge, Full, LimitReached, Unverified }

public static class InventoryAdmissionPolicy
{
    // Native CanAddItem is read-only, but does not model Wisdom's unique-pair
    // acquisition path. That override must be verified from the owned instance.
    public static InventoryAdmission Evaluate(int? nativeResult, bool? hasSpace,
        bool mergeEnabled = false, bool? sameArtifact = false, int? maximum = null, int? enchant = null)
    {
        if (mergeEnabled && sameArtifact != false && nativeResult is 0 or 1 or 2 or 4)
        {
            if (sameArtifact is null || maximum is null || enchant is null) return InventoryAdmission.Unverified;
            return maximum > 0 && enchant < maximum ? InventoryAdmission.Merge : InventoryAdmission.LimitReached;
        }
        if (nativeResult == 1) return InventoryAdmission.Merge;
        if (nativeResult == 0) return hasSpace == true ? InventoryAdmission.Available :
            hasSpace == false ? InventoryAdmission.Full : InventoryAdmission.Unverified;
        return nativeResult == 2 ? InventoryAdmission.Full :
            nativeResult is 3 or 4 or 5 or 6 or 7 or 8 or 10 ? InventoryAdmission.LimitReached : InventoryAdmission.Unverified;
    }

    public static string? BlockReason(InventoryAdmission admission) => admission switch
    {
        InventoryAdmission.Full => "가방 공간이 없습니다. 아이템을 정리한 뒤 다시 확인하세요.",
        InventoryAdmission.LimitReached => "중복·강화 한도 또는 게임의 획득 조건으로 받을 수 없습니다.",
        InventoryAdmission.Unverified => "획득 공간을 확인하지 못했습니다. 게임에서 수동으로 확인하세요.",
        _ => null
    };

    public static bool IsInventoryItem(CandidateKind kind) => kind is CandidateKind.Artifact or CandidateKind.Item or CandidateKind.Tablet;
}
