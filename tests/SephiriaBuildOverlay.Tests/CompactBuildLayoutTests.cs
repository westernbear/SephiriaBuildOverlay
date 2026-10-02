using SephiriaBuildOverlay.Plugin;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public sealed class CompactBuildLayoutTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompactControlsFitInsidePanelWithoutOverlapping(bool gamepad)
    {
        var controls = new[] { CompactBuildLayout.Heading, CompactBuildLayout.Settings, CompactBuildLayout.Close,
            CompactBuildLayout.Input(gamepad), CompactBuildLayout.Load(gamepad), CompactBuildLayout.ActiveTitle, CompactBuildLayout.Resize };
        foreach (var bounds in controls)
        {
            Assert.True(bounds.X >= 0 && bounds.Y >= 0 && bounds.Width > 0 && bounds.Height >= 28);
            Assert.True(bounds.X + bounds.Width <= OverlayUiTokens.CompactWidth);
            Assert.True(bounds.Y + bounds.Height <= OverlayUiTokens.CompactHeight);
        }
        for (var i = 0; i < controls.Length; i++)
            for (var j = i + 1; j < controls.Length; j++)
            {
                var a = controls[i]; var b = controls[j];
                Assert.False(a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y);
            }
    }

    [Fact]
    public void BasicPanelKeepsItsCompactSize()
    { Assert.Equal(420, OverlayUiTokens.CompactWidth); Assert.Equal(144, OverlayUiTokens.CompactHeight); Assert.Equal(28, CompactBuildLayout.ActiveTitle.Height); }

    [Fact]
    public void ScreenPixelTypographyFitsSmallControls()
    {
        Assert.InRange(OverlayUiTokens.BodyFontSize, 14, 18);
        Assert.True(OverlayUiTokens.HeadingFontSize < CompactBuildLayout.Heading.Height);
        Assert.True(OverlayUiTokens.BodyFontSize * 1.5f <= CompactBuildLayout.Settings.Height - 4);
        Assert.True(OverlayUiTokens.SmallFontSize * 1.5f <= CompactBuildLayout.ActiveTitle.Height - 4);
    }
}
