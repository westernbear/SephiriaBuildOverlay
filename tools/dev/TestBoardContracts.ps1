param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Assembly-CSharp.dll'))
try {
    foreach ($requirement in @(
        @('UI_TabletMixPanel', 'playerAvatar', 'UnitAvatar'),
        @('UI_TabletMixPanel', 'connectedTabletMix', 'TabletMix'),
        @('UI_TabletMixPanel', 'mixButton', 'UnityEngine.UI.Button'),
        @('UI_TabletMixPanel', 'itemIcon1', 'UI_ItemIcon'),
        @('UI_TabletMixPanel', 'itemIcon2', 'UI_ItemIcon'),
        @('TabletMix', 'mixCost', 'System.Int32'),
        @('ItemEntity', 'cannotThrow', 'System.Boolean'))) {
        $type = $game.MainModule.Types | Where-Object Name -EQ $requirement[0]
        $field = $type.Fields | Where-Object Name -EQ $requirement[1]
        if (!$field -or $field.FieldType.FullName -ne $requirement[2]) { throw "Board field mismatch: $($requirement -join ':')" }
    }
    $inventory = $game.MainModule.Types | Where-Object Name -EQ 'GridInventory'
    $eligibility = @($inventory.Methods | Where-Object { $_.Name -eq 'CanMixTablet' -and $_.Parameters.Count -eq 7 })
    if ($eligibility.Count -ne 1 -or $eligibility[0].ReturnType.FullName -ne 'System.Boolean') { throw 'Native synthesis eligibility contract changed.' }
    for ($i = 0; $i -lt 4; $i++) {
        if ($eligibility[0].Parameters[$i].ParameterType.FullName -ne 'System.Int32') { throw 'Material ID/rotation parameter changed.' }
    }
    for ($i = 5; $i -lt 7; $i++) {
        if ($eligibility[0].Parameters[$i].ParameterType.FullName -ne 'System.String&') { throw 'Native eligibility result parameter changed.' }
    }
    $calls = @($eligibility[0].Body.Instructions | Where-Object { $_.OpCode.Name -in @('call', 'callvirt') } | ForEach-Object { $_.Operand.Name })
    if (@($calls | Where-Object { $_ -eq 'GetRotatedQuery' }).Count -ne 2) { throw 'Rotated condition comparison changed.' }
    if ($calls | Where-Object { $_ -match '^(MixTablet|ServerMixTablet|CmdMixTablet|ForceRemoveItem|SubMoney|AddMoney)$' }) { throw 'Eligibility is no longer read-only.' }
    $swap = @($inventory.Methods | Where-Object { $_.Name -eq 'Swap' -and $_.Parameters.Count -eq 4 })
    if ($swap.Count -ne 1 -or @($swap[0].Parameters | Where-Object { $_.ParameterType.FullName -ne 'System.SByte' }).Count -ne 0) { throw 'Native swap request signature changed.' }
    $tablet = $game.MainModule.Types | Where-Object Name -EQ 'StoneTablet'
    foreach ($method in @('GetQuery', 'GetConditionQuery', 'ParseQuery', 'GetRotatedQuery')) {
        if (@($tablet.Methods | Where-Object Name -EQ $method).Count -ne 1) { throw "Native tablet query method changed: $method" }
    }
    $mixer = $game.MainModule.Types | Where-Object Name -EQ 'TabletMix'
    if (@($mixer.Properties | Where-Object { $_.Name -eq 'LocalUsed' -and $_.PropertyType.FullName -eq 'System.Boolean' }).Count -ne 1) { throw 'Mixer ownership/used-state contract changed.' }
    Write-Output 'PASS: synthesis material fields, read-only native eligibility, rotated conditions and native swap/query signatures. No game execution or state changes.'
} finally { $game.Dispose() }
