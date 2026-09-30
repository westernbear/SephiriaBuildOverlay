using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Plugin;

internal enum CandidateFrameKind { None, Required, Recommended, NextAction }

// Presentation only. A frame never grants action authorization or changes
// recommendation priority; excluded / unrelated items keep their native UI.
internal static class CandidateFramePolicy
{
    public static float Thickness(CandidateFrameKind frame, float scale) => Math.Max(frame == CandidateFrameKind.NextAction ? 4 : 3,
        (float)Math.Round((frame == CandidateFrameKind.NextAction ? 4 : 3) * scale));
    public static CandidateFrameKind Resolve(bool active, TargetRole? role, bool otherBuildTarget, bool nextAction)
    {
        if (!active) return CandidateFrameKind.None;
        if (nextAction) return CandidateFrameKind.NextAction;
        if (role == TargetRole.Required) return CandidateFrameKind.Required;
        if (role == TargetRole.Recommended || otherBuildTarget) return CandidateFrameKind.Recommended;
        return CandidateFrameKind.None;
    }
}
