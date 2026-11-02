# Etapa 1: simulador y bot v3

Estado: terminada. La etapa 2 no se ha iniciado. No hay medidas de fidelidad v3,
etiquetas de entrenamiento, features v3 ni modelo nuevo.

## Tiempo medido

Implementación y validación técnica: 2026-10-31 01:15:37 a 02:54:04 UTC
(2026-10-30 20:15:37 a 21:54:04 en Bogotá): **1 h 38 min 27 s = 1,64 h**.
Es tiempo de pared, incluye lecturas, compilaciones, pruebas y esperas; no representa
horas humanas de dedicación. El cierre documental y su commit son posteriores a esa ventana.

## Cambios

- `scripts/ai/simulation/v3/GameplayParametersV3.cs`: parámetros leídos de escenas,
  incluyendo Dash, Dig y la velocidad efectiva del proyectil del excavador.
- `SimLevelV3.cs` y `RunRecorderV3.cs`: mapa para simulación y registro separado,
  con seis posiciones para el contacto por tipo de enemigo.
- `LevelSimulatorV3.cs`, `LevelSimulatorV3.Behaviors.cs` y `SimulationFrameV3.cs`:
  simulación versión 4, control dirigido y trazas opcionales sin intervenir en decisiones.
- `BotBrainV3.cs`: política competente versión 1. Prioriza explosivo cercano,
  tirador o excavador vulnerable, huidor, volador y resto. Los empates se resuelven
  por distancia y orden. Percibe vulnerabilidad y avisos con la misma historia
  retrasada de posiciones y proyectiles; no conoce la dirección interna de la embestida.
- `BotDriverV3.cs`: conecta ese cerebro al jugador real. Libera entradas y
  suscripciones al terminar o cancelar; el límite de tiempo también funciona si no hay sala.
- Pruebas nuevas: `SimulationV3Test` y `BotDriverV3Test`.
- `tests/PrepareRuntimeModelsV3.gd`: prepara únicamente la caché local de modelos
  mediante GLTFDocument en ejecución, para un worktree sin importaciones previas.
  No inicia el editor y no altera assets, escenas ni metadatos `.import`.

Las clases nuevas parten de una copia aislada del código histórico. Se reutilizan
BotSettings, BotAction, BotInputApplier, Pcg32 y RoomLayout sin modificarlos.
No se cambiaron archivos existentes del juego, las herramientas históricas ni los
artefactos v1/v2. El menú y el generador por defecto permanecen como estaban.
No existe perfil novato.

## Reglas de los enemigos replicadas

Huidor: Dash decide antes que KeepDistance; fija dirección al iniciar el aviso;
se detiene durante el aviso y embiste hasta contacto, bloqueo o tiempo. El
retroceso de EnemyBase excluye movimiento normal y actualización de comportamientos.
KeepDistance solo evalúa bloqueo si decidió el movimiento de ese fotograma.

Excavador: fuera, hundiéndose, bajo tierra y emergiendo. Solo es invulnerable bajo
tierra; en esa fase no bloquea al jugador, enemigos ni lágrimas y se mueve contra
el mundo. Se replica la órbita en XZ, el recorte al interior, el tiempo mínimo y
la distancia de emergencia, y la emergencia forzada al vencer el tiempo máximo,
con el mismo orden de esquinas y desempate. Su proyectil aplica el override de
velocidad solo cuando es positivo, como EnemyProjectile.FireFrom.

## Validación

| Prueba | Resultado |
|---|---|
| `dotnet build` del worktree | Correcto, 0 advertencias, 0 errores |
| Compilación aislada desde `git archive` | Correcta para cada commit de implementación y protocolo |
| `SimulationV3Test` | 135 comprobaciones correctas |
| `BotDriverV3Test` | 20 comprobaciones correctas |
| `EnemyBehaviorTest` histórico | 13 casos, 43 comprobaciones correctas |
| `HistoricalIntegrityTest` intacto | 51 comprobaciones correctas; 20 SHA-256 idénticos |

La prueba del simulador cubre parámetros de las seis escenas, aviso y dirección
de embestida, contacto y retroceso, paredes, cuatro fases del excavador,
vulnerabilidad, órbita, proyectil y emergencia normal/forzada. Comprueba la
percepción retrasada y la equivalencia de cuatro encuentros aislados históricos.
Tres semillas de humo, 710001..710003, se completaron dos veces con resultados
deterministas. Son pruebas funcionales, no una muestra estadística de fidelidad.

La prueba del controlador recorre una transición real hasta una sala con huidor
y otra con excavador; ambas se despejan. Comprueba cancelación y liberación de
movimiento y disparo. Las escenas reales se instancian sin objetos, decoración
ni The Mother, con salud ilimitada; esas opciones solo afectan a las instancias de prueba.

El primer intento del control histórico confirmó los hashes, pero no terminó
porque faltaba la caché importada de modelos del worktree. Se preparó la caché
sin editor y se repitió: las 51 comprobaciones pasaron. Las ejecuciones en sandbox
mostraron además un mensaje del almacén de certificados de Windows, ajeno a las
aserciones; el control histórico fuera del sandbox pasó sin ese mensaje.

## Protocolo y reproducción

`ai-training/protocols/v3_fidelity.json` se guardó en el commit `544fc46`, antes
de ejecutar simulaciones v3. Permanece intacto. Fija Spearman de daño y tiempo
>= 0,7; sesgo de contacto dentro de ±25 %; tiempos por composición dentro de
±30 %; regla de evidencia de 30 impactos para proyectiles/explosión; cero timeouts;
bootstrap pareado por nivel y muestras separadas de desarrollo, aceptación y reserva.
La aceptación exige congelar código, parámetros y hashes antes de sus ejecuciones.

Comandos, desde la raíz, con `godot` apuntando a la consola .NET instalada:

```powershell
dotnet build
# Solo si faltan las importaciones de modelos; nunca abre el editor:
godot --headless --path . --script tests/PrepareRuntimeModelsV3.gd
godot --headless --path . res://tests/SimulationV3Test.tscn
godot --headless --fixed-fps 60 --path . res://tests/BotDriverV3Test.tscn
godot --headless --fixed-fps 60 --path . res://tests/EnemyBehaviorTest.tscn
godot --headless --path . res://tests/HistoricalIntegrityTest.tscn
```

Comprobar previamente que el editor esté cerrado. No se regeneraron datasets
ni benchmarks históricos y no se ejecutaron partidas de las muestras del protocolo.

## Límites y decisión

Las colisiones siguen siendo cajas resueltas por ejes: no reproducen exactamente
Jolt, correcciones de penetración ni esquinas. Los impactos de áreas se adelantan
un fotograma respecto al motor. El tipo que origina un contacto real se atribuye
al enemigo dañino más cercano; con varios cuerpos simultáneos es un diagnóstico
aproximado. La fuente contacto/proyectil/explosión se identifica por la pila de llamada.

No se ha demostrado fidelidad de daño ni tiempos en niveles v3. Los resultados
funcionales no autorizan etiquetado ni promoción del generador a modo normal.
Recomendación: pasar a la etapa 2, comenzando por cobertura y desarrollo según
el protocolo, solo después de revisar este informe y coordinar una ventana para
las ejecuciones largas. Detenerse aquí hasta recibir la indicación correspondiente.
