using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private UnityGameGateway.NativeBuildWindow? _nativeBuildWindow;

    private void DrawNativeBuildWindow()
    {
        if (!_showImport || _advancedReview) { _nativeBuildWindow?.Hide(); return; }
        _nativeBuildWindow?.HandleMouseResizeEvent(Event.current);
        if (Event.current.type != EventType.Repaint) return;
        try
        {
            _nativeBuildWindow ??= _gateway.CreateBuildWindow(text => _locatorText = text,
                text => { _locatorText = text; _ = ImportAsync(); }, ToggleAdvancedReview, () => _showImport = false,
                _panelScale.Value, scale => { _panelScale.Value = scale; Config.Save(); });
            if (_nativeBuildWindow is null) throw new InvalidOperationException("게임 메뉴 리소스가 아직 없습니다.");
            _nativeBuildWindow.Show(_locatorText, _controller.GamepadMode);
            _nativeBuildWindow.Render(_controller.GamepadMode, _importing || _executing || ControllerReviewPreview, _plan is null ? null : _review?.Build.Title,
                Mathf.Clamp(_uiScale.Value, .75f, 1.75f), _controllerMenu);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Native build window unavailable: " + ex);
            _showImport = false;
            _nativeBuildWindow?.Dispose(); _nativeBuildWindow = null;
            Notify("게임 UI가 준비되지 않았습니다. 잠시 후 다시 열어주세요.", NotificationKind.Warning);
        }
    }
}
