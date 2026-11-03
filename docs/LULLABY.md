# LULLABY
## Documento maestro del proyecto — Videojuego 3D Roguelike con Inteligencia Artificial

**Nombre del proyecto:** LULLABY  
**Género:** Roguelike / Dungeon Crawler / Horror psicológico  
**Inspiración principal:** The Binding of Isaac: Rebirth  
**Motor de desarrollo:** Godot 4.x  
**Lenguaje principal:** C#  
**Modelado 3D:** Blender  
**Plataforma inicial:** Windows (PC)  
**Modalidad:** Un jugador (Single-player)  
**Cámara:** Vista superior con perspectiva 3D  
**Característica principal:** Generación de niveles variables mediante inteligencia artificial y generación procedural.

**Estado:** Preproducción. La historia, el stack y las características principales están definidos conceptualmente. Todavía no se ha confirmado una implementación funcional.

---

# 1. Descripción general

LULLABY es un videojuego independiente de terror psicológico desarrollado en 3D, inspirado en la estructura roguelike de The Binding of Isaac: Rebirth.

El jugador controla a Noah, un bebé que queda atrapado en un mundo de pesadillas donde los objetos cotidianos, los juguetes y los recuerdos familiares se transforman en criaturas y escenarios aterradores.

El videojuego combinará exploración, combate, generación procedural de habitaciones, objetos con efectos especiales, jefes, rutas alternativas y múltiples finales.

Su característica tecnológica más importante será la implementación de un sistema de **inteligencia artificial para la generación de niveles**, con el propósito de que cada partida presente una distribución diferente de habitaciones, enemigos, desafíos y recompensas.

La intención no es copiar The Binding of Isaac, sino tomar como referencia su estructura de partidas y crear una identidad narrativa, visual y mecánica propia.

# 2. Historia principal

## 2.1. El protagonista

Noah es un bebé que vive junto a su madre en una casa antigua.

Cada noche, exactamente a las 3:17 a. m., Noah comienza a llorar sin razón aparente.

Su madre cree que simplemente tiene pesadillas, pero Noah observa cosas que los adultos no pueden ver.

Cuando todos duermen, los pasillos parecen alargarse, los juguetes cambian de posición y extraños sonidos aparecen detrás de las paredes.

## 2.2. El comienzo

Una noche, Noah escucha una canción de cuna proveniente del armario de su habitación.

La melodía es la misma que su madre suele cantarle, pero esta vez suena distorsionada.

Una voz parece pronunciar su nombre.

El armario comienza a abrirse lentamente.

Noah se acerca gateando y, cuando intenta mirar su interior, cae hacia un mundo desconocido.

Al despertar, descubre que su hogar se ha convertido en un gigantesco laberinto de habitaciones oscuras, juguetes deformados y criaturas aterradoras.

Su único mecanismo de defensa son sus lágrimas.

## 2.3. El misterio

Durante las primeras partidas, el jugador cree que Noah simplemente está viviendo una pesadilla.

Sin embargo, al avanzar comienza a encontrar fotografías, grabaciones, juguetes y recuerdos familiares.

Estos objetos revelan que Noah no fue el primer bebé que durmió en aquella habitación.

Antes existió otro niño.

Su madre aparece sosteniéndolo en fotografías antiguas, pero su rostro ha sido arrancado de casi todas ellas.

Mientras explora, Noah comienza a encontrarse con una criatura conocida como **The First**.

Se trata de un bebé completamente oscuro que aparece ocasionalmente al fondo de algunas habitaciones.

No siempre ataca.

En ocasiones simplemente observa al protagonista.

El misterio principal del videojuego será descubrir quién fue The First, qué ocurrió con él y por qué su existencia fue ocultada.

## 2.4. La historia oculta

La historia se contará mediante narrativa ambiental.

En lugar de utilizar grandes cantidades de diálogos, el jugador descubrirá información a través de los escenarios, objetos, enemigos, escenas secretas y finales desbloqueables.

El primer final mostrará a Noah despertando en su habitación mientras su madre lo abraza.

Sin embargo, al completar determinadas condiciones se desbloqueará una nueva zona llamada **THE MEMORY**.

Este escenario contendrá fragmentos del pasado familiar y permitirá acercarse a la verdadera explicación de las pesadillas.

La identidad de The First, su relación con Noah y el desenlace definitivo todavía están pendientes de desarrollo narrativo.

# 3. Mundos y escenarios

Los niveles estarán inspirados en lugares cotidianos de una casa, reinterpretados desde los miedos de un bebé.

| Mundo | Descripción |
|---|---|
| The Cradle (La Cuna) | Habitaciones infantiles, juguetes deformados y muebles gigantes |
| The Drowned (El Ahogado) | Baños inundados, tuberías y criaturas acuáticas |
| The Feast (El Banquete) | Cocinas oscuras y monstruos hechos de comida podrida |
| The Forgotten (El Olvido) | Sótanos, objetos abandonados y recuerdos familiares |
| The Memory (La Memoria) | Escenario secreto que revela fragmentos de la historia |

Cada mundo tendrá una identidad visual, enemigos específicos, ambientación sonora, objetos y jefes propios.

Las habitaciones podrán presentar diferentes formas, tamaños, obstáculos, trampas, iluminación y posiciones de enemigos.

# 4. Jugabilidad

## 4.1. Mecánicas principales

El videojuego tendrá las siguientes características:

- Movimiento libre sobre un plano tridimensional.
- Cámara superior con perspectiva 3D.
- Combate mediante proyectiles de lágrimas.
- Habitaciones conectadas mediante puertas.
- Enemigos con diferentes patrones de ataque.
- Jefes al finalizar determinados niveles.
- Objetos que modifican las estadísticas del protagonista.
- Combinaciones de objetos con efectos especiales.
- Habitaciones secretas y recompensas ocultas.
- Muerte permanente durante cada partida.
- Desbloqueo progresivo de contenido.
- Múltiples rutas y finales.

## 4.2. Sistema de combate

Noah utilizará sus lágrimas como ataque principal.

Estas podrán cambiar según los objetos adquiridos.

Ejemplos:

**Lágrimas congeladas:** ralentizan a los enemigos.

**Lágrimas explosivas:** generan daño en área.

**Lágrimas oscuras:** atraviesan determinados objetivos.

**Lágrimas múltiples:** permiten disparar varios proyectiles simultáneamente.

**Lágrimas eléctricas:** transmiten daño entre enemigos cercanos.

Estas son propuestas iniciales que deberán probarse y equilibrarse.

## 4.3. Objetos y transformaciones

Los objetos serán fundamentales para la variedad de las partidas.

| Objeto | Efecto propuesto |
|---|---|
| Chupete | Reduce ciertos efectos de miedo |
| Oso de peluche | Proporciona protección temporal |
| Sonajero | Aturde enemigos cercanos |
| Fotografía rota | Aumenta el daño, pero revela enemigos ocultos |
| Biberón | Recupera salud |
| Manta vieja | Proporciona resistencia al daño |

Algunos objetos también modificarán la apariencia física de Noah.

El objetivo es que el jugador pueda construir combinaciones distintas en cada partida.

### 4.3.1. Objetos implementados para el MVP

El MVP adapta las ideas anteriores a cinco efectos sencillos. Los valores se definen en `resources/items/`:

| Objeto | Efecto durante la partida | Forma provisional |
|---|---|---|
| Biberón | Recupera hasta 2 puntos de vida, sin superar el máximo; permanece en el suelo si no puede curar | Cilindro azul |
| Fotografía rota | +25 % de daño de las lágrimas | Caja naranja |
| Chupete | +20 % de cadencia | Esfera rosa |
| Oso de peluche | +1 punto de vida máxima efectiva y actual | Cápsula marrón |
| Manta vieja | −50 % de daño recibido (cada golpe de 1 quita medio corazón; la explosión del explosivo, de 2, quita 1); ningún golpe quita menos de 0,5 | Prisma verde |

Los efectos de resistencia se combinan multiplicándose (cada uno reduce lo que deja el anterior) y ningún golpe puede quitar menos de 0,5 de vida (`PlayerHealth.MinHitDamage`, medio corazón), por muchas mantas que se acumulen; un golpe menor que ese mínimo conserva su valor. Hoy solo hay una Manta y no se duplica. La recogida se realiza por contacto. La curación es instantánea; las cuatro mejoras permanecen hasta morir o empezar otra partida y no se acumulan por duplicado. Los modificadores pertenecen a cada instancia del jugador: los valores base de salud, disparo y colisiones siguen intactos. El Sonajero, los efectos de miedo, revelar enemigos ocultos y la protección temporal quedan fuera de esta implementación.

