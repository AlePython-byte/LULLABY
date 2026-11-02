# Aceptación de fidelidad v2: incompleta por errores del motor

La aceptación NO está aprobada. Se agotaron los tres intentos del primer lote (610001..610005): todos completaron sus 50 pares y cerraron con código 0, pero los tres logs contienen errores de GCHandle. El ejecutor se detuvo conforme al protocolo, sin relanzamiento posterior ni cambios.

Semillas solicitadas 610001..610090; diez repeticiones 0..9 por nivel y entorno; un trabajador, solo headless, simulador y juego real secuenciales; cinco niveles por proceso; DOTNET_TieredCompilation ausente. Duración total: 369.98 s.

Resultado: **0/900 pares válidos, 50 excluidos y 850 sin iniciar**. Tres intentos, dos reintentos. Se conservan 150 observaciones de pares completas (300 partidas), todas inválidas. No se rescatan pares anteriores al error ni se promedian resultados contaminados. No hay medias válidas por nivel, Spearman ni IC; tampoco se imputan valores ni se reduce la cohorte para aprobar.

## Veredicto por criterio

| Criterio | Regla congelada | Resultado |
|---|---|---|
| Spearman de daño | ≥0,7 sobre medias de 10; IC 95 % bootstrap por nivel | No evaluable; estimación e IC nulos |
| Spearman de tiempo | ≥0,7 sobre medias de 10; IC 95 % bootstrap por nivel | No evaluable; estimación e IC nulos |
| Sesgo de contacto | ±25 %, al menos un impacto real válido | No evaluable; cero impactos válidos |
| Sesgo de tiempo: basic_only | ±30 % | No evaluable |
| Sesgo de tiempo: with_shooter | ±30 % | No evaluable |
| Sesgo de tiempo: with_exploder | ±30 % | No evaluable |
| Sesgo de tiempo: with_flyer | ±30 % | No evaluable |
| Sesgo de tiempo: with_runner | ±30 % | No evaluable |
| Sesgo de tiempo: with_digger | ±30 % | No evaluable |
| Proyectiles | ≥30 impactos reales para evaluar sesgo ±30 % | Evidencia insuficiente: 0 válidos; no validados |
| Explosiones | ≥30 impactos reales para evaluar sesgo ±30 % | Evidencia insuficiente: 0 válidos; no validadas |
| Finalización, salas despejadas y cero timeouts | 900 pares limpios, ninguna sala imposible | Bloqueado: muestra incompleta |
| Comportamientos controlados | Evidencia histórica y ausencia de fallos reproducibles | Núcleo y evidencia histórica conservados; sin evidencia nueva válida de aceptación |

Los ceros de impactos y timeouts del agregado vacío no son evidencia de ausencia de impactos o problemas. Los indicadores fail del evaluador heredado para datos vacíos tampoco son fallos de fidelidad medidos: quality_gate_evaluated=false. criterion_verdicts en analysis.json explicita esta distinción. Los IC predefinidos usan 2000 remuestreos pareados por nivel, semilla 20261031; no se calculan con cero niveles.

## Incidentes y reintentos

Mensaje invalidante: `ERROR: System.InvalidOperationException: Handle is not initialized.` Las trazas incluyen `GCHandle.FromIntPtr` y `ScriptManagerBridge.SwapGCHandleForType` al cargar escenas con `ResourceLoader.Load` en `FidelityV3Check.cs:175`. Los pares siguientes son el contexto activo del log; no prueban que la causa nazca en ese nivel.

| Intento | Contextos de error (semilla:repetición, fase real) | Líneas ERROR no permitidas | Líneas GCHandle | Salida |
|---|---|---:|---:|---:|
| 1 | 610002:0 (2 ERROR), 610004:0 (2 ERROR), 610005:5 (2 ERROR) | 6 | 18 | 0 |
| 2 | 610002:0 (2 ERROR) | 2 | 6 | 0 |
| 3 | 610002:0 (2 ERROR) | 2 | 6 | 0 |

Totales del canal principal: 10 líneas ERROR invalidantes, 30 líneas GCHandle y cero líneas Illegal instruction. Las líneas GCHandle son trazas, no 30 excepciones independientes. Se validaron también los tres logs nativos; sus recuentos y discrepancias de representación quedan en analysis.json, sin sumarlos dos veces como incidentes.
La única excepción permitida, `ERROR: Failed to read the root certificate store.`, aparece una vez en cada canal de cada proceso, antes de FIDELITY_V2_BEGIN: tres apariciones por canal. No invalida; todos los otros errores sí. Los avisos de Jolt se conservan con contexto en execution_summary.attempt_details y runtime.

Los 50 pares excluidos son el producto completo de semillas 610001..610005 por repeticiones 0..9. Cada entrada de exclusions incluye motivo, tres intentos, códigos, hashes y contexto. Los 850 pares sin iniciar son 610006..610090 por 0..9, enumerados individualmente en unstarted_pairs. No se usa la reserva para reemplazarlos.

## Diagnóstico limitado al desarrollo

La curva de desarrollo ya contenía la misma excepción y la misma ruta `GCHandle.FromIntPtr → SwapGCHandleForType → ResourceLoader.Load → PlayReal`, en 600029:8 y 600030:0. Hubo seis líneas ERROR: System. y 18 líneas GCHandle en aquel intento completo; otro intento terminó con fatal de GCHandle (una línea adicional, salida 3221225501). Los intentos limpios posteriores permitieron completar los 300 pares de desarrollo. Esta revisión solo lee el archivo histórico; no ejecuta nuevas partidas.
Por tanto, el problema técnico precede a esta aceptación y la traza lo sitúa durante la carga de escenas. No identifica la causa subyacente ni demuestra que una sesión concurrente fuese necesaria. El ordenador se confirmó libre para esta ejecución. No hay base válida para diagnosticar sesgo del simulador o un criterio estadístico fallido en aceptación.
Como referencia de desarrollo, con diez repeticiones el Spearman de daño fue 0,825423 (IC 95 % [0,673471; 0,913987]) y el de tiempo 0,996885 ([0,978110; 1,000000]). No sustituyen una aceptación y no se han utilizado para corregir el núcleo.

Se detuvo la ejecución. No se corrigió código ni configuración, no se relanzó la aceptación, no se hicieron combates de reserva y no se abrió el etiquetado masivo. Cualquier siguiente decisión queda pendiente. La comprobación de cobertura de mapas de las tres muestras forma parte del protocolo registrado; no ejecuta combates de reserva.

## Integridad y archivo

Protocolo v2 SHA-256: `2104f07db9bfdef73283283210f9bee67bbe2b8ef416dcbe2a0d4cfc1c10108f`. Protocolo original, núcleo, fuentes, recursos, ensamblado de la medición y los 20 hashes históricos se conservaron. Las huellas se fijaron antes del primer combate y se verificaron al cerrar; no se repitieron pruebas controladas fuera de la ejecución autorizada.
raw_results.zip conserva todos los intentos, resultados, mapas, manifiestos, estado final, logs completos, progreso y autorización externa. sha256.json contiene el inventario verificable de cada miembro. execution/state.json conserva el estado terminal incomplete_retry_limit: no se resetea ni permite otro intento de este lote.
ZIP SHA-256: `83e35b37fb358b3a75b624d6f4653c0046535c6d96c69edbd8a21820602b16c4`.
analysis.json conserva los valores nulos, veredictos, incidentes por canal, exclusiones y pares sin iniciar, diagnóstico de desarrollo y procedencia. No se ha hecho push.
