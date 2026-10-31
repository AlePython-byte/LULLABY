# Diagnóstico v3: ruido, residuos e incidente de carga

Estado: análisis de los datos originales terminado; **pendientes las tres
repeticiones de 600019:1 y la confirmación de equipo libre solicitada**.
No se aplicaron correcciones al simulador, bot o juego real. No se modificó el
protocolo ni sus umbrales. No se ejecutaron aceptación ni reserva.

Trabajo iniciado el 31 de octubre de 2026 a las 18:03:54 UTC, con límite de unas
cuatro horas. El análisis y preparación hasta las 18:20:47 UTC consumieron unos
17 minutos de pared, incluido preparar y compilar la escena de diagnóstico desde
`git archive`. El cierre documental es posterior. La espera de confirmación no
se presenta como trabajo realizado.

## 1. Ruido entre repeticiones

Spearman del daño sobre los mismos 30 niveles:

| Entorno | rep0 vs rep1 | rep0 vs rep2 | rep1 vs rep2 |
|---|---:|---:|---:|
| Juego real | **0,266** | **0,292** | **0,766** |
| Simulador | **0,670** | **0,551** | **0,582** |

IC 95 % exploratorios, bootstrap pareado por nivel, 2000 remuestreos:

| Entorno y comparación | IC 95 % |
|---|---|
| Real 0–1 | [−0,106; 0,586] |
| Real 0–2 | [−0,095; 0,637] |
| Real 1–2 | [0,563; 0,874] |
| Simulador 0–1 | [0,373; 0,858] |
| Simulador 0–2 | [0,212; 0,792] |
| Simulador 1–2 | [0,263; 0,797] |

**Hallazgo: las etiquetas de daño tienen una variación importante entre
repeticiones, también dentro del propio juego real.** Dos de las tres
comparaciones reales están muy por debajo de 0,7; la tercera lo supera. No
corresponde atribuir el 0,528 observado a un fallo demostrado exclusivamente del
simulador.

Hay dos precisiones necesarias para interpretar este hallazgo:

1. Las repeticiones 0, 1 y 2 usan **semillas distintas del bot**, por diseño. Por
   tanto, esto mide variación de la etiqueta al cambiar la reacción y puntería
   del bot; no prueba que Godot sea irreproducible con la misma secuencia de
   entradas. La semilla del bot también está ligada al orden de ejecución, por
   lo que estos datos no separan ruido aleatorio de posibles efectos de orden.
2. El criterio congelado compara **medias de tres partidas por nivel**, no una
   partida contra otra. Una correlación baja entre partidas individuales no
   demuestra un techo estricto inferior a 0,7 para esas medias.

Como aproximación exploratoria de teoría clásica de medición, un modelo aditivo
de errores independientes da ICC(1,1) de 0,368 en el juego real y 0,531 en el
simulador; para la media de tres partidas, ICC(1,3) de **0,636** y **0,772**.
Si además se supone una dificultad latente idéntica y errores independientes
entre entornos, `sqrt(0,636 × 0,772) ≈ 0,701` es un proxy del máximo de
**Pearson** observable entre las medias. No es una cota demostrada de Spearman y
se estima con solo tres repeticiones por nivel.

Conclusión: el umbral de 0,7 queda muy cerca de ese proxy de ruido y debe
investigarse la fiabilidad de la medición. **No está demostrado que sea
matemáticamente inalcanzable**, ni que todo el desacuerdo sea ruido. El 0,528
sigue por debajo del criterio original y no habilita aceptación; cambia la
interpretación del hallazgo, no los umbrales ni los resultados publicados.
No se descartó rep0 ni se eligió la comparación más favorable.

## 2. Los cinco mayores residuos de daño

Ordenados por `abs(media_sim − media_real)`; desempate por semilla solicitada.
Las semillas efectivas, composición completa y residuos por sala están en
`noise_residuals.json`.

