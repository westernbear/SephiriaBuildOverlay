param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Assembly-CSharp.dll'))
try {
    $controller = $game.MainModule.Types | Where-Object Name -EQ 'PlayerInputController'
    $source = Get-Content (Join-Path $PSScriptRoot '../../src/SephiriaBuildOverlay.Plugin/NativeMenuInputPolicy.cs') -Raw -Encoding UTF8
    $handlers = [regex]::Matches($source, '"(Handle\w+)"') | ForEach-Object { $_.Groups[1].Value }
    if ($handlers.Count -ne 11) { throw 'Recheck menu callback contract count.' }
    foreach ($name in $handlers) {
        $method = @($controller.Methods | Where-Object Name -EQ $name)
        if ($method.Count -ne 1 -or $method[0].Parameters.Count -ne 1 -or !$method[0].HasBody -or
            $method[0].Parameters[0].ParameterType.FullName -ne 'UnityEngine.InputSystem.InputAction/CallbackContext') { throw "Input callback contract mismatch: $name" }
    }
    foreach ($requirement in @(@('UI_WeaponEnhancementButton','enhancementMetadata','EnhancementMetadata'),
        @('EnhancementMetadata','enhanced','WeaponEntity'), @('UI_WeaponEnhancementButton','button','UnityEngine.UI.Button'),
        @('WeaponControllerSimple','currentWeapon','WeaponSimple'))) {
        $type = $game.MainModule.Types | Where-Object Name -EQ $requirement[0]
        $field = $type.Fields | Where-Object Name -EQ $requirement[1]
        if (!$field -or $field.FieldType.FullName -ne $requirement[2]) { throw "Weapon field contract mismatch: $($requirement -join ':')" }
    }
    $button = $game.MainModule.Types | Where-Object Name -EQ 'UI_WeaponEnhancementButton'
    $setup = $button.Methods | Where-Object Name -EQ 'SetWeaponMethod'
    if (!($setup.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'enhancementMetadata' })) { throw 'Weapon candidate metadata no longer initialized.' }
    if ($setup.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'weapon' }) { throw 'Recheck old weapon candidate field: native initialization changed.' }
    $preset = $game.MainModule.Types | Where-Object Name -EQ 'UI_PresetPanel'
    foreach ($name in @('IsCharmDiscovered', 'ValidateAndCorrectCostume')) {
        if (@($preset.Methods | Where-Object Name -EQ $name).Count -ne 1) { throw "Unlock read contract mismatch: $name" }
    }
    foreach ($requirement in @(@('UIBase','IsOpened','System.Boolean'), @('UI_NewItemPicker','CurrentAny','System.Boolean'),
        @('UI_NewItemPicker_Controller','CurrentAny','System.Boolean'), @('UI_ReplenishmentIcon','Shop','UnitAI_NewBasic'),
        @('UI_ReplenishmentIcon','Entity','ItemEntity'), @('UI_ReplenishmentIcon','ReplenishmentIdx','System.Int32'),
        @('UI_CharacterStatusPanel','PlayerAvatar','PlayerAvatar'), @('Charm_Basic','DisplayedLevel','System.Int32'),
        @('GameCamera','Camera','UnityEngine.Camera'))) {
        $type = $game.MainModule.Types | Where-Object Name -EQ $requirement[0]
        $property = $type.Properties | Where-Object Name -EQ $requirement[1]
        if (!$property -or $property.PropertyType.FullName -ne $requirement[2]) { throw "Property contract mismatch: $($requirement -join ':')" }
        if ($requirement[1] -eq 'CurrentAny' -and $property.GetMethod.IsStatic) { throw 'Recheck instance picker gate.' }
    }
    foreach ($requirement in @(@('UI_ShopPanel','replenishmentButton','UnityEngine.GameObject'),
        @('UI_ShopPanel','canSell','System.Boolean'), @('UI_TabletMixPanel','slotElementZone','UnityEngine.RectTransform'),
        @('UI_SephiriteRewardPanel','convertRerollDiceButtonGroup','UnityEngine.CanvasGroup'),
        @('PocketDimensionShopArm','itemRenderer','UnityEngine.SpriteRenderer'), @('PocketDimensionShopArm','costType','PocketDimensionCostType'),
        @('StoneTablet','isRotatable','System.Boolean'), @('SephiriteRewardMetadata','instanceID','System.Int32'),
        @('SephiriteRewardMetadata','entityID','System.Int32'))) {
        $type = $game.MainModule.Types | Where-Object Name -EQ $requirement[0]
        $field = $type.Fields | Where-Object Name -EQ $requirement[1]
        if (!$field -or $field.FieldType.FullName -ne $requirement[2]) { throw "Stock/reward field contract mismatch: $($requirement -join ':')" }
    }
    $shop = $game.MainModule.Types | Where-Object Name -EQ 'UI_ShopPanel'
    $replenish = $shop.Methods | Where-Object Name -EQ 'DoReplenishment'
    if (!($replenish.Body.Instructions | Where-Object { $_.Operand.Name -eq 'Pow' }) -or
        !($replenish.Body.Instructions | Where-Object { $_.Operand.Name -eq 'GetSapphire' })) { throw 'Recheck native persistent replenishment currency/cost.' }
    $arm = $game.MainModule.Types | Where-Object Name -EQ 'PocketDimensionShopArm'
    if (@($arm.Methods | Where-Object Name -EQ 'GetPrice').Count -ne 1) { throw 'Rift read-only price contract changed.' }
    $convert = ($game.MainModule.Types | Where-Object Name -EQ 'UI_SephiriteRewardPanel').Methods | Where-Object Name -EQ 'ConvertRerollDice'
    if (!($convert.Body.Instructions | Where-Object { $_.Operand.Name -eq 'OpenYesNo' })) { throw 'Dice conversion confirmation contract changed.' }
    $rotatable = ($game.MainModule.Types | Where-Object Name -EQ 'DungeonManager').Methods | Where-Object {
        $_.Name -eq 'IsTabletRotatable' -and $_.Parameters.Count -eq 2 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Int32' -and $_.Parameters[1].ParameterType.FullName -eq 'System.Boolean'
    }
    if (@($rotatable).Count -ne 1) { throw 'Reward-instance rotation permission contract changed.' }
    Write-Output 'PASS: menu/weapon/unlock, authoritative panel state, boolean picker gates, merchant stock, sapphire replenishment/rift, tablet reward/rotation, enchant level and native conversion confirmation. Read-only; no game input or mutation.'
} finally { $game.Dispose() }
