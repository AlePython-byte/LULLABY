# LULLABY — Memoria Técnica de Arte 3D y Pipeline de Modelado

**Documento para la Sustentación Académica del Proyecto**  
**Área:** Arte 3D, Modelado y Technical Art  
**Responsable:** David  
**Herramientas Principales:** Blender 5.2.3 LTS, Godot Engine 4, Python 3.11  

---

## 1. Visión y Dirección de Arte

*LULLABY* es un roguelike de terror psicológico con perspectiva isométrica/cenital ambientado en *The Cradle*, una manifestación surrealista de una cuna donde las pesadillas infantiles toman forma física.

### 1.1. Concepto Estético: "La Pesadilla Infantil"
El objetivo artístico fue evocar nostalgia perturbadora a través del contraste:
- **Formas blandas e ingenuas:** Juguetes de infancia (cubos, sonajeros, osos de peluche, bloques de encastre, carruseles de cuna).
- **Escala opresiva y distorsión:** Muebles gigantescos, sombras alargadas, proporciones amenazantes (The Mother supera los 2.8 metros frente a Noah, un bebé de apenas 0.9 metros).
- **Gama cromática contrastada:** Colores pastel y mates (azul celeste, lavanda, rosa chicle, madera cálida) yuxtapuestos con fondos oscuros y fríos (`#151822`, `#0a0b10`).

---

## 2. Reglas Técnicas y Pipeline de Producción

Para garantizar que los activos funcionaran de forma transparente con el motor Godot 4, el sistema de navegación del *AI Director* y el renderizador de sombras dinámicas, se definieron reglas estrictas de modelado:

### 2.1. Materiales de Color Plano y Conversión sRGB $\to$ Linear
- **Cero mapas de textura de mapa de bits (0 UV / 0 Textures):** Todos los modelos utilizan materiales planos directamente asignados a la geometría mediante nodos `Principled BSDF`.
  - **Justificación técnica:** 
    1. **Rendimiento extremo:** Se eliminan todas las lecturas de texturas en VRAM y se reduce drásticamente el número de *draw-calls* en Godot.
    2. **Ligereza del build:** Cada archivo `.glb` pesa entre 6 KB y 95 KB, lo que permite que el juego cargue instantáneamente incluso en compilaciones Web/HTML5.
    3. **Compatibilidad:** Evita artefactos de filtrado bilineal o compresión de textura en pantallas de alta densidad.
- **Corrección de Espacio de Color:** Para evitar colores lavados o apagados en Godot (que opera internamente en espacio lineal), se implementó una función matemática estricta de conversión sRGB $\to$ Linear antes de asignar los canales `Base Color`:
  $$\text{Linear}(v) = \begin{cases} \frac{v}{12.92} & \text{si } v \le 0.04045 \\ \left(\frac{v + 0.055}{1.055}\right)^{2.4} & \text{si } v > 0.04045 \end{cases}$$

### 2.2. Presupuestos de Geometría (Triangle Budgets)
Se establecieron límites estrictos de poligonización para asegurar 60 FPS estables en cualquier hardware:

| Categoría | Límite Máximo | Promedio Real | Activos Clave |
| :--- | :---: | :---: | :--- |
| **Noah (Jugador)** | $\le 1000$ tris | 820 tris | Cuerpo regordete, pañal, chupete y ojos |
| **Enemigos Comunes (x6)** | $\le 600$ tris | 280 – 490 tris | Basic, Exploder, Shooter, Flyer, Runner, Digger |
| **The Mother (Boss)** | $\le 4000$ tris | 2,450 tris | Vestido cónico, tijeras/agujas, cofia, delantal |
| **Objetos / Items (x5)** | $\le 400$ tris | 84 – 398 tris | Biberón, Foto rota, Chupete, Osito, Manta |
| **Props Estándar** | $\le 800$ tris | 120 – 680 tris | Bloques, Tambor, Lámpara, Cuna, Tren |
| **Props de Boss / Sala Grande** | $\le 1200$ tris | 750 – 1,120 tris | Mecedora gigante, Reloj de pared, Móvil colgante |

### 2.3. Orientación y Coordenadas Canónicas
- **Eje Frontal ($+Y$ en Blender $\implies -Z$ en Godot):** Todos los personajes y objetos se modelaron mirando hacia $+Y$ con la cabeza/tope hacia $+Z$. Al exportar con `export_yup=True`, Godot interpreta la dirección frontal en $-Z$, que es la convención oficial de Godot para `Vector3.FORWARD`.
- **Pivote en la Base ($Z = 0$):** El origen de cada malla (`Origin`) se ajustó en el plano inferior de contacto ($Z = 0$), garantizando que al instanciarse en la escena no queden flotando ni incrustados en el piso.

### 2.4. Integración Arquitectónica y NavMesh (AI Director)
Para evitar que los modelos interfirieran con el generador de navegación por mallas (`NavigationMesh` / `NavigationAgent3D`) y evitar bugs de colisión:
- **Espesor de muros:** Mínimo de 1.0 m para impedir que proyectiles a alta velocidad traspasen la geometría.
- **Altura libre de sala:** 3.0 m de techo, permitiendo vuelo de enemigos voladores y la silueta de The Mother (2.8 m).
- **Puertas de paso:** 3.0 m de ancho $\times$ 3.0 m de alto para evitar que los enemigos de gran tamaño queden atascados.
- **Props de pared:** Respaldo plano y profundidad máxima de $0.3$ m para no generar obstáculos ciegos en los pasillos de patrullaje.

---

## 3. Catálogo de Activos Producidos

El paquete completo entregado al repositorio consta de **30 modelos 3D**, **10 íconos de interfaz**, y **material promocional**:

