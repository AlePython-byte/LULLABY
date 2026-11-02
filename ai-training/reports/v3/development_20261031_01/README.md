# Fidelidad v3: muestra de desarrollo completa

**Recomendación: diagnosticar antes de pasar a aceptación.** El Spearman de daño
es 0,528, inferior al mínimo preregistrado de 0,7. Tiempo, contacto agregado y
tiempo por composición cumplen sus umbrales. No se ejecutó aceptación ni reserva,
no se generaron etiquetas de entrenamiento y no se modificaron criterios,
simulador, bot, escenas ni enemigos.

## Ejecución y procedencia

Se ejecutaron las 30 semillas solicitadas 600001..600030, con sus semillas
efectivas registradas, y tres repeticiones por nivel: **90 pares, 180 partidas**.
Un solo trabajador, simulador y juego real en secuencia, Godot headless,
60 pasos de física por segundo simulado, `TimeScale=1`. El equipo libre y el
editor cerrado se confirmaron antes de empezar.

Se usó el protocolo `ai-training/protocols/v3_fidelity.json` sin cambios y el
mismo código que en el piloto. No se descartó ni sustituyó ninguna semilla o
repetición. El piloto anterior no se incorporó como una repetición adicional.
La sala del jefe conserva seis básicos; no se incluyen The Mother ni objetos.

Los 90 pares terminaron y todas las salas fueron visitadas y despejadas.
**Atascos detectados: 0. Timeouts: 0 en cada entorno.**

## Métricas registradas

Las correlaciones se calculan sobre las medias de las tres repeticiones de cada
uno de los 30 niveles. Las 90 repeticiones no se consideran niveles independientes.

| Criterio | Resultado | Umbral registrado | Decisión estadística |
|---|---:|---:|---|
| Spearman de daño total | **0,527881** | >= 0,7 | No cumple |
| Spearman de tiempo total | **0,985762** | >= 0,7 | Cumple |
| Sesgo de contacto | **+0,87 %** | ±25 % | Cumple |
| IC 95 % del sesgo de contacto | **[−8,27 %, +10,52 %]** | Informativo | Dentro del margen |

Contacto: 809 puntos de daño y 809 impactos en simulación frente a 802 y 802 en el
juego real. El IC es bootstrap pareado por nivel, 2000 remuestreos, semilla
20261031. Las repeticiones de un nivel permanecen juntas en cada remuestreo.
Sesgo = `(suma_sim − suma_real) / suma_real`.

El daño total medio es 9,189 frente a 9,033 (+1,72 %); el tiempo total medio,
88,385 frente a 88,487 segundos de juego (−0,12 %). Una media global cercana no
demuestra que se ordene correctamente la dificultad de los niveles: esa es la
comprobación que no supera el Spearman de daño.

### Sesgos por composición de sala

| Composición | Salas × repeticiones | Sesgo de tiempo | Sesgo de daño, informativo |
|---|---:|---:|---:|
| Solo básicos | 264 | +1,03 % | +3,21 % |
| Con tirador | 300 | −0,03 % | −4,39 % |
| Con explosivo | 399 | −0,18 % | −5,96 % |
| Con volador | 243 | −2,00 % | −12,98 % |
| Con huidor | 207 | −0,10 % | +5,61 % |
| Con excavador | 129 | +0,26 % | +4,35 % |

Todos los sesgos de **tiempo** cumplen ±30 %. Una sala mixta cuenta en cada tipo
que contiene; las categorías se solapan. El daño de una sala con excavador o
huidor puede proceder de otro enemigo de esa sala. El daño por composición es
diagnóstico, no una puerta adicional ni una atribución causal a ese tipo.

### Fuentes con poca evidencia

- Proyectiles: 18 impactos en simulación y 9 reales; sesgo de daño +100 %.
- Explosión: 0 impactos en simulación y 1 real; 0 frente a 2 puntos de daño.

Ambas fuentes quedan **sin evidencia suficiente** según la regla de al menos
30 impactos reales por fuente. No bloquean por sí mismas, pero no se consideran
validadas. El fallo estadístico de esta muestra es el Spearman de daño.

