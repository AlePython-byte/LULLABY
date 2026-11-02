# Fidelidad v2: preflight

Resultado: preflight_clean_not_a_quality_gate. Pares seleccionados: 1.
Un trabajador, solo headless, simulador y juego real secuenciales; nucleo original intacto.
El preflight no ejecuta ni autoriza aceptacion o reserva y no estima fidelidad con un solo nivel.

Excepcion exacta conservada: `ERROR: Failed to read the root certificate store.`. Maximo una por canal, antes de FIDELITY_V2_BEGIN.
Cualquier otro ERROR:, GCHandle, Illegal instruction, salida fallida o cierre incompleto invalida.

- Lote 600001, intento 1, canal log: certificado permitido 1; incidencias bloqueantes 0.
- Lote 600001, intento 1, canal engine_log: certificado permitido 1; incidencias bloqueantes 0.

analysis.json conserva resultados, procedencia, reglas, autorizacion y todos los incidentes por canal.

## Evidencia del unico par autorizado

Semilla solicitada 600001, repeticion 0, un intento, un proceso headless de
medicion, un trabajador. DOTNET_TieredCompilation ausente. No se genero cobertura
de aceptacion o reserva ni se ejecutaron sus combates. No hay archivo de
autorizacion de ninguna de esas muestras.

| Entorno | Dano | Tiempo de partida (s) | Salas visitadas/despejadas | Timeout |
|---|---:|---:|---:|---|
| Simulador | 6 | 96,03334 | 12/12 | No |
| Juego real | 10 | 94,41667 | 12/12 | No |

Los objetos result completos de ambos entornos son identicos a los de
600001:0 de la curva final, no solamente estos agregados. El par confirma el
circuito de protocolo, generacion, simulacion, PlayReal original, cierre,
hashes, doble log y analisis. No estima Spearman ni prueba la fidelidad con
un unico nivel; sus diferencias de dano no son una puerta estadistica.

En ambos logs la linea exacta del certificado esta en la linea 3, antes de
FIDELITY_V2_BEGIN (linea 5). Hay una aparicion por canal, cero errores no
exceptuados, cero GCHandle y cero Illegal instruction. La linea no se suprime
ni se cambia su severidad en los archivos originales. La excepcion es local
a este ensayo sin red: no afirma que TLS funcione en el equipo.

La enmienda de certificados esta en c4b0767; el arnes y analizador, en 9b6b2a8.
Pasaron 29 pruebas sinteticas nuevas y 13 del analizador original. Ninguna de
ellas ejecuta Godot. Las compilaciones desde git archive son independientes.
Se verificaron los 20 SHA-256 historicos y los hashes del nucleo de la curva;
no se modificaron simulador, bot, escenas, colisiones, PlayReal ni el protocolo
original v3_fidelity.json.

raw_results.zip conserva el manifiesto, protocolo aplicado, parametros,
fingerprints, mapas, resultados, recursos, estado y ambos logs completos.
sha256.json verifica cada miembro y analysis.json registra el hash del ZIP.
La enmienda exacta autorizada sustituye el bloqueo previo por el certificado;
no permite otros errores ni autoriza automaticamente aceptacion. El benchmark
sigue planificado para 300 semillas, sin recorte.

Aceptacion 610001..610090 y reserva 620001..620090 permanecen sin ejecutar.
La ejecucion futura requiere OK explicito, confirmacion de equipo libre y un
archivo externo de autorizacion para ese modo y SHA-256 de protocolo. No se
crea ese archivo como consecuencia de este preflight.
