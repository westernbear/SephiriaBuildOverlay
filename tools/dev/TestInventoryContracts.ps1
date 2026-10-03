param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Assembly-CSharp.dll'))
try {
    $inventory = $game.MainModule.Types | Where-Object Name -EQ 'GridInventory'
    foreach ($requirement in @(
        @('CanAddItem', 'ItemEntity,System.Int32', 'ItemAdditionCheckResult'),
        @('GetEmptySlotCount', 'EItemType', 'System.Int32'),
        @('HasItem', 'ItemEntity,System.SByte&,System.SByte&,System.SByte&', 'System.Boolean'),
        @('FindItem', 'System.SByte,System.SByte', 'NewItemOwnInstance'))) {
        $matches = @($inventory.Methods | Where-Object {
            $_.Name -eq $requirement[0] -and $_.IsPublic -and
            ($_.Parameters.ParameterType.FullName -join ',') -eq $requirement[1] -and $_.ReturnType.FullName -eq $requirement[2]
        })
        if ($matches.Count -ne 1) { throw "Inventory query signature changed: $($requirement -join ':')" }
        $instructions = $matches[0].Body.Instructions
        if ($instructions | Where-Object { $_.OpCode.Name -in @('stfld','stsfld') -or
            ($_.OpCode.Name -match '^call' -and $_.Operand.Name -match '^(set_|Server|Cmd|Rpc|AddItem|RemoveItem|SubMoney|GiveMoney|Enchant|Swap)') }) {
            throw "Inventory query is no longer read-only: $($requirement[0])"
        }
    }
    $result = $game.MainModule.Types | Where-Object Name -EQ 'ItemAdditionCheckResult'
    foreach ($expected in @(@('Success',0),@('Success_Stack',1),@('Full',2),@('LimitQuantity',3),@('HasSameUnique',4),
        @('TypeMatchFail',5),@('UniquePairLevelOver',6),@('HasConnectedUniqueItem',7),@('HasSameUniqueInSubBag',8),@('ItemNotFound',9),@('PermissionDenied',10))) {
        $field = $result.Fields | Where-Object Name -EQ $expected[0]
        if (!$field -or $field.Constant -ne $expected[1]) { throw "Inventory result changed: $($expected[0])" }
    }
    foreach ($route in @(@('UI_SephiriteRewardPanel','AcquireSephiriteRewardByUIElement'),@('UI_ShopPanel','BuyReplenishmentItem'))) {
        $method = ($game.MainModule.Types | Where-Object Name -EQ $route[0]).Methods | Where-Object Name -EQ $route[1]
        $body = $method.Body.Instructions | ForEach-Object ToString
        foreach ($member in @('GridInventory::uniquePairCount','GridInventory::HasItem(','Charm_Basic::maxLevel','DungeonManager::GetGlobalItemStatValue(')) {
            if (!($body | Select-String -SimpleMatch $member)) { throw "Native merge path changed: $($route -join '.') / $member" }
        }
    }
    $rift = ($game.MainModule.Types | Where-Object Name -EQ 'PocketDimensionShopArm').Methods | Where-Object Name -EQ 'HandleInteraction'
    if (!($rift.Body.Instructions.Operand | Where-Object { $_ -and $_.ToString().Contains('GetEmptySlotCount(EItemType)') })) {
        throw 'Rift strict empty-slot admission changed.'
    }
    Write-Output 'PASS: read-only inventory admission signatures/results, reward/replenishment Wisdom merge and rift empty-slot checks. No game actions or state changes.'
} finally { $game.Dispose() }
