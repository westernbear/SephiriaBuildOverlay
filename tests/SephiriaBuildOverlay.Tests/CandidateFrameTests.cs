using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Plugin;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public class CandidateFrameTests
{
    [Fact]
    public void InactiveAndUnrelatedItemsKeepNativeAppearance()
    {
        Assert.Equal(CandidateFrameKind.None, CandidateFramePolicy.Resolve(false, TargetRole.Required, true, true));
        Assert.Equal(CandidateFrameKind.None, CandidateFramePolicy.Resolve(true, null, false, false));
        Assert.Equal(CandidateFrameKind.None, CandidateFramePolicy.Resolve(true, TargetRole.Excluded, false, false));
    }

    [Fact]
    public void FramesDistinguishGoalsAndNextActionOverridesRole()
    {
        Assert.Equal(CandidateFrameKind.Required, CandidateFramePolicy.Resolve(true, TargetRole.Required, false, false));
        Assert.Equal(CandidateFrameKind.Recommended, CandidateFramePolicy.Resolve(true, TargetRole.Recommended, false, false));
        Assert.Equal(CandidateFrameKind.Recommended, CandidateFramePolicy.Resolve(true, null, true, false));
        Assert.Equal(CandidateFrameKind.NextAction, CandidateFramePolicy.Resolve(true, TargetRole.Required, false, true));
        Assert.Equal(CandidateFrameKind.NextAction, CandidateFramePolicy.Resolve(true, null, false, true));
    }
}