| Semilla | Daño sim | Daño real | Δ sim − real | Salas | Enemigos | Voladores | Excavadores |
|---|---:|---:|---:|---:|---:|---:|---:|
| 600022 | 13,67 | 7,33 | **+6,33** | 11 | 38 | 4 | 4 |
| 600027 | 8,67 | 14,00 | **−5,33** | 11 | 39 | 5 | 2 |
| 600018 | 8,33 | 12,67 | **−4,33** | 12 | 37 | 2 | 7 |
| 600003 | 7,33 | 10,67 | **−3,33** | 12 | 33 | 2 | 1 |
| 600015 | 11,00 | 7,67 | **+3,33** | 11 | 34 | 4 | 1 |

Los cinco contienen los seis tipos, frente a 21 de los 30 niveles originales.
La mediana de enemigos es 37 frente a 35 en la muestra completa. La mediana del
área media de combate es 446,4 m² frente a 439,15 m². Las medianas de voladores
(4) y excavadores (2) son iguales en ambos grupos. Son coincidencias
descriptivas de una selección hecha después de observar residuos; no establecen
causalidad ni justifican features o cambios de dificultad.

Tamaños de las salas de combate, incluida la sala histórica del jefe:

- 600022: áreas de 324–676 m²; media 467,6 m²; incluye 18×18, 20×20 y 26×22.
- 600027: 400–676 m²; media 491,6 m²; incluye 16×26, 20×20 y 26×22.
- 600018: 256–676 m²; media 446,4 m²; predominan cuatro salas de 16×26.
- 600003: 256–676 m²; media 374,8 m²; cuatro salas de 18×18.
- 600015: 324–676 m²; media 412,9 m²; cinco salas de 18×18.

Una pista más concreta aparece al localizar el residuo por sala:

- `mixta_tiradores`, 20×20, dos básicos y dos tiradores: aporta +2,33 de daño
  medio en 600022, −3,33 en 600027 y −2,67 en 600003.
- `mixta_variada`, 28×16, con básico, tirador, explosivo y volador: aporta −3,67
  en 600018. **No tiene excavador.**
- En 600015 destacan una `normal_cuadrada`, 18×18 y tres básicos (+1,33), y el
  jefe histórico de seis básicos (+1,33).

Los residuos grandes no se restringen al huidor o excavador; también aparecen
con los tipos históricos y con solo básicos. La misma plantilla presenta ambos
signos. No hay soporte para corregir velocidades o vida de un tipo de enemigo
ni para culpar al excavador por su mera presencia.

## 3. Incidente de 600019, repetición 1

Semilla efectiva: **607938**. Semilla del bot: **6401991111650100716**.
Las dos excepciones del log original son del mismo tipo y mensaje:

1. Línea 175: `System.InvalidOperationException: Handle is not initialized.`
2. Línea 194: `System.InvalidOperationException: Handle is not initialized.`

Ambas pasan por `GCHandle.FromIntPtr` y
`Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType`, después por la carga de
recursos desde `EnemyKinds.Scene`, `RoomController.EnemySceneFor` y
`RoomController.StartCombat`. No son dos fallos distintos identificados por el
log. El mensaje inicial del almacén de certificados es otro incidente separado.

Búsqueda sin distinguir mayúsculas:

- `gchandle`: **6 líneas**, tres por cada excepción, contando pila y backtrace.
- `Illegal instruction`: **0 coincidencias en este log**.

El par original terminó con daño **10/10** (sim/real), tiempo
**77,96667/78,083336 s**, 11 salas visitadas y despejadas y cero timeouts.
Esto confirma su finalización, **no que el error carezca de efecto**. No se
guardaron trazas por fotograma ni un censo de apariciones real suficiente para
demostrar que todo enemigo cargó y actuó exactamente igual. El par se conserva;
no se excluye ni se sustituye para subir Spearman.

### Tres repeticiones preparadas, pendientes de ejecución

`tests/FidelityV3RepeatPair.tscn` fija 600019 y repetición 1 y hace tres intentos
consecutivos, con un trabajador. Verifica los hashes del código y recursos
originales y reutiliza el cuerpo sin cambios de `FidelityV3Check.PlayReal`
mediante una invocación diagnóstica por reflection. No retiene recursos de forma
especial, no fuerza recolección de basura y no modifica las reglas del juego.

Los tres intentos prueban el mismo par y la misma semilla del bot. **No recrean
las 55 partidas previas al incidente**; si no fallan, eso no descarta un fallo
que dependa del estado acumulado o sea infrecuente. No se amplía automáticamente
la prueba a esas 55 partidas.

