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
    Write-Output 'PASS: 11 menu callbacks, native weapon candidate/current-weapon fields and read-only unlock checks. No game input or mutation.'
} finally { $game.Dispose() }