Cada tesoro ofrece un objeto por defecto. La cantidad se configura en `ItemCatalog.ItemsPerTreasure`, a través de `resources/items/Catalog.tres`. Con cantidades mayores, todas las ofertas son recogibles independientes: todavía no existe «elegir uno de N». En cada tesoro se agota el catálogo antes de repetir un objeto. Volver a una habitación conserva tanto las ofertas pendientes como las ya recogidas, sin cambiar su selección ni permitir recogerlas otra vez.

Las recompensas se asignan después de elegir el nivel, con un RNG propio por semilla de partida y celda del tesoro. El catálogo se ordena por identificador estable; cambiar el orden de visita o la cantidad de ofertas no cambia la primera recompensa de una celda. No se modifican las plantillas ni se consume el azar del generador o del director de IA.

Los objetos del suelo se ven como su modelo 3D (sin etiqueta flotante con el nombre). Al recogerlos aparece unos 2,5 s un mensaje arriba, en el centro (`ItemMessageHud`), con el nombre y el efecto y, debajo, la descripción (por ejemplo «Biberón: recupera 2 puntos de vida»); los textos están copiados de `docs/equipo/textos.md` a `GameText.ItemMessages`. Una curación rechazada (vida llena) no muestra nada. El HUD de objetos muestra los nombres obtenidos; el Biberón queda marcado como usado y agrupa las curaciones repetidas. Morir o reiniciar limpia el inventario, las mejoras y el estado de los tesoros. En compilaciones de depuración, **J sin modificadores** crea junto al jugador el siguiente objeto del catálogo para probarlo; **R** repite la partida desde cero con su semilla y **N** comienza otra. Este atajo no funciona en Release.

Pruebas: `tests/ItemSystemTest.tscn` comprueba efectos, recogidas y parámetros base; `tests/TreasureItemsTest.tscn` comprueba visitas reales, HUD, varias ofertas, reinicio y aislamiento del RNG sobre 200 semillas. Cada recogible emite su señal `Collected`; el registro separado por celda y oferta permite añadir posteriormente grupos de elección sin cambiar los efectos del jugador.

## 4.4. Menú, pausa y fin de partida

El juego arranca en la **intro** (`scenes/ui/IntroScene.tscn`, escena principal del proyecto; ver 8.6) y de ahí pasa al **menú principal** (`scenes/ui/MainMenu.tscn`). `TestLevel.tscn` sigue pudiendo lanzarse directo con F6 y por las pruebas, sin pasar por el menú ni por la intro; «Salir al menú» va directo a `MainMenu`, sin intro.

- **Menú principal:** título y subtítulo, **Jugar**, **Dificultad** (Fácil / Normal / Difícil), **Controles** y **Salir**.
- **Arte del menú** (todo opcional y por nombre, con `ResourceLoader.Exists`: se puede reemplazar un PNG por otro con el mismo nombre y medidas, y si falta no pasa nada):
  - **Carátula animada** (`MenuCover`, `assets/ui/caratula/capa_0_fondo`, `capa_1_cuna_noah`, `capa_2_movil` y `capa_3_particulas`, de 1920×1080): capas a pantalla completa, sin deformarse, con un parallax mínimo (senos de 12 a 18 s desfasados; desplazamientos de ±3, ±6, ±10 y ±16 px). No usa shaders ni partículas y no procesa nada si no hay capas. La capa `capa_4_titulo` no se usa: el título va en el logo.
  - **Logo** (`assets/ui/caratula/logo_lullaby.png`, 952×173): sustituye al título y al subtítulo en texto, porque ya trae el subtítulo en su cinta. Con borde oscuro, se lee sobre el papel claro.
  - **Panel de papel** (`assets/ui/panel_menu_papel.png`, 760×980) detrás del menú. Con panel, el menú pasa a la mitad derecha de la ventana (la cuna de la carátula queda a la vista), se compacta para caber en 648 px de alto, el papel se ajusta al menú conservando más o menos su proporción, los textos pasan a marrón oscuro y el volumen baja a la esquina inferior izquierda. Sin panel, el menú queda centrado como antes.
- **Variedad extendida (experimental):** casilla del menú (`GameSettings.ExtendedVariety`, apagada por defecto y solo durante la sesión) que juega la partida con el generador v3, con seis tipos de enemigo (añade al huidor y al excavador). Está etiquetada como experimental porque el modelo de dificultad no está entrenado para esos dos enemigos: el HUD de la semilla lo avisa (en dos líneas, para que quepa en su etiqueta) y la dificultad mostrada es solo orientativa (ver `docs/IA.md`, 11.6.4). El argumento `--generator=` y la tecla G de depuración mandan sobre la casilla.
- **Dificultad:** la elegida se guarda en `GameSettings.Difficulty` mientras el juego está abierto (no se escribe en disco) y es el objetivo del director de IA (Fácil = `Easy`, Normal = `Normal`, Difícil = `Hard`). La partida la muestra en el HUD de la esquina inferior derecha. Antes solo se podía cambiar con atajos de depuración, que no existen en Release. Para el director, el orden de prioridad es: tecla de depuración, argumento `--difficulty=`, menú, valor del inspector de `RunManager`. Con F6 directo no hay elección del menú y manda el inspector (Normal).
- **Imágenes opcionales:** si existen `assets/ui/logo_lullaby.png` y `assets/ui/fondo_menu.png`, el menú las usa; si no, funciona igual con el título en texto y un fondo de color. Se comprueba con `ResourceLoader.Exists`, así que su ausencia no da error ni en el juego ni en la exportación.
- **Controles** (`ControlsPanel`, también dentro de la pausa), solo las teclas reales: **W, A, S, D** mover, **flechas** disparar lágrimas, **Esc o P** pausa y **mantener Tab** para ampliar el minimapa.
- **Pausa** (Esc o P): congela el juego con `SceneTree.Paused`, de modo que el jefe, los enemigos, los proyectiles, los efectos y los temporizadores se detienen y continúan donde estaban al reanudar. Ofrece **Reanudar**, **Controles**, **Reiniciar partida** (semilla nueva y misma dificultad) y **Volver al menú**. No se abre durante un cambio de sala, la victoria o la derrota. Los retrasos de las salas y del final de partida usan temporizadores que se detienen con la pausa, y reiniciar o volver al menú despausan antes de cambiar de escena.
- **Derrota:** al morir Noah la partida termina una sola vez (Noah queda quieto y se eliminan los proyectiles), y tras un breve fundido el juego **se congela** (`SceneTree.Paused`; la pantalla y el audio siguen procesando) y aparece la pantalla de derrota con los botones **Reiniciar** (Enter; semilla nueva, misma dificultad, objetos y salud reiniciados) y **Salir al menú** (Esc). La pantalla es una hoja de cuaderno (`assets/ui/panel_menu_papel.png`, la del menú principal) con el título «Fin de la nana», una frase y **lo que mató a Noah**; a los lados, dos notas pegadas (`nota_salir.png` a la izquierda y `nota_reiniciar.png` a la derecha) que son los propios botones, con su texto y su tecla dibujados por el juego. Cada imagen es opcional y se busca por nombre: sin las notas, los botones van dentro de la hoja; sin la hoja queda el diseño de siempre (con la misma línea «Te venció: …»). Suena `derrota` al mostrarse (sin archivo no suena nada: no tiene respaldo para no repetir `noah_muere`).
  - **Qué la mató** (`DeathCause`, `DeathCauses`): `PlayerHealth.TakeHit(daño, causa)` guarda la causa del último golpe que hizo daño (`LastCause`) antes de aplicarlo, y se reinicia con la partida. Las causas son Básico, Tirador, Un proyectil (el del tirador), Explosivo (contacto y explosión), Volador, Huidor, Excavador (contacto y sus disparos), The Mother (contacto y proyectiles) y Desconocido (daño directo). No cambia ningún daño ni el simulador.
  - **Textos** (`DeathTexts`): dos frases por causa, elegidas por la semilla; sin causa, las tres de siempre.
  - **Pausa:** usa la misma hoja (`PaperSheet`, 600 px de alto centrada en 1152×648) con Reanudar, Controles, volumen, Reiniciar partida y Salir al menú; sin la imagen, el diseño de siempre.
  - Prueba: `tests/DeathScreenTest.tscn`. Si Noah muere en la pelea contra The Mother, no hay victoria aunque el jefe muera después. Las frases de victoria y de derrota se eligen con la semilla de partida, sin consumir ningún azar.
