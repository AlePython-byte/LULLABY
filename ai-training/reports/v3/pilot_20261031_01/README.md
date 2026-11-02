# Piloto de fidelidad v3: 600001, repetición 0

Piloto completado el 31 de octubre de 2026, tras confirmar equipo libre y editor
cerrado. Se ejecutó únicamente un par: una partida simulada y una real, en
secuencia, con un trabajador. No se ejecutaron las muestras completas de
desarrollo, aceptación ni reserva, ni se cambiaron código o criterios.

Semilla solicitada y efectiva: **600001**. Repetición: **0**. Semilla del bot:
**208151688774401750**. El nivel tiene 12 salas y los seis tipos: 19 básicos,
6 tiradores, 7 explosivos, 6 voladores, 1 huidor y 1 excavador.

## Resultado funcional y diferencias observadas

| Medida | Simulador | Juego real |
|---|---:|---:|
| Daño total | 6 | 10 |
| Daño en camino crítico | 5 | 8 |
| Tiempo de partida, segundos de juego | 96,033 | 94,417 |
| Tiempo hasta el jefe, segundos de juego | 83,533 | 83,150 |
| Timeouts | 0 | 0 |
| Salas visitadas y despejadas | 12 | 12 |

No se detectaron atascos ni salas pendientes. Las dos partidas terminaron.
El bot juega con vida ilimitada: `would_die=true` solo indica que el daño
acumulado supera la vida base, no una muerte que haya truncado la medición.

Todo el daño recibido fue por contacto de enemigos básicos: 6 impactos en
simulación y 10 en el juego. La diferencia se reparte entre cuatro salas, incluida
la del jefe histórico: tres salas con 0 frente a 1 y jefe con 5 frente a 6.
No hubo impactos de proyectil ni explosión en ninguno de los dos entornos.

Sesgo de contacto de este par: **−40 %**. Diferencia relativa de tiempo: **+1,71 %**.
El sesgo de contacto observado está fuera del ±25 % registrado, y debe vigilarse
en desarrollo. No se cambia el bot, la física ni los umbrales a partir de este dato.

**Este piloto no aprueba ni rechaza la fidelidad global.** Con un solo nivel no
se puede calcular Spearman. El intervalo bootstrap de un único nivel es
degenerado y no estima la incertidumbre entre niveles; no debe interpretarse como
un intervalo poblacional. Tampoco hay evidencia para validar daño por proyectiles
o explosiones. `analysis.json` conserva explícitamente el estado
`incomplete_not_a_quality_gate`; sus verificaciones por fuente son diagnósticas.

## Recursos medidos

| Medida | Resultado |
|---|---:|
| Simulación, tiempo de pared | 0,143 s |
| Partida real headless, tiempo de pared | 3,211 s |
| Suma de los dos entornos | 3,354 s |
| Ejecución interna completa, incluida cobertura y preparación | 6,316 s |
| Memoria residente máxima del proceso | 192,61 MiB |
| Memoria residente al terminar | 180,81 MiB |
| Memoria privada al terminar | 138,12 MiB |

La compilación previa tardó 10,62 s; no está incluida en los 6,316 s del motor.
La ejecución headless procesó 94,4 segundos de juego en 3,21 segundos de pared,
manteniendo 60 pasos de física por segundo simulado y `TimeScale=1`.
La estimación preliminar de 2–15 minutos para el piloto resultó conservadora.

## Estimación para desarrollo, sin lanzarlo

Desarrollo requiere 30 niveles × 3 repeticiones = **90 pares**, 180 partidas en
total. Una extrapolación directa de este par da **301,9 s, unos 5 minutos** para
los dos entornos, más inicialización y comprobaciones entre pares.

Ventana recomendada: **10–15 minutos** con un trabajador. Memoria provisional:
**250–500 MiB residentes; reservar 1 GiB disponible**. La estimación parte de un
único nivel: no demuestra estabilidad de memoria durante 90 pares ni descarta
combates más largos. La herramienta registra memoria y tiempos después de cada
par. No se propone paralelismo ni se multiplica la memoria del piloto por 90,
porque los entornos se ejecutan secuencialmente y se liberan entre partidas.

Recomendación: medir desarrollo completo con el mismo código y criterios para
conocer el sesgo agregado y la variabilidad. Antes de lanzarlo se requiere una
nueva confirmación de equipo libre. Sus resultados se reportarán antes de
proponer aceptación. Si aparece un atasco o un timeout, detenerse y reportarlo.

## Artefactos y comprobaciones

- `manifest.json`: versiones, parámetros, un trabajador y hashes del código y
  recursos empleados, guardados antes del combate.
- `level_600001.json`, `sim_600001_0.json`, `real_600001_0.json`: mapa y resultados
  completos, incluidos daño por fuente y primeras visitas.
- `resources_600001_0.json` y `status.json`: tiempos, memoria y cierre del piloto.
- `coverage.json`: prechequeo de mapas de las tres muestras, sin combates de
  aceptación ni reserva.
- `analysis.json`: diagnóstico del par con los criterios preregistrados.

El análisis verificó todos los hashes del cierre, el emparejamiento de semillas
y las salas despejadas. El protocolo permanece intacto y no se tocaron escenas,
enemigos ni artefactos históricos. No se generaron etiquetas de entrenamiento.
