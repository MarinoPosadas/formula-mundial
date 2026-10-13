using System.Collections.Generic;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// El fondo de cada circuito, aproximado a su geografía: mar en su dirección, montañas
    /// (alrededor o hacia un lado), skyline de ciudad a lo lejos y neblina de distancia.
    /// Dice además dónde hay agua, para que el decorado no plante árboles en el mar.
    /// </summary>
    public sealed class Entorno
    {
        readonly Vector3 dirMar;
        readonly float lineaMar = float.MaxValue;     // a partir de aquí (proyección sobre dirMar) es mar

        public static readonly Color ColorNiebla = new Color(0.74f, 0.81f, 0.88f);

        public Entorno(Trazado t, DatosCircuito d, CatalogoDecorado cat, ConstructorPista.Materiales m, Transform padre)
        {
            var raiz = new GameObject("Entorno");
            raiz.transform.SetParent(padre, false);
            int semilla = 7; foreach (char ch in d.id ?? "") semilla = semilla * 31 + ch;
            var azar = new System.Random(semilla);

            // Centro y radio del circuito.
            Vector3 centro = Vector3.zero; float radio = 0f;
            for (int i = 0; i < t.Cantidad; i++) centro += t.Punto(i).U();
            centro /= t.Cantidad;
            for (int i = 0; i < t.Cantidad; i++) radio = Mathf.Max(radio, Vector3.Distance(t.Punto(i).U(), centro));

            if (d.mar >= 0f)
            {
                dirMar = Direccion(d.mar);
                float max = float.MinValue;
                for (int i = 0; i < t.Cantidad; i++) max = Mathf.Max(max, Vector3.Dot(t.Punto(i).U(), dirMar));
                // En los urbanos el mar está pegado (el puerto de Mónaco); en el resto, más lejos.
                lineaMar = max + (d.Urbano ? 70f : 450f);
                Malla(raiz, "Mar", m.agua, Mar(dirMar, lineaMar));
            }
            if (d.montanas > 0f)
                Malla(raiz, "Montañas", d.montanas > 600f ? m.montanaAlta : m.montana,
                      Montanas(centro, radio + 2500f, radio + 9000f, d.montanas, d.montanasDir, azar));
            if (d.ciudad >= 0f && cat != null && cat.edificios != null && cat.edificios.Length > 0)
                Skyline(raiz.transform, centro, radio, d.ciudad, cat, azar);
        }

        public bool EnAgua(Vector3 q) => Vector3.Dot(q, dirMar) > lineaMar - 25f;

        static Vector3 Direccion(float grados) =>
            new Vector3(Mathf.Sin(grados * Mathf.Deg2Rad), 0f, Mathf.Cos(grados * Mathf.Deg2Rad));

        /// <summary>La niebla de distancia: funde el horizonte con el cielo.</summary>
        public static void Niebla(bool activa)
        {
            RenderSettings.fog = activa;
            RenderSettings.fogMode = FogMode.Linear;
            // Con lluvia, gris y mucho más cerca.
            RenderSettings.fogColor = Lluvia.Activa ? new Color(0.52f, 0.55f, 0.6f) : ColorNiebla;
            RenderSettings.fogStartDistance = Lluvia.Activa ? 60f : 900f;
            RenderSettings.fogEndDistance = Lluvia.Activa ? 1800f : 13000f;
        }

        static Mesh Mar(Vector3 dir, float linea)
        {
            Vector3 lado = Vector3.Cross(Vector3.up, dir);
            Vector3 a = dir * linea, b = dir * (linea + 12000f);
            var v = new[] { a - lado * 12000f + Vector3.up * 0.05f, a + lado * 12000f + Vector3.up * 0.05f,
                            b - lado * 12000f + Vector3.up * 0.05f, b + lado * 12000f + Vector3.up * 0.05f };
            var mesh = new Mesh { name = "Mar" };
            mesh.vertices = v;
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(240, 0), new Vector2(0, 240), new Vector2(240, 240) };
            // Cara hacia arriba (el orden depende de hacia dónde caiga 'lado').
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            if (mesh.normals[0].y < 0f) { mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 }; mesh.RecalculateNormals(); }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Relieve en anillo (de r0 a r1): alturas de ruido fractal que suben poco a poco desde el
        /// llano, más altas hacia su dirección. Caras planas (estilo low poly). La primera
        /// versión, una sola cresta, parecía una pared de cartón.
        /// </summary>
        static Mesh Montanas(Vector3 c, float r0, float r1, float alto, float dir, System.Random azar)
        {
            const int NA = 220, NR = 16;
            float fx = (float)azar.NextDouble() * 1000f, fz = (float)azar.NextDouble() * 1000f;
            var alturas = new Vector3[NA + 1, NR + 1];
            for (int i = 0; i <= NA; i++)
            {
                float ang = i * 360f / NA;
                float peso = 1f;
                if (dir >= 0f)
                    peso = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.55f, Mathf.Cos((ang - dir) * Mathf.Deg2Rad)));
                Vector3 d = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad));
                for (int j = 0; j <= NR; j++)
                {
                    float u = (float)j / NR;
                    Vector3 p = c + d * Mathf.Lerp(r0, r1, u);
                    // Perfil: sube del llano a la sierra en el primer 40 %.
                    float perfil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.4f, u));
                    float n = 0f, amp = 1f, frec = 1f / 1800f, tot = 0f;
                    for (int o = 0; o < 4; o++)
                    {
                        n += amp * Mathf.PerlinNoise(fx + p.x * frec, fz + p.z * frec);
                        tot += amp; amp *= 0.5f; frec *= 2.1f;
                    }
                    n /= tot;
                    float h = alto * peso * perfil * Mathf.Pow(Mathf.Clamp01((n - 0.25f) / 0.6f), 1.4f) * 1.6f;
                    alturas[i, j] = p + Vector3.up * (h - 4f);
                }
            }
            var v = new List<Vector3>(); var tri = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 e) { int k = v.Count; v.Add(a); v.Add(b); v.Add(e); tri.Add(k); tri.Add(k + 1); tri.Add(k + 2); }
            for (int i = 0; i < NA; i++)
                for (int j = 0; j < NR; j++)
                {
                    Vector3 a = alturas[i, j], b = alturas[i + 1, j], e = alturas[i, j + 1], f = alturas[i + 1, j + 1];
                    Tri(a, e, b); Tri(b, e, f);
                }
            var mesh = new Mesh { name = "Montañas", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            // Caras hacia arriba (si el orden salió al revés, se gira).
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
            {
                for (int k = 0; k < tri.Count; k += 3) { int x = tri[k]; tri[k] = tri[k + 1]; tri[k + 1] = x; }
                mesh.SetTriangles(tri, 0); mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Edificios grandes a 1,2-2,6 km: la silueta de la ciudad.</summary>
        void Skyline(Transform raiz, Vector3 c, float radio, float dir, CatalogoDecorado cat, System.Random azar)
        {
            int n = dir >= 900f ? 70 : 34;
            for (int k = 0; k < n; k++)
            {
                float ang = dir >= 900f ? (float)azar.NextDouble() * 360f : dir + ((float)azar.NextDouble() - 0.5f) * 70f;
                float dist = radio + 1200f + (float)azar.NextDouble() * 1400f;
                Vector3 q = c + Direccion(ang) * dist;
                if (EnAgua(q)) continue;
                var pieza = cat.edificios[azar.Next(cat.edificios.Length)];
                var go = Object.Instantiate(pieza, raiz);
                go.name = "Skyline";
                go.transform.rotation = Quaternion.Euler(0f, (float)azar.NextDouble() * 360f, 0f) * pieza.transform.rotation;
                var b = Medir(go);
                float alto = 50f + (float)azar.NextDouble() * 110f;
                if (b.size.y > 0.01f) go.transform.localScale *= alto / b.size.y;
                b = Medir(go);
                go.transform.position += new Vector3(q.x - b.center.x, -b.min.y, q.z - b.center.z);
            }
        }

        static Bounds Medir(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void Malla(GameObject raiz, string nombre, Material m, Mesh malla)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(raiz.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = ConstructorPista.Registrar(malla);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
        }
    }
}
