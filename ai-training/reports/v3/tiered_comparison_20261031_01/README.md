# Comparacion de compilacion escalonada

Niveles 600001..600005, diez repeticiones por entorno, un trabajador.
Un proceso headless por intento de lote, ejecutados secuencialmente; los reintentos se registran abajo.
Se compara la variable ausente (valor por defecto) con DOTNET_TieredCompilation=0.

Resultado: DIFERENCIAS.
Se comparan los JSON completos de las 100 partidas por configuracion y los cinco mapas: semillas, resultados por sala, dano por fuente y tipo, visitas y tiempos.
Coinciden byte a byte 104 de 105 archivos; comparison.json conserva ambos hashes de cada archivo.
Tambien se cotejan todos los archivos de mapas y partidas guardados por intentos fallidos: 83 archivos adicionales. Se conservan y no se ocultan al elegir el intento completo.
Se excluyen de la igualdad las duraciones de pared, memoria y metadatos de configuracion.

## Comprobacion de igualdad fallida

No se cumple el requisito de resultados identicos previo a la curva completa. No se puede declarar determinismo ni atribuir causalmente el desacuerdo a la variable de entorno.
Los incidentes del motor se conservan incluso cuando el proceso termina con codigo cero; no se reintentan esos pares ni se sustituyen resultados para conseguir igualdad.

| Archivo | Dano por defecto / desactivado | Tiempo por defecto / desactivado (s) |
|---|---|---|
| real_600005_2.json | 4 / 5 | 89.600006 / 90.333336 |

Comando de comprobacion (sale con codigo 1 si hay diferencias):

```powershell
python ai-training/validation/compare_repetition_tiered_v3.py ai-training/reports/v3/tiered_comparison_20261031_01
```

## Incidentes

- default: intento 1, complete, salida 0.
  - Linea 1: `ERROR: Failed to open 'user://logs/godot2026-10-31T18.29.46.log'.`; par None.
  - Linea 3: `ERROR: Failed to open log file for writing: user://logs/godot.log`; par None.
  - Linea 7: `ERROR: Failed to read the root certificate store.`; par None.
  - Linea 68: `WARNING: Jolt Physics job system exceeded the maximum number of jobs. This should not happen. Please report this. Waiting for jobs to become available...`; par {'seed': 600002, 'run': 1, 'phase': 'real'}.
- disabled: intento 1, fatal, salida 3221225501.
  - Linea 1: `ERROR: Failed to open 'user://logs/godot2026-10-31T18.32.21.log'.`; par None.
  - Linea 3: `ERROR: Failed to open log file for writing: user://logs/godot.log`; par None.
  - Linea 7: `ERROR: Failed to read the root certificate store.`; par None.
  - Linea 168: `WARNING: Jolt Physics job system exceeded the maximum number of jobs. This should not happen. Please report this. Waiting for jobs to become available...`; par {'seed': 600004, 'run': 1, 'phase': 'real'}.
  - Linea 208: `ERROR: FATAL: Condition "gchandle.is_released()" is true.`; par {'seed': 600004, 'run': 9, 'phase': 'real'}.
  - Linea 213: `Fatal error. System.Runtime.InteropServices.SEHException (0x80004005): External component has thrown an exception.`; par {'seed': 600004, 'run': 9, 'phase': 'real'}.
- disabled: intento 2, complete, salida 0.
  - Linea 1: `ERROR: Failed to open 'user://logs/godot2026-10-31T18.34.23.log'.`; par None.
  - Linea 3: `ERROR: Failed to open log file for writing: user://logs/godot.log`; par None.
  - Linea 7: `ERROR: Failed to read the root certificate store.`; par None.
  - Linea 223: `ERROR: System.InvalidOperationException: Handle is not initialized.`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 224: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 225: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 230: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 239: `ERROR: System.InvalidOperationException: Handle is not initialized.`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 240: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 241: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 246: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 255: `ERROR: System.InvalidOperationException: Handle is not initialized.`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 256: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 257: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 262: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 271: `ERROR: System.InvalidOperationException: Handle is not initialized.`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 272: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 273: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 278: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 287: `ERROR: System.InvalidOperationException: Handle is not initialized.`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 288: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 289: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.
  - Linea 294: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`; par {'seed': 600005, 'run': 2, 'phase': 'real'}.

results.zip conserva todos los resultados, manifiestos, estados, recursos y logs originales de ambas configuraciones.
Protocolo, plan congelado, bot, simulador y datos historicos intactos. Prueba historica: 51 comprobaciones correctas, incluidos 20 SHA-256.
El commit del ejecutor compilo desde git archive y paso nueve pruebas especificas.

Reproduccion desde la raiz:

```powershell
python ai-training/validation/run_repetition_curve_v3.py ai-training/reports/v3/OTRA_COMPARACION/default --godot RUTA_GODOT_CONSOLE --levels 5 --tiered default
python ai-training/validation/run_repetition_curve_v3.py ai-training/reports/v3/OTRA_COMPARACION/disabled --godot RUTA_GODOT_CONSOLE --levels 5 --tiered 0
python ai-training/validation/compare_repetition_tiered_v3.py ai-training/reports/v3/OTRA_COMPARACION
```
