using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class NotificationAndModalRegressionTests
{
    [Theory]
    [InlineData("<color=red>의상</color>: <indent=10>미해금</indent>", "의상: 미해금")]
    [InlineData("<COLOR=#ff0000>경고</COLOR><br>주사위 1 → 0", "경고\n주사위 1 → 0")]
    [InlineData("&lt;indent=10&gt;&lt;color=yellow&gt;시작 무기&lt;/color&gt;&lt;/indent&gt;", "시작 무기")]
    [InlineData("[INDENT-ID]미해금[COLOR-ID] 옵션{/COLOR-ID}", "미해금 옵션")]
    [InlineData("<INDENT-ID=10>옵션<COLOR-ID=red> 제외</COLOR-ID></INDENT-ID>", "옵션 제외")]
    [InlineData("포인트 < 10, 필요 > 5 · F8", "포인트 < 10, 필요 > 5 · F8")]
    [InlineData("https://sephiria.wiki/builds/test?x=1&amp;y=2", "https://sephiria.wiki/builds/test?x=1&y=2")]
    [InlineData("<b>무기</b> <sprite name=dice> 2개 <link=weapon>선택</link>", "무기 2개 선택")]
    [InlineData("<#FFDD00>미해금</color>", "미해금")]
    public void NativeMarkupAndPlaceholdersNeverAppearInBubble(string input, string expected) =>
        Assert.Equal(expected, NotificationText.Plain(input));

    [Fact]
    public void NotificationQueueSanitizesBeforeDeduplicationAndIgnoresEmptyMarkup()
    {
        var queue = new NotificationQueue();
        queue.Enqueue("<color=red>미해금</color>", NotificationKind.Warning);
        queue.Enqueue("미해금", NotificationKind.Warning);
        queue.Enqueue("<indent=10></indent><color=red></color>", NotificationKind.Warning);
        Assert.Equal(1, queue.PendingCount);
        queue.Advance(0, true);
        Assert.Equal("미해금", queue.Current!.Text); Assert.Equal(NotificationKind.Warning, queue.Current.Kind);
        queue.Advance(7.9f, true); Assert.NotNull(queue.Current);
        queue.Advance(.2f, true); Assert.Null(queue.Current);
    }

    [Fact]
    public void LockedOptionsAreNamedDeduplicatedAndUnknownOrUnlockedOptionsDoNotWarn()
    {
        var warnings = new StartingPresetWarnings();
        warnings.Record("의상", "Frog", "<color=green>개구리</color>", true, false);
        warnings.Record("의상", "Frog", "개구리", true, false);
        warnings.Record("특성", "11", "기지", true, false);
        warnings.Record("무기", "unresolved", "알 수 없음", false, false);
        warnings.Record("아티팩트", "123", "이미 해금", true, true);
        Assert.Equal(new[] { "의상: 개구리", "특성: 기지" }, warnings.Items);
        Assert.Contains("미해금 옵션", warnings.Bubble); Assert.Contains("의상: 개구리", warnings.Bubble);
        Assert.Contains("해금된 옵션만 적용", warnings.Bubble);
        Assert.Null(new StartingPresetWarnings().Bubble);
    }

    [Fact]
    public void LargeLockedBuildMakesOneCompactWarningAndRetainsFullDetails()
    {
        var warnings = new StartingPresetWarnings();
        for (var i = 0; i < 20; i++) warnings.Record("아티팩트", i.ToString(), new string('가', 100), true, false);
        Assert.Equal(20, warnings.Items.Count); Assert.Contains("외 17개", warnings.Bubble);
        Assert.True(warnings.Bubble!.Length < 220);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void MenuCallbackInputIsBlockedOnlyWhileModalOwnsItOrDuringQuit(bool quitting, bool captured, bool expected) =>
        Assert.Equal(expected, NativeMenuInputPolicy.Allows(quitting, captured));

    [Fact]
    public void KeyboardAndMouseCallbacksAreBlockedButMovementReleaseAndOwnedPasteAreUntouched()
    {
        Assert.Contains("HandleOnOpenMapPanel", NativeMenuInputPolicy.Handlers);
        Assert.Contains("HandleOpenPresetPanel", NativeMenuInputPolicy.Handlers);
        Assert.Contains("HandleOnClick", NativeMenuInputPolicy.Handlers);
        Assert.DoesNotContain("HandleOnMove", NativeMenuInputPolicy.Handlers);
        Assert.DoesNotContain("HandleOnAim", NativeMenuInputPolicy.Handlers);
        var module = new object(); var textAction = new object(); var gameAction = new object();
        var owned = new OwnedUiInputScope(module, new[] { textAction });
        Assert.True(owned.AllowsAction(textAction, true)); Assert.False(owned.AllowsAction(gameAction, true));
        var modal = new ModalInputCapture(); modal.SetVisible(true); modal.SetVisible(false);
        modal.Tick(100, true, true); Assert.False(NativeMenuInputPolicy.Allows(false, modal.Capturing));
        modal.Tick(101, true, false); modal.Tick(103, true, false);
        Assert.True(NativeMenuInputPolicy.Allows(false, modal.Capturing));
    }

    [Theory]
    [InlineData(ScreenKind.WeaponUpgrade, ActionKind.Select, true, "선택 · F8")]
    [InlineData(ScreenKind.WeaponUpgrade, ActionKind.Reroll, true, "리롤 · F8")]
    [InlineData(ScreenKind.WeaponUpgrade, ActionKind.Select, false, "수동")]
    [InlineData(ScreenKind.ArtifactReward, ActionKind.Select, true, "F8")]
    public void WeaponOverlayDistinguishesSelectionRerollAndManualBinding(ScreenKind screen, ActionKind kind, bool allowed, string expected) =>
        Assert.Equal(expected, CandidateFramePolicy.ConfirmationLabel(screen, kind, "F8", allowed));
}
