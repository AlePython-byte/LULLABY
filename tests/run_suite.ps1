<#
.SYNOPSIS
  Ejecuta la bateria completa de pruebas de LULLABY en modo headless (Windows PowerShell).

.DESCRIPTION
  Compila (dotnet build), importa los recursos (tambien se revisa su log) y lanza cada prueba con Godot --headless. Una prueba falla si:
    - su codigo de salida no es 0, o
    - se pasa del tiempo maximo, o
    - su log contiene errores de carga de escenas o recursos (Parse Error, Failed loading resource, Failed to load, No loader found,
      referenced non-existent resource, Unknown tag...). Una escena rota no hace fallar a muchas pruebas por si sola, pero deja
      uno de estos mensajes en el log.
  Al final imprime un resumen y sale con 1 si algo fallo. Los logs quedan en LogDir (uno por prueba).

.PARAMETER Godot
  Ruta de la consola de Godot 4.7.2 .NET (por ejemplo Godot_v4.7.2-stable_mono_win64_console.exe). Si no se da, usa la variable de
  entorno GODOT y, si tampoco existe, busca "godot" en el PATH.

.PARAMETER LogDir
  Carpeta de los logs (por defecto, una carpeta temporal).

.PARAMETER Only
  Nombres de las pruebas a ejecutar (por ejemplo -Only AudioEventsTest,MenuFlowTest). Por defecto, todas.

.EXAMPLE
  .\tests\run_suite.ps1 -Godot "C:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe"
#>
param(
    [string]$Godot = $env:GODOT,
    [string]$LogDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'lullaby-tests'),
    [string[]]$Only = @(),
    [int]$TimeoutSeconds = 900,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Godot) { $Godot = 'godot' }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

# Mensajes que delatan una escena o un recurso que no carga. Coincidencia sensible a mayusculas: "Parse Error" de las escenas no
# coincide con el aviso "parse error" de un archivo de ajustes danado que prueban a proposito.
$loadErrorPattern = 'Parse Error|Failed loading resource|Failed to load|No loader found|referenced non-existent resource|Unknown tag|Cannot open resource'

# Cada prueba: nombre de la escena en tests/, argumentos propios tras "--" si los necesita.
$tests = @(
    @{ Name = 'HistoricalIntegrityTest' },
    @{ Name = 'EnemyModelTest' },
    @{ Name = 'EnemyBehaviorTest' },
    @{ Name = 'ItemSystemTest' },
    @{ Name = 'TreasureItemsTest' },
    @{ Name = 'MenuFlowTest' },
    @{ Name = 'PauseTest' },
    @{ Name = 'BossEncounterTest' },
    @{ Name = 'BossVictoryTest' },
    @{ Name = 'DefeatTest' },
    @{ Name = 'PlayerVisualTest' },
    @{ Name = 'GeneratorStressTest' },
    @{ Name = 'VisualModelsTest' },
    @{ Name = 'RoomDecorBaseTest' },
    @{ Name = 'RoomDecorLayoutTest' },
    @{ Name = 'RoomDecorVarietyTest' },
    @{ Name = 'ItemMessageVarietyTest' },
    @{ Name = 'AudioEventsTest' },
    @{ Name = 'DirectorVarietyAudit'; Args = @('--', '--verify-selector', '--check-report'); FixedFps = $false }
)
if ($Only.Count -gt 0) { $tests = $tests | Where-Object { $Only -contains $_.Name } }

