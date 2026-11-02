# Etapa 2: preparación de la fidelidad v3

Estado: **preparada; pendiente de confirmar que el equipo está libre para el piloto**.
No se ejecutaron combates de desarrollo, aceptación ni reserva. No hay todavía
Spearman, sesgos ni veredicto de fidelidad v3. Las etapas 3 a 6 siguen sin comenzar.

La preparación técnica se realizó el 31 de octubre de 2026, desde 15:56:28 hasta
16:13:43 UTC (unos 17 minutos de pared, sin contar el cierre documental).

## Cobertura sin combates

Se usaron exactamente las semillas del protocolo, sin seleccionar por daño.

| Muestra | Niveles | Básico | Tirador | Explosivo | Volador | Huidor | Excavador |
|---|---:|---:|---:|---:|---:|---:|---:|
| Desarrollo, 600001..600030 | 30 | 30 | 30 | 30 | 29 | 29 | 23 |
| Aceptación, 610001..610090 | 90 | 90 | 89 | 89 | 86 | 81 | 80 |
| Reserva, 620001..620090 | 90 | 90 | 86 | 90 | 85 | 80 | 75 |

Cada columna cuenta niveles que contienen ese tipo, por lo que no son excluyentes.
Las tres muestras contienen los seis tipos. Aceptación supera el mínimo de diez
niveles con huidor y diez con excavador. No hay semillas efectivas repetidas entre
muestras ni firmas completas de mapa repetidas. No fue necesario enmendar el protocolo.

Informe definitivo de cobertura: `coverage_20261031_final/coverage.json` y
`coverage_20261031_final/status.json`: 1,18 segundos de ejecución interna,
170,6 MiB de memoria residente máxima y 123,9 MiB de memoria privada al terminar.
Estas cifras corresponden a generar mapas y cargar parámetros; no miden todavía
el coste de jugar niveles completos.

Se conservan los intentos previos. `coverage_20261031/failure.json` registra un
fallo del arnés al localizar el ensamblado: Godot lo carga desde memoria y puede
dejar vacía su ruta. Se corrigió antes de ejecutar combates, comprobando además
que la identidad del módulo cargado coincide con el archivo compilado.
`coverage_20261031_b/` contiene el primer prechequeo correcto; `final/` corresponde
al formato de cierre definitivo con hashes de los artefactos.

## Ejecución preparada

`tests/FidelityV3Check.tscn` admite únicamente cobertura, piloto y desarrollo.
Por defecto hace cobertura. Aceptación y reserva no son modos disponibles en esta
entrega: primero se debe revisar el resultado de desarrollo.

- Un solo trabajador, simulador y juego real en secuencia, sin paralelismo.
- Misma semilla efectiva y semilla de bot por par; salud ilimitada, sin objetos,
  decoración ni The Mother. No se modifican las escenas originales.
- Criterios leídos de `ai-training/protocols/v3_fidelity.json`, con su SHA-256
  registrado. El protocolo permanece intacto.
- Manifiesto previo a combates con parámetros, versiones y hashes de código,
  escenas, recursos y ensamblado. Se comprueba que no cambien entre pares.
- Cada resultado se guarda por separado, sin sobrescribir intentos anteriores.
  El cierre incluye los hashes de todos los archivos de datos.
  Los informes nuevos se escriben con LF y los atributos locales impiden que Git
  transforme sus bytes. Los primeros prechequeos conservan sus finales originales
  de Windows para que sus hashes sigan verificándose al recuperar el commit.
- El primer timeout o partida declarada completa con salas pendientes detiene
  todo. Una alarma conservadora también detiene ante 60 segundos simulados sin
  desplazamiento mayor de 0,1 m, daño a enemigos, muertes ni cambio de sala.
  La alarma señala un posible atasco para revisión: no es un criterio estadístico
  nuevo, no cambia el combate y no permite excluir niveles para aprobar.
- Los resultados parciales se conservan. No se continúa automáticamente a otra
  muestra y no se generan etiquetas de entrenamiento.

El análisis en `ai-training/validation/v3_fidelity.py` usa solo la biblioteca
estándar. Calcula correlaciones sobre las medias por nivel, sesgos por fuente y
composición, contacto por los seis tipos y bootstrap pareado por nivel. Un piloto,
un conjunto incompleto o datos modificados no pueden aprobar la puerta.
La falta de impactos de proyectil/explosión se informa como evidencia insuficiente
según el protocolo, sin afirmar que esas fuentes estén validadas.

## Pruebas de preparación

- `FidelityV3ToolsTest`: 15 comprobaciones correctas, sin combates.
- `python -m unittest discover -s ai-training/validation -p test_v3_fidelity.py -v`:
  13 pruebas correctas con datos sintéticos, sin leer resultados reservados.
- `HistoricalIntegrityTest`: 51 comprobaciones correctas y 20 SHA-256 idénticos.
- Compilación: 0 errores y 0 advertencias; cada commit compila desde `git archive`.

No se cambió código de enemigos, escenas, simulador histórico ni protocolo.

## Próxima ejecución, pendiente de confirmación

Se propone un piloto de **una semilla de desarrollo, 600001, repetición 0**,
con una partida simulada y la correspondiente partida real. Se utiliza para medir
tiempo y memoria; no sustituye las 30 × 3 comparaciones de desarrollo ni es una
prueba de aceptación. Desarrollo se ejecutará completo en una carpeta nueva.

Reserva provisional para el piloto: **2–15 minutos de pared, 300–800 MiB de RAM;
dejar 1 GiB disponible**. Son estimaciones conservadoras, no resultados medidos.
Se registrarán tiempo por entorno, memoria residente máxima y memoria privada.
No se propone paralelismo. Después del piloto se estimará la ejecución de los
90 pares de desarrollo y se pedirá confirmar otra ventana antes de lanzarla.

Comandos desde la raíz, sustituyendo `godot` por la consola .NET instalada:

```powershell
# Solo después de confirmar equipo libre y editor cerrado:
godot --headless --fixed-fps 60 --path . res://tests/FidelityV3Check.tscn -- --mode=pilot --out-dir=ai-training/reports/v3/pilot_20261031_01
python ai-training/validation/v3_fidelity.py ai-training/reports/v3/pilot_20261031_01

# NO lanzar hasta revisar el piloto, estimar recursos y confirmar la ventana:
godot --headless --fixed-fps 60 --path . res://tests/FidelityV3Check.tscn -- --mode=development --out-dir=ai-training/reports/v3/development_20261031_01
python ai-training/validation/v3_fidelity.py ai-training/reports/v3/development_20261031_01
```

Si aparece un atasco o una sala imposible, detenerse y reportarlo antes de corregir
o continuar. Si para aprobar fuera necesario cambiar el comportamiento real de un
enemigo, detenerse: no realizar ese cambio. El punto de control sigue siendo el
3 de noviembre. Se informará desarrollo antes de proponer aceptación.
