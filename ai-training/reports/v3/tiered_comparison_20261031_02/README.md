# Comparacion de configuraciones con intentos limpios

Cinco niveles (600001..600005), diez repeticiones por entorno, un trabajador, procesos headless secuenciales.
default: variable DOTNET_TieredCompilation ausente. disabled: DOTNET_TieredCompilation=0.

Resultado: IDENTICOS.
Archivos comparados: 105 de 105 (100 partidas y cinco mapas). Solo se comparan los primeros intentos completos limpios.
Los datos rechazados se conservan en results.zip y se enumeran; nunca se consideran evidencia de igualdad ni entran en las medias.

| Configuracion | Intentos | Lineas GCHandle | ERROR: System. | Illegal instruction | Lineas invalidantes sin duplicar |
|---|---|---|---|---|---|
| default | 1 | 0 | 0 | 0 | 0 |
| disabled | 1 | 0 | 0 | 0 | 0 |

Los recuentos incluyen pila y backtrace: son lineas, no excepciones independientes. Distintos numeros de intentos y partidas expuestas impiden inferir una reduccion causal de excepciones.
Comparacion anterior: 0 lineas GCHandle sin variable (un intento), 16 con valor 0 (dos intentos: 1 fatal + 15 de cinco excepciones no fatales). El par 600005:2 se contamino con valor 0.

Configuracion elegida: default, por menor numero total de lineas invalidantes; empate a favor de default.

## Configuracion default

## Validez, exclusiones y medias

Se acepta el primer intento completo cuyo log entero no contiene GCHandle, Illegal instruction ni ERROR: System. (sin distinguir mayusculas), con salida cero.
Un error invalida todo el intento del lote de cinco niveles. Se conservan todos los logs y resultados; maximo tres intentos por lote.
Los 50 pares de un lote sin intento limpio se excluyen explicitamente. Incluso los pares anteriores al error quedan invalidados por la regla del log completo.
Las medias usan exactamente las primeras 3, 5 y 10 repeticiones de cada nivel incluido. No se reduce el denominador, no se imputan datos y no se eligen otras semillas de bot.
Se usa la misma cohorte de niveles con diez pares validos para los tres prefijos y sus diferencias bootstrap. Las exclusiones reducen la muestra y pueden sesgarla; el resultado describe solo la cohorte retenida.

Pares excluidos: 0. Niveles incluidos: 5 de 5.

## Intentos e incidentes

- Lote 600001..600005, intento 1: complete; salida 0; motivos []; log `execution\batch_600001_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`

## Configuracion disabled

## Validez, exclusiones y medias

Se acepta el primer intento completo cuyo log entero no contiene GCHandle, Illegal instruction ni ERROR: System. (sin distinguir mayusculas), con salida cero.
Un error invalida todo el intento del lote de cinco niveles. Se conservan todos los logs y resultados; maximo tres intentos por lote.
Los 50 pares de un lote sin intento limpio se excluyen explicitamente. Incluso los pares anteriores al error quedan invalidados por la regla del log completo.
Las medias usan exactamente las primeras 3, 5 y 10 repeticiones de cada nivel incluido. No se reduce el denominador, no se imputan datos y no se eligen otras semillas de bot.
Se usa la misma cohorte de niveles con diez pares validos para los tres prefijos y sus diferencias bootstrap. Las exclusiones reducen la muestra y pueden sesgarla; el resultado describe solo la cohorte retenida.

Pares excluidos: 0. Niveles incluidos: 5 de 5.

## Intentos e incidentes

- Lote 600001..600005, intento 1: complete; salida 0; motivos []; log `execution\batch_600001_attempt_1.log`.
  - Linea 3, par None: `ERROR: Failed to read the root certificate store.`
  - Linea 39, par {'seed': 600001, 'run': 6, 'phase': 'real'}: `WARNING: Jolt Physics job system exceeded the maximum number of jobs. This should not happen. Please report this. Waiting for jobs to become available...`

Protocolo y plan originales intactos. results.zip conserva todos los intentos, resultados, recursos, estados y logs; comparison.json registra su SHA-256.
