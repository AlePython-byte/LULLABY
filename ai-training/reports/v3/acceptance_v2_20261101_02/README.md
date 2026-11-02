# Aceptación v2 tras enmienda: fallo de calidad

**No aprobada: timeout real en 610006:0.** Se detuvo al primer fallo de calidad, sin reintento ni corrección. El primer lote 610001..610005 quedó limpio al primer intento: 50 pares válidos. El segundo proceso falló en el primer par, antes de seleccionar ningún resultado de ese lote.

## Condiciones y decisión previa

Protocolo v2 enmendado antes del relanzamiento, commit f6951b0: hasta seis intentos técnicos por lote y pausa mínima de diez segundos antes de cada reintento, también al reanudar. Criterios, validez de logs, semillas, diez repeticiones 0..9, núcleo y protocolo original conservados. Se superaron treinta pruebas y la compilación desde git archive antes de iniciar Godot; no hubo compilaciones durante la medición.
La reproducción previa en reposo dio 0, 12 y 6 líneas GCHandle, en ningún par, 610004:0 y 610003:0 respectivamente. Fue esporádica, por lo que se aplicó la ruta autorizada de enmienda y aceptación nueva, sin comparar DOTNET_gcConcurrent=0. No demuestra que las compilaciones causaran los errores anteriores. Las partidas diagnósticas no se reutilizaron.
Rango solicitado 610001..610090, diez repeticiones por nivel y entorno, un trabajador, cinco niveles por proceso, solo headless, entornos secuenciales, DOTNET_TieredCompilation y DOTNET_gcConcurrent ausentes. No se ejecutaron combates de reserva; la cobertura de mapas de las tres muestras sigue la comprobación predeclarada sin combates.

## Criterios y estadísticos parciales

**Los siguientes valores son descriptivos de cinco niveles, no un veredicto favorable de aceptación.** Faltan 85 niveles completos. Los campos pass del agregado heredado indican únicamente la comparación numérica del subconjunto; criterion_verdicts deja explícito que no están aceptados. IC 95 % con 2000 remuestreos pareados por nivel y semilla 20261031.

| Criterio | Subconjunto válido | Veredicto final |
|---|---|---|
| Spearman daño ≥0,7 | 0,872082; IC [0,111111; 1,000000] | No evaluable como aceptación completa |
| Spearman tiempo ≥0,7 | 1,000000; IC [1,000000; 1,000000] | No evaluable como aceptación completa |
| Contacto ±25 % | +3,44 %; 421/407 impactos sim/real; IC [−4,25 %; +10,63 %] | Solo descriptivo; no aceptado |
| Tiempo basic_only, ±30 % | +1.05 %; IC [-0.19 %; +4.35 %] | Solo descriptivo; no aceptado |
| Tiempo with_shooter, ±30 % | -0.48 %; IC [-2.04 %; +0.88 %] | Solo descriptivo; no aceptado |
| Tiempo with_exploder, ±30 % | -1.10 %; IC [-1.94 %; -0.31 %] | Solo descriptivo; no aceptado |
| Tiempo with_flyer, ±30 % | -2.65 %; IC [-4.59 %; -1.10 %] | Solo descriptivo; no aceptado |
| Tiempo with_runner, ±30 % | -1.09 %; IC [-1.55 %; -0.70 %] | Solo descriptivo; no aceptado |
| Tiempo with_digger, ±30 % | -1.04 %; IC [-4.09 %; +1.30 %] | Solo descriptivo; no aceptado |
| Proyectiles, ≥30 impactos reales y sesgo ±30 % | 9/10 impactos sim/real; sesgo −10 % | Evidencia insuficiente; no validados |
| Explosiones, ≥30 impactos reales y sesgo ±30 % | 3/0 impactos sim/real; daño 6/0; sesgo indefinido | Evidencia insuficiente; no validadas |
| Cero timeouts y todas las salas despejadas | Un timeout real observado; solo 50/900 pares seleccionados | **Falla y bloquea** |
| Comportamientos controlados | Evidencia histórica y núcleo conservados; no se repitieron ahora | No salva el fallo de finalización observado |

