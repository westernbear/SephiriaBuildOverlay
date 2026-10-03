param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
# Read-only IL inspection. Never load/execute game code or start a game process.
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$managed = Join-Path $GameDir 'Sephiria_Data\Managed'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
$mirror = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Mirror.dll'))
try {
    $requirements = @(
        @('UI_PresetPanel','BuildCompactPresetData',1), @('UI_PresetPanel','TryApplyCompactPresetData',3),
        @('UI_PresetPanel','UpdateCurrentPlayer',2), @('SaveManager','Save',2),
        @('UI_PresetPanel','ValidateAndCorrectCostume',2), @('CostumeDatabase','GetCostumeSkinByID',1),
        @('PassiveDatabase','GetAll',0), @('SwitchManager','GetDestinySwitch',2),
        @('ItemDatabase','GetAllItemCategory',0), @('ItemDatabase','FindItemById',1),
        @('UI_DimensionPocketPanel','GetCapacity',1), @('KeywordDatabase','GetConstValue',2),
        @('PlayerAvatar','GetPassiveStat',1), @('UnitAvatar','GetCustomStatUnsafe',1), @('UI_Cursor','get_Current',0)
    )
    foreach ($requirement in $requirements) {
        $type = $game.MainModule.Types | Where-Object Name -EQ $requirement[0]
        $methods = @($type.Methods | Where-Object { $_.Name -eq $requirement[1] -and $_.Parameters.Count -eq $requirement[2] })
        if ($methods.Count -ne 1) { throw "Native API mismatch: $($requirement -join ':')" }
    }
    $panel = $game.MainModule.Types | Where-Object Name -EQ 'UI_PresetPanel'
    foreach ($field in @('playerAvatar','playerSpawner','playerLocalDataStorage','isEditingCurrentPreset','baseWeaponDatas')) {
        if (!($panel.Fields | Where-Object Name -EQ $field)) { throw "Native preset field missing: $field" }
    }
    foreach ($requirement in @(@('DungeonManager','isRunStarted','System.Boolean'), @('PassiveEntity','id','System.UInt64'),
        @('PassiveEntity','aName','LocalizedString'), @('CostumeEntity','aName','LocalizedString'))) {
        $type = $game.MainModule.Types | Where-Object Name -EQ $requirement[0]
        $field = $type.Fields | Where-Object Name -EQ $requirement[1]
        if (!$field -or $field.FieldType.FullName -ne $requirement[2]) { throw "Native starting metadata mismatch: $($requirement -join ':')" }
    }
    $nativeImport = $panel.Methods | Where-Object Name -EQ 'TryApplyCompactPresetData'
    $costumeValidator = $panel.Methods | Where-Object Name -EQ 'ValidateAndCorrectCostume'
    $calls = @(@($nativeImport.Body.Instructions) + @($costumeValidator.Body.Instructions) | Where-Object { $_.OpCode.Name -match '^call' } | ForEach-Object { $_.Operand.FullName })
    if ($calls -match '::(Purchase|Buy|Spend|AddPassivePoint|CmdAddPassivePoint|SetSapphire)') { throw 'Import path includes a permanent purchase/spend method.' }
    foreach ($expected in @('CostumeDatabase::IsUnlocked','SwitchManager::GetDestinySwitch','UI_PresetPanel::IsCharmDiscovered')) {
        if (!($calls | Where-Object { $_.Contains($expected) })) { throw "Unlock validation absent: $expected" }
    }
    $skinType = $game.MainModule.Types | Where-Object Name -EQ 'CostumeSkinEntity'
    $skinEnum = $skinType.NestedTypes | Where-Object Name -EQ 'ECostumeUnlockType'
    foreach ($entry in @(@('Default',0), @('Purchase',1), @('Locked',2))) {
        $field = $skinEnum.Fields | Where-Object Name -EQ $entry[0]
        if (!$field -or $field.Constant -ne $entry[1]) { throw 'Native skin ownership metadata changed.' }
    }
    $equipSkin = ($game.MainModule.Types | Where-Object Name -EQ 'UI_CostumePanel').Methods | Where-Object Name -EQ 'EquipSkin'
    $skinGate = @($equipSkin.Body.Instructions | Where-Object { $_.Offset -le 0x0054 } | ForEach-Object ToString)
    foreach ($expected in @('SkinPurchased_', 'SaveData::GetBool', 'CostumeSkinEntity::unlockType', 'ceq', 'or')) {
        if (!($skinGate | Where-Object { $_.Contains($expected) })) { throw "Native skin Default-or-purchased contract changed: $expected" }
    }
    $discovery = $panel.Methods | Where-Object Name -EQ 'IsCharmDiscovered'
    $discoveryIl = @($discovery.Body.Instructions | ForEach-Object ToString)
    foreach ($expected in @('WitchHat_Item_{0}_Found', 'SaveData::GetBool', 'PlayerSpawner::unlockedCharms')) {
        if (!($discoveryIl | Where-Object { $_.Contains($expected) })) { throw "Native journal discovery contract changed: $expected" }
    }
    $server = $mirror.MainModule.Types | Where-Object FullName -EQ 'Mirror.NetworkServer'
    $connections = $server.Fields | Where-Object Name -EQ 'connections'
    if (!$connections.IsPublic -or !$connections.IsStatic) { throw 'Native connection count binding differs.' }
    $local = $mirror.MainModule.Types | Where-Object FullName -EQ 'Mirror.LocalConnectionToServer'
    $send = $local.Methods | Where-Object Name -EQ 'Send'
    if (!($send.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match '::Enqueue' })) { throw 'Recheck local host command timing; queue contract changed.' }
    Write-Output 'PASS: 15 native method signatures, preset fields, Default-or-purchased skin ownership, distinct WitchHat discovery/unlockedCharms gates, local connection count and deferred command contract. Game code was not executed.'
} finally { $game.Dispose(); $mirror.Dispose() }