### 3.1. Personajes y Criaturas (`assets/models/`)
1. **`noah.glb`:** Protagonista del juego. Proporciones de infante, chupete celeste, pañal y ojos reflectantes.
2. **`the_mother.glb`:** Jefa final. Silueta victoriana, manos afiladas en forma de tijera de costura y falda monumental.
3. **`basic_enemy.glb`:** Sonajero viviente con manivela mecánica.
4. **`exploder_enemy.glb`:** Dado de goma explosivo con patas cilíndricas.
5. **`shooter_enemy.glb`:** Torre de aros apilables que dispara proyectiles desde la boquilla superior.
6. **`flyer_enemy.glb`:** Estrella móvil de cuna con alas giratorias.
7. **`runner_enemy.glb`:** Conejo de peluche estilizado con orejas caídas y cola de pompón.
8. **`digger_enemy.glb`:** Topo excavador mecánico con pala y manivela.
9. **`enemy_projectile.glb` & `tear.glb`:** Proyectiles estilizados para ataques y lágrimas del protagonista.

### 3.2. Objetos de Poder / Items (`assets/models/items/`)
- **Biberón (`biberon.glb`):** Azul (#4A90E2). Recupera 2 puntos de vida.
- **Fotografía rota (`fotografia_rota.glb`):** Naranja (#E67E22). Marco de madera con fotografía rasgada. +25% de daño.
- **Chupete (`chupete.glb`):** Rosa (#E91E63). Aumenta cadencia de disparo en +20%.
- **Oso de peluche (`oso_peluche.glb`):** Marrón (#8D6E63). +1 de vida máxima y +1 vida actual.
- **Manta vieja (`manta_vieja.glb`):** Verde salvia (#5C946E). Manta doblada con flecos; -20% daño recibido.

### 3.3. Íconos para la Interfaz de Usuario (`assets/ui/`)
Renderizados con canal alfa transparente (RGBA), vista 3/4 frontal e iluminación de tres puntos (Key, Fill y Rim) en dos resoluciones para optimización en Godot:
- **`icono_biberon.png`** (128×128 y 512×512)
- **`icono_fotografia.png`** (128×128 y 512×512)
- **`icono_chupete.png`** (128×128 y 512×512)
- **`icono_oso.png`** (128×128 y 512×512)
- **`icono_manta.png`** (128×128 y 512×512)

### 3.4. Props de Escenario y Ambientación (`assets/models/props/`)
21 piezas modulares distribuidas en props de suelo, estructurales y piezas de pared (con relieve $\le 0.3$ m para no obstruir el *AI Director*):
- **Props de Pared Nuevos (x5):**
  - `cuadro_infantil.glb`: Marco de madera con lienzo crema, barquito en relieve y sol infantil (136 tris, $0.82 \times 0.24 \times 0.70$ m).
  - `reloj_pared.glb`: Reloj cucú de madera con tejadillo rojo, esfera y péndulo dorado oscilante (196 tris, $0.48 \times 0.16 \times 1.13$ m).
  - `repisa_juguetes.glb`: Repisa de madera flotante con patito de goma, libros y cubo (182 tris, $1.05 \times 0.22 \times 0.38$ m).
  - `aplique_estrella.glb`: Lámpara de pared en forma de estrella de 5 puntas con cordón tirador y cuenta rosa (132 tris, $0.40 \times 0.07 \times 0.64$ m).
  - `perchero_pared.glb`: Perchero de pared con 3 pomos redondos de colores y saquito de bebé colgado (216 tris, $0.75 \times 0.09 \times 0.45$ m).
- **Props de Suelo y Sala (x16):**
  - Cuna de barrotes, cuna gigante, marco de puerta, puerta con barrotes, pedestal de tesoro, lámpara de velador, baúl de juguetes, bloques de letras, castillo de bloques, alfombra circular, tambor, xilófono, tren de madera, pelota de colores, libro de cuentos abierto, mecedora gigante, columna de esquina y móvil colgante de cuna.

---

## 4. Material Promocional y Cinematográfico (`assets/promo/`)

Para la difusión y presentación del proyecto:
1. **`portada.png` (1920×1080):** Composición cinematográfica donde Noah y los enemigos rodean a The Mother con iluminación focal cenital y atmósfera oscura.
2. **Posters de Personajes (1200×1200):** 8 renders individuales en plano tres cuartos sobre fondo minimalista (`#151822`) con iluminación de estudio para Noah, The Mother y los 6 tipos de enemigos.
3. **Turnaround de The Mother (`turnaround_the_mother.mp4`):** Video en bucle de 9 segundos (270 fotogramas @ 30 FPS, compresión H.264, 0.83 MB) con un giro suave de 360° que exhibe la escala, las costuras y los detalles de la jefa final.

---

## 5. Resumen de Decisiones de Diseño para la Sustentación

1. **¿Por qué low-poly plano sin texturas complejas?**  
   Prioriza la identidad visual minimalista (evocando el arte de juegos independientes como *Superhot* o *Monument Valley* adaptado al terror), asegura un rendimiento imbatible y facilita que el programador implemente shaders de daño (flash blanco), disolución o iluminación global sin incompatibilidades de mapas UV.

2. **¿Por qué automatizar el pipeline con scripts en Python?**  
   Garantiza reproducibilidad total. Si el equipo requiere cambiar la resolución de los íconos, la paleta global o el formato de exportación, un solo comando regenera todos los activos en segundos con tolerancias idénticas.

3. **¿Cómo se verificó la coherencia de escala?**  
   Se utilizó una referencia métrica constante ($1.0 \text{ unidad} = 1.0 \text{ metro}$): Noah mide 0.9 m, los enemigos entre 0.8 y 1.2 m, y The Mother alcanza 2.8 m, transmitiendo inmediatamente la sensación de indefensión del jugador.

