param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
# Metadata/IL only. Never loads executable game code or changes saves.
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Assembly-CSharp.dll'))
$mirror = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Mirror.dll'))
$steam = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Heathen.Steamworks.dll'))
try {
    foreach ($entry in @(@('UI_SephiriteRewardPanel','openedAvatar','PlayerAvatar'), @('UI_WeaponEnhancementPanel','player','PlayerAvatar'),
        @('UI_MiraclePanel','playerAvatar','PlayerAvatar'), @('UI_TabletMixPanel','playerAvatar','UnitAvatar'),
        @('PlayerLocalDataStorage','avatar','PlayerAvatar'), @('PlayerAvatar','currentCostume','System.String'),
        @('PlayerAvatar','currentCostumeSkin','System.String'), @('Sephirite','isAcquired','System.Boolean'))) {
        $field = ($game.MainModule.Types | Where-Object Name -EQ $entry[0]).Fields | Where-Object Name -EQ $entry[1]
        if (!$field -or $field.FieldType.FullName -ne $entry[2]) { throw "Local owner/result contract changed: $($entry -join ':')" }
    }
    $grid = $game.MainModule.Types | Where-Object Name -EQ 'GridInventory'
    foreach ($route in @(@('UserCode_RpcNotifyAddItem__NewItemOwnInstance','NewItemOwnInstance','OnItemAddedForClient'),
        @('UserCode_RpcUniquePairEnchanted__ItemPosition','ItemPosition','OnUniquePairEnchantedClientside'))) {
        $methods = @($grid.Methods | Where-Object { $_.Name -eq $route[0] -and ($_.Parameters.ParameterType.FullName -join ',') -eq $route[1] })
        if ($methods.Count -ne 1 -or !($methods[0].Body.Instructions.Operand | Where-Object { $_ -and $_.ToString().Contains($route[2]) })) {
            throw "Native client acquisition notification changed: $($route[0])"
        }
    }
    $merge = $grid.NestedTypes | Where-Object Name -EQ 'UniquePairArtifactConvertData'
    foreach ($name in @('instanceID','entityID','enchantedInstanceID')) {
        if (!( $merge.Fields | Where-Object { $_.Name -eq $name -and $_.FieldType.FullName -eq 'System.Int32' })) { throw "Server merge source missing: $name" }
    }
    foreach ($name in @('LocalAddItem','LocalAddItemAtPosition')) {
        $body = ($grid.Methods | Where-Object Name -EQ $name).Body.Instructions | ForEach-Object ToString
        foreach ($member in @('Mirror.NetworkServer::get_active','uniquePairArtifactConvertDataServerside','RpcUniquePairEnchanted')) {
            if (!($body | Select-String -SimpleMatch $member)) { throw "Server acquisition/merge route changed: $name / $member" }
        }
    }
    $storage = $game.MainModule.Types | Where-Object Name -EQ 'PlayerLocalDataStorage'
    foreach ($name in @('UpdateDimensionPocket','UpdateFruitSkewerBonus')) {
        $body = ($storage.Methods | Where-Object Name -EQ $name).Body.Instructions | ForEach-Object ToString
        if (!($body | Select-String -SimpleMatch 'Mirror.SyncList')) { throw "Native owner storage route changed: $name" }
        if ($body -match '::(Cmd|Target|Rpc)') { throw "Recheck newly available storage acknowledgement: $name" }
    }
    $client = $mirror.MainModule.Types | Where-Object FullName -EQ 'Mirror.NetworkClient'
    $connection = $client.Properties | Where-Object Name -EQ 'connection'
    if (!$connection -or $connection.PropertyType.FullName -ne 'Mirror.NetworkConnectionToServer' -or
        !$connection.GetMethod.IsPublic -or !$connection.GetMethod.IsStatic) {
        throw 'Native connection identity property changed. Session observation must read the static property.'
    }
    foreach ($name in @('Disconnect','OnTransportDisconnected','Shutdown')) {
        if (!(($mirror.MainModule.Types | Where-Object FullName -EQ 'Mirror.NetworkClient').Methods | Where-Object Name -EQ $name)) { throw "Network boundary missing: $name" }
    }
    foreach ($route in @(@('SteamInvitation','HandleLeave'), @('EOSLobbyManager','ClearLobbyState'))) {
        $type = $game.MainModule.Types | Where-Object Name -EQ $route[0]
        if (!($type.Fields | Where-Object { $_.Name -eq 'instance' -and $_.IsStatic -and $_.FieldType.FullName -eq $route[0] })) {
            throw "Native lobby singleton changed: $($route[0])"
        }
        if (!($type.Methods | Where-Object { $_.Name -eq $route[1] -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Void' })) {
            throw "Native lobby leave boundary changed: $($route -join '.')"
        }
    }
    $invitation = $game.MainModule.Types | Where-Object Name -EQ 'SteamInvitation'
    if (!($invitation.Fields | Where-Object { $_.Name -eq 'lobbyManager' -and $_.FieldType.FullName -eq 'HeathenEngineering.SteamworksIntegration.LobbyManager' })) {
        throw 'Steam lobby manager binding changed.'
    }
    $lobbyManager = $steam.MainModule.Types | Where-Object FullName -EQ 'HeathenEngineering.SteamworksIntegration.LobbyManager'
    $lobbyData = $steam.MainModule.Types | Where-Object FullName -EQ 'HeathenEngineering.SteamworksIntegration.LobbyData'
    if (!($lobbyManager.Properties | Where-Object { $_.Name -eq 'Lobby' -and $_.GetMethod.IsPublic -and $_.PropertyType.FullName -eq $lobbyData.FullName }) -or
        !($lobbyData.Fields | Where-Object { $_.Name -eq 'id' -and $_.FieldType.FullName -eq 'System.UInt64' })) {
        throw 'Cached Steam lobby identity changed.'
    }
    $eos = $game.MainModule.Types | Where-Object Name -EQ 'EOSLobbyManager'
    if (!($eos.Properties | Where-Object { $_.Name -eq 'CurrentLobbyId' -and $_.GetMethod.IsPublic -and $_.PropertyType.FullName -eq 'System.String' })) {
        throw 'Cached EOS lobby identity changed.'
    }
    foreach ($requirement in @(@('Mirror.NetworkIdentity','get_isOwned'), @('Mirror.NetworkBehaviour','get_isOwned'), @('Mirror.NetworkBehaviour','get_isServer'))) {
        if (!(($mirror.MainModule.Types | Where-Object FullName -EQ $requirement[0]).Methods | Where-Object Name -EQ $requirement[1])) { throw "Ownership API missing: $($requirement -join '.')" }
    }
    Write-Output 'PASS: local screen owners, host server source/merge provenance, native client add/merge notifications, costume/conversion results, unacknowledged owner loadout lists, static connection property, cached Steam/EOS lobby IDs and native leave/disconnect boundaries. Read-only; remote multiplayer was not exercised.'
} finally { $game.Dispose(); $mirror.Dispose(); $steam.Dispose() }
