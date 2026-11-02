# Certificados de Windows: excepcion exacta antes de aceptacion

Linea observada, completa y sin espacios adicionales:

```text
ERROR: Failed to read the root certificate store.
```

En los nueve logs principales de la curva final aparece exactamente una vez,
en la linea 3, antes del primer CURVE_BEGIN (linea 5), nunca al cierre.
El marco siguiente identifica get_system_ca_certificates en os_windows.cpp.
En las dos comparaciones anteriores tambien aparece una vez por log:
14 logs principales en total. Hay otros 11 logs nativos conservados, con una
aparicion por archivo; son copias de canal de procesos ya contados, no otros
11 errores independientes. certificate_audit_20261101.json conserva por archivo
el recuento, lineas, primer par y SHA-256 del archivo fuente comprimido.

## Origen y alcance de la conclusion

El [codigo de Windows de Godot 4.7.2](https://github.com/godotengine/godot/blob/4.7.2-stable/platform/windows/os_windows.cpp#L2404)
abre el almacen ROOT del sistema para obtener certificados CA. Si no puede
abrirlo, emite este mensaje y retorna una cadena vacia; no hay simulacion,
semillas de bot, geometria ni colisiones en esa funcion. La posicion de linea
del binario registrada en el log (2582) difiere de la fuente publicada, pero
coinciden funcion y mensaje: no se usa ese numero como prueba de identidad binaria.

La [inicializacion de certificados de mbedTLS](https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mbedtls/crypto_mbedtls.cpp#L322)
consume esa cadena. El codigo contempla certificados incorporados si estan
habilitados al compilar; no se presume que ese mecanismo concreto este activo
en el binario instalado ni se prueba una conexion TLS.

El [arranque del juego](https://github.com/godotengine/godot/blob/4.7.2-stable/main/main.cpp#L4413)
inicializa esos certificados antes de cargar e instanciar la escena. Concuerda
con la posicion del mensaje en todos los logs auditados.

La medicion usa archivos locales, generador, simulador y juego local; la
revision de scripts y PlayReal no encontro operaciones HTTP/TLS. Por ese alcance,
el fallo de lectura de confianza de red no participa en los parametros,
entradas ni resultados de combate. Esta conclusion se apoya en la ruta del
codigo, no solamente en que el proceso termine. No implica que el mensaje sea
inofensivo para un programa que necesite HTTPS ni demuestra ausencia de otros
fallos: GCHandle y el resto de errores siguen bloqueando.

## Enmienda autorizada

Se conserva el protocolo original intacto. La version anterior de v2, SHA-256
`e1ab71ac414cd0624c79ebb96c68e044e32a01e0c2896e51e7c4ccb18d332e27`,
queda recuperable en el commit bbddd43. La enmienda se registra antes de abrir
aceptacion; no cambia los criterios estadisticos ni las muestras.

Solo se exceptua la igualdad ordinal de la linea completa indicada: sensible
a mayusculas y espacios, sin comodines, prefijos permitidos ni expresiones
regulares. Se tolera como maximo una aparicion por canal y solo antes del
marcador FIDELITY_V2_BEGIN. Una variante, una segunda aparicion o el mismo
mensaje durante medicion invalidan. Cualquier otro ERROR:, GCHandle, Illegal
instruction, salida fallida o resultado incompleto tambien invalidan.

Los logs originales se guardan sin quitar la linea; el analisis informa su
numero por canal. No se desactiva TLS, no se instala otro almacen, no se cambia
el motor ni se redirigen mensajes para aparentar limpieza. La aceptacion
completa permanece pendiente de OK. Solo esta autorizado un preflight de
600001:0, un nivel y un par.
