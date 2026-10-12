"""
Libreas del monoplaza CC0: la textura sin marcas (carrocería roja lisa y paneles blancos)
pintada con los colores de cada escudería.

    python Tools/Coche/libreas.py            -> Assets/Coches/Monoplaza/Libreas/<Escudería>.png

- Rojo de la carrocería (y sus tonos más oscuros o claros) -> color de la escudería, con la
  misma luminosidad relativa.
- Blanco de los paneles -> color secundario (abajo), que es lo que distingue a cada equipo.
- Lo demás (negros, grises, ruedas) se queda como está.
Las libreas se llaman como las escuderías reales; la copia pública las renombra.
"""
import colorsys
import json
import os

from PIL import Image

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.normpath(os.path.join(AQUI, "..", ".."))
SALIDA = os.path.join(RAIZ, "Assets", "Coches", "Monoplaza", "Libreas")
TAM = 1024

# Segundo color de cada escudería (los paneles blancos de la textura).
SECUNDARIO = {
    "McLorin": "#1E1E22", "Marcedes": "#1A1A1C", "Rad Bull": "#1B2A55", "Ferrani": "#F2F2F2",
    "Willians": "#0B2D5C", "Racing Bills": "#F2F2F2", "Aston Mertin": "#C8F02E", "Hoas": "#E6002B",
    "Aodi": "#2B2B2E", "Alpena": "#0093CC", "Cadillan": "#1C1C1E",
}


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def pintar(base, color, segundo):
    c = [x / 255 for x in rgb(color)]
    s2 = rgb(segundo)
    ref_v = 224 / 255                       # luminosidad del rojo principal de la textura
    datos = []
    for r, g, b in base.getdata():
        h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        if s > 0.55 and (h < 0.04 or h > 0.96) and v > 0.25:
            k = v / ref_v                    # rojo: mismo tono relativo en el color del equipo
            datos.append(tuple(min(255, int(x * 255 * k)) for x in c))
        elif s < 0.08 and v > 0.93:
            datos.append(s2)                 # blanco: segundo color
        elif r > 230 and 120 < g < 160 and 120 < b < 160:
            datos.append(tuple(int(255 * (0.5 + 0.5 * x)) for x in c))   # rosado: color aclarado
        else:
            datos.append((r, g, b))
    out = Image.new("RGB", base.size)
    out.putdata(datos)
    return out


if __name__ == "__main__":
    base = Image.open(os.path.join(AQUI, "f2002", "sin_marcas.png")).convert("RGB").resize((TAM, TAM), Image.NEAREST)
    parrilla = json.load(open(os.path.join(RAIZ, "Assets", "Resources", "Temporada", "parrilla_2026.json"), encoding="utf-8"))
    os.makedirs(SALIDA, exist_ok=True)
    for e in parrilla["equipos"]:
        pintar(base, e["color"], SECUNDARIO[e["nombre"]]).save(os.path.join(SALIDA, e["material"] + ".png"))
        print(e["material"])
