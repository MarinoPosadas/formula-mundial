using System.Collections.Generic;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Viste un circuito a partir de su trazado: muros (con escapatoria por fuera de las curvas en
    /// los permanentes, pegados a la pista en los urbanos), grava, pit lane y boxes, gradas,
    /// pórtico de salida, publicidad, árboles o edificios. Todo se coloca comprobando que no
    /// invade otro tramo del circuito, y con una semilla por circuito: sale siempre igual.
    /// </summary>
    public static class ConstructorDecorado
    {
        public static GameObject Construir(Trazado t, DatosCircuito datos, CatalogoDecorado cat,
                                           ConstructorPista.Materiales m, Transform padre, Entorno entorno = null)
        {
            var raiz = new GameObject("Decorado");
            raiz.transform.SetParent(padre, false);
            var c = new Contexto(t, datos, cat, m, raiz.transform, entorno);
            c.CalcularBarreras();
            c.Muros();
            if (datos.Urbano) c.Aceras();
            if (!datos.Urbano) c.Gravas();
            c.Boxes();
            c.Portico();
            c.Gradas();
            c.Publicidad();
            if (datos.Urbano) c.Ciudad(); else c.Arboles();
            return raiz;
        }

        class Contexto
        {
            readonly Trazado t;
            readonly DatosCircuito datos;
            readonly CatalogoDecorado cat;
            readonly ConstructorPista.Materiales mat;
            readonly Transform raiz;
            readonly System.Random azar;
            readonly int n;
            readonly float ancho, L;
            readonly Vector3[] P, R;
            readonly float[] k;                      // curvatura con signo (+ izquierda)
            readonly float[][] off = new float[2][]; // distancia al muro: [0] izquierda, [1] derecha
            readonly int ladoBoxes;                  // -1 izquierda, +1 derecha
            readonly Dictionary<(int, int), List<int>> rejilla = new Dictionary<(int, int), List<int>>();
            readonly List<(Vector3 c, float r)> reservas = new List<(Vector3, float)>();
            const float Celda = 40f;
            const float AltoMaximoEdificio = 60f;
            const float AltoDeChoque = 4.5f;

            // Pit lane: de 520 m antes de la meta a 280 m después.
            const float PitDesde = -520f, PitHasta = 280f, PitAncho = 8f, Garaje = 14f, FondoGaraje = 16f;

            readonly Entorno entorno;

            public Contexto(Trazado t, DatosCircuito d, CatalogoDecorado cat, ConstructorPista.Materiales m, Transform raiz, Entorno entorno)
            {
                this.t = t; datos = d; this.cat = cat; mat = m; this.raiz = raiz; this.entorno = entorno;
                ancho = d.ancho > 0f ? d.ancho : ConstructorPista.AnchoNormal;
                L = t.Longitud; n = t.Cantidad;
                int semilla = 17; foreach (char ch in d.id ?? "") semilla = semilla * 31 + ch;
                azar = new System.Random(semilla);
                P = new Vector3[n]; R = new Vector3[n]; k = new float[n];
                for (int i = 0; i < n; i++)
                {
                    P[i] = t.Punto(i).U();
                    Vector3 f = (t.Punto(i + 1) - t.Punto(i - 1)).U(); f.y = 0f;
                    R[i] = Vector3.Cross(Vector3.up, f.normalized).normalized;
                    k[i] = t.Curvatura(i, 6);
                    var celda = CeldaDe(P[i]);
                    if (!rejilla.TryGetValue(celda, out var l)) rejilla[celda] = l = new List<int>();
                    l.Add(i);
                }
                // Boxes por fuera del lazo: en un circuito horario el exterior queda a la izquierda.
                ladoBoxes = d.giro == "horario" ? -1 : 1;
            }

            (int, int) CeldaDe(Vector3 p) => (Mathf.FloorToInt(p.x / Celda), Mathf.FloorToInt(p.z / Celda));
            int Lado(int s) => s < 0 ? 0 : 1;
            float Radio(int i) => 1f / Mathf.Max(Mathf.Abs(k[i]), 1e-4f);
            int Indice(float s) => Mathf.Clamp(Mathf.RoundToInt(t.Envolver(s) / L * n), 0, n - 1) % n;
            float DistCircular(float a, float b) { float d = Mathf.Abs(t.Envolver(a) - t.Envolver(b)); return Mathf.Min(d, L - d); }
            bool EnPit(float s) { float r = DistRel(s); return r >= PitDesde && r <= PitHasta; }
            float DistRel(float s) { float r = t.Envolver(s); return r > L / 2 ? r - L : r; }

            /// <summary>¿Hay pista (de otro tramo que el propio) a menos de 'radio' de q?</summary>
            bool Libre(Vector3 q, float radio, float sPropia = float.NaN, float ventana = 120f)
            {
                var (cx, cz) = CeldaDe(q);
                int alcance = Mathf.CeilToInt(radio / Celda);
                for (int dx = -alcance; dx <= alcance; dx++)
                    for (int dz = -alcance; dz <= alcance; dz++)
                        if (rejilla.TryGetValue((cx + dx, cz + dz), out var l))
                            foreach (int i in l)
                            {
                                if (!float.IsNaN(sPropia) && DistCircular(t.DistanciaDe(i), sPropia) < ventana) continue;
                                Vector3 v = P[i] - q; v.y = 0f;
                                if (v.sqrMagnitude < radio * radio) return false;
                            }
                foreach (var (c, r) in reservas)
                {
                    Vector3 v = c - q; v.y = 0f;
                    if (v.magnitude < r) return false;
                }
                return true;
            }

            float Aleatorio(float a, float b) => a + (b - a) * (float)azar.NextDouble();

            // ── Muros ────────────────────────────────────────────────────────

            public void CalcularBarreras()
            {
                foreach (int lado in new[] { -1, 1 })
                {
                    var o = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float radio = Radio(i);
                        bool fuera = (k[i] > 0f && lado > 0) || (k[i] < 0f && lado < 0);
                        if (datos.Urbano) o[i] = ancho / 2f + 1.2f;
                        else
                        {
                            o[i] = ancho / 2f + 9f;
                            if (fuera && radio < 350f) o[i] += 28f * (1f - radio / 350f);   // escapatoria
                            if (!fuera) o[i] = Mathf.Min(o[i], Mathf.Max(ancho / 2f + 3f, 0.6f * radio));
                        }
                    }
                    o = Suavizar(MaximoMovil(o, 12), 8);
                    // Que el muro no invada otro tramo del circuito.
                    for (int i = 0; i < n; i++)
                    {
                        float s = t.DistanciaDe(i);
                        while (o[i] > ancho / 2f + 1.2f && !Libre(P[i] + R[i] * (lado * o[i]), ancho / 2f + 3f, s, 150f))
                            o[i] -= 1.5f;
                    }
                    o = Suavizar(MinimoMovil(o, 4), 4);
                    // En el lado de boxes, el muro pasa por detrás de los garajes.
                    if (lado == ladoBoxes)
                        for (int i = 0; i < n; i++)
                        {
                            float r = DistRel(t.DistanciaDe(i));
                            float f = Rampa(r, PitDesde, PitHasta, 100f);
                            o[i] = Mathf.Lerp(o[i], ancho / 2f + PitAncho + 4f + FondoGaraje + 3f, f);
                        }
                    off[Lado(lado)] = o;
                }
            }

            static float Rampa(float x, float desde, float hasta, float transicion) =>
                Mathf.Clamp01(Mathf.Min((x - desde) / transicion, (hasta - x) / transicion));

            static float[] MaximoMovil(float[] a, int v)
            {
                var r = new float[a.Length];
                for (int i = 0; i < a.Length; i++) { float m = float.MinValue; for (int j = -v; j <= v; j++) m = Mathf.Max(m, a[(i + j + a.Length) % a.Length]); r[i] = m; }
                return r;
            }

            static float[] MinimoMovil(float[] a, int v)
            {
                var r = new float[a.Length];
                for (int i = 0; i < a.Length; i++) { float m = float.MaxValue; for (int j = -v; j <= v; j++) m = Mathf.Min(m, a[(i + j + a.Length) % a.Length]); r[i] = m; }
                return r;
            }

            static float[] Suavizar(float[] a, int v)
            {
                var r = new float[a.Length];
                for (int i = 0; i < a.Length; i++) { float s = 0f; for (int j = -v; j <= v; j++) s += a[(i + j + a.Length) % a.Length]; r[i] = s / (2 * v + 1); }
                return r;
            }

            Vector3 EnMuro(int i, int lado, float extra = 0f) => P[i] + R[i] * (lado * (off[Lado(lado)][i] + extra));

            public void Muros()
            {
                float alto = datos.Urbano ? 1.2f : 1.0f;
                var material = datos.Urbano ? mat.hormigon : mat.barrera;
                foreach (int lado in new[] { -1, 1 })
                {
                    var pts = new Vector3[n + 1];
                    for (int i = 0; i <= n; i++) pts[i] = EnMuro(i % n, lado);
                    Malla("Muro " + (lado < 0 ? "izquierdo" : "derecho"), material, Pared(pts, alto, true));
                }
            }

            /// <summary>Pared vertical a lo largo de unos puntos, con sus dos caras y su remate.</summary>
            Mesh Pared(Vector3[] pts, float alto, bool cerrada)
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                float s = 0f;
                for (int cara = 0; cara < 2; cara++)
                {
                    s = 0f;
                    int b = v.Count;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        if (i > 0) s += Vector3.Distance(pts[i - 1], pts[i]);
                        v.Add(pts[i] + Vector3.down * 0.3f); uv.Add(new Vector2(s / 2f, 0f));
                        v.Add(pts[i] + Vector3.up * alto); uv.Add(new Vector2(s / 2f, 1f));
                        if (i == 0) continue;
                        int a0 = b + 2 * (i - 1), a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                        if (cara == 0) { tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(b0); tri.Add(a1); tri.Add(b1); }
                        else { tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(b0); tri.Add(b1); tri.Add(a1); }
                    }
                }
                var mesh = new Mesh { name = "Pared" };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>Cinta plana entre dos desplazamientos (función de i), solo donde 'incluir' es cierto.</summary>
            Mesh Cinta(int lado, System.Func<int, (float a, float b)> franja, System.Func<int, bool> incluir, float alto, float metrosTextura)
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (!incluir(i) || !incluir(j)) continue;
                    var (a0, b0) = franja(i); var (a1, b1) = franja(j);
                    if (b0 <= a0 || b1 <= a1) continue;
                    int b = v.Count;
                    Vector3 up = Vector3.up * alto;
                    v.Add(P[i] + R[i] * (lado * a0) + up); v.Add(P[i] + R[i] * (lado * b0) + up);
                    v.Add(P[j] + R[j] * (lado * a1) + up); v.Add(P[j] + R[j] * (lado * b1) + up);
                    float s0 = t.DistanciaDe(i) / metrosTextura, s1 = s0 + 5f / metrosTextura;
                    uv.Add(new Vector2(0, s0)); uv.Add(new Vector2(1, s0)); uv.Add(new Vector2(0, s1)); uv.Add(new Vector2(1, s1));
                    if (lado > 0) { tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3); }
                    else { tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b + 3); tri.Add(b + 2); }
                }
                var mesh = new Mesh { name = "Cinta" };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>Urbanos: del borde de la pista a 3,5 m detrás del muro, todo es acera.</summary>
            public void Aceras()
            {
                foreach (int lado in new[] { -1, 1 })
                {
                    int L2 = Lado(lado);
                    Malla("Acera", mat.acera, Cinta(lado, i => (ancho / 2f, off[L2][i] + 3.5f), i => true, 0.01f, 4f));
                }
            }

            public void Gravas()
            {
                foreach (int lado in new[] { -1, 1 })
                {
                    var hay = new bool[n];
                    for (int i = 0; i < n; i++)
                    {
                        bool fuera = (k[i] > 0f && lado > 0) || (k[i] < 0f && lado < 0);
                        if (fuera && Radio(i) < 180f) for (int d = -8; d <= 8; d++) hay[(i + d + n) % n] = true;
                    }
                    if (lado == ladoBoxes) for (int i = 0; i < n; i++) if (EnPit(t.DistanciaDe(i))) hay[i] = false;
                    int L2 = Lado(lado);
                    Malla("Grava", mat.grava, Cinta(lado, i => (ancho / 2f + 1.5f, off[L2][i] - 1.5f), i => hay[i], 0.012f, 6f));
                }
            }

            // ── Boxes y salida ───────────────────────────────────────────────

            public void Boxes()
            {
                int lado = ladoBoxes;
                // Pit lane: entra y sale en rampa hacia la pista.
                Malla("Pit lane", mat.asfalto, Cinta(lado, i =>
                {
                    float f = Rampa(DistRel(t.DistanciaDe(i)), PitDesde, PitHasta, 120f);
                    return (ancho / 2f + 1.5f * f, ancho / 2f + (PitAncho + 4f) * f);
                }, i => EnPit(t.DistanciaDe(i)), 0.018f, 10f));

                // Muro de boxes entre la pista y el pit lane (donde ya está separado del todo).
                var muro = new List<Vector3>();
                for (float s = PitDesde + 130f; s <= PitHasta - 130f; s += 5f)
                {
                    int i = Indice(s);
                    muro.Add(P[i] + R[i] * (lado * (ancho / 2f + 3f)));
                }
                if (muro.Count > 1) Malla("Muro de boxes", mat.hormigon, Pared(muro.ToArray(), 1.0f, false));

                // 22 garajes (dos por escudería) mirando a la pista, con oficinas en los extremos.
                float centro = -150f;
                for (int j = -1; j <= 22; j++)
                {
                    float s = centro + (j - 10.5f) * Garaje;
                    int i = Indice(s);
                    Vector3 q = P[i] + R[i] * (lado * (ancho / 2f + PitAncho + 4f + FondoGaraje / 2f));
                    var pieza = j < 0 || j == 22 ? cat.oficinaBoxes : cat.garaje;
                    Poner(pieza, q, -R[i] * lado, Garaje, false, cat.giroKenney, "Garaje");
                    reservas.Add((q, Garaje));
                }
            }

            public void Portico()
            {
                int i = Indice(4f);
                Vector3 f = (P[(i + 1) % n] - P[i]); f.y = 0f;
                // Los semáforos miran a la parrilla, que está detrás de la línea.
                Poner(cat.porticoSalida, P[i], -f.normalized, ancho + 6f, false, cat.giroKenney, "Pórtico de salida", comprobar: false);
            }

            void FilaDeGradas(float s0, float s1, int lado, string nombre)
            {
                for (float s = s0; s <= s1; s += 15f)
                {
                    int i = Indice(s);
                    Vector3 q = EnMuro(i, lado, 10f);
                    if (!Libre(q, ancho / 2f + 10f, t.DistanciaDe(i), 200f)) continue;
                    Poner(cat.gradaCubierta, q, -R[i] * lado, 15f, false, cat.giroKenney, nombre);
                    reservas.Add((q, 9f));
                }
            }

            public void Gradas()
            {
                // Tribuna principal, enfrente de los boxes.
                FilaDeGradas(-400f, 120f, -ladoBoxes, "Tribuna principal");

                // Y en las curvas más lentas, por fuera, separadas entre sí.
                var curvas = new List<int>();
                for (int i = 0; i < n; i++)
                    if (Radio(i) < 120f && Mathf.Abs(k[i]) >= Mathf.Abs(k[(i + n - 3) % n]) && Mathf.Abs(k[i]) >= Mathf.Abs(k[(i + 3) % n]))
                        curvas.Add(i);
                curvas.Sort((a, b) => Mathf.Abs(k[b]).CompareTo(Mathf.Abs(k[a])));
                var elegidas = new List<float>();
                foreach (int i in curvas)
                {
                    float s = t.DistanciaDe(i);
                    if (DistCircular(s, 0f) < 600f || elegidas.Exists(e => DistCircular(e, s) < 500f)) continue;
                    elegidas.Add(s);
                    int fuera = k[i] > 0f ? 1 : -1;
                    FilaDeGradas(s - 30f, s + 30f, fuera, "Grada de curva");
                    if (elegidas.Count == 4) break;
                }
            }

            public void Publicidad()
            {
                int alterna = 0;
                for (float s = 300f; s < L - 300f; s += 170f)
                {
                    int i = Indice(s);
                    if (Radio(i) < 700f || EnPit(s)) continue;
                    int lado = (alterna++ % 2 == 0) ? 1 : -1;
                    Vector3 q = EnMuro(i, lado, 3f);
                    if (!Libre(q, ancho / 2f + 3f, s, 150f)) continue;
                    bool torre = azar.NextDouble() < 0.35;
                    var pieza = torre ? (azar.NextDouble() < 0.5 ? cat.torreRoja : cat.torreVerde) : cat.valla;
                    Poner(pieza, q, -R[i] * lado, torre ? 9f : 12f, torre, cat.giroKenney, "Publicidad");
                    reservas.Add((q, 6f));
                }
            }

            // ── Alrededores ──────────────────────────────────────────────────

            public void Arboles()
            {
                Vector3 min = P[0], max = P[0];
                foreach (var p in P) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                min -= Vector3.one * 350f; max += Vector3.one * 350f;
                int puestos = 0, cuantos = datos.Desierto ? 60 : 900;   // en el desierto, pocos
                for (int intento = 0; intento < 6000 && puestos < cuantos; intento++)
                {
                    var q = new Vector3(Aleatorio(min.x, max.x), 0f, Aleatorio(min.z, max.z));
                    if (entorno != null && entorno.EnAgua(q)) continue;
                    if (!Libre(q, ancho / 2f + 45f)) continue;
                    bool grande = azar.NextDouble() < 0.6;
                    float giro = Aleatorio(0f, 360f);
                    Poner(grande ? cat.arbolGrande : cat.arbolPequeno, q, Quaternion.Euler(0, giro, 0) * Vector3.forward,
                          Aleatorio(grande ? 14f : 8f, grande ? 22f : 13f), true, 0f, "Árbol", comprobar: false);
                    puestos++;
                }
            }

            public void Ciudad()
            {
                if (cat.edificios == null || cat.edificios.Length == 0) return;
                foreach (int lado in new[] { -1, 1 })
                {
                    float s = 0f;
                    while (s < L)
                    {
                        float anchoEdificio = Aleatorio(16f, 26f);
                        float centro = s + anchoEdificio / 2f;
                        int i = Indice(centro);
                        s += anchoEdificio + 2f;
                        if (lado == ladoBoxes && EnPit(centro)) continue;
                        // Acera de 3 m y el edificio detrás.
                        Vector3 q = EnMuro(i, lado, 3f + anchoEdificio / 2f);
                        if (entorno != null && entorno.EnAgua(q)) continue;
                        if (!Libre(q, ancho / 2f + anchoEdificio / 2f + 2f, centro, 150f)) continue;
                        var pieza = cat.edificios[azar.Next(cat.edificios.Length)];
                        var e = Poner(pieza, q, -R[i] * lado, anchoEdificio, false, cat.giroEdificios, "Edificio");
                        // Los rascacielos del kit, escalados por ancho, pasaban de 100 m: tope de 60.
                        if (e != null)
                        {
                            float alto = Medir(e).size.y;
                            if (alto > AltoMaximoEdificio) e.transform.localScale *= AltoMaximoEdificio / alto;
                            var bb = Medir(e);
                            e.transform.position += new Vector3(q.x - bb.center.x, q.y - bb.min.y, q.z - bb.center.z);
                        }
                    }
                    for (float f = 20f; f < L; f += 40f)
                    {
                        int i = Indice(f);
                        if (lado == ladoBoxes && EnPit(f)) continue;
                        Vector3 q = EnMuro(i, lado, 1.2f);
                        if (!Libre(q, ancho / 2f + 1f, f, 100f)) continue;
                        // Sin comprobar por caja: la de una farola ocupa todo el hueco bajo el brazo y
                        // la daba siempre por invasora. El sitio del poste ya se ha mirado (Libre).
                        var farola = Poner(cat.farolaCalle, q, -R[i] * lado, 9f, true, cat.giroFarolaCalle, "Farola", comprobar: false);
                        if (farola != null) AlPie(farola, q);
                    }
                }
            }

            // ── Utilidades ───────────────────────────────────────────────────

            /// <summary>
            /// Coloca una pieza: su frente mirando a 'haciaDonde', escalada a un ancho (o alto) en
            /// metros, centrada en q y apoyada en el suelo (sus medidas se toman del modelo).
            /// </summary>
            GameObject Poner(GameObject prefab, Vector3 q, Vector3 haciaDonde, float tam, bool porAlto, float giro, string nombre,
                             bool comprobar = true)
            {
                if (prefab == null) return null;
                var go = Object.Instantiate(prefab, raiz);
                go.name = nombre;
                Quaternion propia = prefab.transform.rotation;
                go.transform.rotation = propia;
                go.transform.localScale = prefab.transform.localScale;
                Bounds b = Medir(go);
                float medida = porAlto ? b.size.y : b.size.x;
                if (medida > 1e-4f) go.transform.localScale *= tam / medida;
                haciaDonde.y = 0f;
                if (haciaDonde.sqrMagnitude < 1e-6f) haciaDonde = Vector3.forward;
                go.transform.rotation = Quaternion.LookRotation(haciaDonde.normalized, Vector3.up) * Quaternion.Euler(0f, giro, 0f) * propia;
                b = Medir(go);
                go.transform.position += new Vector3(q.x - b.center.x, q.y - b.min.y, q.z - b.center.z);
                // Si alguna parte de la pieza cae sobre la pista (de cualquier tramo), fuera.
                if (comprobar && Invade(go, 1.5f)) { Object.Destroy(go); return null; }
                return go;
            }

            /// <summary>
            /// ¿Pisa la pieza la pista? Se recorre su planta real (la caja de cada malla, girada,
            /// en una rejilla de 2 m) contra todo el trazado, sin excluir el tramo propio: en una
            /// horquilla el edificio de fuera puede caer sobre la pista que vuelve, y uno de 26 m
            /// puede tener la pista cruzándolo lejos de su centro.
            /// </summary>
            /// <summary>Mueve la pieza para que su pie (los vértices más bajos) quede en q.</summary>
            static void AlPie(GameObject go, Vector3 q)
            {
                var bajos = new List<Vector3>();
                float min = float.MaxValue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    foreach (var v in mf.sharedMesh.vertices) min = Mathf.Min(min, mf.transform.TransformPoint(v).y);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var w = mf.transform.TransformPoint(v);
                        if (w.y < min + 0.3f) bajos.Add(w);
                    }
                if (bajos.Count == 0) return;
                Vector3 pie = Vector3.zero;
                foreach (var w in bajos) pie += w;
                pie /= bajos.Count;
                go.transform.position += new Vector3(q.x - pie.x, q.y - min, q.z - pie.z);
            }

            bool Invade(GameObject go, float margen)
            {
                float limite = ancho / 2f + margen;
                float suelo = go.transform.position.y;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds;
                    var tr = mf.transform;
                    // Rejilla en los tres ejes locales: hay modelos con Z hacia arriba (FBX_cities).
                    int N(float tam) => Mathf.Clamp(Mathf.CeilToInt(tam / 2f), 1, 40);
                    int nx = N(tr.TransformVector(new Vector3(b.size.x, 0, 0)).magnitude);
                    int ny = N(tr.TransformVector(new Vector3(0, b.size.y, 0)).magnitude);
                    int nz = N(tr.TransformVector(new Vector3(0, 0, b.size.z)).magnitude);
                    for (int ix = 0; ix <= nx; ix++)
                        for (int iy = 0; iy <= ny; iy++)
                            for (int iz = 0; iz <= nz; iz++)
                            {
                                var w = tr.TransformPoint(new Vector3(b.min.x + b.size.x * ix / nx, b.min.y + b.size.y * iy / ny, b.min.z + b.size.z * iz / nz));
                                // Solo cuenta lo que un coche podría tocar: lo que vuela por
                                // encima (brazos de farola, pórticos) no es un choque.
                                if (w.y - suelo > AltoDeChoque) continue;
                                if (CercaDePista(w, limite)) return true;
                            }
                }
                return false;
            }

            bool CercaDePista(Vector3 q, float radio)
            {
                var (cx, cz) = CeldaDe(q);
                int alcance = Mathf.CeilToInt(radio / Celda);
                for (int dx = -alcance; dx <= alcance; dx++)
                    for (int dz = -alcance; dz <= alcance; dz++)
                        if (rejilla.TryGetValue((cx + dx, cz + dz), out var l))
                            foreach (int i in l)
                            {
                                Vector3 v = P[i] - q; v.y = 0f;
                                if (v.sqrMagnitude < radio * radio) return true;
                            }
                return false;
            }

            static Bounds Medir(GameObject go)
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                return b;
            }

            void Malla(string nombre, Material m, Mesh malla)
            {
                if (malla.vertexCount == 0) return;
                var go = new GameObject(nombre);
                go.transform.SetParent(raiz, false);
                go.AddComponent<MeshFilter>().sharedMesh = ConstructorPista.Registrar(malla);
                go.AddComponent<MeshRenderer>().sharedMaterial = m;
            }
        }
    }
}
