# Protocolo v2 y calendario hasta el 8 de noviembre

Preparacion documental del 1 de noviembre de 2026. No se ejecuto Godot,
aceptacion, reserva, etiquetado ni entrenamiento para elaborar este informe.
Todas las fechas y ventanas del calendario son de America/Bogota.

## Congelacion y alcance

`ai-training/protocols/v3_fidelity_v2.json`, commit `bbddd43`, congela diez
repeticiones por nivel y entorno antes de abrir resultados de aceptacion.
SHA-256: `e1ab71ac414cd0624c79ebb96c68e044e32a01e0c2896e51e7c4ccb18d332e27`.
El original conserva exactamente su SHA-256
`91d0239fad57636476455b66eb06d49a1326b731d6777b2a166fa36aafb1e220`.

Los objetos `criteria`, `uncertainty` y `samples` son identicos al original:
Spearman de dano y tiempo >= 0,7 sobre medias por nivel, contacto dentro de
+-25 %, tiempo de cada composicion dentro de +-30 %, cero timeouts y salas
pendientes, y pruebas de comportamiento correctas. Proyectiles y explosiones
mantienen +-30 % y al menos 30 impactos reales por fuente; con menos impactos
se conserva el dictamen original de evidencia insuficiente no bloqueante,
sin declarar esa fuente validada. No se rebaja ningun umbral.

La adaptacion se eligio mirando desarrollo: Spearman de dano 0,533684 con tres,
0,641606 con cinco y 0,825423 con diez. El IC 95 % con diez es
[0,673471; 0,913987]; incluye valores inferiores a 0,7. No es una confirmacion
independiente ni demuestra que todo el desacuerdo sea ruido. Tres repeticiones
siguen sin cumplir el criterio original. Se conservan ambos protocolos y todos
los resultados originales.

Aceptacion: **610001..610090**; reserva: **620001..620090**. Son las muestras del
protocolo original, segun la instruccion actual. Las notas que proponian usar
620001 para una version corregida quedan como historia de una ruta no aplicada;
no se renumeran ni borran. La cobertura de mapas ya fue inspeccionada sin
combates en la preparacion anterior; no se afirma que esos mapas fueran
completamente desconocidos. No se consultaron aqui resultados de combates de
aceptacion ni reserva.

## Regla de intentos y requisito de muestra completa

Un proceso por cinco niveles, diez repeticiones 0..9 por entorno, un trabajador,
simulador y juego real secuenciales. Se selecciona el primer intento completo,
con salida cero, hashes correctos y ambos logs limpios despues del cierre.
Se invalidan GCHandle, Illegal instruction y **cualquier ERROR:**, sin distinguir
mayusculas; no hay mensajes de error permitidos. Los avisos se conservan y un
fallo reproducible de comportamiento bloquea por el criterio original.

Hasta tres intentos por lote, sin resetear contadores al reanudar. Si se agotan,
se enumeran los 50 pares excluidos y los no iniciados, con motivo e historial,
y se detiene como incompleto. No se aprueba con 85 niveles u otra cohorte
reducida: hacen falta **90 niveles, 900 pares validos y 1800 partidas**. Las
medias conservan sus diez repeticiones; no se rellenan ni sustituyen datos.
Timeouts, salas imposibles o pendientes, atascos y fallos de comportamiento no
pueden esconderse bajo reintentos tecnicos para aprobar.

**Bloqueo conocido del entorno:** los nueve logs de la curva contienen
`ERROR: Failed to read the root certificate store.`. Por tanto, ninguno seria
valido bajo esta regla nueva, mas estricta que la de la curva, que permitia ese
mensaje. La tasa anterior de exito no es transferible sin resolverlo. La etapa 1
registro que este mensaje aparecia en sandbox y que el control fuera de el no
lo mostro, pero eso no sustituye una comprobacion nueva. Primero hay que lograr
logs de desarrollo sin errores, con el equipo libre y OK; no ignorar el mensaje
ni consumir aceptacion probando el entorno. Si persiste, no lanzar aceptacion.

