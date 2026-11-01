# Curva diagnostica con intentos limpios

Configuracion: DOTNET_TieredCompilation ausente (etiqueta default); un trabajador, solo desarrollo.
El protocolo y plan originales permanecen intactos. Esta regla de validez adicional fue autorizada tras observar contaminacion por excepciones.
IC 95 %: bootstrap pareado por nivel, 2000 remuestreos, semilla 20261031; prefijos fijos.

| Repeticiones | Niveles | Spearman dano (IC 95 %) | Spearman tiempo (IC 95 %) |
|---|---|---|---|
| 3 | 30 | 0.533684 [0.168437, 0.786377] | 0.985762 [0.944909, 0.995995] |
| 5 | 30 | 0.641606 [0.329361, 0.846740] | 0.993771 [0.969188, 0.999553] |
| 10 | 30 | 0.825423 [0.673471, 0.913987] | 0.996885 [0.978110, 1.000000] |

## Validez, exclusiones y medias

Se acepta el primer intento completo cuyo log entero no contiene GCHandle, Illegal instruction ni ERROR: System. (sin distinguir mayusculas), con salida cero.
Un error invalida todo el intento del lote de cinco niveles. Se conservan todos los logs y resultados; maximo tres intentos por lote.
Los 50 pares de un lote sin intento limpio se excluyen explicitamente. Incluso los pares anteriores al error quedan invalidados por la regla del log completo.
Las medias usan exactamente las primeras 3, 5 y 10 repeticiones de cada nivel incluido. No se reduce el denominador, no se imputan datos y no se eligen otras semillas de bot.
Se usa la misma cohorte de niveles con diez pares validos para los tres prefijos y sus diferencias bootstrap. Las exclusiones reducen la muestra y pueden sesgarla; el resultado describe solo la cohorte retenida.

Pares excluidos: 0. Niveles incluidos: 30 de 30.

## Intentos e incidentes

- Lote 600001..600005, intento 1: complete; salida 0; motivos []; log `execution\batch_600001_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
- Lote 600006..600010, intento 1: invalid; salida supervisor_interrupted; motivos ['supervisor_interrupted_without_verified_successful_exit']; log `execution\batch_600006_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
- Lote 600006..600010, intento 2: complete; salida 0; motivos []; log `execution\batch_600006_attempt_2.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
  - Linea 84, par {'seed': 600007, 'run': 5, 'phase': 'real'}: `WARNING: Jolt Physics job system exceeded the maximum number of jobs. This should not happen. Please report this. Waiting for jobs to become available...`
- Lote 600011..600015, intento 1: complete; salida 0; motivos []; log `execution\batch_600011_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
- Lote 600016..600020, intento 1: complete; salida 0; motivos []; log `execution\batch_600016_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
  - Linea 119, par {'seed': 600018, 'run': 2, 'phase': 'real'}: `WARNING: Jolt Physics job system exceeded the maximum number of jobs. This should not happen. Please report this. Waiting for jobs to become available...`
- Lote 600021..600025, intento 1: complete; salida 0; motivos []; log `execution\batch_600021_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
- Lote 600026..600030, intento 1: invalid; salida 0; motivos ['gchandle', 'error: system.']; log `execution\batch_600026_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
  - Linea 197, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `ERROR: System.InvalidOperationException: Handle is not initialized.`
  - Linea 198, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`
  - Linea 199, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`
  - Linea 204, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`
  - Linea 226, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `ERROR: System.InvalidOperationException: Handle is not initialized.`
  - Linea 227, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`
  - Linea 228, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`
  - Linea 233, par {'seed': 600029, 'run': 8, 'phase': 'real'}: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`
  - Linea 263, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `ERROR: System.InvalidOperationException: Handle is not initialized.`
  - Linea 264, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`
  - Linea 265, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`
  - Linea 270, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`
  - Linea 292, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `ERROR: System.InvalidOperationException: Handle is not initialized.`
  - Linea 293, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`
  - Linea 294, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`
  - Linea 299, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`
  - Linea 321, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `ERROR: System.InvalidOperationException: Handle is not initialized.`
  - Linea 322, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`
  - Linea 323, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`
  - Linea 328, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`
  - Linea 348, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `ERROR: System.InvalidOperationException: Handle is not initialized.`
  - Linea 349, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)`
  - Linea 350, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak) in /root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:line 1284`
  - Linea 355, par {'seed': 600030, 'run': 0, 'phase': 'real'}: `       [2] Godot.NativeInterop.godot_bool Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(nint, System.IntPtr*, Godot.NativeInterop.godot_bool) (/root/godot/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ScriptManagerBridge.cs:1284)`
