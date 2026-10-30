# IA de LULLABY: un director de dificultad entrenado con datos

**Resumen.** Cada partida de LULLABY genera varios niveles candidatos, **predice con un modelo de aprendizaje automático qué tan difícil es cada uno** y elige uno cercano a la dificultad pedida (fácil, normal o difícil). El modelo no se programó con reglas: se **entrenó con 5000 niveles cuya dificultad midió un bot** que los jugó en un simulador, y se evaluó con niveles que nunca vio. Todo funciona sin internet y sin servicios de pago; en el juego el modelo se evalúa con código C# puro.

Este documento explica qué hace la IA, cómo se construyó, qué resultados dio (con las cifras de los informes de `ai-training/reports/`) y cuáles son sus límites.

## Contenido

1. [Qué hace la IA y por qué es aprendizaje automático](#1-qué-hace-la-ia-y-por-qué-es-aprendizaje-automático)
2. [Flujo completo](#2-flujo-completo)
3. [Datos y conjuntos](#3-datos-y-conjuntos)
4. [Modelos probados y por qué ganó la regresión lineal](#4-modelos-probados-y-por-qué-ganó-la-regresión-lineal)
5. [Resultados](#5-resultados)
6. [Variedad de niveles](#6-variedad-de-niveles)
7. [Limitaciones](#7-limitaciones)
8. [Mejoras futuras](#8-mejoras-futuras)
9. [Cómo reproducir todo](#9-cómo-reproducir-todo)
10. [Anexo: glosario de métricas y archivos](#10-anexo-glosario-de-métricas-y-archivos)
11. [Versión 2: tipos de enemigo](#11-versión-2-tipos-de-enemigo)

Las secciones 1 a 10 describen la **versión 1** (generador v1, un solo tipo de enemigo). La sección 11 describe la **versión 2**, que añade el tirador, el explosivo y el volador. La versión 1 sigue intacta y reproducible.

---

## 1. Qué hace la IA y por qué es aprendizaje automático

### Qué hace en el juego

El documento maestro (`docs/LULLABY.md`, sección 7) plantea que cada partida tenga niveles distintos y que un sistema local ayude a decidir cómo construirlos. La IA cumple ese papel con el componente que el documento llama *director*:

1. El jugador empieza una partida y el juego obtiene una **semilla de partida**.
2. El director pide al generador procedural **30 niveles candidatos** a partir de esa semilla.
3. Un modelo **predice la dificultad** de cada candidato.
4. El director ordena los candidatos por cercanía a la **dificultad objetivo** y **elige al azar uno de los 5 más cercanos**.
5. El juego carga ese nivel.

La dificultad objetivo tiene tres valores: `Easy`, `Normal` y `Hard`, que corresponden a los percentiles 25, 50 y 75 de `damage_total_mean` en los datos de entrenamiento: **9.8, 13.4 y 17.8**. La "dificultad" de un nivel es el daño medio que recibe un bot al recorrerlo entero (sección 2.4).

### Por qué es aprendizaje automático

Es **aprendizaje supervisado** (regresión):

- Hay **ejemplos**: 5000 niveles, cada uno descrito por 14 números (sus *características*) y con una **etiqueta** medida: la dificultad que se observó al jugarlo.
- Un algoritmo **ajusta los parámetros del modelo a partir de esos ejemplos** (en la regresión lineal, minimizando el error cuadrático). Nadie escribió a mano la relación entre, por ejemplo, el número de enemigos por sala y el daño.
- El modelo **generaliza**: se evalúa con niveles que no usó para aprender (conjunto de prueba y conjunto de confirmación, sección 3), y en ellos sigue prediciendo bien.

Una distinción importante: **las etiquetas se miden, no se calculan con una fórmula de las características**. Si la etiqueta fuera una función de las mismas 14 características, el modelo solo "redescubriría" esa fórmula y no habría aprendizaje real. Aquí la etiqueta viene de simular partidas completas, que depende de la geometría de las salas, el movimiento de los enemigos, los choques y el comportamiento del bot.

### Qué no es

No es IA generativa ni usa servicios externos. El modelo cabe en un archivo JSON de unos 3 KB (14 coeficientes por etiqueta más la normalización) y se evalúa en C# dentro del juego. El entrenamiento se hizo una vez, fuera del juego, con Python.

---

## 2. Flujo completo

```
 semilla
    │
    ▼
┌────────────┐   ┌────────────┐   ┌─────────────┐
│ Generador  │──▶│ Validador  │──▶│ 14 métricas │──────────────┐
│ procedural │   │ de niveles │   │ del nivel   │              │
└────────────┘   └────────────┘   └─────────────┘              │
                                        │                      │
   ┌────────────────────────────────────┘                      │
   ▼ (solo para construir el dataset)                          │
┌───────────────────┐     ┌────────────────┐                   │
│ Simulador + bot   │────▶│ Etiquetas      │                   │
│ (5 partidas/nivel)│     │ (daño, tiempo) │                   │
└───────────────────┘     └────────────────┘                   │
                                │                              │
                                ▼                              │
                       ┌──────────────────┐                    │
                       │ Entrenamiento y  │                    │
                       │ validación       │                    │
                       └──────────────────┘                    │
                                │                              │
                                ▼                              ▼
                       ┌──────────────────┐        ┌────────────────────┐
                       │ Exportación JSON │───────▶│ Director en el     │
                       │ (modelo)         │        │ juego (C# puro)    │
                       └──────────────────┘        └────────────────────┘
```

### 2.1 Generador procedural (`scripts/generation/DungeonGenerator.cs`)

Construye un nivel a partir de una semilla, solo con datos (no toca escenas):

- Crece sobre una cuadrícula desde la sala inicial, añadiendo salas vecinas al azar **solo si la celda nueva tiene un único vecino ya existente**. Así el mapa es un árbol: sin bucles ni racimos.
- Marca los tipos de sala: inicio, normal, **jefe** (la sala sin salida más lejana del inicio, medida en pasos) y **tesoro** (otra sala sin salida).
- Asigna a cada sala una **plantilla** del catálogo (`resources/rooms/templates/`): tamaño y puntos de aparición de enemigos.
- Es **determinista**: la misma semilla y los mismos parámetros dan siempre el mismo nivel. Usa un generador de números aleatorios propio y sembrado, sin depender del orden de diccionarios.

### 2.2 Validador (`scripts/generation/LevelValidator.cs`)

Comprueba que el nivel se pueda completar: sin celdas duplicadas, todas las salas alcanzables desde el inicio, camino al jefe, distancia mínima al jefe, sin salas aisladas y puntos de aparición dentro de cada sala y lejos de los puntos de llegada del jugador. Si un nivel no es válido, el generador reintenta con una semilla derivada. Es el *LevelValidator* del documento maestro.

### 2.3 Métricas del nivel (`scripts/ai/LevelMetrics.cs`)

Resume cada nivel en **14 números**, calculados solo con los datos del mapa y de forma determinista. Su nombre y su orden están fijados y versionados, porque el modelo depende de ese orden exacto (`feature_set_version` = 1).

| Característica | Qué mide |
|---|---|
| `room_count` | número de salas |
| `boss_distance` | pasos del inicio al jefe |
| `dead_end_count` | salas sin salida (sin contar el inicio) |
| `branching_factor` | hijos medios por sala interna |
| `total_enemies` | enemigos de todo el nivel |
| `enemies_per_room_mean`, `enemies_per_room_std` | media y desviación de enemigos por sala |
| `enemy_density` | enemigos por metro cuadrado |
| `room_area_cv` | variación del tamaño de las salas |
| `critical_path_enemies`, `critical_path_enemy_ratio` | enemigos en el camino al jefe, en cifra y como fracción |
| `treasure_distance` | pasos del inicio al tesoro |
| `template_variety` | plantillas distintas usadas / número de salas |
| `adjacent_same_template_ratio` | fracción de salas vecinas con la misma plantilla |

### 2.4 Simulador y bot (`scripts/ai/simulation/`)

Para obtener las etiquetas hace falta **medir qué tan difícil es un nivel**. Hacerlo con personas sería lento y caro, así que se construyó un **simulador determinista en C# puro**, sin escenas ni física del motor, que replica las reglas del juego: velocidad del jugador, cadencia y alcance de las lágrimas, vida, velocidad y daño de los enemigos, invulnerabilidad del jugador, paredes y puertas cerradas hasta despejar. Los valores numéricos **se leen de las mismas escenas que usa el juego**, para que no se desincronicen.

Un **bot de habilidad fija y reproducible** juega los niveles: recorre todas las salas (la rama del jefe al final), se mantiene a distancia de los enemigos, se alinea para disparar en las 4 direcciones y tiene un pequeño ruido de reacción. Juega con **vida ilimitada** para no truncar la medición.

Dos controles de calidad:

- **Prueba de fidelidad** (`tests/FidelityCheck`): el mismo bot juega los mismos niveles en el simulador y en el juego real, con el bot controlando al jugador real, y se comparan los resultados. Se exige una correlación de Spearman de al menos 0.7 antes de etiquetar nada.
- **Prueba de esquinas** (`tests/CornerTrapTest`): detectó que un jugador en una esquina con un enemigo en la diagonal quedaba atrapado. Se resolvió con un retroceso del enemigo tras un golpe que hace daño, y el simulador replica esa regla (versión 2).

### 2.5 Etiquetas (`tests/LabelExport`)

Cada nivel se juega **5 veces con semillas de bot distintas** y se guardan la media y la desviación de cada medida. La **etiqueta principal** es `damage_total_mean` (daño total recibido); la **secundaria**, `time_total_mean` (tiempo total). También se guardan el daño recibido en el camino al jefe (`damage_critical_mean`) y el tiempo hasta llegar a él (`time_to_boss_mean`), y dos columnas auxiliares (`death_rate`, `timeouts`) que **no se usan como etiqueta**.

### 2.6 Entrenamiento y validación (`ai-training/training/`)

Scripts de Python reproducibles (`random_state = 42`). Comparan cinco modelos con **validación cruzada anidada** y eligen uno con la regla del error estándar (secciones 4 y 5). El conjunto de prueba no interviene en ninguna elección.

### 2.7 Exportación y paridad

`export_model.py` escribe el modelo en `ai-training/models/difficulty_v1.json`, con una copia en `resources/ai/difficulty_v1.json` para que el juego la lea y viaje en el ejecutable (la carpeta `ai-training` está oculta al motor). El formato admite regresión lineal y árboles; el código de árboles, aunque hoy no se usa, está probado con un fixture. La prueba `tests/ModelParityTest` comprueba que **C# reproduce las predicciones de Python**.

### 2.8 Director en el juego (`scripts/ai/AIDungeonDirector.cs`)

Es el componente descrito en la sección 1. Todo es **determinista dada la semilla de partida**: las semillas de los 30 candidatos salen de ella con una función hash, y el azar de la elección entre los 5 mejores se siembra con la misma semilla. La misma semilla y la misma dificultad producen siempre el mismo nivel. El director solo depende del generador, de las métricas y del modelo: **no usa el simulador ni el bot**, que son herramientas de desarrollo.

En el juego, `RunManager` usa el director y el HUD muestra la semilla y la dificultad objetivo (Fácil, Normal o Difícil) frente a la predicha. El jugador elige la dificultad en el menú principal (también en Release) y esa elección es el objetivo `Easy`, `Normal` o `Hard` del director. En compilaciones de depuración, N cambia la semilla, R la repite y T cambia la dificultad.

---

## 3. Datos y conjuntos

| Conjunto | Semillas del generador | Niveles | Para qué sirve |
|---|---|---|---|
| Entrenamiento | 1..5000 | 5000 | ajustar los modelos y elegir hiperparámetros (con validación cruzada) |
| Prueba | 100001..101000 | 1000 | medir el rendimiento del modelo ya elegido |
| Confirmación | 200001..201000 | 1000 | segunda medida, hecha al final, con todo ya fijado |

Además, el *benchmark* del director usa **300 semillas de partida nuevas (300001..300300)**, cada una con sus 30 candidatos.

### Por qué se separan

Un modelo evaluado con los mismos datos con los que aprendió casi siempre parece mejor de lo que es. Por eso:

- **Validación cruzada solo sobre el entrenamiento.** Los datos de entrenamiento se dividen en 5 partes; en cada ronda se entrena con 4 y se mide en la que queda fuera. Los hiperparámetros se eligen con una validación interna de 3 partes dentro de cada ronda (validación cruzada *anidada*), de modo que las métricas no quedan infladas por haber elegido sobre los mismos datos.
- **Conjunto de prueba, usado para medir y no para elegir.** El modelo ya estaba fijado cuando se evaluó. El script de evaluación deja un marcador y se niega a repetirse sin `--force`. Se ejecutó más de una vez con `--force` para ajustar el informe (formato, diagnósticos del director y gráficas); esas repeticiones no cambiaron ninguna elección, porque los modelos los fija `train.py` antes de leer nada de la prueba.
- **Conjunto de confirmación, evaluado una sola vez** con el JSON exacto que usa el juego y sin ajustar nada con su resultado. Es la medida más honesta de cuánto se puede esperar con niveles nuevos.

### Comprobación de que no hay solapamiento

- Semillas efectivas en común: **0** entre entrenamiento y prueba, entre entrenamiento y confirmación, y entre prueba y confirmación (`data_checks` y `confirmation_metrics.json`). La semilla efectiva es la que reproduce exactamente un nivel tras los reintentos del generador.
- Vectores de características idénticos (niveles **distintos** con las 14 métricas iguales): **7** filas de prueba con una de entrenamiento; **14** de confirmación con entrenamiento y **2** con prueba. No se descartaron. No son el mismo nivel, pero sirven para recordar que 14 agregados no determinan el nivel por completo (sección 7).
- El *benchmark* del director comprobó que ninguna semilla efectiva de sus candidatos aparece en los CSV de los tres conjuntos.

---

## 4. Modelos probados y por qué ganó la regresión lineal

Se probaron cinco modelos, con una búsqueda pequeña de hiperparámetros para cada familia con parámetros:

- **Media del entrenamiento**: predice siempre el promedio. Es la línea base: lo mínimo que debe superar cualquier modelo.
- **Regresión lineal**: una suma ponderada de las 14 características normalizadas.
- **Ridge**: regresión lineal con penalización sobre los coeficientes (`alpha` ∈ {0.01, 0.1, 1, 10, 100, 1000}).
- **Gradient Boosting**: muchos árboles pequeños, cada uno corrige los errores del anterior.
- **Random Forest**: 200 árboles entrenados con submuestras de datos y características.

### Resultados en validación cruzada anidada (solo entrenamiento)

**Etiqueta principal, `damage_total_mean`**

| Modelo | MAE | RMSE | R² | Spearman | Parámetros exportables |
|---|---|---|---|---|---|
| Media | 4.673 | 5.841 | −0.001 | −0.040 | 1 |
| **Regresión lineal** | **2.929** | **3.775** | **0.582** | **0.762** | **43** |
| Ridge (`alpha` = 1) | 2.929 | 3.774 | 0.582 | 0.762 | 43 |
| Gradient Boosting | 3.003 | 3.838 | 0.568 | 0.748 | 5600 |
| Random Forest | 3.037 | 3.879 | 0.559 | 0.743 | 97 538 |

**Etiqueta secundaria, `time_total_mean`**

| Modelo | MAE | RMSE | R² | Spearman | Parámetros exportables |
|---|---|---|---|---|---|
| Media | 14.089 | 16.877 | −0.000 | −0.015 | 1 |
| **Regresión lineal** | **3.074** | **3.914** | **0.946** | **0.974** | **43** |
| Ridge (`alpha` = 1) | 3.073 | 3.914 | 0.946 | 0.974 | 43 |
| Gradient Boosting | 3.169 | 4.030 | 0.943 | 0.973 | 2758 |
| Random Forest | 3.314 | 4.214 | 0.938 | 0.970 | 96 970 |

(Los "parámetros exportables" son los números que habría que guardar en el JSON: 43 en un modelo lineal y el total de nodos en los modelos de árboles.)

### Por qué ganó la lineal

Se aplicó la **regla del error estándar**: entre todos los modelos cuyo RMSE de validación cruzada queda a menos de 1 error estándar del mejor, se elige el más simple.

- Para el daño, Ridge es el mejor por RMSE (3.7745), pero la lineal (3.7746) queda a menos de 1 error estándar (0.0195) y es más simple. Gradient Boosting (3.838) y Random Forest (3.879) quedan **fuera** de ese margen (umbral 3.794).
- Para el tiempo, ocurre lo mismo (Ridge 3.9136, lineal 3.9138, margen 0.0244). Gradient Boosting (4.030) y Random Forest (4.214) quedan fuera (umbral 3.938).

En resumen: **los modelos de árboles no mejoran a la lineal, y tienen entre 64 y 2268 veces más parámetros** (2758 o 5600 nodos en Gradient Boosting y unos 97 000 en Random Forest, frente a 43). Con estos datos, la limitación no es la capacidad del modelo sino la información que contienen las 14 características (sección 7). La lineal, además, es transparente (cada coeficiente se puede leer), se exporta en un JSON de unos 3 KB y se evalúa en C# con unas pocas operaciones.

Se eligió un modelo por etiqueta; en ambos casos fue la regresión lineal.

### Qué características pesan

La importancia por permutación (cuánto cae el R² en la prueba al barajar una característica) muestra que el modelo se apoya sobre todo en la cantidad de enemigos:

- Daño: `enemies_per_room_mean` (1.97), `total_enemies` (0.92) y `room_area_cv` (0.78).
- Tiempo: `total_enemies` (0.91) y `room_count` (0.30).

Valores superiores a 1 aparecen porque esas características están muy correlacionadas entre sí (`enemies_per_room_mean` es casi `total_enemies / room_count`); conviene leerlas como un ranking y no como cifras absolutas.

---

## 5. Resultados

### 5.1 Precisión de las predicciones

**Conjunto de prueba (1000 niveles) y conjunto de confirmación (1000 niveles)**

| Etiqueta | Conjunto | MAE | RMSE | R² | Spearman |
|---|---|---|---|---|---|
| `damage_total_mean` | Prueba | 2.913 | 3.760 | **0.578** | 0.762 |
| `damage_total_mean` | Confirmación | 3.005 | 3.888 | **0.528** | 0.727 |
| `time_total_mean` | Prueba | 3.075 | 3.942 | **0.942** | 0.970 |
| `time_total_mean` | Confirmación | 3.158 | 4.023 | **0.940** | 0.970 |

Para comparar, predecir siempre la media da en la prueba un MAE de 4.645 y R² ≈ 0 para el daño, y un MAE de 13.574 para el tiempo.

- **El tiempo se predice muy bien** y de forma estable entre conjuntos (R² 0.942 y 0.940).
- **El daño se predice con más dificultad.** En validación cruzada el R² fue 0.582, en prueba 0.578 y en confirmación 0.528. La caída de 0.05 entre prueba y confirmación no se puede atribuir con seguridad a una causa: puede ser variación de muestreo con 1000 niveles, o que la prueba salió algo favorable. Por eso se recomienda tomar **el rango 0.53–0.58** como expectativa realista para el daño.

### 5.2 R² frente al techo estimado

La estimación del techo sale de la varianza dentro de los 47 grupos de entrenamiento con vector de características idéntico: niveles que el modelo no puede distinguir. Es una **estimación aproximada, no un límite exacto**.

| Etiqueta | Techo aproximado | R² en prueba | R² en confirmación |
|---|---|---|---|
| `damage_total_mean` | ~0.73 | 0.578 (≈ 79 % del techo) | 0.528 (≈ 72 %) |
| `time_total_mean` | ~0.96 | 0.942 (≈ 98 %) | 0.940 (≈ 98 %) |

En el tiempo, el modelo ya está casi en el techo. En el daño queda margen que las 14 características actuales no permiten aprovechar.

### 5.3 El director frente al azar y al oráculo

**Cómo se mide.** Para un objetivo de dificultad, cada estrategia elige un nivel de un grupo de 30 y se calcula el **error = |dificultad real del nivel elegido − objetivo|**. La dificultad real es la medida por el bot. Las estrategias:

- **Azar**: un nivel cualquiera del grupo (se calcula la media exacta de los 30 errores).
- **Director** (el del juego): azar entre los 5 niveles con predicción más cercana al objetivo.
- **Siempre el mejor**: el nivel con la predicción más cercana.
- **Oráculo**: el nivel cuya dificultad *real* está más cerca del objetivo. Es una cota superior: requiere medir con el simulador los 30 candidatos, algo imposible durante el juego.

**Benchmark con partidas nuevas.** 300 semillas de partida (300001..300300) × 3 objetivos × 30 candidatos; la dificultad real de los 9000 candidatos se midió con el simulador y el bot (5 partidas por nivel). Error medio, con el error estándar entre partidas:

| Objetivo | Azar | Director | Siempre el mejor | Oráculo |
|---|---|---|---|---|
| Easy (9.8) | 5.541 | 2.682 | 2.861 | 0.289 |
| Normal (13.4) | 4.571 | 2.905 | 2.785 | 0.227 |
| Hard (17.8) | 5.751 | 3.351 | 3.303 | 0.376 |
| **Promedio** | **5.288 ± 0.031** | **2.980 ± 0.077** | **2.983 ± 0.075** | **0.297 ± 0.010** |

- El director **reduce el error un 43.7 % frente al azar** (de 5.288 a 2.980 puntos de daño).
- Recorre el **46 % de la distancia** entre el azar y el oráculo.
- Es prácticamente igual que elegir siempre el mejor (43.6 %). La diferencia de 0.003 puntos entre ambos es mucho menor que el error estándar.
- Por objetivo, la reducción es del 51.6 % en Easy, 36.4 % en Normal y 41.7 % en Hard.

**Comprobación con el conjunto de prueba.** El mismo experimento con 2000 grupos de 30 niveles de la prueba da resultados muy parecidos (error medio: azar 5.316, director 2.984, siempre el mejor 3.131, oráculo 0.278; reducción del 43.9 % para el director y del 41.1 % para el mejor).

**Un resultado inesperado.** En esa prueba, elegir al azar entre los 5 mejores dio algo *menos* error que elegir siempre el mejor (2.984 frente a 3.131). Al mirar el error según la posición del ranking, baja del puesto 1 (3.131) al puesto 4 (2.871) y sube después. Un *bootstrap* sobre los niveles de prueba (200 remuestreos) da, para esa diferencia, un intervalo del 95 % de −0.42 a +0.15. El intervalo **incluye el cero**, así que lo que se puede afirmar es que **la variedad que da el azar entre los 5 mejores no cuesta nada medible en precisión**. El benchmark nuevo, con 300 partidas, confirma que ambas estrategias dan prácticamente el mismo error.

---

## 6. Variedad de niveles

El objetivo del proyecto es que **cada partida sea distinta**. El *benchmark* midió la variedad con **300 semillas de partida distintas y el mismo objetivo**:

| Estrategia | Niveles distintos (estructura completa) | Salas compartidas entre dos niveles cualesquiera, media (máximo) |
|---|---|---|
| Candidato sin elegir | 300 / 300 | 0.34 (5) |
| Director | 300 / 300 en los tres objetivos | 0.36–0.39 (5) |
| Siempre el mejor | 300 / 300 | 0.35–0.40 (5–6) |

- "Estructura completa" incluye celdas, tipos, plantillas, tamaños y puntos de aparición. "Salas compartidas" cuenta las celdas donde dos niveles tienen una sala con el mismo tipo y la misma plantilla, sin contar la sala inicial, que es siempre igual.
- Con niveles de 8 a 12 salas (configuración del generador, ver el `.meta.json` de los datasets), **dos niveles cualesquiera comparten menos de media sala de media**. Solo entre el 0.6 % y el 1.0 % de los pares comparten 3 o más. Por forma (celdas y tipos) también hay 300 de 300 distintos, salvo un caso en "siempre el mejor" con objetivo Hard (299).
- El director **no reduce la variedad** frente a un nivel sin elegir: la media de salas compartidas apenas cambia (0.34 frente a 0.36–0.39).

### De dónde viene la variedad

La variedad **viene de que la semilla de partida cambia los 30 candidatos**: cada semilla da un conjunto de candidatos distinto, así que ya cambia el nivel, sea cual sea la estrategia de elección.

Por eso el **azar entre los 5 mejores no añade variedad medible entre partidas distintas**: "siempre el mejor" también da 300 niveles distintos con 300 semillas distintas. Su valor sería otro: ante *el mismo* conjunto de candidatos, no devolver siempre el mismo nivel. Hoy no ocurre, porque la elección está sembrada con la semilla de partida: **la misma semilla y la misma dificultad dan siempre el mismo nivel**, lo cual es deseable para poder repetir una partida (sección 13 del documento maestro, *Reproducibilidad*). Dentro de una partida, las opciones que ve el director tampoco se parecen entre sí: los 5 mejores comparten de media 0.35 salas, igual que los 30 candidatos (0.35).

---

## 7. Limitaciones

1. **La dificultad la mide un bot, no personas.** El bot tiene habilidad fija y juega con vida ilimitada. "Difícil" significa "el bot recibe mucho daño". No se ha contrastado con jugadores humanos, que se mueven, apuntan y se equivocan de otra manera.
2. **El simulador subestima el daño real en torno a un 12 %** respecto al juego real (prueba de fidelidad). La relación de orden entre niveles se conserva (la prueba exige Spearman ≥ 0.7), pero los valores absolutos de las etiquetas están ligeramente por debajo de lo que ocurriría en el juego.
3. **Un solo tipo de enemigo y un solo mundo.** Todos los niveles usan el mismo enemigo básico, y la dificultad que describen las métricas es sobre todo de *cantidad*. Cuando haya más tipos de enemigos, objetos y jefes, hará falta ampliar las características, reetiquetar y reentrenar.
4. **El modelo comprime el rango de dificultad.** Sobrestima los niveles fáciles y subestima los difíciles. En la prueba, en el tramo de daño real más bajo (2.0–8.8) el sesgo es de **+2.49** puntos; en el más alto (19.0–38.6), de **−3.78**, con un MAE de 4.32 frente a 2.3–2.8 en los tramos centrales. Por eso el director es menos fiable en los extremos: con el objetivo Hard el error del director es el mayor (3.351, frente a 2.682 en Easy).
5. **El daño se predice peor que el tiempo, y queda lejos del techo.** El R² del daño es 0.578 en prueba y 0.528 en confirmación, frente a un techo aproximado de 0.73. Las 14 características son agregados del nivel y no ven la disposición exacta de las salas. Niveles distintos con las mismas 14 métricas (7 casos entre prueba y entrenamiento) pueden tener dificultades distintas.
6. **Datos de un solo generador.** Prueba y confirmación salen del mismo generador y de las mismas plantillas que el entrenamiento. Miden la generalización a niveles nuevos del mismo tipo, **no** a niveles de otro estilo.
7. **Muestras limitadas.** La prueba y la confirmación tienen 1000 niveles cada una; el benchmark, 300 partidas. Los 2000 grupos de la métrica del director reutilizan los mismos 1000 niveles, de modo que no son independientes: por eso se presenta un *bootstrap* y no un intervalo simple. Además, cada etiqueta es la media de 5 partidas del bot y tiene ruido propio, que pone un suelo al error que cualquier modelo puede alcanzar.
8. **La importancia por permutación está inflada** por la correlación entre características (sección 4).
9. **La elección entre los 5 mejores no se evaluó con jugadores.** Se mide con el bot; la percepción de dificultad de una persona puede no coincidir.

---

## 8. Mejoras futuras

- **Características que describan la disposición.** El techo aproximado del daño (0.73) frente al R² logrado (0.53–0.58) sugiere que falta información. Por ejemplo: enemigos por sala a lo largo del camino al jefe, tamaño de las salas con más enemigos o patrones de aparición, en lugar de solo agregados.
- **Corregir la compresión del rango**, por ejemplo con calibración posterior de las predicciones o dando más peso a los extremos al entrenar, para que Easy y Hard sean más fiables.
- **Etiquetas con jugadores reales.** Recoger datos de partidas humanas y compararlos con los del bot, o reetiquetar con ellos.
- **Acercar el simulador al juego** para reducir la subestimación del daño, y repetir la prueba de fidelidad.
- **Más contenido**: más tipos de enemigos, jefes y mundos, con un modelo por mundo (la arquitectura del documento maestro lo prevé).
- **Director adaptativo.** El documento maestro (sección 7.4) plantea que el director use también la salud del jugador, su progreso y los objetos que tiene. Se puede usar el mismo modelo y cambiar la dificultad objetivo entre niveles de una misma partida.
- **Más candidatos o mejor estrategia de elección**, y medir si un grupo mayor de 30 reduce el error.
- **Modelos más complejos solo si hay más información**: los árboles no ganaron con las características actuales, pero podrían hacerlo con características más ricas. El formato de exportación ya los admite.

---

## 9. Cómo reproducir todo

Todos los comandos se ejecutan desde la raíz del repositorio. `godot` es la consola de Godot 4.7.2 edición .NET (en Windows, `Godot_v4.7.2-stable_mono_win64_console.exe`). Las salidas deterministas se pueden comprobar repitiendo el comando y comparando el hash del archivo resultante.

### 9.1 Compilar y preparar el entorno

```
dotnet build

python -m venv ai-training/.venv
ai-training/.venv/Scripts/python.exe -m pip install -r ai-training/requirements.txt
```

### 9.2 Prueba del generador

```
godot --headless --path . res://tests/GeneratorStressTest.tscn
```

Genera 1000 semillas, valida cada nivel y comprueba el determinismo.

### 9.3 Datasets de características

```
# entrenamiento (por defecto: 5000 niveles, semilla inicial 1)
godot --headless --path . res://tests/DatasetExport.tscn

# prueba
godot --headless --path . res://tests/DatasetExport.tscn -- --first-seed=100001 --count=1000 --out=ai-training/datasets/levels_v1_test.csv

# confirmación
godot --headless --path . res://tests/DatasetExport.tscn -- --first-seed=200001 --count=1000 --out=ai-training/datasets/levels_v1_confirm.csv
```

### 9.4 Controles del simulador (opcional, antes de etiquetar)

```
godot --headless --fixed-fps 60 --path . res://tests/FidelityCheck.tscn
godot --headless --fixed-fps 60 --path . res://tests/CornerTrapTest.tscn
```

### 9.5 Etiquetas (simulador y bot)

```
# entrenamiento
godot --headless --path . res://tests/LabelExport.tscn

# prueba
godot --headless --path . res://tests/LabelExport.tscn -- --first-seed=100001 --count=1000 --out=ai-training/datasets/levels_v1_test_labels.csv

# confirmación
godot --headless --path . res://tests/LabelExport.tscn -- --first-seed=200001 --count=1000 --out=ai-training/datasets/levels_v1_confirm_labels.csv
```

### 9.6 Entrenamiento, evaluación y exportación

```
ai-training/.venv/Scripts/python.exe ai-training/training/train.py
ai-training/.venv/Scripts/python.exe ai-training/training/evaluate.py
ai-training/.venv/Scripts/python.exe ai-training/training/export_model.py
```

- `train.py` produce `reports/cv_results.json` y la gráfica de comparación de modelos (unos 3 minutos).
- `evaluate.py` evalúa **una sola vez** sobre la prueba y produce `reports/metrics.json` y las gráficas. Para reproducir el mismo resultado hay que usar `--force`; no se debe usar para elegir otro modelo.
- `export_model.py` escribe `ai-training/models/difficulty_v1.json`, la copia `resources/ai/difficulty_v1.json` y las predicciones de Python sobre la prueba.

### 9.7 Paridad con C#, benchmark del director y confirmación

```
# C# reproduce las predicciones de Python
godot --headless --path . res://tests/ModelParityTest.tscn

# niveles aleatorios frente a niveles elegidos por el director (unos 2 minutos)
godot --headless --path . res://tests/DirectorBenchmark.tscn

# evaluación única de confirmación (se niega a repetirse sin --force)
ai-training/.venv/Scripts/python.exe ai-training/training/evaluate_confirmation.py
```

Los resultados quedan en `ai-training/reports/` (`director_benchmark.json` y `confirmation_metrics.json`).

### 9.8 Jugar con el director

Abrir el proyecto en Godot y pulsar F5 (arranca el menú principal, donde se elige la dificultad). Lanzar `TestLevel.tscn` directo con F6 usa la dificultad del inspector de `RunManager`. En compilaciones de depuración: **N** cambia la semilla de partida, **R** la repite y **T** cambia la dificultad (Easy, Normal, Hard). También se pueden fijar por línea de comandos:

```
godot --path . -- --seed=42 --difficulty=Hard
```

Desde la versión 2 de la IA (sección 11), el generador por defecto es el v2; para jugar con el que describe esta sección (v1) se añade `--generator=1` o se pulsa G en depuración.

### Determinismo

Todo el flujo es reproducible: semillas fijas en el generador y el simulador, `random_state = 42` en el entrenamiento y salidas sin marcas de tiempo. Repetir cada paso con los mismos argumentos produce archivos idénticos byte a byte.

---

## 10. Anexo: glosario de métricas y archivos

### Métricas

- **MAE** (error absoluto medio): cuánto se equivoca el modelo de media, en las mismas unidades que la etiqueta (puntos de daño o segundos). Menos es mejor.
- **RMSE**: parecido al MAE, pero penaliza más los errores grandes.
- **R²**: fracción de la variación de la etiqueta que explica el modelo. 1 es perfecto; 0 equivale a predecir siempre la media.
- **Spearman**: correlación entre los *órdenes* de los valores reales y los predichos. Mide si el modelo ordena bien los niveles aunque se equivoque en el valor exacto. Es lo que importa al director, que compara candidatos entre sí.
- **Validación cruzada**: dividir los datos de entrenamiento en partes y medir siempre en una parte que el modelo no vio.
- **Error estándar**: medida de cuánto varía una métrica entre repeticiones; sirve para decidir si dos modelos se distinguen de verdad.

### Archivos principales

| Archivo | Contenido |
|---|---|
| `scripts/generation/` | generador, validador, plantillas y reglas de colocación |
| `scripts/ai/LevelMetrics.cs` | las 14 características |
| `scripts/ai/DifficultyModel.cs` | lee y evalúa el modelo exportado |
| `scripts/ai/AIDungeonDirector.cs` | director de IA |
| `scripts/ai/simulation/` | simulador, bot y parámetros leídos de las escenas (solo desarrollo) |
| `tests/` | generación de datasets, etiquetas, fidelidad, paridad y benchmark |
| `ai-training/training/` | entrenamiento, evaluación, exportación y confirmación |
| `ai-training/datasets/` | características, etiquetas y sus `.meta.json` |
| `ai-training/models/difficulty_v1.json` | modelo exportado (copia del juego en `resources/ai/`) |
| `ai-training/reports/` | `metrics.json`, `cv_results.json`, `confirmation_metrics.json`, `director_benchmark.json` y gráficas |

---

## 11. Versión 2: tipos de enemigo

La versión 2 del generador añade salas con varios tipos de enemigo:
- el **tirador** mantiene la distancia, avisa antes de disparar y lanza proyectiles lentos;
- el **explosivo** persigue, enciende una mecha al acercarse y explota con daño en área;
- el **volador** se mueve con un patrón senoidal.

El modelo v1 no distingue tipos de enemigo, así que la versión 2 de la IA repite todo el proceso con su propio conjunto de características, sus propios datos y su propio modelo.

**Regla de oro.** Todo lo de la versión 1 queda intacto y reproducible. Se volvieron a generar el dataset de características, las etiquetas y el benchmark v1 con el código final, y sus SHA-256 son idénticos a los originales:

| Archivo | SHA-256 |
|---|---|
| `levels_v1.csv` | `a30d3a36…` |
| `levels_v1_labels.csv` | `54b79ccf…` |
| `director_benchmark.json` | `5921a2fa…` |

Todo lo nuevo lleva nombre propio: `LevelMetricsV2`, `levels_v2*.csv`, `difficulty_v2.json` y los informes en `ai-training/reports/v2/`.

### 11.1 Características v2 (`scripts/ai/LevelMetricsV2.cs`)

Son **22**: las 14 de la versión 1, en el mismo orden y calculadas por el mismo código, seguidas de 8 nuevas (`feature_set_version` = 2).

| Característica | Qué mide |
|---|---|
| `shooter_count`, `exploder_count`, `flyer_count` | tiradores, explosivos y voladores del nivel |
| `ranged_enemy_fraction` | tiradores / total de enemigos |
| `special_enemy_fraction` | enemigos no básicos / total de enemigos |
| `mixed_room_count` | salas con dos o más tipos de enemigo |
| `critical_path_special_enemies` | enemigos no básicos en el camino al jefe |
| `kind_variety` | tipos de enemigo distintos en el nivel |

No hay un conteo de básicos, porque sería el total menos los otros tres. El resumen del dataset de entrenamiento (`DatasetExport`) **no marcó ninguna característica como constante ni como casi duplicada** (umbral de correlación 0.98), así que no se quitó ninguna. Las correlaciones más altas fueron:
- `shooter_count` y `ranged_enemy_fraction`: 0.951;
- `boss_distance` y `critical_path_enemies`: 0.914.

### 11.2 Simulador y bot

**Simulador (versión 3).** Replica los tres tipos nuevos y lee sus números de las mismas escenas que el juego:
- **Tirador:** banda de distancia, desplazamiento lateral cuando queda bloqueado, aviso, disparo y proyectil.
- **Explosivo:** mecha, movimiento reducido durante la mecha y explosión en área solo contra el jugador.
- **Volador:** usa las mismas funciones del patrón que el juego y no choca con otros enemigos.
- **Proyectiles:** chocan con paredes, puertas cerradas y el jugador, respetando su invulnerabilidad.

El enemigo básico sigue por la misma rama de código que en la versión 2 del simulador. Por eso las etiquetas v1 salen idénticas byte a byte, y los artefactos v1 siguen declarando la versión 2 del simulador. Las aproximaciones nuevas están documentadas en la cabecera de `LevelSimulator.cs`.

**Bot.** Con tipos de enemigo:
- **Prioridad fija de objetivos:**
  1. un explosivo a 6 m o menos;
  2. un tirador;
  3. un volador;
  4. un básico o un explosivo lejano.

  Los empates se resuelven por distancia.
- **Huida:** se aleja de los explosivos con la mecha encendida.
- **Esquiva:** se aparta de los proyectiles cuya trayectoria pasa cerca.

La huida y la esquiva usan **la misma observación retrasada** (6 ± 3 fotogramas) y el mismo ruido que el resto de sus decisiones, para que no sea perfecto. Con solo enemigos básicos y sin proyectiles, el bot toma exactamente las mismas decisiones que en la versión 1.

**El bot define qué es "difícil".** La dificultad que se mide es la de *este* bot: un bot que esquivara todo haría inofensivo al tirador, y uno que no esquivara nada lo haría el enemigo más peligroso.

### 11.3 Fidelidad y criterio de aprobación

Primera prueba: 30 niveles del generador v2 × 3 semillas de bot, simulador frente a juego real.
- **Spearman:** superó la puerta, con 0.836 en daño y 0.980 en tiempo.
- **Sesgo por composición:** falló la puerta adicional de ±30 %. En salas con explosivo el simulador daba un 33.3 % menos de daño.
- **Diagnóstico por fuente:** la explosión dio 0 de daño en ambos lados, y todo el déficit era **daño por contacto de enemigos básicos** (23 frente a 36). No se etiquetó nada.

Se decidió entonces repetir la prueba con 90 niveles (los 30 anteriores y 60 nuevos) y fijar un criterio **antes de ver esos datos**:
- **a)** Spearman de daño y de tiempo ≥ 0.7.
- **b)** Explosión y proyectiles: con al menos 30 golpes reales de esa fuente, sesgo dentro de ±30 %. Con menos, "sin evidencia suficiente": no bloquea, pero se documenta como limitación.
- **c)** El sesgo del contacto no puede ser peor que el de v1 (aprox. −15 %) en más de 10 puntos.
- **d)** Sesgo de tiempo por composición de sala dentro de ±30 %.

Resultados con 90 niveles × 3 bots (270 partidas por lado):

| Fuente | Golpes sim | Golpes reales | Sesgo | IC 95 % (bootstrap sobre niveles) | Criterio |
|---|---|---|---|---|---|
| Contacto | 3324 | 3817 | −12.9 % | [−16.6 %, −9.2 %] | c) cumple (límite ±25 %) |
| Proyectiles | 9 | 3 | +200 % | [0 %, +1000 %] | b) sin evidencia suficiente |
| Explosión | 1 | 0 | — | — | b) sin evidencia suficiente |

| Composición de sala | Salas | Sesgo de tiempo | Sesgo de daño (informativo) |
|---|---|---|---|
| Solo básicos | 1497 | −3.2 % | −13.2 % |
| Con tirador | 606 | −0.8 % | −11.0 % |
| Con explosivo | 336 | −1.4 % | −27.8 % |
| Con volador | 375 | −1.3 % | −19.4 % |

- **Spearman:** 0.850 en daño total y 0.985 en tiempo total.
- **Veredicto:** se cumplen los cuatro criterios.

### 11.4 Datos

Mismo protocolo y mismas semillas del generador que en la versión 1, ahora con el generador v2.

| Conjunto | Semillas | Niveles |
|---|---|---|
| Entrenamiento | 1..5000 | 5000 |
| Prueba | 100001..101000 | 1000 |
| Confirmación | 200001..201000 | 1000 |

- **Solapamiento por semilla efectiva:** 0 entre cualquier par de conjuntos (`reports/v2/data_checks.json`).
- **Vectores de características idénticos (informativo):** 2 filas de prueba con una de entrenamiento, 1 de confirmación con entrenamiento y 0 entre confirmación y prueba.
- **Determinismo:** cada CSV de características y de etiquetas, y su `.meta.json`, se generó dos veces, y las dos ejecuciones son idénticas byte a byte.
- **Etiqueta principal en el entrenamiento** (`damage_total_mean`): media 12.43 y rango 1.6–34.4. Su fiabilidad frente al ruido de las 5 partidas es 0.917.
- **Daño medio por sala que recibe el bot, según composición:**
  - solo básicos: 1.916;
  - con tirador: 0.942;
  - con volador: 0.632;
  - con explosivo: 0.193.

### 11.5 Modelos

Mismo protocolo que en la versión 1: validación cruzada anidada (5 × 3), cinco modelos y regla del error estándar. Resultados en validación cruzada, solo con el entrenamiento:

| Modelo | Daño: RMSE | Daño: R² | Daño: Spearman | Tiempo: RMSE | Tiempo: R² | Parámetros exportables |
|---|---|---|---|---|---|---|
| Media | 4.776 | −0.001 | −0.037 | 16.123 | −0.000 | 1 |
| **Regresión lineal** | **3.128** | **0.571** | **0.748** | **3.526** | **0.952** | **67** |
| Ridge | 3.127 | 0.571 | 0.749 | 3.526 | 0.952 | 67 |
| Gradient Boosting | 3.173 | 0.558 | 0.731 | 3.674 | 0.948 | ~2750 |
| Random Forest | 3.333 | 0.513 | 0.702 | 4.092 | 0.936 | ~349 000 |

**Los árboles tampoco superan a la lineal en la versión 2.** Ridge es el mejor por RMSE, pero la lineal queda a menos de 1 error estándar y es más simple. Gradient Boosting y Random Forest quedan fuera de ese margen, igual que en la versión 1.

**Prueba (evaluada una vez) y confirmación (evaluada una vez, con el JSON exacto del juego):**

| Etiqueta | Conjunto | MAE | RMSE | R² | Spearman |
|---|---|---|---|---|---|
| `damage_total_mean` | Prueba | 2.481 | 3.173 | 0.564 | 0.747 |
| `damage_total_mean` | Confirmación | 2.405 | 3.122 | 0.577 | 0.764 |
| `time_total_mean` | Prueba | 2.839 | 3.578 | 0.950 | 0.974 |
| `time_total_mean` | Confirmación | 2.865 | 3.616 | 0.950 | 0.974 |

- **Línea base:** predecir siempre la media da en la prueba un MAE de 3.785 para el daño y de 13.180 para el tiempo.
- **Techo aproximado de R²:** no se estimó, porque con 22 características solo hay 2 grupos de niveles con vector idéntico en el entrenamiento (se exigen al menos 20).
- **Características que más pesan** (importancia por permutación en la prueba):
  - daño: `enemies_per_room_mean` (2.55), `total_enemies` (1.35), `room_area_cv` (1.08) y `flyer_count` (0.59);
  - tiempo: `total_enemies` (1.33), `room_count` (0.18) y `flyer_count` (0.13).
- **Compresión del rango:** como en v1, el modelo sobrestima los niveles fáciles y subestima los difíciles. En el tramo de daño real más bajo (2.4–8.4) el sesgo es +2.29; en el más alto (16.2–32.8), −3.31, con un MAE de 3.70.

**Exportación y paridad.**
- `difficulty_v2.json` (con `generator_version` = 2) tiene 4 KB; su copia está en `resources/ai/`.
- `ModelParityTest --version=2` da una diferencia máxima entre Python y C# de 7.0e-12 (tolerancia 1e-6).

### 11.6 Director

`RunManager` carga el modelo según la versión del generador:
- la **v1** usa `difficulty_v1.json` y las 14 características;
- la **v2** usa `difficulty_v2.json` y las 22.

La versión por defecto es la 2: el juego arranca con el generador v2 y su modelo. En compilaciones de depuración, la tecla G y el argumento `--generator=1` permiten usar la v1, y el HUD muestra siempre la versión en uso. Los objetivos de la v2 salen de los percentiles 25, 50 y 75 del entrenamiento v2: **Easy 9.0, Normal 11.8 y Hard 15.4**. El aviso del HUD ("modelo no entrenado") ya no aparece con la versión 2; solo se mostraría si faltara el modelo v2.

**Benchmark v2.** 300 semillas de partida nuevas (400001..400300) × 3 objetivos × 30 candidatos. Ninguna semilla efectiva de los candidatos aparece en los CSV v2. Error medio |dificultad real − objetivo|:

| Objetivo | Azar | Director | Siempre el mejor | Oráculo |
|---|---|---|---|---|
| Easy (9.0) | 4.542 | 2.109 | 1.942 | 0.170 |
| Normal (11.8) | 3.855 | 2.534 | 2.609 | 0.200 |
| Hard (15.4) | 4.742 | 3.022 | 2.912 | 0.283 |
| **Promedio** | **4.380 ± 0.027** | **2.555 ± 0.067** | **2.488 ± 0.068** | **0.218 ± 0.007** |

- El director **reduce el error un 41.7 % frente al azar** y recorre el 44 % del camino hasta el oráculo. "Siempre el mejor" lo reduce un 43.2 %; la diferencia entre ambos (0.067) es del orden de su error estándar.
- **Variedad:** con el mismo objetivo, las 300 semillas dan 300 niveles distintos en las tres estrategias y los tres objetivos. Dos niveles cualesquiera comparten de media entre 0.22 y 0.27 salas.
- **Con el conjunto de prueba** (2000 grupos de 30): azar 4.343, director 2.493, siempre el mejor 2.599 y oráculo 0.245. Es una reducción del 42.6 % para el director.

#### 11.6.1 Desempate opcional por variedad

`AIDungeonDirector.PreferEnemyVariety` vale `false` por defecto y conserva la selección histórica aleatoria entre los cinco candidatos más cercanos al objetivo. `RunManager` lo activa explícitamente solo al jugar con el generador v2. Con la opción activa se mantienen esos mismos cinco candidatos y se elige el que contiene más tipos de enemigo distintos; los empates se resuelven por menor distancia predicha al objetivo y después por índice de candidato. La selección es determinista para la misma semilla, dificultad y configuración. El log indica cuántos tipos tiene el nivel elegido.

Este desempate no cambia las plantillas, el catálogo, el modelo ni los datos de entrenamiento. Los benchmarks históricos crean el director con la opción desactivada y conservan sus resultados. En la auditoría de 200 semillas consecutivas (1–200), dificultad Normal, los niveles con al menos tres tipos pasan de 176/200 (88 %) a 200/200 (100 %); los básicos pasan del 74,35 % al 70,86 % de los enemigos. La mejora medida es de **composición**, no de dificultad simulada: esta comparación no ejecuta combates del bot. Informe: `ai-training/reports/diagnostics/director_variety_v2.json`.

Para reproducir la comparación y comprobar ambos modos, el determinismo y el informe sin sobrescribirlo:

```
godot --headless --path . res://tests/DirectorVarietyAudit.tscn -- --verify-selector --check-report
```

#### 11.6.2 Objetos del juego y composición base evaluada

Las recompensas del MVP se añaden **después** de que el director seleccione el nivel. `TreasureItemSpawner` usa un RNG independiente por semilla de partida y celda, con una constante propia para separar su secuencia. La asignación y el estado de recogida viven fuera de `LevelMap` y `RoomData`: no cambian las plantillas, las características, el modelo ni los datos de entrenamiento. Tampoco consumen el azar global ni el del generador o el director.

La predicción del HUD sigue describiendo la **composición base, sin objetos**. Las mejoras del jugador pueden cambiar la dificultad de una partida humana; el modelo no estima ese cambio y no se ha reentrenado para incluirlo. `GameplayParameters`, el simulador y el bot continúan sin objetos y leen los mismos valores base.

`TreasureItemSpawner.Enabled` vale `false` por defecto y se activa explícitamente en la escena jugable. `FidelityCheck` lo desactiva antes de añadir esa escena al árbol, conservando la vida ilimitada del bot y evitando recogidas en la comparación entre simulación y juego real. Los benchmarks del director no dependen de las recompensas y mantienen su selección histórica con `PreferEnemyVariety=false`.

Para comprobar efectos, parámetros históricos, persistencia y aislamiento sin sobrescribir los informes existentes:

```
godot --headless --path . res://tests/ItemSystemTest.tscn
godot --headless --path . res://tests/TreasureItemsTest.tscn
```

La segunda prueba compara el nivel elegido antes y después de asignar recompensas para 200 semillas consecutivas, comprueba la independencia del RNG global y ejercita transiciones reales, cantidades configurables, orden de visita, recogida única, HUD y reinicio. Los valores base se contrastan con los metadatos históricos de las etiquetas v1/v2 en la primera prueba.

#### 11.6.3 Alcance de la predicción: The Mother

Todo lo medido en este documento (características, etiquetas, modelo, objetivos Easy/Normal/Hard y benchmarks) corresponde a la **composición histórica**: las salas de las plantillas de `resources/rooms/templates/` y, en la sala del jefe, la plantilla `jefe.tres` con **seis enemigos básicos**.

En el juego, la sala del jefe se juega con The Mother (`RoomManager.BossScene`, ver `docs/LULLABY.md`, sección 5.1). Ese jefe se aplica después de que el director elige el nivel, igual que las recompensas: no cambia plantillas, características, modelo, etiquetas ni datos de entrenamiento, y el director sigue contando los seis básicos de esa sala al calcular `total_enemies`, `critical_path_enemies` y el resto de métricas. Por eso:

- **La dificultad predicha no estima el combate contra The Mother.** Es un jefe de 180 puntos de vida con salvas y embestidas que no existe en el simulador ni en el bot, así que no hay etiquetas ni fidelidad medida para él.
- **Tampoco estima el efecto de las mejoras de los objetos** (ver 11.6.2).
- `FidelityCheck` pone `BossScene = null` antes de jugar cada nivel, para evaluar la composición de seis básicos del modelo. Con `BossScene` vacío el juego vuelve a esa composición y despejar la sala del jefe no termina la partida.

La prueba `tests/HistoricalIntegrityTest.tscn` fija los SHA-256 de los datasets, etiquetas, modelos exportados y benchmarks de v1 y v2 (los de v1 son los de la tabla de la introducción de la versión 2), de `jefe.tres` y de `Catalog.tres`, y las colisiones de Noah (1×1) y del básico (0.9×0.9), y comprueba que la prueba de fidelidad sigue sin el jefe alternativo:

```
godot --headless --path . res://tests/HistoricalIntegrityTest.tscn
```

### 11.7 Limitaciones de la versión 2

1. **El simulador subestima el daño por contacto**, tanto en v1 como en v2. En la prueba de fidelidad v1 (30 niveles) el daño total, todo por contacto, fue 13.24 en el simulador frente a 16.00 en el juego (−17 %). En la v2 (90 niveles), el contacto da −12.9 % (IC 95 % de −16.6 % a −9.2 %).
   - **Causa:** el simulador mueve cajas eje por eje y el juego usa `MoveAndSlide` de Jolt.
   - **Por qué no se corrigió:** cambiaría las etiquetas v1.
   - **Efecto en las salas:** el sesgo pesa más donde casi todo el daño es de contacto y hay poco daño en total, como en las salas con explosivo (−27.8 % de daño por sala).
2. **La dificultad es la de un bot competente que esquiva bien las explosiones y los proyectiles.** En las 270 partidas del juego real de la prueba de fidelidad, el bot recibió 0 golpes de explosión y 3 de proyectiles. Por eso la fidelidad de esas dos fuentes queda **sin evidencia suficiente**: no se puede afirmar que el simulador reproduzca bien su daño. Un jugador humano que no esquive igual sufrirá más esos enemigos de lo que predice el modelo.
3. **La predicción describe la composición histórica de seis básicos en la sala del jefe**, no el combate contra The Mother ni el efecto de las mejoras de los objetos (secciones 11.6.2 y 11.6.3). Para incluirlos habría que ampliar el simulador y el bot, reetiquetar y reentrenar.
4. **Como en v1:** el modelo comprime el rango de dificultad, las etiquetas son del bot y no de personas, y prueba y confirmación salen del mismo generador que el entrenamiento.

### 11.8 Cómo reproducir la versión 2

```
godot --headless --fixed-fps 60 --path . res://tests/FidelityCheck.tscn -- --generator=2 --count=90

godot --headless --path . res://tests/DatasetExport.tscn -- --version=2
godot --headless --path . res://tests/DatasetExport.tscn -- --version=2 --first-seed=100001 --count=1000 --out=ai-training/datasets/levels_v2_test.csv
godot --headless --path . res://tests/DatasetExport.tscn -- --version=2 --first-seed=200001 --count=1000 --out=ai-training/datasets/levels_v2_confirm.csv
godot --headless --path . res://tests/LabelExport.tscn -- --version=2
godot --headless --path . res://tests/LabelExport.tscn -- --version=2 --first-seed=100001 --count=1000 --out=ai-training/datasets/levels_v2_test_labels.csv
godot --headless --path . res://tests/LabelExport.tscn -- --version=2 --first-seed=200001 --count=1000 --out=ai-training/datasets/levels_v2_confirm_labels.csv

ai-training/.venv/Scripts/python.exe ai-training/training/check_datasets.py --version 2
ai-training/.venv/Scripts/python.exe ai-training/training/train.py --version 2
ai-training/.venv/Scripts/python.exe ai-training/training/evaluate.py --version 2
ai-training/.venv/Scripts/python.exe ai-training/training/export_model.py --version 2
godot --headless --path . res://tests/ModelParityTest.tscn -- --version=2
ai-training/.venv/Scripts/python.exe ai-training/training/evaluate_confirmation.py --version 2
godot --headless --path . res://tests/DirectorBenchmark.tscn -- --version=2

# jugar: el generador por defecto es el v2; con --generator=1 (o la tecla G en depuración) se usa el v1
godot --path . -- --generator=1
```

Sin `--version` (o con `--version 1`), todos los comandos producen los artefactos v1 de siempre.