## Costes medidos y metodo de proyeccion

Fuente: `repetition_curve_20261031_02/raw_results.zip`, SHA-256
`fb6c143ed983b27d82db26176661b155cfc0f347b9c9d86a0ab35366d9fa050c`.
Las cifras y formulas se conservan en `fidelity_v2_budget_20261101.json`.

- 300 pares seleccionados: simulacion media **0,049142639 s**, juego real medio
  **3,054079619 s**; suma de fases **3,103222258 s/par**.
- Los seis lotes validos suman **976,829244 s = 16,28 min**, o
  **3,256097478 s/par** incluyendo trabajo del lote. Esto, no el intervalo entre
  inicio y cierre que incluye la noche interrumpida, calibra aceptacion.
- Percentil empirico 95 de una simulacion: **0,081938 s**; de un par de fases:
  **4,3458105 s**. Se usa el indice inferior floor(0,95*(n-1)). Multiplicarlo por
  toda una carga es un escenario de velocidad lenta, **no un IC del trabajo**.
- Memoria residente maxima observada: **202,86 MiB**. Memoria privada maxima
  observada entre pares: **157,54 MiB**, no un pico privado continuo medido.

Aceptacion central = 900 pares * 3,256097478 s = **48,84 min**. El escenario
lento, escalando la tasa p95 por la misma proporcion de trabajo del lote, da
**68,40 min**. Las ventanas incluyen margen para arranques, E/S, analisis y
variacion; no son garantias ni incluyen desarrollar herramientas.

Para presupuestar se supone conservar 5000 niveles de entrenamiento, 1000 de
prueba y 1000 de confirmacion, y un benchmark de 300 grupos de 30 candidatos.
Son dimensiones de planificacion: aun falta el soporte v3 para producirlos.

Las etiquetas y el benchmark utilizan **solo el simulador**, no el coste de
juego real ni los 88 s de tiempo virtual medio de cada partida. En el benchmark
se simulan una sola vez los 30 candidatos de cada semilla de partida y se
reutilizan para los tres objetivos y las cuatro estrategias: no multiplicar
otra vez por tres o por cuatro.

| Trabajo, un trabajador | Simulaciones o pares con 10 | Base con 5 repeticiones | Base con 10 | Ventana recomendada con 10 |
|---|---:|---:|---:|---|
| Aceptacion, 90 niveles, ambos entornos | 900 pares / 1800 partidas | No aplica | 48,84 min | 75-120 min |
| Etiquetas entrenamiento, 5000 niveles | 50 000 simulaciones | 20,48 min | 40,95 min | 60-90 min |
| Etiquetas prueba, 1000 niveles | 10 000 simulaciones | 4,10 min | 8,19 min | 12-18 min |
| Etiquetas confirmacion, 1000 niveles | 10 000 simulaciones | 4,10 min | 8,19 min | 12-18 min |
| Tres conjuntos, 7000 niveles, una pasada | 70 000 simulaciones | 28,67 min | 57,33 min | 85-120 min |
| Tres conjuntos, dos pasadas para determinismo | 140 000 simulaciones | 57,33 min | 114,67 min | 170-240 min |
| Benchmark completo, 300 semillas x 30 candidatos | 90 000 simulaciones | 36,86 min | 73,71 min | 110-150 min |
| Alternativa reducida, 100 semillas x 30 candidatos | 30 000 simulaciones | 12,29 min | 24,57 min | 40-60 min |
| Benchmark completo, dos pasadas si se exige reproducir todo | 180 000 simulaciones | 73,71 min | 147,43 min | 220-300 min |

Con tres intentos completos equivalentes de aceptacion, el escenario central
es **146,52 min** y el lento **205,20 min**. Reservar hasta **3,5 h** si se quiere
absorber ese escenario; no es una cota dura y un fallo puede detener antes la
muestra. Una eventual reserva completa tiene la misma carga y requiere otro OK.
No se presupuesta una tasa fiable de excepciones a partir de nueve procesos.