- Lote 600026..600030, intento 2: invalid; salida 3221225501; motivos ['gchandle', 'exit_code=3221225501']; log `execution\batch_600026_attempt_2.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
  - Linea 67, par {'seed': 600027, 'run': 2, 'phase': 'real'}: `ERROR: FATAL: Condition "gchandle.is_released()" is true.`
  - Linea 72, par {'seed': 600027, 'run': 2, 'phase': 'real'}: `Fatal error. System.Runtime.InteropServices.SEHException (0x80004005): External component has thrown an exception.`
- Lote 600026..600030, intento 3: complete; salida 0; motivos []; log `execution\batch_600026_attempt_3.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`

analysis.json conserva los IC, diferencias de prefijos, hashes, exclusiones y comparacion descriptiva con los datos originales.

## Reanudacion y cierre del 1 de noviembre de 2026

Al revisar el estado, el PID 26140 ya no existia. Habia 53 pares terminados: 50 validados del primer lote y tres del intento interrumpido del segundo. El estado y los hashes eran coherentes.
Se reanudo en esta misma carpeta con la variable DOTNET_TieredCompilation ausente, un trabajador y solo headless. El primer lote no se repitio. El intento parcial se conservo intacto y se marco invalido por no tener cierre ni codigo de salida verificado.
Cierre: 300 pares validados, 30 niveles incluidos, 0 pares excluidos; 9 intentos y 3 reintentos en total.
Los IC se recalcularon a partir de los resultados seleccionados y coinciden exactamente con analysis.json. Se verificaron los hashes de los resultados y logs anteriores y el prefijo original de progress.log.
wall_seconds incluye el intervalo en que la ejecucion estuvo detenida. time_total y los Spearman de tiempo corresponden a tiempos de partida; esa espera no entra en las medias.
raw_results.zip conserva todos los intentos completos y parciales, manifiestos, mapas, recursos, estado final, logs, estado anterior a la reanudacion, inventario original y auditoria. sha256.json permite verificar cada miembro; analysis.json conserva el hash del ZIP.
No se cambio codigo, bot, simulador, plan ni protocolo. Los resultados son diagnosticos de desarrollo; no constituyen aceptacion ni abren la reserva.


## Lectura de los resultados

El Spearman de dano aumenta de 0,533684 con tres repeticiones a 0,641606 con
cinco y 0,825423 con diez. El punto con diez supera 0,7, pero su IC 95 %
[0,673471; 0,913987] incluye valores inferiores. Con tres repeticiones el
criterio puntual sigue sin cumplirse. Este diagnostico de desarrollo no
sustituye el protocolo original ni constituye aceptacion.

La diferencia pareada de dano entre diez y tres es +0,291739, IC 95 %
[0,044003; 0,586712]. Las diferencias cinco menos tres y diez menos cinco
incluyen cero en sus intervalos. Los seis calculos y todos sus remuestreos
validos quedan resumidos en analysis.json; ninguno de los 2000 remuestreos
resulto indefinido.

Los tres reintentos se distribuyen en uno por interrupcion del lote
600006..600010 y dos por errores del motor en 600026..600030. En el primer
intento de este ultimo lote hubo seis excepciones no fatales: dos en
600029:8 y cuatro en 600030:0, 18 lineas GCHandle. El segundo intento murio
por un fatal de GCHandle en 600027:2, una linea adicional. El tercero cerro
limpio. Esos intentos contaminados no entran en ninguna media.

En todos los intentos hubo 19 lineas GCHandle, seis lineas ERROR: System.,
cero lineas Illegal instruction, nueve errores del almacen de certificados
y dos avisos de capacidad del sistema de trabajos de Jolt. El fatal tuvo
codigo de salida 3221225501; la ausencia del texto Illegal instruction no
significa ausencia de una terminacion fatal. Los logs completos se conservan.

No hubo pares excluidos de forma definitiva: las tres medias usan los mismos
30 niveles con sus diez pares limpios, sin cambiar denominadores ni rellenar
datos. Se conservan 365 pares con registro de recursos de todos los intentos;
solo 300 se seleccionaron. Los otros 65 son tres del intento interrumpido,
50 del intento completo contaminado y 12 del intento fatal, ademas de los
archivos parciales que este ultimo alcanzo a escribir.

Comparacion descriptiva con las tres repeticiones del desarrollo original:
una de las 180 partidas comparadas difiere en el resultado completo,
600019:2 del juego real: dano 8 antes frente a 9 ahora, tiempo 79,06667 s
frente a 79,41667 s. El original se conserva intacto. Esta comparacion no
identifica la causa ni permite afirmar determinismo universal a partir del
ensayo previo de cinco niveles.