- **Textos:** están copiados de `docs/equipo/textos.md` a constantes (`GameText`, `MotherTexts`), porque `docs/` no viaja en la exportación y no se lee en ejecución. Los controles del documento (clic izquierdo, interactuar) no existen en el juego y no se muestran.

Pruebas, en headless: `tests/MenuFlowTest.tscn` (menú, imágenes opcionales, carátula, panel y parallax, controles y dificultad hasta el director y el HUD), `tests/PauseTest.tscn` (pausa con el jefe atacando y con el retraso de la sala, Esc y P, casos ignorados, reiniciar y menú) y `tests/DefeatTest.tscn` (derrota, reintentar, menú y recarga de respaldo).

# 5. Jefes principales

**The Mother:** Una representación monstruosa de la figura materna que intenta atrapar constantemente a Noah.

**The Doctor:** Criatura compuesta por agujas, guantes médicos y luces de hospital.

**The Bath:** Una entidad formada por agua que intenta arrastrar al protagonista.

**The Shadow:** Un enemigo que imita los movimientos y determinados ataques del jugador.

**The First:** Personaje misterioso que desempeña un papel fundamental en la historia.

Los jefes podrán disponer de varias fases, patrones de ataque y condiciones especiales de combate.

## 5.1. The Mother (jefe implementado para el MVP)

The Mother es el jefe de The Cradle. Vive en `scenes/bosses/TheMother.tscn` y su lógica en `scripts/bosses/`. Aparece en la sala del jefe del nivel, que conserva el tamaño de su plantilla (26×26 m). La composición histórica de esa sala, seis enemigos básicos (`resources/rooms/templates/jefe.tres`), no se modifica: The Mother entra como un **jefe alternativo** (`RoomManager.BossScene`) que sustituye a los seis básicos solo al jugar. Con ese campo vacío, la sala vuelve a tener los seis básicos y despejarla no termina la partida.

Tiene **180 puntos de vida** (una lágrima base hace 1 de daño) y se mueve a 1.5 m/s al acercarse. Hace un solo ataque a la vez, siempre avisado, y repite este ciclo hasta morir:

| Fase | Qué hace | Duración |
|---|---|---|
| Entrada | Quieta; muestra «Vuelve a la cuna, mi pequeño...» | 1.5 s (la frase se ve 3 s) |
| Salva | Tres proyectiles hacia donde está Noah en el instante de disparar. Antes de cada uno levanta los brazos | Aviso de 0.7 s por disparo, uno cada 1.25 s |
| Pausa | Quieta | 1.0 s |
| Acercamiento | Persigue a Noah hasta tenerlo a 18 m o menos (si ya está más cerca, no hace falta) | Lo que tarde |
| Embestida | Se detiene, marca el recorrido con una franja naranja en el suelo y fija la dirección. Luego carga en línea recta a 8 m/s hasta 6 m; termina antes si choca con Noah o llega al límite de la arena | Aviso de 1.0 s |
| Recuperación | Quieta; después vuelve a la salva | 1.5 s |

- La dirección de la embestida se fija al empezar el aviso, así que se esquiva apartándose durante ese segundo.
- El centro del jefe no se acerca a menos de 3 m de las paredes: la embestida se detiene en ese límite y nunca atraviesa la pared.
- Hace 1 punto de daño por contacto (con 1 s entre golpes) y 1 por proyectil (velocidad 5 m/s, desaparece a los 3 s o al chocar). Noah conserva su segundo de invulnerabilidad tras un golpe.
- La vida del jefe se muestra en la parte inferior de la pantalla mientras dura el combate.

### Fases

La vida (180) decide la fase. Los umbrales son el 66 % (118,8) y el 33 % (59,4). Un golpe grande puede saltarse la fase intermedia: se aplica la más alta alcanzada, con una sola invulnerabilidad.

| Fase | Vida | Cambios respecto a la fase 1 |
|---|---|---|
| 1 | 100 % a 66 % | El ciclo de la tabla anterior |
| 2 | 66 % a 33 % | Tras la salva se desplaza en arco (**órbita**, 1.6 s, radio 6 m, 2.5 m/s) en vez de la pausa quieta. Tras la embestida lanza un **anillo de 10 proyectiles** (5 m/s) con aviso de 0.6 s |
| 3 | 33 % a 0 % | Salva cada 1.0 s, órbita de 1.0 s, **dos embestidas encadenadas** (avisos de 0.7 s y 0.6 s), anillo de 12 proyectiles que **alterna con una espiral** (primero el anillo, luego la espiral, y así) y recuperación de 0.9 s |

- **Cambio de fase:** corta el ataque en curso y deja al jefe quieto e **invulnerable 1.2 s**, parpadeando con el color de aviso, y muestra una frase (`MotherTexts.PhaseTwo` y `PhaseThree`). Los proyectiles ya lanzados siguen su camino. Después hay 0.5 s de recuperación. La victoria funciona también en este instante (`Kill` ignora la invulnerabilidad).
- **Anillo:** el aviso dibuja un círculo rojo en el suelo y una franja azul sobre el **hueco** por el que escapar (50° en la fase 2, 44° en la 3). El hueco se fija al empezar el aviso: en la fase 2 mira a Noah; en la 3 se desvía 25° (alterna de lado) para obligar a moverse. Los proyectiles nacen a 2.4 m del centro del jefe y no se dispara el que nacería a menos de 1.2 m de Noah.
- **Espiral (fase 3):** `MotherSpiral`. Tres brazos a 120° que barren 45° cada uno durante 2 s (22,5°/s) y emiten un proyectil cada 0.2 s a 5 m/s (11 tandas, 33 proyectiles). Entre los sectores quedan **tres huecos de escape de 75°** por los que no pasa nada. Aviso de 0.8 s: círculo rojo, los tres huecos en azul y una marca roja donde arranca cada brazo. El hueco más cercano se fija al empezar el aviso a 55° de Noah (alterna de lado y de sentido de giro), así que Noah empieza dentro de un sector y hay que moverse; basta con ir al hueco o acercarse al jefe. The Mother está quieta durante todo el ataque y, como en el anillo, no nace ningún proyectil encima de Noah. Después, recuperación de 0.9 s. Los estados son `SpiralWarning` y `Spiral`.
- **Identidad visual por fase** (`MotherVisual` + `EnemyVisual.SetModelLook`; solo aspecto, la colisión no cambia): fase 2 con tinte **naranja** sostenido y ojos brillantes; fase 3 con **rojo profundo**, ojos más intensos y una respiración suave de la escala (±4 %, 1.1 Hz). Los cambios se mezclan en ~0.6 s y el parpadeo del cambio de fase y los destellos de golpe siguen viéndose encima. Los colores y la intensidad se editan en el grupo «Fases» del nodo `Animation`.
- **Barra del jefe** (`BossHud`): marcas finas al 66 % y 33 % y una etiqueta de fase junto al nombre («Fase I», «Fase II», «Fase III», de `MotherTexts.PhaseLabel`).
- **Sonido:** `jefe_fase` (opcional, sin archivo no suena ni da error).

### Fin de la partida

Al derrotar a The Mother la partida termina **una sola vez**:

1. En ese instante Noah queda quieto y sin poder disparar, las puertas y las transiciones se bloquean, no puede recibir daño y se eliminan todos los proyectiles enemigos.
2. Tras una pausa breve (0.8 s, para ver cómo se deshace el jefe) la pantalla se funde a oscuro y aparece la **pantalla de victoria** con una de las tres frases de `docs/equipo/textos.md` (la semilla de partida elige cuál, sin tocar ningún generador de azar) y el botón **Nueva partida**.
3. **Nueva partida** empieza otra partida con una semilla distinta de la actual, aunque la actual viniera de `--seed` o de una semilla fija en el inspector, con la misma dificultad y versión del generador. El inventario, las mejoras y la salud de Noah se reinician. No depende de las teclas de depuración, así que funciona igual en Release.

