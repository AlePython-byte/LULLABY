# Regla adicional de validez de la curva diagnostica

Autorizada despues de la comparacion `tiered_comparison_20261031_01`.
El protocolo `v3_fidelity.json`, el plan congelado, el combate, el bot y los
historicos permanecen intactos. No se ejecuta aceptacion ni reserva.

## Evidencia anterior

Las excepciones de 600005:2 aparecieron con `DOTNET_TieredCompilation=0`:
cinco `System.InvalidOperationException`, con 15 lineas que contienen GCHandle
entre pila y backtrace. Hubo ademas una linea GCHandle en el intento fatal
anterior de esa configuracion, en 600004:9.

| Configuracion | Intentos | Lineas GCHandle | ERROR: System. |
|---|---:|---:|---:|
| Variable ausente | 1 | 0 | 0 |
| Valor 0 | 2 | 16 | 5 |

Se cuentan los logs de cada intento una vez, sin volver a contar `progress.log`.
Las lineas no son excepciones independientes. La exposicion y el numero de
intentos difieren: estos datos no demuestran una reduccion causal al cambiar
la variable. En esa comparacion no las evito.

## Ejecucion y seleccion

- Cinco niveles por proceso headless, diez repeticiones del bot por entorno,
  un trabajador; se conserva el orden y las semillas originales.
- El log completo del proceso es la unidad de validez. Cualquier aparicion de
  `GCHandle`, `Illegal instruction` o `ERROR: System.` invalida **todo el
  intento**, aunque termine con salida cero. Busqueda sin distinguir mayusculas.
- Hasta tres intentos por lote. Se elige el primero completo, con salida cero,
  hashes verificados y log limpio. Todos los intentos anteriores se conservan.
- Si ninguno queda limpio, se enumeran los 50 pares del lote en `excluded_pairs`,
  con semilla, repeticion, motivo e historial de intentos y logs. Los pares
  anteriores al error y los no alcanzados tampoco se aceptan a partir de un
  log contaminado o proceso interrumpido.
- Un supervisor interrumpido sin codigo de salida persistido no convierte un
  intento en valido por encontrar solamente `CURVE_COMPLETE` en su log.
- Fallos de cierre, configuracion o proceso ajenos a estos marcadores detienen
  la ejecucion para revision. No se aceptan cierres parciales.

## Efecto sobre las medias

Las medias conservan los prefijos fijos 0..2, 0..4 y 0..9; no se rellenan
faltantes ni se reemplazan por otras semillas de bot. Se usa la misma cohorte
de niveles con diez pares validos para los tres prefijos y las diferencias
bootstrap. Con esta unidad de validacion, agotar un lote excluye cinco niveles
enteros y sus 50 pares de los tres prefijos. Los denominadores por nivel siguen
siendo 3, 5 y 10: se reduce el numero de niveles, no las repeticiones por media.

El informe enumera las exclusiones, muestra cuantos niveles quedan y advierte
que la seleccion por limpieza puede sesgar la muestra. IC 95 %: bootstrap
pareado por nivel, 2000 remuestreos, semilla 20261031, conservando ambos entornos
y todos los prefijos juntos. No es un veredicto de aceptacion.

## Nueva comparacion y configuracion

Salida independiente: `tiered_comparison_20261031_02`. Solo se comparan los
primeros intentos limpios completos: 100 archivos de partidas y cinco mapas.
Los rechazados quedan archivados y registrados, pero no determinan la igualdad.
Se exige evidencia de los 50 pares limpios en ambas configuraciones antes de
lanzar la curva completa; una comparacion incompleta no pasa por vacuidad.

Si los resultados completos son identicos, se elige la configuracion con menos
lineas invalidantes en todos sus intentos, contando una sola vez una linea que
coincida con varios marcadores. Se registran aparte GCHandle, Illegal instruction
y ERROR: System. En empate se conserva la variable ausente. La seleccion es
operativa; no demuestra una tasa causal de excepciones menor.

La curva completa nueva se guarda en `repetition_curve_20261031_02`. El ejecutor
genera automaticamente `analysis.json` y `README.md` al terminar, incluso con
exclusiones. El estado y los logs permiten auditar y reanudar la ejecucion.
