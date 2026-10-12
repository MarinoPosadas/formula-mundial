# Genera los datos de circuito que lee el juego.
#
#   fuentes/f1-circuits.geojson  trazado GPS real (github.com/bacinger/f1-circuits, MIT)
#   salidas.json                 dónde está la salida y en qué sentido se corre; sale de
#                                encajar.py, que coloca el GPS sobre los planos oficiales
#                                (Assets/Resources/Planos). Tarda ~7 min,
#                                así que solo se repite si cambian los planos.
#
# Escribe Assets/Resources/Circuitos/<id>.json (uno por circuito) y calendario_2026.json.
# Puntos en metros, cada PASO m, empezando en la línea de salida, en sentido de carrera y
# con el norte en +Z. El primer punto es (0, 0).
#
# Uso, desde la raíz del proyecto:  python -I Tools/Circuitos/generar_circuitos.py
import json, math, os
import numpy as np

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.normpath(os.path.join(AQUI, "..", ".."))
SALIDA = os.path.join(RAIZ, "Assets", "Resources", "Circuitos")
PASO = 5.0

# Calendario 2026 tal como se anunció. Ronda, id del GeoJSON, nombre del GP, código de
# bandera (Resources/Flags), plano y fecha del domingo.
CALENDARIO = [
    (1, "au-1953", "Gran Premio de Australia", "AUS", "australia", "2026-03-08"),
    (2, "cn-2004", "Gran Premio de China", "CHN", "china", "2026-03-15"),
    (3, "jp-1962", "Gran Premio de Japón", "JPN", "japon", "2026-03-29"),
    (4, "bh-2002", "Gran Premio de Baréin", "BRN", None, "2026-04-12"),
    (5, "sa-2021", "Gran Premio de Arabia Saudí", "KSA", None, "2026-04-19"),
    (6, "us-2022", "Gran Premio de Miami", "USA", "miami", "2026-05-03"),
    (7, "ca-1978", "Gran Premio de Canadá", "CAN", "canada", "2026-05-24"),
    (8, "mc-1929", "Gran Premio de Mónaco", "MON", "monaco", "2026-06-07"),
    (9, "es-1991", "Gran Premio de Barcelona-Cataluña", "ESP", "barcelona-catalunya", "2026-06-14"),
    (10, "at-1969", "Gran Premio de Austria", "AUT", "austria", "2026-06-28"),
    (11, "gb-1948", "Gran Premio de Gran Bretaña", "GBR", "gran-bretana", "2026-07-05"),
    (12, "be-1925", "Gran Premio de Bélgica", "BEL", "belgica", "2026-07-19"),
    (13, "hu-1986", "Gran Premio de Hungría", "HUN", "hungria", "2026-07-26"),
    (14, "nl-1948", "Gran Premio de los Países Bajos", "NED", "paises-bajos", "2026-08-23"),
    (15, "it-1922", "Gran Premio de Italia", "ITA", "italia", "2026-09-06"),
    (16, "es-2026", "Gran Premio de España", "ESP", "espana", "2026-09-13"),
    (17, "az-2016", "Gran Premio de Azerbaiyán", "AZE", "azerbaiyan", "2026-09-26"),
    (18, "sg-2008", "Gran Premio de Singapur", "SGP", "singapur", "2026-10-11"),
    (19, "us-2012", "Gran Premio de Estados Unidos", "USA", "eeuu", "2026-10-25"),
    (20, "mx-1962", "Gran Premio de la Ciudad de México", "MEX", "mexico", "2026-11-01"),
    (21, "br-1940", "Gran Premio de São Paulo", "BRA", "brasil", "2026-11-08"),
    (22, "us-2023", "Gran Premio de Las Vegas", "USA", "las-vegas", "2026-11-21"),
    (23, "qa-2004", "Gran Premio de Catar", "QAT", "qatar", "2026-11-29"),
    (24, "ae-2009", "Gran Premio de Abu Dabi", "UAE", "abu-dhabi", "2026-12-06"),
]


# Pole de 2025 (aprox., en segundos): el simulador ajusta el ritmo de cada circuito para que
# la vuelta de referencia caiga ahí. Madrid no tiene: se queda con el modelo sin corregir.
POLES = {
    "au-1953": 75.1,
    "cn-2004": 90.6,
    "jp-1962": 87.0,
    "bh-2002": 89.8,
    "sa-2021": 87.3,
    "us-2022": 86.2,
    "ca-1978": 70.9,
    "mc-1929": 70.0,
    "es-1991": 71.5,
    "at-1969": 64.0,
    "gb-1948": 84.9,
    "be-1925": 100.5,
    "hu-1986": 75.4,
    "nl-1948": 68.7,
    "it-1922": 78.8,
    "az-2016": 101.1,
    "sg-2008": 89.2,
    "us-2012": 92.5,
    "mx-1962": 75.6,
    "br-1940": 69.5,
    "us-2023": 92.3,
    "qa-2004": 79.4,
    "ae-2009": 82.2,
}

