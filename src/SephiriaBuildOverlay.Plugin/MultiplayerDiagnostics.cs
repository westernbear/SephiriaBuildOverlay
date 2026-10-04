using System.Collections;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private readonly Queue<object> _requestHistory = new();

    private void TraceRequest(string status, RecommendedAction? action = null, RunSnapshot? snapshot = null)
    {
        snapshot ??= _lastSnapshot;
        _requestHistory.Enqueue(new { utc = DateTime.UtcNow, requestId = _confirmationRequestId, status,
            action = action?.Kind.ToString(), target = action?.TargetToken, sourceInstance = snapshot?.Candidates.FirstOrDefault(x => x.Token == action?.TargetToken)?.SourceInstanceId,
            session = snapshot?.Network.SessionId, role = snapshot?.Network.Role.ToString(), player = snapshot?.LocalPlayerId,
            run = snapshot?.RunId, revision = snapshot?.Revision });
        while (_requestHistory.Count > 64) _requestHistory.Dequeue();
        _log.LogInfo($"Native request observation: request={_confirmationRequestId}, status={status}, session={snapshot?.Network.SessionId}, player={snapshot?.LocalPlayerId}");
    }

    internal object ReadMultiplayerDiagnostics()
    {
        FindLocalPlayerId(out var owned, out var player);
        var avatarType = GameType("PlayerAvatar");
        var avatar = player == null || avatarType is null ? null : player.GetComponent(avatarType);
        var panelType = GameType("UI_PresetPanel");
        var panels = panelType is null ? Array.Empty<Component>() : Resources.FindObjectsOfTypeAll(panelType).OfType<Component>()
            .Where(x => x != null && x.gameObject.scene.IsValid() && ReferenceEquals(ReadNamedObject(x, "playerAvatar"), avatar)).ToArray();
        var storage = panels.Length == 1 ? ReadNamedObject(panels[0], "playerLocalDataStorage") : null;
        return new { network = _network, owned, requestPending = _requestPending, requests = _requestHistory.ToArray(),
            screens = _panels.Where(x => x.Component != null).Select(x => new { type = x.Component.GetType().Name,
                visible = x.Component.gameObject.activeInHierarchy && ReadBool(x.Component, "IsOpened"), localOwner = IsLocalPanel(x.Component) }).ToArray(),
            preset = new { context = StartingPresetContext(), ready = StartingPresetReady(), localPanels = panels.Length,
                storageServer = storage is not null && ReadBool(storage, "isServer"),
                storageOwner = storage is not null && ReadBool(storage, "isOwned"),
                syncDirection = storage is null ? null : ReadNamedString(storage, "syncDirection"),
                currentCostume = avatar == null ? null : ReadNamedString(avatar, "currentCostume"),
                currentSkin = avatar == null ? null : ReadNamedString(avatar, "currentCostumeSkin"),
                pocket = (storage is null ? null : ReadNamedObject(storage, "dimensionPocketItem") as IEnumerable)?.Cast<object>().ToArray() },
            gameActionsExecutedByDiagnostics = false };
    }
}
