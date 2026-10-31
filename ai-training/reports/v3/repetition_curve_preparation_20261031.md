# Curva de repeticiones v3: preparada, sin ejecutar

Plan congelado antes de medir: `ai-training/protocols/v3_repetition_curve_plan.json`,
commit `4988090`. El protocolo original `v3_fidelity.json` y sus umbrales siguen
intactos. No hay correcciones de simulador o bot.

## Diseño fijo

- Desarrollo únicamente: semillas solicitadas 600001..600030.
- Lote nuevo completo: repeticiones del bot 0..9 en ambos entornos.
- 300 pares, 600 partidas; un trabajador, entornos secuenciales.
- Medias anidadas de los primeros 3, 5 y 10 intentos; no elegir combinaciones.
- Spearman de daño y tiempo sobre las 30 medias por nivel.
- IC 95 % por bootstrap pareado de niveles, 2000 remuestreos, semilla 20261031.
  Se mantienen juntas las repeticiones y ambos entornos. Se usan los mismos
  remuestreos para calcular las diferencias 5−3, 10−3 y 10−5.
- El resultado original de tres repeticiones se conserva por separado; el
  prefijo nuevo no lo reemplaza. Se registran diferencias entre resultados.
- Sin partidas ni generación de mapas de aceptación o reserva en este lote.
- Parada al primer atasco, timeout, sala pendiente o cambio de código/plan.
  Las excepciones del motor se conservan y se asocian al par activo.

La escena nueva `FidelityV3RepetitionCurve` reutiliza sin cambios
`FidelityV3Check.PlayReal`. Se comprueban los hashes del código y recursos del
experimento original, salvo el ensamblado que ahora incluye la nueva escena de
diagnóstico. Se mantiene simulador versión 4 y política del bot versión 1.
El analizador es `ai-training/validation/repetition_curve_v3.py`.

Compilación correcta y cinco pruebas específicas del análisis correctas. Los
commits de preparación compilan por separado desde `git archive`.

## Ventana pendiente

Estimación comunicada: **25–35 minutos**, **250–500 MiB de RAM**, dejando
**1 GiB disponible**, un trabajador. La proyección directa del desarrollo de
90 pares ronda 18 minutos; la ventana incluye variación en las siete semillas
de bot adicionales, comprobaciones y margen.

Se solicitó confirmar esa ventana. Al comprobar el equipo después de preparar
y compilar, apareció un nuevo proceso de Godot activo; por ello no se lanzó
el lote. Se volvió a solicitar editor cerrado y equipo libre. No se interpreta
la ausencia de respuesta como aprobación de esa ventana.

Comandos preparados, solo tras confirmar disponibilidad:

```powershell
dotnet build
godot --headless --fixed-fps 60 --path . --log-file build/fidelity-v3-repetition-curve-20261031-01.log res://tests/FidelityV3RepetitionCurve.tscn -- --out-dir=ai-training/reports/v3/repetition_curve_20261031_01
python ai-training/validation/repetition_curve_v3.py ai-training/reports/v3/repetition_curve_20261031_01 --log-file build/fidelity-v3-repetition-curve-20261031-01.log
```

La ejecución debe usar ese archivo de log con `--log-file` en la llamada real
de Godot, para conservar las incidencias por par. No se ha creado la carpeta
de resultados de la curva.

## Ruta condicionada, todavía sin decidir

Si el Spearman de daño con diez repeticiones llega a 0,7, se propondrá un archivo
nuevo `v3_fidelity_v2.json`, con diez repeticiones y los mismos umbrales. Se
documentará que el protocolo original de tres no se cumplió, y que el cambio
se eligió a partir de desarrollo. La propuesta se congelará antes de cualquier
evaluación futura; la aceptación será en 620001..620090 y requerirá aprobación.

Si no llega, se recomendará primero diferir impactos de áreas u objetivo estable
del bot según la evidencia de la curva. No se implementará ninguna corrección
sin autorización. Barrido y deslizamiento quedan fuera de alcance salvo evidencia
que los exija. Ninguna rama ejecuta aceptación ni abre la reserva en esta tarea.