Si Noah ya estaba muerto cuando cae el jefe, no hay victoria: la partida se reinicia como siempre. Una vez declarada la victoria, Noah no puede morir ni reiniciar la escena por error.

### Depuración y pruebas

En compilaciones de depuración, **B** lleva a la sala del jefe y **U** le quita 20 de vida (sin modificadores); R y N siguen reiniciando la partida con la misma semilla o con otra. Pruebas, todas en headless:

```
godot --headless --path . res://tests/BossEncounterTest.tscn
godot --headless --path . res://tests/BossVictoryTest.tscn
godot --headless --path . res://tests/BossPhasesTest.tscn
godot --headless --path . res://tests/HistoricalIntegrityTest.tscn
```

`BossEncounterTest` comprueba la composición con y sin el jefe alternativo, los avisos completos antes de cada ataque, que nunca haya dos ataques a la vez y que la embestida respete las paredes. `BossVictoryTest` mata al jefe en cada estado de su ciclo y comprueba que la partida termina una vez, que no quedan proyectiles y que **Nueva partida** cambia la semilla y deja el inventario vacío y la salud completa. `BossPhasesTest` comprueba los umbrales, la invulnerabilidad del cambio de fase, que los ataques nuevos tengan aviso completo, no se solapen y no salgan de la arena, el hueco y la seguridad del anillo, la espiral (geometría, alternancia con el anillo, duración, huecos, nada sobre Noah y una simulación de que es esquivable con la velocidad real de Noah desde cualquier punto de la arena) y que reintentar, también en plena espiral, no deje residuos. `HistoricalIntegrityTest` fija los datos históricos de la IA, la sala de seis básicos y las colisiones de Noah (1×1) y del básico (0.9×0.9).

# 6. Stack tecnológico

El videojuego se desarrollará utilizando herramientas gratuitas, priorizando la posibilidad de trabajar sin servicios de pago.

| Componente | Tecnología |
|---|---|
| Motor gráfico | Godot 4.x, edición .NET |
| Lenguaje de programación | C# |
| Entorno de desarrollo | Visual Studio Code |
| Modelado y animación 3D | Blender |
| Texturas e ilustraciones | Krita |
| Shaders y materiales | Godot Shader Language |
| Física y colisiones | Godot Physics |
| Navegación de enemigos | Godot Navigation |
| Inteligencia artificial | C# + sistema de generación inteligente |
| Generación procedural | Algoritmos desarrollados en C# |
| Datos del videojuego | Godot Resources y JSON |
| Audio | Sistema de audio de Godot |
| Control de versiones | Git |
| Repositorio remoto | GitHub |
| Exportación | Ejecutable para Windows |

La edición .NET de Godot es necesaria para trabajar con C#.

El juego será 3D real, aunque sus mecánicas de movimiento y combate se desarrollarán principalmente sobre un plano horizontal.

# 7. Inteligencia artificial — Característica principal

## 7.1. Objetivo

Uno de los requisitos más importantes del proyecto será implementar inteligencia artificial funcional.

La propuesta es utilizarla para que **cada partida tenga niveles diferentes**, evitando que el jugador recorra siempre las mismas habitaciones en el mismo orden.

La IA participará en la composición de los escenarios, distribución de desafíos y selección de contenidos.

## 7.2. Funcionamiento general

Se propone un sistema híbrido que combine generación procedural tradicional con inteligencia artificial.

La generación procedural garantizará que los niveles sean jugables, mientras que la IA ayudará a decidir cómo construirlos según las características de la partida.

El proceso propuesto será:

1. El jugador inicia una nueva partida.
2. El sistema genera una semilla aleatoria.
3. El generador construye la estructura inicial del nivel.
4. La IA selecciona o modifica la composición de las habitaciones.
5. Se distribuyen enemigos, obstáculos, recompensas y desafíos.
6. Un validador comprueba que el nivel pueda completarse.
7. Godot construye el escenario 3D.
8. El jugador comienza a explorar.

La IA no necesariamente creará modelos 3D desde cero. Inicialmente trabajará con habitaciones, módulos, obstáculos y enemigos previamente diseñados.

## 7.3. Ejemplo de generación

**Partida 1**

La primera habitación conecta con dos salas pequeñas. Una contiene enemigos débiles y otra una recompensa. El jefe se encuentra al final de un pasillo.

**Partida 2**

La primera habitación conecta con una sala grande, seguida de una habitación con trampas. Existe un camino alternativo hacia una tienda y una habitación secreta.

**Partida 3**

El nivel presenta habitaciones estrechas, más obstáculos, enemigos a distancia y una distribución diferente de recompensas.

En las tres partidas se utilizan recursos del mismo mundo, pero cambia la composición del nivel.

## 7.3b. Retraso de aparición de enemigos (limitación conocida con el simulador)

Los enemigos de una sala aparecen **0,7 s** después de empezar el combate en el juego real (`RoomController.SpawnDelay`, editable en `Room.tscn`; la pausa lo detiene). El simulador (`LevelSimulator`) sigue esperando **1 s** (`RoomController.StartDelay`, que lee `GameplayParameters` y que figura como `start_delay: 1` en los metadatos de los datasets de etiquetas): los modelos de dificultad se entrenaron con ese valor y no se ha regenerado nada.

**Consecuencia:** el juego real da a Noah 0,3 s menos de margen que el simulador antes de que aparezcan los enemigos, así que la dificultad real puede ser algo mayor que la predicha. Es una diferencia pequeña y aceptada por ahora. Para eliminarla habría que bajar los dos valores a 0,7 s, regenerar las etiquetas y volver a entrenar los modelos. `PauseTest` fija ambos valores (los enemigos nacen a ~0,7 s y el simulador lee 1 s) para que no se cambien sin querer.

## 7.4. Enfoque técnico de la IA

Para evitar depender de servicios costosos, se plantea comenzar con un sistema local.

La implementación recomendada se divide en tres componentes:

**A. Generador procedural**

Construirá mapas utilizando algoritmos como grafos, selección de habitaciones y generación basada en semillas.

**B. Director inteligente**

Evaluará información de la partida, como el progreso del jugador, su salud, los objetos adquiridos y la dificultad del nivel.

A partir de esa información podrá seleccionar diferentes configuraciones de habitaciones y desafíos.

**C. Validador de niveles**

Comprobará que las habitaciones estén conectadas correctamente, que las puertas sean accesibles y que exista un camino válido hacia el jefe.

## 7.5. Inteligencia artificial real frente a generación aleatoria

Es importante diferenciar ambos conceptos.

Un generador que simplemente coloca habitaciones aleatoriamente no constituye, por sí solo, un modelo de inteligencia artificial aprendido.

Para que el proyecto incluya una implementación de IA más sólida, se propone desarrollar un sistema que pueda **aprender a evaluar o seleccionar niveles**.

La alternativa inicial recomendada es un modelo pequeño de aprendizaje automático, entrenado para puntuar distribuciones de habitaciones según dificultad, variedad y calidad de diseño.

Podría desarrollarse un prototipo de entrenamiento en Python con scikit-learn y utilizar sus resultados desde C#.

Esto permitiría demostrar una participación real del modelo en la selección de niveles.

No es obligatorio utilizar una IA generativa de texto ni depender de una API externa.

## 7.6. Objetivos de la IA

- Generar partidas con estructuras diferentes.
- Reducir la repetición excesiva de habitaciones.
- Mantener una progresión razonable de dificultad.
- Seleccionar configuraciones coherentes con cada mundo.
- Evitar niveles imposibles.
- Proporcionar rejugabilidad.
- Funcionar localmente sin necesidad de internet.

**Importante:** La IA puede aumentar la variedad, pero no garantiza niveles completamente únicos. La calidad dependerá de la cantidad de habitaciones disponibles, las reglas de generación y el entrenamiento del modelo.

# 8. Dirección artística y UI/UX

LULLABY utilizará una estética **3D estilizada de terror psicológico infantil**.

No se buscará hiperrealismo.

La intención será construir personajes y escenarios que recuerden a juguetes, figuras de plástico, muñecos de arcilla y muebles infantiles.

El contraste visual será fundamental.

Los objetos familiares tendrán colores relativamente suaves, pero aparecerán deformados por sombras, iluminación y proporciones inquietantes.

