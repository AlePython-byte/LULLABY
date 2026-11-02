<#
.SYNOPSIS
  Resume los CSV de partida que escribe el juego (PlaytestLog) y los agrupa por dificultad.

.DESCRIPTION
  Busca recursivamente los archivos partida_*.csv de la carpeta indicada y, por cada dificultad, imprime:
    - partidas, victorias, muertes y abandonos;
    - tasa de victoria (victorias / (victorias + muertes): los abandonos no cuentan como derrota);
    - porcentaje de partidas que llegaron al jefe y vida media con la que llegaron;
    - duracion media de la pelea contra el jefe;
    - causa de muerte mas comun;
    - dano medio por partida y por causa.
  Un archivo sin fila FIN (el juego se cerro de golpe) se cuenta aparte y no entra en el resumen.

.PARAMETER Carpeta
  Carpeta con los CSV (puede tener una subcarpeta por persona). Por defecto, la carpeta actual.

.PARAMETER PorJugador
  Ademas del resumen por dificultad, repite la tabla por persona: el nombre es la primera subcarpeta dentro de Carpeta.

.PARAMETER Salida
  Si se da, guarda el resumen por dificultad en este archivo CSV.

.EXAMPLE
  .\tools\Resumir-Partidas.ps1 -Carpeta C:\partidas -PorJugador
#>
param(
    [string]$Carpeta = '.',
    [switch]$PorJugador,
    [string]$Salida = ''
)

