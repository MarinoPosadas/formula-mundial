using System;
using System.Collections.Generic;
using System.Numerics;

namespace F1.Nucleo
{
    /// <summary>
    /// La línea central de un circuito: puntos en metros (x = este, z = norte, y = altura),
    /// empezando en la línea de salida y en sentido de carrera. Todo se mide en distancia
    /// recorrida desde la salida, que es lo que entiende el simulador.
    /// </summary>
    public sealed class Trazado
    {
        /// <summary>Cuánto se eleva el tramo que pasa por encima en un cruce (Suzuka).</summary>
        public const float AlturaPuente = 8f;
        /// <summary>Longitud de cada rampa del puente, a cada lado del cruce.</summary>
        public const float RampaPuente = 300f;
        const float MesetaPuente = 40f;

        readonly Vector3[] puntos;
        readonly float[] acumulada;   // acumulada[i] = distancia de la salida al punto i; [n] = longitud
        readonly List<(int a, int b)> cruces;

        public int Cantidad => puntos.Length;
        public float Longitud { get; }
        public IReadOnlyList<(int a, int b)> Cruces => cruces;

        public Trazado(float[] x, float[] z, bool alzarCruces = true)
        {
            if (x == null || z == null || x.Length != z.Length || x.Length < 4)
                throw new ArgumentException("El trazado necesita al menos cuatro puntos con x y z.");

            int n = x.Length;
            puntos = new Vector3[n];
            for (int i = 0; i < n; i++) puntos[i] = new Vector3(x[i], 0f, z[i]);

            // Distancias en planta: el puente no cambia lo que mide la vuelta.
            acumulada = new float[n + 1];
            for (int i = 1; i <= n; i++)
                acumulada[i] = acumulada[i - 1] + DistanciaPlanta(puntos[i - 1], puntos[i % n]);
            Longitud = acumulada[n];

            cruces = BuscarCruces();
            if (alzarCruces) Alzar();
        }

        public Vector3 Punto(int i) => puntos[Indice(i)];
        public float DistanciaDe(int i) => acumulada[Indice(i)];

        /// <summary>Punto de la línea central a una distancia de la salida (da vueltas).</summary>
        public Vector3 Posicion(float distancia)
        {
            float d = Envolver(distancia);
            int i = Tramo(d);
            float largo = acumulada[i + 1] - acumulada[i];
            float t = largo > 0f ? (d - acumulada[i]) / largo : 0f;
            return Vector3.Lerp(puntos[i], puntos[(i + 1) % puntos.Length], t);
        }

        /// <summary>Dirección de avance (unitaria) a una distancia de la salida.</summary>
        public Vector3 Tangente(float distancia)
        {
            Vector3 v = Posicion(distancia + 2f) - Posicion(distancia - 2f);
            return v.LengthSquared() > 0f ? Vector3.Normalize(v) : Vector3.UnitZ;
        }

        /// <summary>
        /// Curvatura con signo en el punto i (1/m): positiva gira a la izquierda, negativa a la
        /// derecha. Se mide con los puntos a ±salto para no medir el ruido del GPS.
        /// </summary>
        public float Curvatura(int i, int salto = 3)
        {
            Vector3 a = Punto(i - salto), b = Punto(i), c = Punto(i + salto);
            float ab = DistanciaPlanta(a, b), bc = DistanciaPlanta(b, c), ca = DistanciaPlanta(c, a);
            float cruz = (b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X);
            float denom = ab * bc * ca;
            return denom > 1e-6f ? 2f * cruz / denom : 0f;
        }

        public float Envolver(float d) => ((d % Longitud) + Longitud) % Longitud;

        public const float SeparacionParrilla = 8f;   // metros entre posiciones de salida
        public const float LateralParrilla = 3.2f;

        /// <summary>
        /// Cajón de salida k (0 = pole): distancia a la línea (negativa, detrás de ella) y
        /// desplazamiento lateral (+ derecha). La pole va por el lado de dentro de la curva 1.
        /// </summary>
        public (float distancia, float lateral) Cajon(int k)
        {
            float lado = PrimeraCurvaALaDerecha() ? 1f : -1f;
            if (k % 2 == 1) lado = -lado;
            return (-(6f + SeparacionParrilla * k), lado * LateralParrilla);
        }

        bool PrimeraCurvaALaDerecha()
        {
            for (int i = 0; i < Cantidad / 3; i++)
            {
                float k = Curvatura(i, 6);
                if (MathF.Abs(k) > 1f / 300f) return k < 0f;
            }
            return true;
        }

        /// <summary>Distancia por pista de a hasta b, hacia delante (0..Longitud).</summary>
        public float Adelante(float a, float b) => Envolver(b - a);

        int Indice(int i) => ((i % puntos.Length) + puntos.Length) % puntos.Length;

        int Tramo(float d)
        {
            int lo = 0, hi = puntos.Length - 1;
            while (lo < hi)
            {
                int m = (lo + hi + 1) / 2;
                if (acumulada[m] <= d) lo = m; else hi = m - 1;
            }
            return lo;
        }

        static float DistanciaPlanta(Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        List<(int, int)> BuscarCruces()
        {
            // Segmentos que se cortan en planta y no son vecinos. Con ~1.200 puntos son
            // ~700.000 pruebas: se hace una vez al cargar el circuito.
            var res = new List<(int, int)>();
            int n = puntos.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 p1 = puntos[i], p2 = puntos[(i + 1) % n];
                for (int j = i + 8; j < n; j++)
                {
                    if (i < 8 && j > n - 8 + i) continue;   // vecinos a través de la salida
                    Vector3 q1 = puntos[j], q2 = puntos[(j + 1) % n];
                    if (SeCortan(p1, p2, q1, q2)) res.Add((i, j));
                }
            }
            return res;
        }

        static bool SeCortan(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2)
        {
            float Lado(Vector3 a, Vector3 b, Vector3 c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
            float d1 = Lado(q1, q2, p1), d2 = Lado(q1, q2, p2), d3 = Lado(p1, p2, q1), d4 = Lado(p1, p2, q2);
            return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
        }

        void Alzar()
        {
            foreach (var (a, b) in cruces)
            {
                // Sube el paso que no está cerca de la salida, para que la parrilla quede llana.
                int alto = DistanciaCircular(acumulada[b], 0f) > RampaPuente + 50f ? b : a;
                float centro = acumulada[alto];
                for (int m = 0; m < puntos.Length; m++)
                {
                    float dd = DistanciaCircular(acumulada[m], centro);
                    if (dd >= RampaPuente) continue;
                    float t = dd <= MesetaPuente ? 1f : 1f - (dd - MesetaPuente) / (RampaPuente - MesetaPuente);
                    float h = AlturaPuente * t * t * (3f - 2f * t);
                    if (h > puntos[m].Y) puntos[m].Y = h;
                }
            }
        }

        float DistanciaCircular(float a, float b)
        {
            float d = MathF.Abs(a - b);
            return MathF.Min(d, Longitud - d);
        }
    }
}
