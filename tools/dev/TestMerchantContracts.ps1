param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Assembly-CSharp.dll'))
try {
    $unit = $game.MainModule.Types | Where-Object Name -EQ 'UnitAvatar'
    $stats = @($unit.Methods | Where-Object { $_.Name -eq 'GetCustomStat' -and $_.IsPublic -and $_.Parameters.Count -eq 1 })
    foreach ($parameter in @('ECustomStat','System.String')) {
        if (@($stats | Where-Object { $_.Parameters[0].ParameterType.FullName -eq $parameter }).Count -ne 1) { throw "Negotiation overload changed: $parameter" }
    }
    $price = ($game.MainModule.Types | Where-Object Name -EQ 'ItemDatabase').Methods | Where-Object Name -EQ 'GetItemBuyPrice'
    if ($price.Parameters.Count -ne 3 -or ($price.Parameters.ParameterType.FullName -join ',') -ne 'ItemEntity,System.Int32,System.Int32') { throw 'Native price signature changed.' }
    $shop = $game.MainModule.Types | Where-Object Name -EQ 'UI_ShopPanel'
    foreach ($pool in @('shopInventoryIconList','replenishmentIcons')) {
        if (!($shop.Fields | Where-Object Name -EQ $pool)) { throw "Native merchant pool missing: $pool" }
    }
    $npc = $game.MainModule.Types | Where-Object Name -EQ 'UnitAI_NewBasic'
    $stock = $npc.Fields | Where-Object Name -EQ 'replenishments'
    if ($stock.FieldType.FullName -ne 'System.Collections.Generic.List`1<UnitAI_NewBasic/ReplenishmentItem>') { throw 'Native replenishment collection changed.' }
    $itemType = $game.MainModule.Types | Where-Object Name -EQ 'EItemType'
    foreach ($kind in @(@('Charm',5),@('StoneTablet',6))) {
        $field = $itemType.Fields | Where-Object Name -EQ $kind[0]
        if (!$field -or $field.Constant -ne $kind[1]) { throw "Merchant item kind changed: $($kind[0])" }
    }
    $types = New-Object 'System.Collections.Generic.List[Mono.Cecil.TypeDefinition]'
    function Add-MerchantAuditTypes($entries) {
        foreach ($entry in $entries) { $types.Add($entry); Add-MerchantAuditTypes $entry.NestedTypes }
    }
    Add-MerchantAuditTypes $game.MainModule.Types
    $callers = @($types | ForEach-Object {
        foreach ($method in $_.Methods) {
            if ($method.HasBody -and @($method.Body.Instructions | Where-Object {
                $_.OpCode.Name -match '^call' -and $_.Operand -and $_.Operand.ToString().Contains('UI_ShopPanel::Open(')
            }).Count -gt 0) { $method.DeclaringType.FullName.Split('/')[0] }
        }
    } | Sort-Object -Unique)
    if (($callers -join ',') -ne 'FunctionNode_Trade,Safe') { throw "Native trade entry points changed: $($callers -join ',')" }
    $vendors = ($game.MainModule.Types | Where-Object Name -EQ 'EProceduralMerchantType').Fields | Where-Object { $_.HasConstant -and $_.Constant -gt 0 }
    [PSCustomObject]@{ passed=$true; gameActionsExecuted=$false; tradeEntryPoints=$callers; proceduralMerchantKinds=@($vendors.Name);
        itemShop='UI_ShopPanel'; riftShop='PocketDimensionShopArm'; negotiation='UnitAvatar.GetCustomStat(ECustomStat)';
        stockCollection='List<ReplenishmentItem>'; currencies='money / voucher / persistent sapphire'; nonItemServices='InventoryShop / ExpShop / TreeShopItemStorage are not artifact offers' } | ConvertTo-Json -Depth 3
} finally { $game.Dispose() }
