# Diagnóstico puntual 610006:0

Ejecutor temporal y separado de las rutas de aceptación. Una sola partida real,
ningún combate simulado y ningún reintento. Requiere autorización explícita y
equipo libre. No modifica decisiones, entradas, escenas ni parámetros existentes;
invoca por reflexión el cuerpo original de FidelityV3Check.PlayReal y conserva
su monitor. Comprueba fuentes, mapa, configuración y semillas contra el intento
fallido. El ensamblado incluye únicamente el observador nuevo además del código
existente; no reproduce los combates previos del proceso de aceptación.

La traza registra cada cinco segundos virtuales y al cambiar de sala: posición,
movimiento acumulado y neto, entrada ya aplicada, colisiones ya calculadas,
enemigos vivos, salud, fases y puertas con su estado lógico y de colisión.
Quieto significa recorrido inferior a 0,2 m en la ventana; vueltas u oscilación
significa recorrido superior a 2 m y desplazamiento neto inferior a 0,5 m.
Son descriptores de movimiento, no una causa atribuida automáticamente.

El límite externo es 900 segundos de pared; el límite virtual sigue siendo el
original de 600 segundos. La carpeta debe ser nueva: su reutilización se rechaza
antes de lanzar Godot. Un fallo de arranque también consume el intento autorizado.
El resultado diagnóstico nunca sustituye la aceptación no aprobada; v3 sigue
experimental. No se usa la reserva para combates ni se evalúan métricas nuevas.
