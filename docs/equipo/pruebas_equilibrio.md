# Pruebas de equilibrio: cómo jugar y recoger los datos

Objetivo: saber si las partidas son ganables y no resultan ni frustrantes ni triviales antes del congelamiento del 5 de noviembre. Cada uno juega entre **5 y 10 partidas** y envía los archivos que genera el juego. Los datos se necesitan el **3 y el 4 de noviembre**; la decisión se toma el 4.

## Qué hay que hacer

1. Abre `LULLABY.exe` (la versión que te pasan, no la del editor).
2. Empieza **siempre desde el menú principal con el botón «Jugar»**. Solo así se registra la partida.
3. Elige la dificultad y juega como jugarías de verdad. Reparto sugerido de 10 partidas: **2 en Fácil, 4 en Normal y 4 en Difícil**. Con 5 partidas: 1, 2 y 2.
4. No hace falta terminar todas. Si te cansas, vuelves al menú o cierras la ventana: queda anotada como «abandono» y no cuenta como derrota.
5. Al morir o ganar, usa «Nueva partida» (o vuelve al menú y pulsa «Jugar» otra vez).
6. Si una partida te pareció injusta, aburrida o frustrante, apúntalo en una línea con la hora aproximada (lo que digan los números no siempre lo cuenta).

No hace falta usar ninguna tecla especial. La pausa no cuenta en el tiempo.

## Dónde están los archivos

Cada partida genera un archivo `partida_AAAAMMDD_HHMMSS_<semilla>.csv` en:

```
%APPDATA%\Godot\app_userdata\LULLABY\playtest\
```

Pega esa ruta en el Explorador de archivos (o en Win+R) para abrirla. No contiene datos personales: solo la partida (dificultad, semilla, salas, vida, objetos, daño por causa, tiempos y resultado).

## Cómo enviarlos

1. Crea una carpeta con **tu nombre** (por ejemplo `David`) y copia dentro todos los `partida_*.csv`.
2. Comprime la carpeta y envíasela a Alejandro, junto con tus notas.
3. Cuando te confirmen que llegaron, puedes borrar los archivos de `playtest\`.

## Qué contiene cada CSV

Una fila `SALA` por cada visita a una sala (vida al entrar y al salir, objeto recogido, curación, daño por causa y segundos) y una fila `FIN` con los totales y el resultado: `victoria`, `muerte` (con la causa) o `abandono`, la vida con la que se llegó al jefe y cuánto duró la pelea contra The Mother. Las columnas `dano_<causa>` son una por tipo de enemigo (Chaser = básico, Shooter = tirador, Projectile = proyectil, Exploder = explosivo, Flyer = volador, Runner = huidor, Digger = excavador, TheMother) y Unknown. La curación incluye el biberón y el +1 de vida del oso.

## Cómo resumirlos (Alejandro)

Pon las carpetas de cada persona dentro de una carpeta común (por ejemplo `partidas\Alejandro`, `partidas\David`, `partidas\Luis`) y ejecuta desde la raíz del repositorio:

```
powershell -ExecutionPolicy Bypass -File tools\Resumir-Partidas.ps1 -Carpeta partidas -PorJugador
```

Imprime, por dificultad (y por persona con `-PorJugador`): partidas, victorias, muertes y abandonos, tasa de victoria (los abandonos no cuentan), porcentaje que llega al jefe, vida media al llegar, duración media del jefe, causa de muerte más común y daño medio por causa. Con `-Salida resumen.csv` guarda la tabla por dificultad. Los archivos sin fila `FIN` (el juego se cerró de golpe) se cuentan aparte y se ignoran.