$ErrorActionPreference = 'Stop'
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$root = (Resolve-Path $Carpeta).Path.TrimEnd('\')

function ConvertTo-Number {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    return [double]::Parse($Text, $invariant)
}

function Get-Mean {
    param($Values)
    $numbers = @($Values | Where-Object { $null -ne $_ })
    if ($numbers.Count -eq 0) { return $null }
    return ($numbers | Measure-Object -Average).Average
}

function Format-Value {
    param($Value, [string]$Format = '0.0')
    if ($null -eq $Value) { return '-' }
    return ([double]$Value).ToString($Format, $invariant)
}

function Write-Table {
    param($Rows)
    $rows = @($Rows)
    if ($rows.Count -eq 0) { return }
    $columns = @($rows[0].PSObject.Properties.Name)
    $widths = @{}
    foreach ($column in $columns) {
        $widths[$column] = $column.Length
        foreach ($row in $rows) { $widths[$column] = [Math]::Max($widths[$column], ([string]$row.$column).Length) }
    }
    $header = ($columns | ForEach-Object { $_.PadRight($widths[$_]) }) -join '  '
    Write-Host $header
    Write-Host (($columns | ForEach-Object { ('-' * $widths[$_]) }) -join '  ')
    foreach ($row in $rows) { Write-Host (($columns | ForEach-Object { ([string]$row.$_).PadRight($widths[$_]) }) -join '  ') }
    Write-Host ''
}

$files = @(Get-ChildItem -Path $root -Recurse -Filter 'partida_*.csv' -File)
if ($files.Count -eq 0) {
    Write-Host "No hay archivos partida_*.csv en $root"
    exit 0
}

$games = New-Object System.Collections.Generic.List[object]
$incomplete = 0
foreach ($file in $files) {
    $rows = @(Import-Csv -Path $file.FullName)
    $end = $rows | Where-Object { $_.fila -eq 'FIN' } | Select-Object -First 1
    if ($null -eq $end) { $incomplete++; continue }

    $relative = $file.FullName.Substring($root.Length).TrimStart('\')
    $player = if ($relative.Contains('\')) { $relative.Split('\')[0] } else { '(sin carpeta)' }
    $damage = @{}
    foreach ($property in $end.PSObject.Properties) {
        if ($property.Name -like 'dano_*') { $damage[$property.Name.Substring(5)] = ConvertTo-Number $property.Value }
    }

    $games.Add([pscustomobject]@{
        Jugador   = $player
        Dificultad = $end.dificultad
        Resultado = $end.resultado
        Causa     = $end.causa_muerte
        VidaJefe  = ConvertTo-Number $end.vida_al_jefe
        SegJefe   = ConvertTo-Number $end.segundos_jefe
        Segundos  = ConvertTo-Number $end.segundos
        Dano      = ($damage.Values | Measure-Object -Sum).Sum
        DanoCausa = $damage
    })
}

Write-Host ("Archivos: {0}; partidas con fila FIN: {1}; sin FIN (cierre brusco, ignorados): {2}" -f $files.Count, $games.Count, $incomplete)
if ($games.Count -eq 0) { exit 0 }

$order = @{ Easy = 0; Normal = 1; Hard = 2 }

function Get-Summary {
    param($Group, [string]$Label)
    $victories = @($Group | Where-Object { $_.Resultado -eq 'victoria' }).Count
    $deaths = @($Group | Where-Object { $_.Resultado -eq 'muerte' }).Count
    $abandoned = @($Group | Where-Object { $_.Resultado -eq 'abandono' }).Count
    $finished = $victories + $deaths
    $reached = @($Group | Where-Object { $null -ne $_.VidaJefe })
    $topCause = $Group | Where-Object { $_.Resultado -eq 'muerte' } | Group-Object Causa | Sort-Object Count -Descending | Select-Object -First 1

    [pscustomobject]@{
        Grupo = $Label
        Partidas = $Group.Count
        Victorias = $victories
        Muertes = $deaths
        Abandonos = $abandoned
        'Tasa victoria' = if ($finished -gt 0) { '{0:P0}' -f ($victories / $finished) } else { '-' }
        'Llegan al jefe' = '{0:P0}' -f ($reached.Count / $Group.Count)
        'Vida media al jefe' = Format-Value (Get-Mean ($reached | ForEach-Object { $_.VidaJefe }))
        'Seg. jefe (media)' = Format-Value (Get-Mean ($reached | ForEach-Object { $_.SegJefe }))
        'Muerte mas comun' = if ($topCause) { '{0} ({1})' -f $topCause.Name, $topCause.Count } else { '-' }
        'Dano medio' = Format-Value (Get-Mean ($Group | ForEach-Object { $_.Dano }))
        'Seg. medios' = Format-Value (Get-Mean ($Group | ForEach-Object { $_.Segundos })) '0'
    }
}

function Get-DamageByCause {
    param($Group, [string]$Label)
    $row = [ordered]@{ Grupo = $Label }
    $causes = $Group | ForEach-Object { $_.DanoCausa.Keys } | Sort-Object -Unique
    foreach ($cause in $causes) {
        $row[$cause] = Format-Value (Get-Mean ($Group | ForEach-Object { $_.DanoCausa[$cause] })) '0.00'
    }
    [pscustomobject]$row
}

$byDifficulty = $games | Group-Object Dificultad | Sort-Object { if ($order.ContainsKey($_.Name)) { $order[$_.Name] } else { 9 } }
$summary = @($byDifficulty | ForEach-Object { Get-Summary $_.Group $_.Name })
$summary += Get-Summary $games.ToArray() 'Todas'

Write-Host ''
Write-Host 'Por dificultad'
Write-Table $summary

Write-Host 'Dano medio por partida y por causa'
Write-Table @($byDifficulty | ForEach-Object { Get-DamageByCause $_.Group $_.Name })

if ($PorJugador) {
    Write-Host 'Por persona y dificultad'
    $perPlayer = @()
    foreach ($player in ($games | Group-Object Jugador | Sort-Object Name)) {
        foreach ($difficulty in ($player.Group | Group-Object Dificultad | Sort-Object { if ($order.ContainsKey($_.Name)) { $order[$_.Name] } else { 9 } })) {
            $perPlayer += Get-Summary $difficulty.Group ("{0} / {1}" -f $player.Name, $difficulty.Name)
        }
    }
    Write-Table $perPlayer
}

if ($Salida) {
    $summary | Export-Csv -Path $Salida -NoTypeInformation -Encoding UTF8
    Write-Host "Resumen guardado en $Salida"
}