Las ventanas de etiquetas presuponen lotes de 50-100 niveles y escritura por
flujo; el benchmark debe retener solo el grupo de candidatos que este evaluando.
La memoria no se multiplica por tres al pasar de 30 a 90 niveles si se recicla
el proceso cada cinco niveles. Presupuesto operativo, aun por comprobar:
**300-500 MiB** por proceso de aceptacion, **300-600 MiB** para los procesos de
simulacion por lotes; dejar **1 GiB libre**. Los exportadores historicos precargan
todos los mapas, asi que ese limite de memoria no se promete si se copia ese
diseno sin adaptarlo.

**Ajustar modelos no es etiquetar.** Diez repeticiones duplican las simulaciones
para etiquetar, pero siguen siendo 5000 filas para entrenar: no duplican por si
solas el numero de ajustes ni la RAM de las matrices. No hay tiempo ni memoria
Python medidos por esta curva de Godot. Reservar provisionalmente **4-8 h y
2-4 GiB libres** para validacion cruzada y modelos en serie; son presupuestos de
planificacion, no extrapolaciones medidas ni cotas. Calibrar un fold usando
solo entrenamiento, sin abrir prueba o confirmacion, antes de cerrar esa ventana.

Aceptacion + dos pasadas de etiquetas + un benchmark completo: base calculada
**3,95 h**, ventana operativa conjunta **6-8,5 h**, aparte ajuste de modelos,
preparacion de herramientas, diagnosticos y cualquier reserva.

## Preparacion tecnica pendiente, no incluida en los minutos de simulacion

1. `FidelityV3Protocol` y `v3_fidelity.py` siguen fijados al hash del original;
   `FidelityV3Check` solo acepta cobertura, piloto y desarrollo. El ejecutor de
   la curva limita las semillas a 600001..600030 y aplica la regla menos estricta
   de errores anterior. Crear soporte aislado para v2, log completo de ambos
   canales, 90 x 10 y cierre estricto; no cambiar el nucleo de medicion.
2. `ToolVersionGuard.MaxSupportedVersion` es 2. `LabelExport` usa el simulador y
   bot historicos, aunque admite --runs; pasarle version 3 no crea etiquetas v3.
   Faltan caracteristicas v3 y su exportacion con LevelSimulatorV3/BotBrainV3,
   metadatos, lotes, reanudacion y comprobacion de determinismo.
3. `DirectorBenchmark` fija cinco repeticiones y usa Parallel.For sin limitar
   el paralelismo. Faltan soporte v3, diez repeticiones, procesamiento acotado
   y un trabajador. Conservar los 30 candidatos y todas las estrategias.
4. `training/common.py` solo admite versiones 1 y 2; `train.py` usa n_jobs=-1
   fuera de los estimadores. Faltan soporte v3, exportacion/paridad e integracion;
   imponer un trabajo y un hilo numerico en todas las capas, sin cambiar v1/v2.

El JSON nuevo congela la decision estadistica; **no convierte esas herramientas
en un ejecutor listo para lanzar**. Antes del primer combate de aceptacion se
requieren pruebas sinteticas, integridad y un manifiesto con hashes de protocolo,
nucleo, parametros, arnes, analizador y ensamblado realmente ejecutado.

## Calendario recomendado, condicionado a ventanas de equipo libre

