"""Generador de Sprites Pixel Art de Corazones (16x16) para LULLABY:
  - corazon_lleno.png (16x16)
  - corazon_medio.png (16x16)
  - corazon_vacio.png (16x16)

Basado fielmente en la imagen de referencia:
  - Corazón de 11x10 px centrado en un lienzo transparente de 16x16 px
  - Borde negro/oscuro (#181818)
  - Relleno rojo vivo (#E71D28)
  - Brillo blanco clásico (#FFFFFF)
  - Fondo y vacíos transparentes (RGBA 0, 0, 0, 0)
"""

from pathlib import Path
from PIL import Image

# Coordenadas relativas del corazón (11 columnas x 10 filas):
# . = Transparente
# # = Borde oscuro (#1A1A1A)
# X = Rojo (#E71D28)
# W = Brillo blanco (#FFFFFF)

FULL_HEART = [
    "..##...##..",
    ".#XX#.#XX#.",
    "#XWWX#XXXX#",
    "#XWXXXXXXX#",
    "#XXXXXXXXX#",
    ".#XXXXXXX#.",
    "..#XXXXX#..",
    "...#XXX#...",
    "....#X#....",
    ".....#.....",
]

HALF_HEART = [
    "..##...##..",
    ".#XX#.#..#.",
    "#XWWX#....#",
    "#XWXXX....#",
    "#XXXXX....#",
    ".#XXXX...#.",
    "..#XXX..#..",
    "...#XX.#...",
    "....#X#....",
    ".....#.....",
]

EMPTY_HEART = [
    "..##...##..",
    ".#..#.#..#.",
    "#....#....#",
    "#.........#",
    "#.........#",
    ".#.......#.",
    "..#.....#..",
    "...#...#...",
    "....#.#....",
    ".....#.....",
]

COLOR_OUTLINE = (24, 24, 24, 255)      # Negro borde pixel art
COLOR_RED = (231, 29, 40, 255)         # Rojo clásico
COLOR_SHINE = (255, 255, 255, 255)     # Brillo blanco
COLOR_TRANSPARENT = (0, 0, 0, 0)       # Transparente

CHAR_MAP = {
    '.': COLOR_TRANSPARENT,
    '#': COLOR_OUTLINE,
    'X': COLOR_RED,
    'W': COLOR_SHINE,
}

REPO_UI_DIR = Path(r"C:\LULLABY\assets\ui")
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\ui\corazones")
BRAIN_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

for d in [REPO_UI_DIR, DRIVE_DIR, BRAIN_DIR]:
    d.mkdir(parents=True, exist_ok=True)

def create_heart_image(grid_rows, canvas_size=(16, 16), offset=(2, 3)):
    img = Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    pixels = img.load()
    
    off_x, off_y = offset
    for r_idx, row in enumerate(grid_rows):
        for c_idx, char in enumerate(row):
            x = off_x + c_idx
            y = off_y + r_idx
            if 0 <= x < canvas_size[0] and 0 <= y < canvas_size[1]:
                color = CHAR_MAP[char]
                if color[3] > 0:
                    pixels[x, y] = color
    return img

def main():
    sprites = [
        ("corazon_lleno", FULL_HEART),
        ("corazon_medio", HALF_HEART),
        ("corazon_vacio", EMPTY_HEART),
    ]
    
    print("=== GENERANDO SPRITES PIXEL ART DE CORAZONES (16x16) ===")
    for name, grid in sprites:
        img_16 = create_heart_image(grid, canvas_size=(16, 16), offset=(2, 3))
        
        # 1. Guardar versión oficial 16x16
        p16_repo = REPO_UI_DIR / f"{name}.png"
        img_16.save(p16_repo, "PNG")
        
        p16_drive = DRIVE_DIR / f"{name}.png"
        img_16.save(p16_drive, "PNG")
        
        p16_brain = BRAIN_DIR / f"{name}.png"
        img_16.save(p16_brain, "PNG")
        
        # 2. Generar versiones escaladas limpias (Nearest Neighbor) para inspección visual
        img_128 = img_16.resize((128, 128), Image.Resampling.NEAREST)
        p128_brain = BRAIN_DIR / f"{name}_preview_128.png"
        img_128.save(p128_brain, "PNG")
        img_128.save(DRIVE_DIR / f"{name}_preview_128.png", "PNG")
        
        print(f"  [OK] {name}.png (16x16 y preview 128x128)")

    # 3. Crear una tira comparativa (Sprite Strip) con los 3 corazones juntos para visualizar el HUD completo
    strip_16 = Image.new("RGBA", (16 * 3 + 4, 16), (0, 0, 0, 0))
    for i, (name, grid) in enumerate(sprites):
        img_single = create_heart_image(grid, canvas_size=(16, 16), offset=(2, 3))
        strip_16.paste(img_single, (i * (16 + 2), 0))
    
    strip_upscaled = strip_16.resize((strip_16.width * 8, strip_16.height * 8), Image.Resampling.NEAREST)
    strip_path = BRAIN_DIR / "hud_corazones_strip_preview.png"
    strip_upscaled.save(strip_path, "PNG")
    strip_upscaled.save(DRIVE_DIR / "hud_corazones_strip_preview.png", "PNG")
    print(f"  [OK] hud_corazones_strip_preview.png guardado.")

if __name__ == "__main__":
    main()
