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

En el juego, `RunManager` usa el director y el HUD muestra la semilla y la dificultad objetivo frente a la predicha. En compilaciones de depuración, N cambia la semilla, R la repite y T cambia la dificultad.

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

Abrir el proyecto en Godot y pulsar F5. En compilaciones de depuración: **N** cambia la semilla de partida, **R** la repite y **T** cambia la dificultad (Easy, Normal, Hard). También se pueden fijar por línea de comandos:

```
godot --path . -- --seed=42 --difficulty=Hard
```

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