| Fecha | Trabajo y ventana | Condicion de salida |
|---|---|---|
| 1 nov | Protocolo y presupuesto congelados; empezar preparacion de arnes/analizador v2 y revisar compatibilidad, sin Godot | Diseno firmado; aceptacion sin abrir |
| 2 nov | Completar herramientas v2 y preparacion v3, pruebas sinteticas y compilacion; preflight limpio en desarrollo solo tras OK y equipo libre | Sin ERROR en logs; hashes del nucleo intactos; no gastar semillas de aceptacion en depurar |
| 3 nov, punto de control | Aceptacion 610001..610090: reservar 2 h, o 3,5 h con margen de reintentos, mas revision de informe | Solo avanzar con 90 niveles y todos los criterios. Si no esta listo o falla, no etiquetar masivamente |
| 4 nov | Si paso: terminar features/exportadores v3, fijar splits y protocolo de modelado. Si fallo: diagnostico en desarrollo y correccion, sin ejecutar reserva automaticamente | Decision honesta y herramientas verificadas; nucleo corregido exige nueva congelacion |
| 5 nov | Ruta aprobada: etiquetas 5000+1000+1000 con diez y doble generacion, 3-4 h. Ruta corregida: reserva 620001..620090, solo con otro OK y equipo libre | Datos y hashes reproducibles, sin solapamiento efectivo; o reserva aprobada antes de cualquier etiquetado |
| 6 nov | Ajuste y CV solo con entrenamiento; presupuesto 4-8 h pendiente de calibrar. Elegir y congelar modelo antes de prueba; evaluar prueba y confirmacion una vez, paridad e integracion | No retocar por resultados de holdout. Si reserva no ha pasado, no prometer cierre completo v3 |
| 7 nov | Benchmark completo, 2-2,5 h; si se decide recorte previo, alternativa 100 semillas, 40-60 min. Revision de integracion y limitaciones | Informe con incertidumbre y alcance reales; todas las estrategias, mismos candidatos y diez repeticiones |
| 8 nov | Margen para compilacion, comprobaciones de integridad, informe final y entrega | Sin abrir otra muestra ni relajar criterios para cumplir fecha |

La ruta con correccion/reserva puede desplazar etiquetado y modelado al 6-7:
no esta garantizada. Si el punto de control del 3 no permite iniciar una
aceptacion limpia, o la reserva no pasa a tiempo, cerrar el 8 con v1/v2 intactos
y v3 experimental y documentado, sin afirmar modelo v3 validado. No usar reserva
para seleccionar modelos ni ejecutarla cuando aceptacion ya haya pasado.

## Que recortar y que conservar

**No recortaria de entrada el benchmark**: su simulacion central ronda 74 min,
no dias. La mayor incertidumbre es implementar y verificar soporte v3 y la
CV serial, no duplicar las repeticiones. Conservar la ruta completa de 300
semillas mientras las herramientas y las ventanas esten listas.

Plan B: decidir **antes de simular ningun candidato del benchmark**, por ejemplo
el 6 al mediodia si el tiempo reservado ya no alcanza, entre 300 semillas y un
subconjunto fijo de las primeras 100 del rango que se preregistre para v3.
Conservar 30 candidatos, diez repeticiones, tres objetivos y las mismas cuatro
estrategias; publicar n=100 e IC mas anchos. Ahorra unos 49 min centrales, o
70-90 min de ventana. No elegir semillas favorables ni ampliar o truncar el
benchmark despues de ver el efecto.

Si falta tiempo para busquedas de arboles, se puede proponer y congelar antes
una comparacion limitada a media, lineal y Ridge, conservando CV anidada y la
regla de un error estandar; declarar que no se evaluaron las otras familias.
No tomar esa decision mirando prueba o confirmacion ni atribuir a v3 la
comparacion de familias historica.

No recortar aceptacion a menos de 90 niveles, diez repeticiones, ninguno de los
criterios, analisis de fuentes/composiciones, logs limpios, determinismo,
confirmacion o paridad. Tampoco reutilizar etiquetas v1/v2 como si fueran v3.
La congelacion de hoy no autoriza ningun lanzamiento: se espera OK explicito
y confirmacion de equipo libre. El estado de autorizacion falso del JSON
registra la situacion al congelarlo; el OK futuro se documentara en el
manifiesto de ejecucion, sin editar el protocolo congelado para habilitarlo.