La dirección artística incluirá:

- Modelos 3D estilizados.
- Iluminación ambiental oscura.
- Sombras pronunciadas.
- Materiales de plástico, tela y madera.
- Animaciones ligeramente antinaturales.
- Partículas y efectos de lágrimas.
- Interfaz minimalista.
- Elementos visuales relacionados con juguetes y recuerdos.

La interfaz mostrará información como salud, objetos, mapa, recursos y estado de la partida sin ocupar demasiado espacio.

## 8.1. Modelos 3D integrados

Los modelos viven en `assets/models/` (fuentes en `assets/source/blender/`). Todos son solo visuales: no cambian ninguna colisión ni parámetro del juego.

- **Enemigos** (los seis): el modelo cuelga del nodo `Visual` de cada escena con un giro fijo de 180°, sin color provisional (`UseKindColor = false`), con `EnemyFacing` y destello de golpe sobre copias de sus materiales. El volador flota (su punto más bajo está a 0,33 m del suelo). El excavador conserva `DiggerVisual`: su modelo cuelga del nodo `Body`, que se hunde al enterrarse, y el nodo `Mound` de la escena es el montículo que queda.
- **Objetos del tesoro:** cada recogida carga `assets/models/items/{Id}.glb`, centrado y escalado a 0,5 m, que flota y gira suavemente (`FloatingSpin`). Si el archivo no existe, se ve la figura provisional de siempre.
- **Proyectiles:** `tear.glb` (lágrima de Noah, orientada con su vuelo y la punta hacia atrás) y `enemy_projectile.glb` (tirador y excavador), con la figura provisional como respaldo.
- `ModelLibrary` (`scripts/decor/`) carga los `.glb` centrados por su caja envolvente, porque algunos orígenes no están centrados.

Prueba: `godot --headless --fixed-fps 60 --path . res://tests/VisualModelsTest.tscn`.

## 8.2. Ambientación de las salas de The Cradle

Es solo visual y **opcional**: `RoomManager.DecorEnabled` (apagado por defecto, encendido en `TestLevel.tscn`) la activa en cada sala. Con ella apagada, las pruebas, el simulador y los benchmarks ven las salas de siempre. Ninguna pieza tiene colisión: paredes, puertas (`Door`), `RoomLayout` y las colisiones de Noah y los enemigos no cambian.

- **Puertas:** un marco (`marco_puerta`, hueco libre de 3 m) alrededor de cada hueco y unos barrotes (`puerta_barrotes`) como aspecto de la puerta cerrada. Los barrotes cuelgan de `Door` (`AttachSkin`): se ven mientras está cerrada y se ocultan al abrirla; la caja roja provisional deja de verse.
- **Columnas** (`columna_esquina`) en las esquinas interiores del lado norte. La cámara mira desde el sur con 60° de inclinación y la pared sur oculta el suelo que tiene delante, así que el sur no se decora (`RoomDecorator.SkipSouth`).
- **Texturas opcionales** en `assets/textures/` (`RoomSurfaces`): `parquet.png` (suelo de salas normales y de inicio), `papel_tapiz.png` (paredes), `alfombra.png` (suelo del tesoro) y `madera_cuna.png` (suelo del jefe). Se cargan solo por código con `ResourceLoader.Exists` y en triplanar de espacio mundo (una repetición cada `TextureTileMeters`, 2 m por defecto, editable en `Room.tscn`). Si una no existe, esa superficie usa un color pastel apagado (también editable en `Room.tscn`, grupo «Decoración»). Ninguna escena las referencia, así que su ausencia no rompe el juego ni la exportación.
- **Luz y ambiente:** un solo recurso, `resources/atmosphere/cradle_atmosphere.tres` (`CradleAtmosphere`), editable desde el inspector de Godot: entorno (ambiente, niebla, tonemap, fondo) y luz principal (color, energía, orientación, sombras). El nodo `Atmosphere` de `TestLevel.tscn` (`AtmosphereApplier`) lo aplica al empezar. No afecta al encuadre de `RoomCamera`.

- **Piezas por tipo de sala** (`RoomDecorator`), todas visuales y sin colisión, con un generador de azar APARTE sembrado con la semilla de partida y la celda (`SeedMix`): decorar no consume el azar global ni el del generador, el director o los objetos, así que no altera la generación, las métricas ni los benchmarks, y la misma semilla da siempre la misma decoración.
  - **Salas de combate** (Normal, `WallDecorPlanner`): solo piezas pegadas a la pared y alfombras planas. Las piezas de pared salen de `WallPieces` (7): estantería (`estanteria_gigante`) y cortina (`cortina_ventana`) desde el suelo, y cuadro (`cuadro_infantil`), reloj (`reloj_pared`), repisa (`repisa_juguetes`), aplique (`aplique_estrella`) y perchero (`perchero_pared`) con la base a 1,5, 1,5, 2,2, 1,5 y 1,9 m de altura. `ScaleFor` amplía solo X e Y locales; Z sigue a escala 1. `DecorPiece` conserva la escala para aplicarla tanto a las mallas como al cálculo de puertas, esquinas y separación. Por cada pared (norte, este y oeste; la sur no se ve) se eligen 0 a 2 piezas, su posición a lo largo de la pared y su espejado; y de 0 a 2 alfombras. Relieve medido (fondo del modelo menos 3 cm hundidos en la pared) de 0,29 m (estantería), 0,21 (cuadro), 0,19 (repisa), 0,18 (cortina), 0,13 (reloj), 0,07 (perchero) y 0,04 (aplique), siempre por debajo de 0,3 m; a 1,2 m de las esquinas, a 2,3 m del centro de cada puerta, a 0,6 m entre sí y como mucho 4 piezas de pared. El resto del catálogo sobresale más de 0,3 m y vive en inicio, tesoro y jefe.
  - **Inicio, tesoro y jefe** (`FreeDecorPlanner`): un subconjunto del catálogo de piezas exentas (bloques, pelota, tambor, xilófono, lámpara, baúl, tren, castillo, caballito, cuna, libro, móvil y oso gigante; 6 a 8 en el inicio, 4 a 6 en el tesoro, 6 a 9 en el jefe) junto a las paredes con giro, espejado y separación aleatorios. Fuera de las llegadas del jugador, del hueco de las puertas, del área central (la cuadrícula de objetos del tesoro, el centro del inicio y el recorrido de The Mother) y de la pared sur, y sin solaparse. En el jefe siempre hay `mecedora_gigante`, `cuna_gigante` o las dos, de lado contra la pared; solo pueden invadir 0,5 m el pasillo de The Mother (la parte que sobra se hunde en la pared de 1 m de grosor).
- **Ambientación extra de las salas de combate** (solo visual, sin colisión; no toca `RoomLayout`, la IA, los hashes v1/v2 ni el simulador, que sigue con la decoración apagada):
  - **Juguetes sueltos** (`FloorPropsPlanner`): de 3 a 6 piezas del catálogo pequeño de `FreeDecorPlanner` (sin el oso gigante) que se SUMAN a las piezas de pared y las alfombras. Junto a las paredes norte, este y oeste, con tendencia a las esquinas, con giro y espejado aleatorios y azar propio (`SeedMix`). Fuera de puertas, llegadas, centro (±3 m) y pared sur, sin solapar nada y dentro del encuadre de `RoomCamera`. Si las mallas o los triángulos de la sala superan el presupuesto (60 mallas, 60 000 triángulos) se descarta la pieza más cara. Se llaman `Toy{i}{modelo}` (sin guion bajo, para no alterar los recuentos de las piezas de pared).
  - **Aspecto por sala** (`RoomLook`): en las salas Normal se elige por semilla una variante entre las que existan de `floor_madera|baldosas|alfombra.png` y `wall_estrellas|nubes|rayas.png` (triplanar mundial, `RoomSurfaces.Textured`). Sin variantes, la textura de siempre (`parquet`, `papel_tapiz`...); sin ninguna, el color de siempre con una variación sutil de tono (±0,025), saturación (±10 %) y brillo (±12 %) propia de la sala. Inicio, tesoro y jefe conservan su textura y solo varían el tono del color.
  - **Luces** (`RoomLights`): 1 o 2 `OmniLight3D` cálidas, sin sombras, a 2,2 m, de rango 7 m y energía 0,6 a 0,9, en todas las salas decoradas, en el nodo `Lights` de la sala. Se apagan todas con `RoomLights.Enabled = false` (valor por defecto en el propio script).