# Lanza Godot con argumentos, guarda su salida en un log y devuelve codigo de salida, motivo y lineas de error de carga.
function Invoke-Godot {
    param([string[]]$GodotArguments, [string]$Log)
    $err = $Log -replace '\.log$', '.err.log'
    $process = Start-Process -FilePath $Godot -ArgumentList $GodotArguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $Log -RedirectStandardError $err
    $null = $process.Handle   # sin esto, ExitCode puede quedar vacio con -PassThru
    $reason = ''
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        $exitCode = -1
        $reason = "tiempo maximo ($TimeoutSeconds s)"
    } else {
        $process.WaitForExit()
        $exitCode = $process.ExitCode
    }

    $text = ''
    if (Test-Path $Log) { $text += (Get-Content -Raw $Log) }
    if (Test-Path $err) { $text += (Get-Content -Raw $err) }
    $loadErrors = @()
    if ($text) {
        $loadErrors = @($text -split "`r?`n" | Where-Object { $_ -cmatch $loadErrorPattern } | Select-Object -Unique)
    }
    if ($exitCode -ne 0 -and -not $reason) { $reason = "codigo de salida $exitCode" }
    if ($loadErrors.Count -gt 0) { $reason = (($reason, "errores de carga en el log ($($loadErrors.Count))") | Where-Object { $_ }) -join '; ' }
    return [pscustomobject]@{ ExitCode = $exitCode; Reason = $reason; LoadErrors = $loadErrors; Ok = (($exitCode -eq 0) -and ($loadErrors.Count -eq 0)) }
}

function Write-Result {
    param([string]$Name, $Result, [int]$Seconds, [string]$Log)
    if ($Result.Ok) {
        Write-Host ("  OK     {0,-26} {1,4} s" -f $Name, $Seconds) -ForegroundColor Green
    } else {
        Write-Host ("  FALLO  {0,-26} {1,4} s  {2}" -f $Name, $Seconds, $Result.Reason) -ForegroundColor Red
        foreach ($line in ($Result.LoadErrors | Select-Object -First 5)) { Write-Host "         $line" -ForegroundColor Yellow }
        Write-Host "         log: $Log" -ForegroundColor DarkGray
    }
}

Push-Location $root
try {
    if (-not $SkipBuild) {
        Write-Host '== dotnet build ==' -ForegroundColor Cyan
        & dotnet build | Out-Host
        if ($LASTEXITCODE -ne 0) { Write-Host 'La compilacion fallo.' -ForegroundColor Red; exit 1 }
    }

    # La importacion tambien cuenta: una escena o un recurso roto ya da error aqui.
    Write-Host '== importar recursos ==' -ForegroundColor Cyan
    $importLog = Join-Path $LogDir 'import.log'
    $started = Get-Date
    $import = Invoke-Godot -GodotArguments @('--headless', '--path', '.', '--import') -Log $importLog
    Write-Result -Name 'importacion' -Result $import -Seconds ([int]((Get-Date) - $started).TotalSeconds) -Log $importLog

    $results = @([pscustomobject]@{ Name = 'importacion'; Ok = $import.Ok; Reason = $import.Reason })
    foreach ($test in $tests) {
        $name = $test.Name
        $log = Join-Path $LogDir "$name.log"
        $arguments = @('--headless')
        if ($test.FixedFps -ne $false) { $arguments += @('--fixed-fps', '60') }
        $arguments += @('--path', '.', "res://tests/$name.tscn")
        if ($test.Args) { $arguments += $test.Args }

        $started = Get-Date
        $result = Invoke-Godot -GodotArguments $arguments -Log $log
        Write-Result -Name $name -Result $result -Seconds ([int]((Get-Date) - $started).TotalSeconds) -Log $log
        $results += [pscustomobject]@{ Name = $name; Ok = $result.Ok; Reason = $result.Reason }
    }

    $failed = @($results | Where-Object { -not $_.Ok })
    Write-Host ''
    Write-Host ("Comprobaciones: {0}, correctas: {1}, fallidas: {2}" -f $results.Count, ($results.Count - $failed.Count), $failed.Count) -ForegroundColor Cyan
    if ($failed.Count -gt 0) {
        foreach ($item in $failed) { Write-Host ("  - {0}: {1}" -f $item.Name, $item.Reason) -ForegroundColor Red }
        exit 1
    }
    exit 0
}
finally {
    Pop-Location
}
