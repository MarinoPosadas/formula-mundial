using System;

namespace F1.Nucleo
{
    /// <summary>Lo que un coche es capaz de hacer. Unidades del SI (m/s y m/s²).</summary>
    public struct Prestaciones
    {
        public float velocidadPunta;     // m/s
        public float agarreBaja;         // aceleración lateral a velocidad cero (m/s²): agarre mecánico
        public float agarreAero;         // lo que añade la carga aerodinámica por (m/s)²
        public float traccion;           // aceleración máxima a baja velocidad (m/s²)
        public float frenada;            // deceleración máxima (m/s²)

        /// <summary>
        /// Un F1 de referencia. Calibrado el 10-10-2026 contra las poles de 2025 en 23 circuitos
        /// (barrido de ~1.000 combinaciones): error medio 0,0 % y dispersión 3,1 % entre
        /// circuitos. Lo que queda lo corrige el ajuste de ritmo de cada circuito.
        /// </summary>
        public static Prestaciones Referencia => new Prestaciones
        {
            velocidadPunta = 92f,        // ~330 km/h
            agarreBaja = 18f,            // ~1,8 g en las curvas lentas
            agarreAero = 0.012f,         // efectivo, no físico: a 250 km/h da ~7,8 g, más que un F1
                                         // real (5-6 g); compensa curvas que el GPS suavizado cierra de más
            traccion = 13f,
            frenada = 55f,               // ~5,6 g
        };

        /// <summary>Ventana de la curvatura (en puntos de 5 m): 5 = ±25 m, la que mejor encaja.</summary>
        public const int VentanaCurva = 5;

        public float AgarreA(float v) => agarreBaja + agarreAero * v * v;

        /// <summary>La tracción se apaga al acercarse a la punta (la resistencia del aire gana).</summary>
        public float AceleracionA(float v) => traccion * MathF.Max(0f, 1f - (v / velocidadPunta) * (v / velocidadPunta));
    }

    /// <summary>
    /// Velocidad del coche en cada punto de la vuelta lanzada: la que permite el agarre en
    /// cada curva, recortada para llegar frenando a la siguiente y para no acelerar más de
    /// lo que da el motor. Da también el tiempo hasta cada punto.
    /// </summary>
    public sealed class PerfilVelocidad
    {
        readonly Trazado trazado;
        readonly float[] velocidad;   // m/s en cada punto
        readonly float[] tiempo;      // s desde la salida hasta cada punto; [n] = vuelta

        public float TiempoVuelta => tiempo[tiempo.Length - 1];
        public float VelocidadMaxima { get; }
        public float VelocidadMinima { get; }

        public PerfilVelocidad(Trazado t, Prestaciones p, int ventanaCurva = Prestaciones.VentanaCurva)
        {
            trazado = t;
            int n = t.Cantidad;
            velocidad = new float[n];

            // 1) Límite de cada curva: v²·|k| = agarre(v) = a + b·v²  ->  v² = a / (|k| - b)
            for (int i = 0; i < n; i++)
            {
                float k = MathF.Abs(t.Curvatura(i, ventanaCurva));
                float limite = k > p.agarreAero ? MathF.Sqrt(p.agarreBaja / (k - p.agarreAero)) : float.MaxValue;
                velocidad[i] = MathF.Min(p.velocidadPunta, limite);
            }

            // 2) y 3) Frenar hacia atrás y acelerar hacia delante. Dos vueltas para que el
            // final de una alimente el principio de la otra (la vuelta es cerrada).
            for (int vuelta = 0; vuelta < 2; vuelta++)
            {
                for (int j = 2 * n - 1; j >= 0; j--)
                {
                    int i = j % n, sig = (i + 1) % n;
                    float ds = Tramo(i);
                    float vMax = MathF.Sqrt(velocidad[sig] * velocidad[sig] + 2f * Frenada(p, velocidad[sig]) * ds);
                    if (velocidad[i] > vMax) velocidad[i] = vMax;
                }
                for (int j = 0; j < 2 * n; j++)
                {
                    int i = j % n, sig = (i + 1) % n;
                    float ds = Tramo(i);
                    float vMax = MathF.Sqrt(velocidad[i] * velocidad[i] + 2f * p.AceleracionA(velocidad[i]) * ds);
                    if (velocidad[sig] > vMax) velocidad[sig] = vMax;
                }
            }

            tiempo = new float[n + 1];
            float vmax = 0f, vmin = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float v0 = velocidad[i], v1 = velocidad[(i + 1) % n];
                tiempo[i + 1] = tiempo[i] + 2f * Tramo(i) / MathF.Max(0.1f, v0 + v1);
                vmax = MathF.Max(vmax, v0); vmin = MathF.Min(vmin, v0);
            }
            VelocidadMaxima = vmax; VelocidadMinima = vmin;
        }

        // La frenada también se apoya en la carga aerodinámica: a más velocidad, más frena.
        static float Frenada(Prestaciones p, float v) => MathF.Min(p.frenada, p.AgarreA(v) * 1.25f);

        float Tramo(int i) => (i + 1 == trazado.Cantidad ? trazado.Longitud : trazado.DistanciaDe(i + 1)) - trazado.DistanciaDe(i);

        public float VelocidadEn(int i) => velocidad[((i % velocidad.Length) + velocidad.Length) % velocidad.Length];

        /// <summary>Velocidad a una distancia de la salida, interpolada.</summary>
        public float VelocidadA(float distancia)
        {
            float d = trazado.Envolver(distancia);
            int i = Indice(d);
            float t = (d - trazado.DistanciaDe(i)) / MathF.Max(1e-3f, Tramo(i));
            return velocidad[i] + (velocidad[(i + 1) % velocidad.Length] - velocidad[i]) * t;
        }

        /// <summary>Distancia recorrida en una vuelta lanzada tras t segundos desde la salida.</summary>
        public float DistanciaTras(float segundos)
        {
            float t = segundos % TiempoVuelta;
            if (t < 0f) t += TiempoVuelta;
            int lo = 0, hi = tiempo.Length - 2;
            while (lo < hi) { int m = (lo + hi + 1) / 2; if (tiempo[m] <= t) lo = m; else hi = m - 1; }
            float frac = (t - tiempo[lo]) / MathF.Max(1e-4f, tiempo[lo + 1] - tiempo[lo]);
            return trazado.DistanciaDe(lo) + frac * Tramo(lo);
        }

        int Indice(float d)
        {
            int lo = 0, hi = trazado.Cantidad - 1;
            while (lo < hi) { int m = (lo + hi + 1) / 2; if (trazado.DistanciaDe(m) <= d) lo = m; else hi = m - 1; }
            return lo;
        }
    }
}