- **Precalentamiento de la ambientación** (`DecorWarmup` y `DecorWarmupRunner`, lanzado por `MainMenu`): la primera sala de combate de la sesión daba un tirón de ~130-190 ms porque en ese momento se cargaban las texturas de suelo y pared, se cargaban y medían los modelos y se contaban los triángulos de cada uno para el presupuesto (`FloorPropsPlanner.CostOf` era lo que más pesaba, ~70 ms). Ahora, mientras se ve el menú, el runner carga las texturas y los modelos de uno en uno, en el hilo principal y con un presupuesto de 6 ms por fotograma (`DecorWarmup.Step`), los guarda, mide cada modelo y cuenta sus triángulos, y dibuja una vez en un `SubViewport` propio los materiales y modelos con una luz direccional con sombra y una omni; después lo libera. Sin hilos a propósito: `LoadThreadedRequest` con varias cargas a la vez chocaba en el renderizador de pruebas (errores de RID en `material_storage` y cierres con -1073741819, unas 3 de cada 30 corridas) y con una sola carga a la vez era lentísimo en headless (~1,6 s por recurso). Es idempotente y reanudable, tolera archivos ausentes y no cambia ninguna lógica del juego. Con la ventana real, construir la primera sala pasó de ~135-145 ms a ~17-20 ms y el fotograma de la entrada de ~170-190 ms a ~60 ms. `DecorWarmup.Enabled = false` lo apaga. Prueba: `res://tests/DecorWarmupTest.tscn`.
- **Pedestal del tesoro** (`pedestal_tesoro`): con la ambientación activa, cada objeto descansa sobre uno (visual, sin colisión; queda vacío al recoger el objeto).

Medidas de las cinco piezas colgadas en Godot (metros, ancho × alto; fondo sin cambios). El relieve se comprueba sobre las mallas instanciadas en las tres paredes, con y sin espejado:

| Pieza | Antes | Escala X / Y | Después | Base antes → después | Relieve aprox. |
|---|---|---|---|---|---|
| Cuadro | 0,820 × 0,700 | 2 / 2 | 1,640 × 1,400 | 1,30 → 1,50 | 0,212 |
| Reloj | 0,485 × 1,130 | 2 / 1,3 | 0,970 × 1,469 | 1,05 → 1,50 | 0,132 |
| Repisa | 1,050 × 0,379 | 1,8 / 1,8 | 1,890 × 0,682 | 1,20 → 2,20 | 0,190 |
| Aplique | 0,399 × 0,640 | 2,2 / 2,2 | 0,879 × 1,408 | 1,50 → 1,50 | 0,040 |
| Perchero | 0,750 × 0,450 | 2,2 / 2,2 | 1,650 × 0,990 | 1,10 → 1,90 | 0,065 |

El reloj requiere una ampliación vertical menor por su péndulo: a ×1,8 y con base a 1,5 m llegaría a 3,534 m; a ×1,3 llega a 2,969 m. Las otras cuatro piezas terminan entre 2,882 y 2,908 m. El cuadro conserva su geometría y separación de aproximadamente 7 cm respecto a la pared, pendiente de corrección artística. Se mantiene la distribución aleatoria en este y oeste: desplazar una cara lateral hacia el fondo reduce su área proyectada (en la comparación del cuadro a ±un cuarto de la profundidad de sala, el fondo conserva un 49–69 % del área cercana, según la plantilla). La ampliación del ancho mejora la lectura sin ese desplazamiento.

Pruebas: `godot --headless --fixed-fps 60 --path . res://tests/RoomDecorBaseTest.tscn` (estructura) `res://tests/RoomDecorLayoutTest.tscn` (planes, reglas de colocación, encuadre de cámara —también las cinco piezas en los extremos permitidos de las tres paredes—, presupuesto y pedestales) y `res://tests/RoomDecorVarietyTest.tscn` (determinismo, variedad entre semillas, solapes, puertas, medidas antes/después, relieve real y aislamiento del azar y de la generación).

Pruebas de la ambientación extra: `godot --headless --fixed-fps 60 --path . res://tests/RoomAmbienceTest.tscn` (juguetes, aspecto por sala con y sin texturas, luces y presupuesto).

## 8.3. Minimapa

Arriba a la derecha (`scripts/ui/minimap/`, escena `scenes/ui/Minimap.tscn`, instanciada en `TestLevel`). **Solo lee** el nivel: el `LevelMap` (celdas en rejilla y tipos de sala) y las salas que carga `RoomManager` (`RoomLoaded`), de donde saca las visitadas; no cambia la generación, los hashes v1/v2 ni la IA. Funciona igual con los generadores v1, v2 y v3 y en la sala del jefe.

- **Estados de sala:** actual (resaltada), visitada, vecina (contigua a una visitada: visible pero atenuada) y oculta (no se dibuja). Los iconos de tesoro y jefe solo se ven en salas vecinas, visitadas o actual. Las conexiones se dibujan entre una sala visitada y cualquier sala visible.
- **Pequeño:** panel de 192×192 px con opacidad ~60 %, iconos de 32 px (x1), centrado en la sala actual y recortado al panel; una línea fina entre salas.
- **Expandido:** se mantiene **Tab** (acción `minimapa_expandir`, propia: no usa `ui_focus_next`). Panel centrado de entre 320 y 512 px que se ajusta a las salas descubiertas, opacidad ~92 %, iconos x2 con el icono de puerta de 32 px entre salas si caben (si no, x1). El juego **no se pausa**; al soltar vuelve a pequeño, y si se pausa vuelve a pequeño y no reacciona a la tecla.
- **Pixel art:** iconos `assets/ui/minimapa_*.png` (32×32) a escala entera y con filtro `Nearest`. Si falta alguno se dibuja por código (`ResourceLoader.Exists`). `minimapa_sala_desconocida.png` no se usa (las salas ocultas no se dibujan).
- **Capas:** su capa (2) queda sobre los HUD y bajo el fundido, la pausa y las pantallas de victoria y derrota. No crea nodos por sala: al reintentar o volver al menú nace o se libera con la escena, sin residuos.

Prueba: `godot --headless --fixed-fps 60 --path . res://tests/MinimapTest.tscn`.

## 8.4. Corazones de vida y controles en el piso

- **Corazones** (`HeartsRow`, dentro de `HealthHud`): sustituyen al texto «Vida: X/Y». Arriba a la izquierda, en una fila, con los iconos pixel art `assets/ui/corazon_lleno|medio|vacio.png` (16×16) a escala x3 y filtro `Nearest`; el HUD de objetos va debajo. Solo muestran la vida: no cambian ningún valor de vida ni de daño. 1 corazón = 1 punto de vida y hay tantos como la vida máxima redondeada hacia arriba (como mucho 24). **Regla de redondeo:** al corazón *i* le corresponde `v = clamp(vida − i, 0, 1)`; `v ≥ 0,75` lleno, `0,25 ≤ v < 0,75` medio, menos vacío. **Garantía:** con vida mayor que 0 nunca se muestran 0 corazones (con 0,1 se ve medio). Ejemplos: 5,2/6 → 5 llenos y 1 vacío; 4,5/6 → 4 llenos, 1 medio y 1 vacío. Al perder vida, el corazón que baja de estado se sacude y se tiñe de rojo ~0,35 s (solo visual). Si falta una textura se dibuja por código.
- **Daño y Manta:** cada golpe de contacto o proyectil quita 1 corazón; la explosión del explosivo, 2. La dificultad elegida no cambia el daño. La Manta vieja reduce el daño un 50 %, y **ningún golpe quita menos de 0,5** (`PlayerHealth.MinHitDamage`): con Manta, un golpe de 1 quita medio corazón y la explosión (2) quita 1. Los efectos de resistencia se combinan multiplicándose y ese mínimo acota el resultado.
- **Controles en el piso** (`FloorControlsDecal`): `assets/ui/controles_piso.png` (1600×600) como un quad horizontal de 7,2×2,7 m en el centro de la sala de **inicio** (solo esa), a 2 cm del suelo, con la parte superior de la imagen hacia el norte, con transparencia, sin sombra ni colisión y dibujado por debajo de Noah, los enemigos y los objetos. No tapa las puertas ni las zonas de llegada, y la decoración exenta del inicio deja libre su rectángulo (con 0,3 m de margen) haya o no textura. Si la textura no existe no se crea nada. **La imagen muestra MOVER (WASD), DISPARAR (flechas) y PAUSA (solo ESC); falta Tab (minimapa) y la tecla P como segunda tecla de pausa.**