# Circuitos urbanos (muros pegados a la pista, edificios alrededor) y su ancho de pista. El
# resto son permanentes y miden 13 m.
URBANOS = {"mc-1929": 10.0, "sg-2008": 12.0, "az-2016": 12.0, "sa-2021": 12.0, "us-2023": 13.0, "es-2026": 13.0}

# Entorno aproximado a la geografía real, para el fondo: suelo (hierba/desierto), montañas
# (altura en m y dirección en grados desde el norte; -1 = alrededor), mar (dirección; -1 =
# no hay) y skyline de ciudad (dirección; -1 = no hay; 999 = alrededor).
ENTORNOS = {
    "au-1953": dict(suelo="hierba", montanas=0, montanasDir=-1, mar=215, ciudad=0),
    "cn-2004": dict(suelo="hierba", montanas=0, montanasDir=-1, mar=-1, ciudad=90),
    "jp-1962": dict(suelo="hierba", montanas=350, montanasDir=290, mar=110, ciudad=-1),
    "bh-2002": dict(suelo="desierto", montanas=0, montanasDir=-1, mar=-1, ciudad=-1),
    "sa-2021": dict(suelo="desierto", montanas=0, montanasDir=-1, mar=270, ciudad=90),
    "us-2022": dict(suelo="hierba", montanas=0, montanasDir=-1, mar=-1, ciudad=150),
    "ca-1978": dict(suelo="hierba", montanas=200, montanasDir=300, mar=110, ciudad=320),
    "mc-1929": dict(suelo="hierba", montanas=700, montanasDir=0, mar=150, ciudad=-1),
    "es-1991": dict(suelo="hierba", montanas=400, montanasDir=10, mar=-1, ciudad=200),
    "at-1969": dict(suelo="hierba", montanas=1200, montanasDir=-1, mar=-1, ciudad=-1),
    "gb-1948": dict(suelo="hierba", montanas=0, montanasDir=-1, mar=-1, ciudad=-1),
    "be-1925": dict(suelo="hierba", montanas=300, montanasDir=-1, mar=-1, ciudad=-1),
    "hu-1986": dict(suelo="hierba", montanas=200, montanasDir=-1, mar=-1, ciudad=250),
    "nl-1948": dict(suelo="hierba", montanas=0, montanasDir=-1, mar=270, ciudad=90),
    "it-1922": dict(suelo="hierba", montanas=900, montanasDir=0, mar=-1, ciudad=200),
    "es-2026": dict(suelo="hierba", montanas=900, montanasDir=330, mar=-1, ciudad=220),
    "az-2016": dict(suelo="desierto", montanas=0, montanasDir=-1, mar=150, ciudad=330),
    "sg-2008": dict(suelo="hierba", montanas=0, montanasDir=-1, mar=160, ciudad=300),
    "us-2012": dict(suelo="hierba", montanas=150, montanasDir=-1, mar=-1, ciudad=320),
    "mx-1962": dict(suelo="hierba", montanas=1500, montanasDir=130, mar=-1, ciudad=999),
    "br-1940": dict(suelo="hierba", montanas=300, montanasDir=-1, mar=-1, ciudad=999),
    "us-2023": dict(suelo="desierto", montanas=900, montanasDir=270, mar=-1, ciudad=999),
    "qa-2004": dict(suelo="desierto", montanas=0, montanasDir=-1, mar=90, ciudad=180),
    "ae-2009": dict(suelo="desierto", montanas=0, montanasDir=-1, mar=0, ciudad=110),
}

def metros(coords):
    c = np.array(coords, float)
    if np.allclose(c[0], c[-1]): c = c[:-1]
    lat0 = math.radians(c[:, 1].mean())
    return np.c_[np.radians(c[:, 0]) * 6371000 * math.cos(lat0), np.radians(c[:, 1]) * 6371000]

