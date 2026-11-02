# Reproducción del primer lote con el equipo en reposo

Se ejecutaron tres intentos independientes de 610001..610005, diez repeticiones
0..9 por nivel y entorno, un solo trabajador y solo headless. No hubo
compilaciones durante la reproducción. Se reutilizaron el ensamblado y las
fuentes congelados de la aceptación anterior; DOTNET_TieredCompilation y
DOTNET_gcConcurrent ausentes. Ningún combate de reserva.

| Intento | Líneas GCHandle | Contexto activo | Resultado del intento |
|---|---:|---|---|
| 1 | 0 | Ninguno | Limpio, 50 pares verificados |
| 2 | 12 | 610004:0, real | Inválido; cuatro líneas ERROR: System. |
| 3 | 6 | 610003:0, real | Inválido; dos líneas ERROR: System. |

Los recuentos coinciden en el log principal y el nativo; no se suman los
canales duplicados. Los tres procesos completaron los 50 pares y cerraron con
código 0. Cada canal contiene una sola línea exacta de certificados permitida
al arranque. Los errores restantes invalidan el intento entero. Los registros
conservan todos los avisos y trazas, asociados al contexto activo.

El fallo no aparece siempre en el mismo par: un intento quedó limpio y los
otros fallaron en pares distintos. Es un patrón esporádico en esta muestra,
no prueba de aleatoriedad ni de una causa concreta. El error persiste con el
equipo en reposo; no se hizo un contraste controlado con y sin compilaciones,
por lo que no se puede atribuir causalmente el incidente anterior a ellas.

Esto activa la ruta autorizada de enmienda a seis intentos por lote con pausa
de diez segundos y aceptación completa nueva. No activa la comparación con
DOTNET_gcConcurrent=0, condicionada a fallos sistemáticos en los mismos pares.
No se calcularon criterios de fidelidad con estas partidas y ninguna se
incorpora a la nueva aceptación. Las ejecuciones anteriores quedan intactas.

analysis.json detalla los incidentes por canal y las líneas con contexto.
raw_results.zip conserva todos los datos, estado, progreso, autorización,
ejecutor de reproducción y copia exacta del protocolo anterior a la enmienda.
sha256.json permite verificar cada miembro; el SHA-256 del ZIP está en
analysis.json. El patrón se fijó como sistemático únicamente si los tres
intentos eran inválidos y al menos un par con GCHandle se repetía en los tres.