Pruebas: `godot --headless --fixed-fps 60 --path . res://tests/HeartsHudTest.tscn` y `res://tests/FloorControlsTest.tscn`.

## 8.5. Audio

Todo el sonido pasa por `AudioManager` (`scripts/audio/`, autoload). Los ids, archivos y límites están en constantes de C# (`SoundCatalog`); el juego no lee `docs/` en ejecución. Los efectos de Luis están en `assets/audio/sfx/` (créditos en `assets/audio/CREDITS.md`).

- **Buses:** `Master`, `SFX` y `Musica` (`default_bus_layout.tres`; si faltaran, se crean al iniciar). La música está en `assets/audio/music/` (ver «Música» más abajo).
- **Eventos:**

| Sonido | Evento | Tipo |
|---|---|---|
| `lagrima_disparo` | Noah dispara | plano, tono ±5 %, −10 dB |
| `lagrima_impacto` | una lágrima impacta | plano, tono ±5 % |
| `noah_dano` / `noah_muere` | Noah recibe un golpe / muere | plano |
| `enemigo_golpe` | un enemigo recibe un golpe que no lo mata | 3D, tono ±5 % |
| `enemigo_muere` | muere un enemigo | 3D |
| `mecha_explosivo` / `explosion` | el explosivo enciende su mecha / explota (no suena además su muerte) | 3D |
| `tirador_disparo` | dispara el tirador, el excavador o la salva de The Mother | 3D |
| `volador_aleteo` | bucle mientras haya voladores vivos (un único reproductor en su posición media; el bucle se activa por código, no por la importación) | 3D |

- **Sonidos del lote 2** (Luis, `assets/audio/sfx/`; créditos en `assets/audio/CREDITS.md`): suenan por su propio id y están enganchados así:
  - `puerta_cierra` al empezar el combate de una sala y `puerta_abre` al despejarla (una sola voz por evento, no una por puerta; planos).
  - `objeto_recoger` al recoger un objeto y `curar` al recoger el biberón (cuando cura de verdad; el biberón no suena `objeto_recoger`).
  - `menu_click` en todos los botones de los menús y de la pausa, y `menu_confirmar` en **Jugar** (un botón elige su sonido con el metadato `ui_sound`).
  - `jefe_aparece` cuando The Mother aparece al empezar el combate de su sala (3D).
  - `jefe_ataque_1` en el **anillo** y `jefe_ataque_2` en la **espiral** (3D, una sola vez por ataque; antes sonaban disparos de tirador). Ojo: en `CREDITS.md` el primero está descrito como «carga o embestida» y el segundo como «ataque viscoso»; la asignación actual es la pedida (anillo y espiral).
  - `jefe_muere` al morir The Mother (por su propio id) y `victoria` / `derrota` al mostrar la pantalla de victoria / derrota (una vez cada una).
- **Sonidos que aún no tienen archivo:** `jefe_golpe` y `jefe_fase`. Mientras no existan, el golpe de The Mother usa `enemigo_golpe` con el tono un 20 % más grave y el cambio de fase no suena. Si un archivo no existe, no suena y no da error (`ResourceLoader.Exists`).
- **Voces:** como mucho 8 planas y 16 en 3D a la vez, de 1 a 4 por sonido y un intervalo mínimo entre repeticiones; sin hueco, el sonido nuevo se descarta, salvo `noah_muere` y `explosion`, que roban la voz más antigua. La variación de tono usa un generador de azar propio: no toca el azar global.
- **3D:** el oyente (`AudioListener3D`) está en el soporte de la cámara, en el centro de la sala a nivel de suelo; los sonidos llegan hasta 30 m. `noah_muere` (casi 7 s) se corta al reiniciar la partida o volver al menú.
- **A prueba de fallos:** el audio nunca puede alterar el juego. Toda la API que usa la lógica (`Play`, `PlayAt`, `PlayFrom`, `PlayMusic`, `StopMusic`, `StopAll`) captura cualquier excepción, la avisa una sola vez y sigue; tras 5 fallos el audio se desactiva solo (`AudioManager.Enabled`). La lógica pide el sonido siempre DESPUÉS de aplicar el daño, liberar lo que muere y resolver la derrota o la recogida. Los comportamientos de los enemigos piden su sonido con `PlayFrom`, que usa el primer nodo 3D que los contiene (The Mother cuelga sus `ShootBehavior` de `MotherBehavior`, no del enemigo).
- **Volúmenes:** tres deslizadores («Volumen general», «Efectos» y «Música», de 0 a 100) en el menú principal y en el de pausa. Por defecto, 80 %, 100 % y 70 % (bus `Musica`). Se guardan en `user://settings.cfg` (`ConfigFile`) y se cargan al iniciar; si el archivo no existe o está dañado, se usan los valores por defecto.
- **Música:** `menu`, `the_cradle` y `jefe` (`SoundCatalog`, siempre en bucle; el bucle está activo en la importación de los `.ogg`). `AudioManager.PlayMusic(id)` las cruza con un fundido de un segundo (`MusicPlayer`) y pedir la que ya suena no la reinicia. `menu` suena en el menú principal; `the_cradle`, en las salas de la partida (de una sala normal a otra no se corta); `jefe`, al entrar en la sala del jefe. Al morir Noah o ganar, la música se apaga con fundido (`FadeOutMusic`) y suena la derrota o la victoria; al volver al menú vuelve `menu`. Cada pista trae `StartSeconds` (desde dónde arranca) y `LoopOffsetSeconds` (adonde vuelve el bucle) en `SoundCatalog`, para saltarse la entrada casi muda de `the_cradle` (7,25 s) y `jefe` (15,5 s); `menu` va entera (0 y 0). Valores medidos con las muestras (la música real empieza en 7,75 s y 16,0 s, menos medio segundo); ajustar a oído. Sigue sonando con el juego en pausa. Si falta un archivo, queda en silencio sin errores. Prueba: `res://tests/AudioMusicTest.tscn`.
- **Pruebas de resistencia:** `tests/RoomEnduranceTest.tscn` recorre 14 o más salas seguidas con el nivel real (combate con lágrimas reales, tesoro con objetos) y en cada una comprueba que una lágrima daña a un enemigo y que un proyectil enemigo daña a Noah; mide también nodos, memoria y objetos por sala para detectar acumulaciones. Se ejecuta con el audio real y con el audio apagado (`--audio=real|silent|off`, `--generator=2|3`, `--fight=1`, `--items=1`).
- **Pruebas:** en `--headless` no suena nada (driver de audio ficticio) y `AudioManager` registra cada petición. `tests/AudioEventsTest.tscn` comprueba que cada evento pide su sonido y que los faltantes no rompen. `tests/run_suite.ps1` ejecuta toda la batería (PowerShell, Windows) y falla también si algún log contiene errores de carga de escenas o recursos.

# 9. Arquitectura de software

Se utilizará una arquitectura modular para facilitar el desarrollo y mantenimiento.

Los principales sistemas serán:

**PlayerController:** Movimiento y acciones de Noah.

**CombatSystem:** Proyectiles, daño y efectos de combate.

**HealthSystem:** Salud, daño recibido y muerte.

**EnemyAI:** Comportamientos de enemigos.

**RoomManager:** Administración de habitaciones.

**DungeonGenerator:** Construcción procedural de niveles.

**AIDungeonDirector:** Selección inteligente de configuraciones.

**LevelValidator:** Validación de accesibilidad y jugabilidad.

**ItemSystem:** Objetos, estadísticas y efectos.

**BossSystem:** Mecánicas especiales de los jefes.

**RunManager:** Administración de cada partida.

**SaveSystem:** Persistencia de desbloqueos y configuración.

**AudioManager:** Música y efectos de sonido.

**UIManager:** Interfaz y menús.

# 10. Estructura propuesta del repositorio

