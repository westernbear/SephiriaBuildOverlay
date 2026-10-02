namespace SephiriaBuildOverlay.Plugin;

internal static class ScreenSpaceUiMetrics
{
    public const float ReferencePixelsPerUnit = 100;
    public const float PanelBorderPixels = 6;

    // Image's slice border is expressed in sprite units, not screen pixels.
    // A sprite authored for a scaled native canvas must not consume the
    // compact panel's content padding on our unscaled overlay canvas.
    public static float SliceMultiplier(float maximumBorder, float spritePixelsPerUnit)
    {
        if (float.IsNaN(maximumBorder) || float.IsInfinity(maximumBorder) || maximumBorder < 0 ||
            float.IsNaN(spritePixelsPerUnit) || float.IsInfinity(spritePixelsPerUnit) || spritePixelsPerUnit <= 0) return 1;
        return Math.Max(1, maximumBorder / spritePixelsPerUnit * ReferencePixelsPerUnit / PanelBorderPixels);
    }
}