El IC de Spearman de daño tiene 1973 remuestreos finitos y 27 indefinidos; el de tiempo, 1998 y 2. Con solo cinco niveles, el IC degenerado de tiempo no demuestra precisión poblacional. Los recuentos y los IC de fuentes y composiciones están completos en analysis.json. No se promediaron resultados del lote fallido ni se imputaron faltantes.

## Fallo de finalización

Par 610006:0, fase real: timed_out=true a los 600,00006 s virtuales. Visitó 4 de 12 salas y despejó 3; no alcanzó al jefe. La sala pendiente es (−2,0), plantilla mixta_trinchera (16×26), con Digger, Exploder y Shooter. Entró a los 22,716667 s; clear_time=−1. Ocho salas quedaron sin visitar. El simulador del mismo par terminó las doce salas a los 109,26667 s, sin timeout.
Mensaje exacto: `ERROR: FidelityV3V2Check+QualityFailure: Timeout o salas sin visitar/despejar.` El proceso salió con código 1 y quedó en failed_quality. Es un fallo observado de finalización; no demuestra por sí solo que esa sala sea imposible para todos los recorridos. No se registró la traza por fotograma ni el enemigo superviviente, por lo que no se atribuye la causa a un enemigo concreto.
Los contadores timeouts={sim:0,real:0} del agregado seleccionado solo cubren el primer lote limpio. observed_quality_failure registra el timeout real excluido del agregado; no se oculta como incidencia técnica ni se reintenta para conseguir una muestra que apruebe.

## Incidentes, reintentos y partidas pendientes

Dos procesos: primer lote salida 0, limpio; segundo salida 1 por calidad. **Cero líneas GCHandle, cero Illegal instruction y cero reintentos técnicos en esta aceptación.** Hay una línea ERROR de calidad, reflejada también en el canal nativo. La línea exacta de certificados aparece una vez por canal en cada proceso, antes de FIDELITY_V2_BEGIN, y es la única excepción permitida. Los avisos y logs completos se conservan.
**Contabilidad de los 900 pares:** 50 válidos, 1 observado con timeout y 849 nunca iniciados. No hubo exclusiones por agotar intentos técnicos. El lote 610006..610010 tiene 50 pares bloqueados por calidad: el primero falló y los otros 49 no se iniciaron. El estado enumera además 800 pares de los lotes siguientes (610011..610090). not_selected_pairs enumera los 850 pares no seleccionados y su motivo; execution_summary.quality_blocked_batch_pairs enumera los 50 del lote detenido. Nada se descarta en silencio.

## Diagnóstico sobre la muestra de desarrollo

En desarrollo, misma plantilla: 150 observaciones reales, 0 sin despejar; duración de combate entre 5.033335000000001 y 7.833340000000007 s.
En desarrollo, misma plantilla y tipos de aparición: 150 observaciones reales, 0 sin despejar; duración de combate entre 5.033335000000001 y 7.833340000000007 s.
Se revisaron únicamente los 300 pares limpios ya archivados de desarrollo: cero timeouts en ambos entornos. analysis.json incluye cada observación de esta plantilla y su composición. Estos precedentes no prueban que la ruta y el estado concretos del mapa 610006 sean despejables ni identifican el mecanismo del bloqueo. No se ejecutaron nuevas partidas de diagnóstico, no se tocó el núcleo y no se modificó el criterio tras observar el fallo.
La aceptación queda fallida y detenida. No se relanza, no se corrige y no se usa la reserva sin una decisión posterior explícita.

## Integridad y archivo

Duración de la ejecución: 143.51 s. Protocolo v2 SHA-256: `6cdb35ad0531dc2c5fe48b835d41052bd8288fe228f4f793fbbcd51ce93dc5f1`.
Fuentes, parámetros, escenas, recursos y ensamblado ejecutado quedaron registrados antes de los combates y permanecieron congelados. Se verificaron al cierre el núcleo de referencia y los veinte hashes históricos. El protocolo original y todos los informes anteriores están intactos.
raw_results.zip contiene ambos procesos, resultados completos y parciales, fallo, manifiestos, mapas, estado terminal, logs, progreso, autorización y protocolo. sha256.json verifica cada miembro. ZIP SHA-256: `af02a34b1004a1509234291f309a99714fb98764c684c93a0a5b867aff99ab03`.
analysis.json conserva procedencia, IC, incidente de calidad, diagnóstico de desarrollo y contabilidad individual. No se ha hecho push.
