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
| Manta vieja | −20 % de daño recibido | Prisma verde |

La recogida se realiza por contacto. La curación es instantánea; las cuatro mejoras permanecen hasta morir o empezar otra partida y no se acumulan por duplicado. Los modificadores pertenecen a cada instancia del jugador: los valores base de salud, disparo y colisiones siguen intactos. El Sonajero, los efectos de miedo, revelar enemigos ocultos y la protección temporal quedan fuera de esta implementación.

Cada tesoro ofrece un objeto por defecto. La cantidad se configura en `ItemCatalog.ItemsPerTreasure`, a través de `resources/items/Catalog.tres`. Con cantidades mayores, todas las ofertas son recogibles independientes: todavía no existe «elegir uno de N». En cada tesoro se agota el catálogo antes de repetir un objeto. Volver a una habitación conserva tanto las ofertas pendientes como las ya recogidas, sin cambiar su selección ni permitir recogerlas otra vez.

Las recompensas se asignan después de elegir el nivel, con un RNG propio por semilla de partida y celda del tesoro. El catálogo se ordena por identificador estable; cambiar el orden de visita o la cantidad de ofertas no cambia la primera recompensa de una celda. No se modifican las plantillas ni se consume el azar del generador o del director de IA.

El HUD muestra los nombres obtenidos; el Biberón queda marcado como usado y agrupa las curaciones repetidas. Morir o reiniciar limpia el inventario, las mejoras y el estado de los tesoros. En compilaciones de depuración, **J sin modificadores** crea junto al jugador el siguiente objeto del catálogo para probarlo; **R** repite la partida desde cero con su semilla y **N** comienza otra. Este atajo no funciona en Release.

Pruebas: `tests/ItemSystemTest.tscn` comprueba efectos, recogidas y parámetros base; `tests/TreasureItemsTest.tscn` comprueba visitas reales, HUD, varias ofertas, reinicio y aislamiento del RNG sobre 200 semillas. Cada recogible emite su señal `Collected`; el registro separado por celda y oferta permite añadir posteriormente grupos de elección sin cambiar los efectos del jugador.

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
godot --headless --path . res://tests/HistoricalIntegrityTest.tscn
```

`BossEncounterTest` comprueba la composición con y sin el jefe alternativo, los avisos completos antes de cada ataque, que nunca haya dos ataques a la vez y que la embestida respete las paredes. `BossVictoryTest` mata al jefe en cada estado de su ciclo y comprueba que la partida termina una vez, que no quedan proyectiles y que **Nueva partida** cambia la semilla y deja el inventario vacío y la salud completa. `HistoricalIntegrityTest` fija los datos históricos de la IA, la sala de seis básicos y las colisiones de Noah (1×1) y del básico (0.9×0.9).

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
