# Diagnóstico único de 610006:0

**Aceptación: no aprobada. V3 permanece experimental.** La única reproducción
real volvió a terminar por timeout a los 600,00006 s virtuales, con 4/12 salas
visitadas y 3 despejadas. El resultado completo coincide con el registrado en
aceptación. No hubo GCHandle ni otros errores; solo la línea exacta de
certificados permitida al arranque, una vez por canal.

**Clasificación: enemigo inalcanzable para la política de alineación del bot.**
La inaccesibilidad física del enemigo no está demostrada. El bloqueo observado
es una oscilación sin progreso frente a un tirador en una esquina, coherente
con la interacción entre margen de pared y alineación cardinal del bot.

| Tiempo virtual | Evidencia en la sala (−2,0), `mixta_trinchera` |
|---|---|
| 22,72 s | Entrada; puertas abiertas durante la transición |
| 27,72 s | Combate; quedan Exploder y Shooter; puertas cerradas |
| 32,72–597,72 s | Solo Shooter, inmóvil en (7,298; −12,329), vida 1 |
| 600,00 s | Timeout; sala sin despejar y jefe no alcanzado |

En las 114 muestras desde 32,72 s el bot sigue en la misma sala: X entre
1,459 y 2,167; Z entre −11,455 y −10,548. Recorre entre 12,5 y 18,8 m por
ventana de cinco segundos, pero dentro de esa zona pequeña: oscila, no está
quieto. Ninguna de esas muestras registra disparo ni colisión deslizante del
jugador. El tirador conserva posición y vida. Las tres puertas permanecen
cerradas con colisión activa, coherentemente con el enemigo vivo; no se
observa una sala despejada cuya puerta se niegue a abrir.

La lectura del código existente explica el patrón: `ChooseSpot` limita el
punto deseado a ±(5,5; 10,5) m en esta sala por `wall_margin=2`; `AimAt` sigue
exigiendo alineación cardinal. En las muestras, la separación mínima en Z
respecto al tirador es 0,874 m; aun con el máximo ruido favorable de 0,15 m,
queda por encima de la tolerancia de 0,60 m. La separación en X también
impide disparar en el otro eje. Esta inferencia concuerda con la ausencia de
progreso; no demuestra que un humano u otra política no puedan despejarla.
No hay evidencia para clasificarlo como dificultad real ni como inmovilización
física del bot. No se hicieron intervenciones para probar un contrafactual.

Se reutilizó por reflexión el cuerpo original de `FidelityV3Check.PlayReal`,
con su monitor, bot y configuración. Mapa, parámetros, semilla efectiva 610006
y semilla de bot 8141311271217602447 se comprobaron contra aceptación.
Observador separado en `ai-training/diagnostics/`, sin cambios en fuentes
existentes ni protocolos. Implementación y preparación: 6 min 14 s; ejecución
inferior a 21 s de pared. Un trabajador, solo headless, una partida real,
cero partidas simuladas y cero reintentos; no hubo compilaciones durante ella.

Límites: traza cada cinco segundos y en cambios de sala; no excluye sucesos
entre muestras. El proceso y ensamblado incorporan el observador y no recrean
las 50 partidas previas ni el combate simulado anterior. La coincidencia de
este único resultado no prueba determinismo universal. No se usó la reserva
para combates, no se corrigió nada y no se lanzó otra partida después.

`evidence.json` conserva cifras, hashes y procedencia. `raw_trace.zip` incluye
los 122 registros de traza, resultado, manifiesto, logs completos, lanzamiento
y copia del observador; `sha256.json` verifica cada miembro. Los protocolos,
informes anteriores y veinte hashes históricos siguen intactos.