## Incidente de ejecución

Durante la comparación 56, semilla solicitada **600019, repetición 1**, Godot
registró dos excepciones:

```text
System.InvalidOperationException: Handle is not initialized.
GCHandle.FromIntPtr
Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType
ResourceLoader.Load -> EnemyKinds.Scene -> RoomController.StartCombat
```

Las excepciones quedaron registradas por el motor sin abortar el proceso. La
partida correspondiente y las posteriores terminaron, sin alarma de atasco ni
timeout. Se conserva el log completo; **no se ha demostrado que el incidente
carezca de efecto sobre el resultado**, ni se atribuye el fallo de correlación
a esa excepción sin evidencia. No se excluyó el par afectado para mejorar cifras.
El mensaje inicial del almacén de certificados de Windows es un incidente
distinto; el log contiene dos excepciones de manejadores además de ese mensaje.

## Pistas para diagnóstico, sin nuevas ejecuciones

Las mayores diferencias de daño medio por nivel son:

| Semilla solicitada | Simulador | Juego real | Diferencia sim − real |
|---|---:|---:|---:|
| 600022 | 13,67 | 7,33 | +6,33 |
| 600027 | 8,67 | 14,00 | −5,33 |
| 600018 | 8,33 | 12,67 | −4,33 |

Estos casos muestran desviaciones de signo opuesto que se compensan al sumar
contacto. No identifican por sí solos la causa. Conviene comparar por sala y paso
los contactos, trayectorias, temporizadores e invulnerabilidad de esas semillas.
Por separado, hay que investigar la carga y el ciclo de vida de recursos del
arnés en el caso 600019:1. No se propone cambiar la velocidad, vida, colisiones
ni comportamiento del juego real para ajustar los resultados.

No se ejecutaron repeticiones extra ni correcciones en esta entrega. Después de
un diagnóstico y, si procede, cambios limitados al simulador o al arnés, habría
que volver a medir desarrollo en una carpeta nueva antes de proponer aceptación.
La reserva permanece intacta: no hubo un fallo de aceptación.

## Tiempo y memoria

- Ejecución interna completa: **323,07 s = 5 min 23 s = 0,0897 h** de pared.
- Suma de simulaciones puras: 5,25 s; suma de partidas reales headless: 304,15 s.
- Tiempo por par: entre 2,32 y 4,72 s, sin contar comprobaciones entre pares.
- Memoria residente máxima: **220,55 MiB**; al terminar: **174,91 MiB**.
- Memoria privada al terminar: **148,18 MiB**.

La ejecución quedó dentro de la ventana prevista de 10–15 minutos y de la
reserva de memoria. No se observó crecimiento sostenido durante los controles
intermedios. Estas cifras no autorizan paralelismo para próximas pruebas.

## Archivos, integridad y reproducción del análisis

`analysis.json` contiene el resultado numérico completo. `raw_results.zip`
conserva **303 JSON originales**: mapa por semilla, los 90 resultados de cada
entorno, 90 registros de recursos, cobertura, manifiesto y cierre. También
contiene `audit/runtime.log` y `audit/index.json`, con SHA-256 de cada archivo
archivado. Los archivos originales permanecen en disco; no se sobrescribieron
ni eliminaron. Se agrupan en un ZIP para registrar el experimento como una
unidad revisable, sin cientos de entradas de resultados sueltos.

El análisis verificó hashes, emparejamiento de semillas, número de pares y
finalización de todas las salas. Los 20 SHA-256 históricos siguen coincidiendo
con `HistoricalIntegrityTest`; no se lanzó ninguna otra escena de prueba.

Para recuperar los originales en una carpeta nueva y recalcular el análisis,
sin ejecutar ninguna partida:

```powershell
python -m zipfile -e ai-training/reports/v3/development_20261031_01/raw_results.zip build/development_20261031_recovered
python ai-training/validation/v3_fidelity.py build/development_20261031_recovered
```

El estado automático es `fail`. La recomendación humana coincide: **diagnosticar
antes de aceptación**, por el Spearman de daño y el incidente de manejadores.
Aceptación requiere una indicación explícita posterior; no se ha iniciado.