Al iniciar el diagnóstico había un proceso de Godot activo. Se solicitó
confirmación de editor cerrado y equipo libre antes de ejecutar; no se lanzó
la escena de repetición ni se abrió el editor. La estimación comunicada fue de
30–60 s y 250–500 MiB de RAM, dejando 1 GiB disponible. La escena compiló desde
`git archive`, pero su ejecución permanece pendiente de esa confirmación.

Comandos previstos, solo tras confirmación, desde la raíz:

```powershell
dotnet build
godot --headless --fixed-fps 60 --path . res://tests/FidelityV3RepeatPair.tscn -- --out-dir=ai-training/reports/v3/diagnosis_20261031_01/repeats
```

## 4. Correcciones propuestas, ninguna aplicada

**A. Alinear la fase de impactos de áreas en `LevelSimulatorV3`.**
Encolar las colisiones de lágrimas y proyectiles detectadas en un paso y
aplicar sus efectos en la fase del siguiente paso que corresponda a las señales
de `Area3D`, con orden estable de creación y baja. La aproximación actual aplica
los impactos en el mismo paso. Hipótesis: un fotograma de diferencia al matar
básicos o activar invulnerabilidad amplifica las divergencias posteriores de
contacto. Validar primero la fase exacta con una traza dirigida; no ajustar daños,
cadencia ni tamaños. Es la opción más acotada para ensayar una vez autorizada.

**B. Objetivo estable en `BotBrainV3`.**
Añadir un identificador estable por aparición a las observaciones y conservar
el objetivo mientras siga siendo válido; cambiarlo al morir, enterrarse o
aparecer una amenaza de prioridad superior. Hoy se vuelve a elegir el más
cercano dentro de la prioridad en cada paso, sin memoria de objetivo.
Mantener la percepción retrasada, ruido de puntería y prioridades existentes.
Hipótesis: reducir alternancias amplifica menos diferencias pequeñas entre
trayectorias y reduce variación de daño. Cambia la política de medición y exige
una nueva versión del bot y de sus futuras etiquetas; no es un bot novato.

**C. Resolver movimiento por primer impacto y deslizamiento en `LevelSimulatorV3.Move`.**
Sustituir el movimiento X-luego-Z por barrido de la caja, primer tiempo de choque
y deslizamiento iterativo; leer y reproducir el margen real del cuerpo sin
alterar colisiones ni escenas. Hipótesis: aproxima mejor `MoveAndSlide`, esquinas
y contactos en grupos de básicos. Tiene mayor alcance y riesgo: conviene
reservarla para cuando una traza demuestre una discrepancia de deslizamiento,
en lugar de calibrar contactos con un multiplicador global.

Ninguna hipótesis está demostrada por los agregados. No se han implementado A,
B o C, ni se ha parcheado la carga de recursos. **Se espera autorización antes
de aplicar cualquier corrección.**

Si se autoriza una corrección, se conservará el protocolo actual y se congelará
otra versión con los mismos umbrales, identificadores y hashes nuevos del código
corregido. Según la instrucción recibida, la aceptación posterior se evaluará
con **620001..620090**, no con 610001..610090. No se ha abierto ninguna muestra
de aceptación.

## Verificación y artefactos

`noise_residuals.json` conserva matrices de daño, correlaciones, intervalos,
aproximación ICC, composición de los cinco niveles, residuos por sala y bloques
del log. Se verificaron todos los hashes del ZIP original antes del análisis.
Tres pruebas sintéticas verifican casos límite de fiabilidad y búsqueda de log;
la herramienta diagnóstica compiló desde una instantánea de `git archive`.
Los 20 hashes históricos siguen intactos; no hubo cambios en escenas, enemigos,
simulador, bot ni protocolo congelado.

Reproducción del análisis sin partidas, en una salida nueva:

```powershell
python ai-training/validation/diagnose_v3.py ai-training/reports/v3/development_20261031_01/raw_results.zip build/v3_noise_residuals_reproduced.json
python -m unittest discover -s ai-training/validation -p test_diagnose_v3.py -v
```
