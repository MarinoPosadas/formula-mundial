using System.Collections.Generic;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Construye la pista a partir del trazado: asfalto, pianos en las curvas, línea de
    /// salida, cajones de parrilla, césped y los muros del puente si lo hay. Todo con mallas
    /// generadas; no hay piezas que colocar a mano.
    /// </summary>
    public static class ConstructorPista
    {
        public const float AnchoNormal = 13f;     // ancho de pista de un circuito permanente (metros)
        // Ancho del circuito que se está construyendo (Mónaco 10, urbanos estrechos 12...).
        static float Ancho = AnchoNormal;
        const float AnchoPiano = 1.4f;
        const float RadioPiano = 140f;           // hay piano donde el radio baja de esto

        // Mallas generadas por código (pista y decorado): las únicas que se pueden destruir al
        // cambiar de circuito. Las de las piezas importadas son assets y no se tocan.
        static readonly List<Mesh> generadas = new List<Mesh>();

        public static Mesh Registrar(Mesh m) { generadas.Add(m); return m; }

        public static void LiberarMallas()
        {
            foreach (var m in generadas) if (m != null) Object.Destroy(m);
            generadas.Clear();
        }

        public struct Materiales
        {
            public Material asfalto, cesped, piano, blanco, muro, cuadros, grava, hormigon, barrera, acera, sueloUrbano, arena, agua, montana, montanaAlta;
        }

        public static GameObject Construir(Trazado t, Materiales m, Transform padre = null, float ancho = AnchoNormal, bool urbano = false, bool desierto = false)
        {
            Ancho = ancho;
            var raiz = new GameObject("Pista");
            if (padre != null) raiz.transform.SetParent(padre, false);

            Malla(raiz, "Asfalto", m.asfalto, Cinta(t, -Ancho / 2, Ancho / 2, 0.02f, 10f));
            Malla(raiz, "Pianos", m.piano, Pianos(t));
            Malla(raiz, "Lineas", m.blanco, LineasDeParrilla(t));
            Malla(raiz, "Meta", m.cuadros, Franja(t, -1f, 1f, -Ancho / 2, Ancho / 2, 0.05f, 6f));
            var muros = MurosDelPuente(t);
            if (muros.vertexCount > 0) Malla(raiz, "Puente", m.muro, muros);
            // En los urbanos, alrededor no hay césped: hay ciudad.
            Malla(raiz, "Cesped", urbano && m.sueloUrbano != null ? m.sueloUrbano : desierto && m.arena != null ? m.arena : m.cesped, Cesped(t));
            return raiz;
        }

        /// <summary>Posición y orientación del cajón de parrilla k (0 = pole).</summary>
        public static Pose Parrilla(Trazado t, int k)
        {
            var (d, lateral) = t.Cajon(k);
            return Situar(t, d, lateral);
        }

        /// <summary>Un punto de la pista: distancia desde la salida y desplazamiento lateral (+ derecha).</summary>
        public static Pose Situar(Trazado t, float distancia, float lateral)
        {
            Vector3 c = t.Posicion(distancia).U(), f = t.Tangente(distancia).U();
            Vector3 derecha = Vector3.Cross(Vector3.up, f).normalized;
            return new Pose(c + derecha * lateral, Quaternion.LookRotation(f, Vector3.up));
        }

        // ── Mallas ───────────────────────────────────────────────────────────

        class Datos
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> tri = new List<int>();

            public void Quad(int a, int b, int c, int d) { tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d); }

            public Mesh Malla(string nombre)
            {
                var m = new Mesh { name = nombre };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                return Registrar(m);
            }
        }

        static void Malla(GameObject raiz, string nombre, Material mat, Mesh malla)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(raiz.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = malla;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Vector3 Derecha(Trazado t, int i)
        {
            Vector3 f = (t.Punto(i + 1) - t.Punto(i - 1)).U(); f.y = 0f;
            return Vector3.Cross(Vector3.up, f.normalized).normalized;
        }

        /// <summary>Cinta a lo largo de toda la vuelta, entre dos desplazamientos laterales.</summary>
        static Mesh Cinta(Trazado t, float izq, float der, float alto, float metrosPorTextura)
        {
            var d = new Datos();
            int n = t.Cantidad;
            for (int i = 0; i <= n; i++)
            {
                Vector3 c = t.Punto(i).U() + Vector3.up * alto, r = Derecha(t, i);
                float v = (i == n ? t.Longitud : t.DistanciaDe(i)) / metrosPorTextura;
                d.v.Add(c + r * izq); d.uv.Add(new Vector2(0f, v));
                d.v.Add(c + r * der); d.uv.Add(new Vector2(1f, v));
                if (i > 0) d.Quad(2 * i - 2, 2 * i - 1, 2 * i, 2 * i + 1);
            }
            return d.Malla("Cinta");
        }

        /// <summary>Pianos a los dos lados donde la curva es más cerrada que RadioPiano.</summary>
        static Mesh Pianos(Trazado t)
        {
            int n = t.Cantidad;
            var hay = new bool[n];
            for (int i = 0; i < n; i++)
                if (Mathf.Abs(t.Curvatura(i)) > 1f / RadioPiano)
                    for (int k = -2; k <= 2; k++) hay[((i + k) % n + n) % n] = true;

            var d = new Datos();
            foreach (float lado in new[] { -1f, 1f })
            {
                float a = lado * Ancho / 2, b = lado * (Ancho / 2 + AnchoPiano);
                for (int i = 0; i < n; i++)
                {
                    if (!hay[i] || !hay[(i + 1) % n]) continue;
                    int baseV = d.v.Count;
                    for (int s = 0; s <= 1; s++)
                    {
                        Vector3 c = t.Punto(i + s).U() + Vector3.up * 0.03f, r = Derecha(t, i + s);
                        // La textura de piano es rojo/blanco: una franja por metro.
                        float v = t.DistanciaDe(i) + s * (t.DistanciaDe(i + 1) - t.DistanciaDe(i) + (i + 1 == n ? t.Longitud : 0f));
                        d.v.Add(c + r * a); d.uv.Add(new Vector2(0f, v));
                        d.v.Add(c + r * b); d.uv.Add(new Vector2(1f, v));
                    }
                    if (lado > 0) d.Quad(baseV, baseV + 1, baseV + 2, baseV + 3);
                    else d.Quad(baseV + 1, baseV, baseV + 3, baseV + 2);
                }
            }
            return d.Malla("Pianos");
        }

        /// <summary>Franja transversal entre dos distancias (línea de salida).</summary>
        static Mesh Franja(Trazado t, float d0, float d1, float izq, float der, float alto, float repeticionesU)
        {
            var d = new Datos();
            foreach (float dist in new[] { d0, d1 })
            {
                Vector3 c = t.Posicion(dist).U() + Vector3.up * alto, f = t.Tangente(dist).U();
                Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
                float v = dist == d0 ? 0f : 1f;
                d.v.Add(c + r * izq); d.uv.Add(new Vector2(0f, v));
                d.v.Add(c + r * der); d.uv.Add(new Vector2(repeticionesU, v));
            }
            d.Quad(0, 1, 2, 3);
            return d.Malla("Franja");
        }

        /// <summary>Una raya blanca delante de cada uno de los 22 cajones.</summary>
        static Mesh LineasDeParrilla(Trazado t)
        {
            var d = new Datos();
            for (int k = 0; k < 22; k++)
            {
                Pose p = Parrilla(t, k);
                Vector3 f = p.rotation * Vector3.forward, r = p.rotation * Vector3.right;
                Vector3 c = p.position + f * 2.8f + Vector3.up * 0.04f;
                int b = d.v.Count;
                d.v.Add(c - r * 1.2f); d.v.Add(c + r * 1.2f);
                d.v.Add(c - r * 1.2f + f * 0.3f); d.v.Add(c + r * 1.2f + f * 0.3f);
                for (int i = 0; i < 4; i++) d.uv.Add(Vector2.zero);
                d.Quad(b, b + 1, b + 2, b + 3);
            }
            return d.Malla("Parrilla");
        }

        /// <summary>Paredes laterales donde la pista va elevada (el puente de Suzuka).</summary>
        static Mesh MurosDelPuente(Trazado t)
        {
            var d = new Datos();
            int n = t.Cantidad;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = t.Punto(i).U(), p1 = t.Punto(i + 1).U();
                if (p0.y < 0.3f && p1.y < 0.3f) continue;
                foreach (float lado in new[] { -1f, 1f })
                {
                    Vector3 o0 = p0 + Derecha(t, i) * (lado * Ancho / 2), o1 = p1 + Derecha(t, i + 1) * (lado * Ancho / 2);
                    // Dos caras, para que se vea desde fuera y desde dentro, cada una con sus
                    // propios vértices: si los compartieran, las normales se anularían a franjas.
                    for (int cara = 0; cara < 2; cara++)
                    {
                        int b = d.v.Count;
                        d.v.Add(new Vector3(o0.x, -0.1f, o0.z)); d.v.Add(o0 + Vector3.up * 0.9f);
                        d.v.Add(new Vector3(o1.x, -0.1f, o1.z)); d.v.Add(o1 + Vector3.up * 0.9f);
                        for (int k = 0; k < 4; k++) d.uv.Add(Vector2.zero);
                        if (cara == 0) d.Quad(b, b + 1, b + 2, b + 3); else d.Quad(b + 1, b, b + 3, b + 2);
                    }
                }
            }
            return d.Malla("Muros");
        }

        static Mesh Cesped(Trazado t)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < t.Cantidad; i++)
            {
                var p = t.Punto(i);
                minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X);
                minZ = Mathf.Min(minZ, p.Z); maxZ = Mathf.Max(maxZ, p.Z);
            }
            const float margen = 5000f, baldosa = 20f;   // que no se vea el borde ni desde la vista general
            minX -= margen; maxX += margen; minZ -= margen; maxZ += margen;
            var d = new Datos();
            d.v.Add(new Vector3(minX, 0f, minZ)); d.v.Add(new Vector3(maxX, 0f, minZ));
            d.v.Add(new Vector3(minX, 0f, maxZ)); d.v.Add(new Vector3(maxX, 0f, maxZ));
            d.uv.Add(new Vector2(minX, minZ) / baldosa); d.uv.Add(new Vector2(maxX, minZ) / baldosa);
            d.uv.Add(new Vector2(minX, maxZ) / baldosa); d.uv.Add(new Vector2(maxX, maxZ) / baldosa);
            d.Quad(0, 1, 2, 3);
            return d.Malla("Cesped");
        }
    }
}
