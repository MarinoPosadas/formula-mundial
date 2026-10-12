# Encaja el trazado GPS sobre la imagen del plano (giro + escala + desplazamiento) y lee
# en el plano la salida (barra roja) y el sentido (flecha). No necesita trazar el plano,
# así que vale también para los que tienen cruces (Suzuka, Zandvoort).
# Uso: python encajar.py <geojson> <carpeta planos> <salida.json> <hoja.png>
import sys, json, math
import numpy as np
from PIL import Image, ImageDraw

PLANOS = {"abu-dhabi": "ae-2009", "australia": "au-1953", "austria": "at-1969", "azerbaiyan": "az-2016",
          "barcelona-catalunya": "es-1991", "belgica": "be-1925", "brasil": "br-1940", "canada": "ca-1978",
          "china": "cn-2004", "eeuu": "us-2012", "espana": "es-2026", "gran-bretana": "gb-1948",
          "hungria": "hu-1986", "italia": "it-1922", "japon": "jp-1962", "las-vegas": "us-2023",
          "mexico": "mx-1962", "miami": "us-2022", "monaco": "mc-1929", "paises-bajos": "nl-1948",
          "qatar": "qa-2004", "singapur": "sg-2008"}

def metros(coords):
    c = np.array(coords, float)
    if np.allclose(c[0], c[-1]): c = c[:-1]
    lat0 = math.radians(c[:, 1].mean())
    return np.c_[np.radians(c[:, 0]) * 6371000 * math.cos(lat0), np.radians(c[:, 1]) * 6371000]

def remuestrear(p, n):
    q = np.r_[p, p[:1]]
    s = np.r_[0, np.cumsum(np.linalg.norm(np.diff(q, axis=0), axis=1))]
    t = np.linspace(0, s[-1], n, endpoint=False)
    return np.c_[np.interp(t, s, q[:, 0]), np.interp(t, s, q[:, 1])], t, s[-1]

def dist_min(A, B):
    return np.sqrt(((A[:, None, :] - B[None, :, :]) ** 2).sum(-1)).min(1)

def colocar(G, th, s, c_img):
    R = np.array([[math.cos(th), -math.sin(th)], [math.sin(th), math.cos(th)]])
    Q = (G - G.mean(0)) @ R.T * s
    return Q + c_img

geo = {f["properties"]["id"]: f for f in json.load(open(sys.argv[1], encoding="utf-8"))["features"]}
res, hoja_items = {}, []
for plano, gid in PLANOS.items():
    img = np.asarray(Image.open(f"{sys.argv[2]}/{plano}.jpg").convert("RGB")).astype(int)
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    rojo = ((r - g) > 60) & ((r - b) > 60)
    negro = ((r + g + b) < 300) & ~rojo
    ys, xs = np.nonzero(negro)
    T = np.c_[xs, -ys].astype(float)[::5]                    # plano con y hacia arriba
    G, t, L = remuestrear(metros(geo[gid]["geometry"]["coordinates"]), 240)
    c_img = (T.min(0) + T.max(0)) / 2
    mejor = None
    for th in np.radians(np.arange(0, 360, 3)):
        Q0 = colocar(G, th, 1.0, 0)
        s = min(np.ptp(T[:, 0]) / np.ptp(Q0[:, 0]), np.ptp(T[:, 1]) / np.ptp(Q0[:, 1]))
        Q = colocar(G, th, s, 0); Q += c_img - (Q.min(0) + Q.max(0)) / 2
        e = dist_min(Q[::3], T).mean()
        if mejor is None or e < mejor[0]: mejor = (e, th, s, c_img - (colocar(G, th, s, 0).min(0) + colocar(G, th, s, 0).max(0)) / 2)
    # Afinar giro, escala y desplazamiento alrededor del mejor.
    e, th, s, d0 = mejor
    for paso in (1.0, 0.3):
        for dth in np.radians(np.arange(-2, 2.01, paso)):
            for ds in (0.97, 0.985, 1.0, 1.015, 1.03):
                for dx in (-4, 0, 4):
                    for dy in (-4, 0, 4):
                        Q = colocar(G, th + dth, s * ds, 0) + d0 + [dx, dy]
                        e2 = dist_min(Q[::2], T).mean()
                        if e2 < e: e, th, s, d0 = e2, th + dth, s * ds, d0 + [dx, dy]
    Q = colocar(G, th, s, 0) + d0
    # Salida: barra roja (la parte sobre el trazo o, si no lo toca, la más cercana a él).
    ry, rx = np.nonzero(rojo); Rj = np.c_[rx, -ry].astype(float)
    d_r = dist_min(Rj, T)
    barra = Rj[d_r < 6] if (d_r < 6).sum() > 5 else Rj[d_r <= d_r.min() + 3]
    i0 = int(np.argmin(dist_min(Q, barra)))
    tan = Q[(i0 + 3) % len(Q)] - Q[i0 - 3]; tan /= np.linalg.norm(tan)
    punta = Rj[np.argmax(np.linalg.norm(Rj - barra.mean(0), axis=1))]
    sentido = 1 if (punta - Q[i0]) @ tan > 0 else -1
    res[gid] = {"plano": plano, "salida_m": round(float(t[i0]), 1), "sentido": sentido,
                "error_px": round(float(e), 2), "longitud": geo[gid]["properties"]["length"]}
    print(f"{plano:20} {gid}  error medio {e:4.1f} px  salida a {t[i0]:6.0f} m del primer punto GPS  "
          f"sentido {'como el GPS' if sentido > 0 else 'CONTRARIO al GPS'}")
    hoja_items.append((plano, img, Q, i0, sentido))

json.dump(res, open(sys.argv[3], "w", encoding="utf-8"), indent=1, ensure_ascii=False)

W, H, cols = 400, 204, 4
rows = (len(hoja_items) + cols - 1) // cols
hoja = Image.new("RGB", (W * cols, H * rows), "white")
for i, (n, img, Q, i0, sg) in enumerate(hoja_items):
    im = Image.fromarray(np.uint8(255 - (255 - img) // 4)); d = ImageDraw.Draw(im)
    P = [(x, -y) for x, y in Q]
    d.line(P + P[:1], fill=(40, 70, 220), width=3)
    x, y = P[i0]; d.ellipse([x - 8, y - 8, x + 8, y + 8], outline=(0, 170, 0), width=4)
    j = (i0 + 6 * sg) % len(P); d.line([P[i0], P[j]], fill=(0, 170, 0), width=6)
    d.text((8, 6), n, fill=(0, 0, 0))
    hoja.paste(im.resize((W, H)), ((i % cols) * W, (i // cols) * H))
hoja.save(sys.argv[4])
