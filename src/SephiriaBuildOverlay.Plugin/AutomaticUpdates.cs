using BepInEx;
using BepInEx.Configuration;
using SephiriaBuildOverlay.Core.Updates;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private ConfigEntry<bool> _automaticUpdates = null!;
    private Task<string?>? _updateTask;
    private AutoUpdateClient? _updateClient;
    private CancellationTokenSource? _updateCancellation;
    private void StartAutomaticUpdates()
    {
        _automaticUpdates = Config.Bind("Updates", "Enabled", true,
            "최신 정식 릴리즈를 다운로드하고 다음 실행 때 적용합니다. 설정/세이브/다른 모드는 변경하지 않습니다.");
        try
        {
            var store = new UpdateStore(Paths.BepInExRootPath);
            var receipt = store.TakeReceipt()?.Split('\n');
            if (receipt?.Length == 2)
            {
                if (receipt[0] == "applied" && receipt[1] == PluginVersion)
                    Notify("모드 " + receipt[1] + " 업데이트 완료", NotificationKind.Success);
                else if (receipt[0] == "restored")
                    Notify("업데이트를 완료하지 못해 이전 버전을 복구했습니다.", NotificationKind.Warning);
            }
            if (!_automaticUpdates.Value) return;
            var expectedPath = Path.Combine(store.PluginDirectory, "SephiriaBuildOverlay.Plugin.dll");
            var actualPath = typeof(SephiriaBuildOverlayPlugin).Assembly.Location;
            if (!string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
            { Logger.LogInfo("Auto update disabled for non-production/ScriptEngine DLL location."); return; }
            var patcher = Path.Combine(Paths.BepInExRootPath, "patchers", "SephiriaBuildOverlay.Updater.dll");
            if (!File.Exists(patcher)) { Logger.LogWarning("Auto update requires the paste-ready package's preloader patcher."); return; }
            _updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _updateClient = new AutoUpdateClient();
            var client = _updateClient; var token = _updateCancellation.Token;
            _updateTask = Task.Run(() => client.StageLatestAsync(PluginVersion, store, token), token);
        }
        catch (Exception ex) { Logger.LogWarning("Auto update unavailable: " + ex.Message); }
    }

    private void TickAutomaticUpdates()
    {
        if (!_automaticUpdates.Value) _updateCancellation?.Cancel();
        if (_updateTask == null || !_updateTask.IsCompleted) return;
        var finished = _updateTask; _updateTask = null;
        if (finished.IsFaulted)
            Logger.LogWarning("Auto update check/download failed; current version retained: " + finished.Exception!.GetBaseException().Message);
        else if (!finished.IsCanceled && finished.Result is string version)
        {
            Logger.LogInfo("Verified update " + version + " staged; applies on next game start.");
            Notify("새 버전 " + version + " · 다음 실행 때 적용됩니다", NotificationKind.Success);
        }
        DisposeAutomaticUpdateClient();
    }
    private void StopAutomaticUpdates()
    {
        _updateCancellation?.Cancel();
        var task = _updateTask; _updateTask = null;
        if (task != null && !task.IsCompleted)
        {
            var client = _updateClient; var cancellation = _updateCancellation;
            _updateClient = null; _updateCancellation = null;
            _ = task.ContinueWith(t => { _ = t.Exception; client?.Dispose(); cancellation?.Dispose(); }, TaskScheduler.Default);
        }
        else { _ = task?.Exception; DisposeAutomaticUpdateClient(); }
    }
    private void DisposeAutomaticUpdateClient()
    {
        _updateClient?.Dispose(); _updateClient = null;
        _updateCancellation?.Dispose(); _updateCancellation = null;
    }
}
