param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$native = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Sephiria_Data\Managed\Assembly-CSharp.dll'))
try {
    foreach ($r in @(
        @('Charm_UpCharmDamage', 'damageBonusByLevel', 'System.Int32[]'),
        @('Charm_UpCharmDamage', 'dependencyDamageBonusByLevel', 'System.Int32[]'),
        @('Charm_UpCharmDamage', 'hasDependencyCondition', 'System.Boolean'),
        @('Charm_UpCharmDamage', 'maxRarity', 'EItemRarity'),
        @('Charm_UpCharmDamage', 'xOffset', 'System.SByte'),
        @('Charm_UpCharmDamage', 'yOffset', 'System.SByte'),
        @('Charm_ReduceMPCost', 'reducePercentByLevel', 'System.Int32[]'),
        @('Charm_RightSpellCooldownHelper', 'cooldownRecoveryByLevel', 'System.Int32[]'),
        @('Charm_NearLevelDamage', 'allDamageBonusByLevel', 'System.Single[]'),
        @('Charm_NearLevelDamage', 'directions', 'ItemPosition[]'),
        @('Charm_3Elemental_ByRow', 'lineCategory', 'System.String[]'),
        @('Charm_WhitePaper', 'match', 'System.Int32'))) {
        $t = $native.MainModule.Types | Where-Object Name -eq $r[0]
        $f = $t.Fields | Where-Object Name -eq $r[1]
        if (!$f -or $f.FieldType.FullName -ne $r[2]) { throw "Special artifact field changed: $($r -join ':')" }
    }
    foreach ($name in @('OnPreSetEffectRefreshed', 'GetItemCategory')) {
        $owners = @($native.MainModule.Types | Where-Object { $_.Name -like 'Charm_*' -and $_.Methods.Name -contains $name } | Select-Object -ExpandProperty Name | Sort-Object)
        $expected = @('Charm_3Elemental_ByRow', 'Charm_Basic', 'Charm_UpCharmDamage', 'Charm_WhitePaper')
        if (@(Compare-Object $owners $expected).Count -ne 0) { throw "Category callback owners changed: $name" }
    }
    $needle = $native.MainModule.Types | Where-Object Name -eq Charm_UpCharmDamage
    $search = $needle.Methods | Where-Object Name -eq SearchCategory
    if ($search.Body.Instructions.Operand.Name -contains 'IsEffectEnabled' -or $search.Body.Instructions.Operand.Name -notcontains 'IsDependencyValid') {
        throw 'Needle category traversal/disabled-intermediate rule changed.'
    }
    foreach ($r in @(@('Charm_ReduceMPCost', 'sub'), @('Charm_RightSpellCooldownHelper', 'add'))) {
        $m = ($native.MainModule.Types | Where-Object Name -eq $r[0]).Methods | Where-Object Name -eq SearchMagic
        $find = @($m.Body.Instructions | Where-Object { $_.Operand.Name -eq 'FindItem' })
        if ($find.Count -ne 1 -or @($m.Body.Instructions | Where-Object { $_.Offset -lt $find[0].Offset -and $_.OpCode.Name -eq $r[1] }).Count -ne 1 -or
            $m.Body.Instructions.Operand.Name -notcontains 'Charm_Magic') { throw "Directed magic support rule changed: $($r[0])" }
    }
    foreach ($r in @(@('Charm_FireIce', 'AddStat'), @('Charm_FireIceWeapon', 'CheckPosition'))) {
        $m = ($native.MainModule.Types | Where-Object Name -eq $r[0]).Methods | Where-Object Name -eq $r[1]
        $x = @($m.Body.Instructions | Where-Object { $_.Operand.Name -eq 'XIdx' })[0]
        if (!$x -or $x.Next.OpCode.Name -ne 'ldc.i4.2' -or $x.Next.Next.OpCode.Name -notlike 'bgt*') { throw 'Native left/right boundary changed.' }
    }
    $m = ($native.MainModule.Types | Where-Object Name -eq Charm_NearLevelDamage).Methods | Where-Object Name -eq UpdateDamageBonus
    foreach ($call in @('get_DisplayedLevel', 'Min', 'FloorToInt')) { if ($m.Body.Instructions.Operand.Name -notcontains $call) { throw "Neighbor displayed-level arithmetic changed: $call" } }
    $m = ($native.MainModule.Types | Where-Object Name -eq Charm_PlanetModule).Methods | Where-Object Name -eq SearchPlanet
    if ($m.Body.Instructions.Operand.Name -notcontains 'Charm_SummonGreenBat' -or $m.Body.Instructions.Operand -notcontains 'PLANET') { throw 'Actual summoned-planet eligibility changed.' }
    $m = ($native.MainModule.Types | Where-Object Name -eq Charm_CompanionChaos).Methods | Where-Object Name -eq SearchCompanion
    if ($m.Body.Instructions.Operand.Name -notcontains 'ICompanionCharm' -or $m.Body.Instructions.Operand.Name -notcontains 'Width') { throw 'Full-row companion rule changed.' }
    $m = ($native.MainModule.Types | Where-Object Name -eq Charm_3Elemental_ByRow).Methods | Where-Object Name -eq SearchCategory
    if ($m.Body.Instructions.OpCode.Name -notcontains 'rem' -or $m.Body.Instructions.Operand.Name -notcontains 'YIdx') { throw 'Periodic row category rule changed.' }
    $database = $native.MainModule.Types | Where-Object Name -eq ItemDatabase
    if (!($database.Fields | Where-Object { $_.Name -eq 'itemCategories' -and $_.IsStatic })) { throw 'Native category table is not the expected static field.' }
    Write-Output 'PASS: special artifact fields/callbacks, directed chains and helpers, X<=2 side, neighbor arithmetic, planet/companion and row rules. Assembly inspected read-only; no game actions.'
} finally { $native.Dispose() }