```text
LULLABY/
├── project.godot
├── scenes/
│   ├── player/
│   ├── enemies/
│   ├── bosses/
│   ├── rooms/
│   ├── levels/
│   ├── items/
│   └── ui/
│
├── scripts/
│   ├── player/
│   ├── combat/
│   ├── enemies/
│   ├── generation/
│   ├── ai/
│   ├── items/
│   ├── systems/
│   └── ui/
│
├── resources/
│   ├── items/
│   ├── enemies/
│   └── rooms/
│
├── assets/
│   ├── models/
│   ├── textures/
│   ├── animations/
│   ├── audio/
│   └── shaders/
│
├── ai-training/
│   ├── datasets/
│   ├── models/
│   └── training/
│
├── tests/
├── docs/
├── .gitignore
└── README.md
```

La carpeta `ai-training` será utilizada para los experimentos de aprendizaje automático. El juego final deberá poder ejecutar la lógica de selección inteligente sin requerir Python instalado en el equipo del jugador.

# 11. Plan de desarrollo


## 8.6. Intro

Escena principal del proyecto (`IntroScene`, `scripts/ui/`): ocho cuadros estilo libro de cuentos con subtítulo en Patrick Hand, la voz de la narración y el canto de la madre de fondo. Se reproduce **una vez** al abrir el juego y pasa a `MainMenu`.

- **Cuadros** (`IntroFrames`): `assets/intro/escena_NN.png` (1920×1080), `voz_NN.mp3` y `canto_mama.mp3` (25 s). Fundido de entrada de 0,8 s, zoom de 1,00 a 1,06 con un paneo distinto por cuadro (zoom a Noah, paneo a la madre, zoom al móvil, zoom leve, zoom hacia atrás, paneo lateral, zoom a la puerta, zoom lento y fundido a negro), fundido de salida. Cada cuadro dura lo que su voz más 0,6 s (mínimo 3,5 s): en total unos 39 s.
- **Sonido:** la voz va por el bus `SFX` y el canto por `Musica` a −10 dB (~30 % frente a la voz); respetan los deslizadores, que se aplican al entrar. El canto va en bucle con dos reproductores (`LoopingBackdrop`) que se cruzan un segundo antes del final, y se apaga con fundido en el último cuadro; el menú arranca después con su música.
- **Rendimiento:** las ilustraciones se importan con compresión con pérdida (calidad 0,9) y mipmaps (28,4 MB de PNG → 5,5 MB importados; sin pérdida eran 20 MB) y se cargan de una en una, con la pantalla en negro, soltándose al acabar su cuadro. Antes de pasar al menú, dentro del fundido a negro, se vacían los finalizadores de C#.
- **Saltar:** Enter, Esc, Espacio o clic (fundido corto, voz y canto cortados). Pista discreta «Pulsa Enter para saltar».
- **Cuándo no se ve:** en headless y en las pruebas, si ya se mostró en la sesión, con el argumento `--sin-intro` (en el editor: Depurar → Personalizar instancias de ejecución → argumentos de lanzamiento) o si no hay ninguna ilustración. Sin una imagen se omite ese cuadro, sin voz dura 3,5 s y sin canto no suena fondo, sin errores.
- Prueba: `res://tests/IntroTest.tscn`.

## Fase 1 — Preparación del proyecto

- Instalar Godot 4 .NET.
- Configurar C# y Visual Studio Code.
- Crear el proyecto base.
- Configurar Git y GitHub.
- Definir convenciones de código.
- Crear las carpetas principales.

## Fase 2 — Prototipo jugable

- Crear un modelo provisional de Noah.
- Implementar movimiento.
- Configurar cámara superior.
- Implementar lágrimas como proyectiles.
- Crear una habitación de prueba.
- Implementar un enemigo básico.
- Añadir sistema de salud y colisiones.
- Crear puertas que se desbloquean al derrotar enemigos.

**Resultado esperado:** Un prototipo funcional de combate y exploración 3D.

## Fase 3 — Sistema roguelike

- Crear habitaciones modulares.
- Implementar conexiones mediante puertas.
- Crear el generador procedural.
- Implementar semillas aleatorias.
- Añadir habitaciones especiales.
- Implementar recompensas y objetos.
- Crear el sistema de muerte y reinicio de partida.

**Resultado esperado:** Partidas con mapas diferentes y un ciclo de juego completo.

## Fase 4 — Inteligencia artificial

- Crear datos de evaluación de niveles.
- Definir métricas de calidad y dificultad.
- Entrenar un modelo pequeño.
- Integrar el sistema inteligente con Godot.
- Validar las distribuciones generadas.
- Comparar niveles aleatorios con niveles seleccionados por IA.
- Documentar resultados.

**Resultado esperado:** Sistema de IA funcional que influye de manera verificable en la generación de niveles.

## Fase 5 — Diseño artístico

- Modelar a Noah.
- Crear enemigos y animaciones.
- Diseñar escenarios definitivos.
- Implementar materiales y shaders.
- Mejorar iluminación y efectos visuales.
- Diseñar la interfaz.

## Fase 6 — Contenido y narrativa

- Implementar mundos adicionales.
- Crear jefes.
- Añadir objetos especiales.
- Implementar eventos secretos.
- Incorporar fragmentos narrativos.
- Crear rutas alternativas y finales.

## Fase 7 — Pruebas y publicación

- Corregir errores.
- Optimizar rendimiento.
- Equilibrar enemigos y objetos.
- Probar la generación de niveles.
- Preparar el ejecutable de Windows.
- Documentar instalación y funcionamiento.
- Preparar la presentación del proyecto.

# 12. Alcance mínimo viable (MVP)

Para evitar que el desarrollo se vuelva demasiado grande, la primera versión jugable tendrá un alcance reducido.

**Contenido mínimo propuesto:**

- Un personaje jugable: Noah.
- Un mundo: The Cradle.
- Entre 8 y 12 habitaciones modulares reutilizables.
- Tres tipos de enemigos.
- Un jefe.
- Cinco objetos con efectos diferentes.
- Sistema de combate mediante lágrimas.
- Generación procedural de niveles.
- Un director de IA que seleccione configuraciones.
- Sistema de muerte y reinicio.
- Menú principal y HUD.
- Exportación para Windows.

El MVP deberá demostrar que el jugador puede iniciar una partida, explorar habitaciones, combatir, recoger objetos, derrotar un jefe y comenzar una nueva partida con una estructura diferente.

# 13. Requisitos de calidad

El proyecto deberá priorizar:

**Modularidad:** Los sistemas deben estar separados y permitir ampliaciones.

**Rendimiento:** Los modelos y efectos deben estar optimizados para equipos de gama media.

**Mantenibilidad:** Código C# organizado, nombres claros y responsabilidades definidas.

**Reproducibilidad:** Las semillas deberán permitir repetir una generación cuando se mantengan las mismas reglas, versiones y condiciones.

**Validación:** Los niveles no deberán contener caminos obligatorios bloqueados o habitaciones inaccesibles.

**Escalabilidad:** Se deberán poder agregar nuevos mundos, enemigos, objetos y habitaciones sin reescribir el generador.

**Desarrollo gratuito:** Se priorizarán herramientas libres y modelos locales, evitando dependencias de servicios de pago.

# 14. Decisiones pendientes

Antes de comenzar el desarrollo definitivo se deberán resolver algunos aspectos:

1. Elegir la apariencia final de Noah.
2. Definir el estilo exacto de cámara y movimiento.
3. Determinar cómo se entrenará y evaluará el modelo de IA.
4. Elegir el método de integración del modelo con Godot.
5. Definir el sistema de dificultad y progresión.
6. Establecer el desenlace definitivo de la historia.
7. Determinar el alcance de la primera entrega académica.
8. Crear y configurar el repositorio oficial del proyecto.

# 15. Objetivo final

Desarrollar un videojuego 3D roguelike de terror psicológico que combine combate, exploración, narrativa ambiental y generación inteligente de niveles.

LULLABY deberá ofrecer partidas variadas mediante una arquitectura que integre algoritmos procedurales con un componente real de inteligencia artificial, manteniendo una identidad artística propia y una historia que se descubra progresivamente.

El resultado esperado es un videojuego ejecutable para Windows, desarrollado principalmente con Godot, C# y Blender, que demuestre tanto competencias de desarrollo de software como la integración práctica de inteligencia artificial en videojuegos.

---

**Fin del documento maestro — LULLABY, versión conceptual 1.0.**