def catmull_rom(p, sub=8):
    # Centrípeta y cerrada: redondea las esquinas que deja un GPS con un punto cada 30-60 m.
    n, out = len(p), []
    for i in range(n):
        P0, P1, P2, P3 = p[i - 1], p[i], p[(i + 1) % n], p[(i + 2) % n]
        t0 = 0.0
        t1 = t0 + np.linalg.norm(P1 - P0) ** 0.5 + 1e-9
        t2 = t1 + np.linalg.norm(P2 - P1) ** 0.5 + 1e-9
        t3 = t2 + np.linalg.norm(P3 - P2) ** 0.5 + 1e-9
        for t in np.linspace(t1, t2, sub, endpoint=False):
            A1 = (t1 - t) / (t1 - t0) * P0 + (t - t0) / (t1 - t0) * P1
            A2 = (t2 - t) / (t2 - t1) * P1 + (t - t1) / (t2 - t1) * P2
            A3 = (t3 - t) / (t3 - t2) * P2 + (t - t2) / (t3 - t2) * P3
            B1 = (t2 - t) / (t2 - t0) * A1 + (t - t0) / (t2 - t0) * A2
            B2 = (t3 - t) / (t3 - t1) * A2 + (t - t1) / (t3 - t1) * A3
            out.append((t2 - t) / (t2 - t1) * B1 + (t - t1) / (t2 - t1) * B2)
    return np.array(out)

def arco(p):
    q = np.r_[p, p[:1]]
    return np.r_[0, np.cumsum(np.linalg.norm(np.diff(q, axis=0), axis=1))]

def remuestrear(p, desde, paso):
    s = arco(p); L = s[-1]; q = np.r_[p, p[:1]]
    n = int(round(L / paso))
    t = (desde + np.arange(n) * (L / n)) % L
    return np.c_[np.interp(t, s, q[:, 0]), np.interp(t, s, q[:, 1])], L

def radio_minimo(p):
    # Radio de giro más cerrado, con una ventana de 15 m para no medir ruido.
    k = max(1, int(15 / PASO))
    a, b, c = np.roll(p, k, 0), p, np.roll(p, -k, 0)
    ab, bc, ca = (np.linalg.norm(x - y, axis=1) for x, y in ((a, b), (b, c), (c, a)))
    area2 = np.abs((b - a)[:, 0] * (c - a)[:, 1] - (b - a)[:, 1] * (c - a)[:, 0])
    return float(np.min(ab * bc * ca / (2 * area2 + 1e-9)))

geo = {f["properties"]["id"]: f for f in
       json.load(open(os.path.join(AQUI, "fuentes", "f1-circuits.geojson"), encoding="utf-8"))["features"]}
salidas = json.load(open(os.path.join(AQUI, "salidas.json"), encoding="utf-8"))
os.makedirs(SALIDA, exist_ok=True)

calendario = []
for ronda, gid, gp, bandera, plano, fecha in CALENDARIO:
    f = geo[gid]; props = f["properties"]
    p = metros(f["geometry"]["coordinates"])
    s_gps = arco(p)
    info = salidas.get(gid)
    verificada = info is not None
    salida_m = info["salida_m"] if info else 0.0
    sentido = info["sentido"] if info else 1
    if sentido < 0:                       # dar la vuelta conservando el punto de salida
        salida_m = s_gps[-1] - salida_m
        p = np.r_[p[:1], p[1:][::-1]]
    liso = catmull_rom(p)
    # Llevar la salida medida sobre el GPS al trazado suavizado (misma fracción de vuelta).
    desde = salida_m / s_gps[-1] * arco(liso)[-1]
    pts, L = remuestrear(liso, desde, PASO)
    pts -= pts[0]
    area = 0.5 * np.sum(pts[:, 0] * np.roll(pts[:, 1], -1) - np.roll(pts[:, 0], -1) * pts[:, 1])
    datos = {
        "id": gid, "nombre": props["Name"], "granPremio": gp, "localidad": props["Location"],
        "bandera": bandera, "plano": plano or "",
        "longitudOficial": props["length"], "longitudMedida": round(L, 1),
        "salidaVerificada": verificada,
        "giro": "horario" if area < 0 else "antihorario",
        "radioMinimo": round(radio_minimo(pts), 1),
        "poleReferencia": POLES.get(gid, 0.0),
        "tipo": "urbano" if gid in URBANOS else "permanente",
        "ancho": URBANOS.get(gid, 13.0),
        **ENTORNOS[gid],
        "x": [round(v, 2) for v in pts[:, 0]], "z": [round(v, 2) for v in pts[:, 1]],
    }
    json.dump(datos, open(os.path.join(SALIDA, gid + ".json"), "w", encoding="utf-8"), ensure_ascii=False)
    calendario.append({"ronda": ronda, "circuito": gid, "granPremio": gp, "fecha": fecha})
    print(f"{ronda:2} {gid}  {L:7.0f} m (oficial {props['length']})  {len(pts):4} puntos  "
          f"{datos['giro']:11} radio mín. {datos['radioMinimo']:5.1f} m  "
          f"{'' if verificada else 'SALIDA SIN VERIFICAR'}")

json.dump({"temporada": 2026, "carreras": calendario},
          open(os.path.join(SALIDA, "calendario_2026.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
