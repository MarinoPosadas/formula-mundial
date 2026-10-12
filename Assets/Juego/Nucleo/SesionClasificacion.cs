using System;
using System.Collections.Generic;

namespace F1.Nucleo
{
    /// <summary>
    /// La clasificación tal como se ve en pista: cada coche sale a su vuelta lanzada por turno,
    /// unos segundos detrás del anterior, y cruza la meta exactamente en el tiempo que decidió
    /// Clasificacion.Simular. Aquí no se decide nada nuevo; solo se reparte en el tiempo.
    /// </summary>
    public sealed class SesionClasificacion
    {
        public const float Paso = 0.1f;
        /// <summary>
        /// Segundos entre un coche y el siguiente al empezar la vuelta lanzada (como mucho). Toda
        /// la tanda tiene que caber en una vuelta: si no, el primero cruza la meta justo cuando
        /// el último aparece en el mismo sitio.
        /// </summary>
        public const float Separacion = 4f;
        /// <summary>Metros de vuelta de lanzamiento que se ven antes de la línea.</summary>
        public const float Lanzada = 300f;
        /// <summary>Segundos que se le ve tras cruzar la meta, levantando el pie, antes de irse a boxes.</summary>
        public const float Retirada = 3f;

        public sealed class Coche
        {
            public Clasificacion.Vuelta vuelta;
            public int puesto;            // en la parrilla (0 = pole)
            public float aparece, inicio, fin, retira;   // segundos de sesión
            /// <summary>Si falló: dónde se fue largo (m desde la línea) y cuándo (s de sesión); si no, -1.</summary>
            public float errorEn = -1f, errorCuando = -1f;
            internal readonly List<float> grabD = new List<float>(), grabL = new List<float>();
        }

        public Circuito Circuito { get; }
        /// <summary>En el orden en que salen a pista.</summary>
        public Coche[] Coches { get; }
        public float Duracion { get; }

        public SesionClasificacion(Circuito c, Clasificacion.Vuelta[] parrilla, Azar azar)
        {
            Circuito = c;
            float L = c.Trazado.Longitud;
            // Salen primero los más lentos, con algo de mezcla: la tabla va cambiando de líder
            // hasta el final, como en la última tanda de una Q3.
            var orden = new List<int>();
            var clave = new float[parrilla.Length];
            for (int i = 0; i < parrilla.Length; i++) { orden.Add(i); clave[i] = parrilla[i].segundos + azar.Normal(0f, 0.25f); }
            orden.Sort((a, b) => clave[b].CompareTo(clave[a]));

            float hueco = Math.Min(Separacion, (parrilla[0].segundos - 12f) / parrilla.Length);
            Coches = new Coche[parrilla.Length];
            float duracion = 0f;
            for (int k = 0; k < orden.Count; k++)
            {
                var v = parrilla[orden[k]];
                var co = new Coche { vuelta = v, puesto = orden[k] };
                co.inicio = 6f + k * hueco;
                co.fin = co.inicio + v.segundos;
                // Sin el error, va a su ritmo limpio; el error se lo come en un sitio: se va largo
                // en una frenada (hacia fuera, hasta la escapatoria del circuito) y pierde ahí lo que decidió la simulación.
                float limpio = v.ritmo - v.perdida;
                float Vb(float d) => Math.Max(5f, c.Velocidad(((d % L) + L) % L, limpio));
                float errIni = -1f, errLargo = 0f, errLado = 1f;
                if (v.error && v.perdida > 0f)
                {
                    // Una frenada fuerte al azar (donde la velocidad cae más de un 25 % en 60 m).
                    var frenadas = new List<float>();
                    for (float d = 200f; d < L - 300f; d += 10f)
                        if (Vb(d + 60f) < Vb(d) * 0.75f && (frenadas.Count == 0 || d - frenadas[frenadas.Count - 1] > 150f)) frenadas.Add(d);
                    errIni = frenadas.Count > 0 ? frenadas[Math.Min(frenadas.Count - 1, (int)(azar.Uniforme() * frenadas.Count))] : L * 0.5f;
                    // Largo del tramo perdido hasta sumar la pérdida (con la caída media de la curva).
                    float objetivo = c.Segundos(v.ritmo) - c.Segundos(limpio), perdido = 0f;
                    for (errLargo = 20f; errLargo < 600f && perdido < objetivo; errLargo += 5f)
                    {
                        perdido = 0f;
                        for (float d = errIni; d < errIni + errLargo; d += 5f) perdido += 5f / (Vb(d) * Caida((d - errIni) / errLargo)) - 5f / Vb(d);
                    }
                    int i = Math.Clamp((int)(errIni / L * c.Trazado.Cantidad), 0, c.Trazado.Cantidad - 1);
                    errLado = c.Trazado.Curvatura(i + 8, 6) > 0f ? 1f : -1f;    // hacia fuera de la curva
                    co.errorEn = errIni;
                }
                float F(float d) => errIni >= 0f && d >= errIni && d < errIni + errLargo ? Caida((d - errIni) / errLargo) : 1f;

                // Velocidades escaladas para que la vuelta dure justo lo decidido.
                // Tiempo acumulado cada 5 m: T(d) y lo que falta hasta la meta.
                int nTramos = (int)Math.Ceiling(L / 5f);
                var acum = new float[nTramos + 1];
                for (int m = 0; m < nTramos; m++) acum[m + 1] = acum[m] + Math.Min(5f, L - m * 5f) / (Vb(m * 5f) * F(m * 5f));
                float T = acum[nTramos];
                float escala = T / v.segundos;
                float V(float d) => Vb(d) * (d >= 0f ? F(d) : 1f) * escala;

                // Vuelta de lanzamiento: hacia atrás desde la línea, cuánto tarda en recorrer Lanzada.
                float tLanzada = 0f;
                for (float d = -Lanzada; d < 0f; d += 5f) tLanzada += 5f / V(d);
                co.aparece = co.inicio - tLanzada;

                // Grabación a pasos de 0,1 s desde que aparece hasta que se retira.
                float x = -Lanzada, t = co.aparece, lateral = 0f, frena = 1f;
                while (true)
                {
                    co.grabD.Add(x); co.grabL.Add(lateral);
                    if (t >= co.fin + Retirada) break;
                    float vel = V(x);
                    if (t >= co.fin)
                    {
                        // Levanta el pie y se abre hacia un lado para dejar paso.
                        frena = Math.Max(0.45f, frena - 0.2f * Paso);
                        vel *= frena;
                        lateral = Math.Min(4.5f, lateral + 1.5f * Paso);
                    }
                    else if (x >= 0f)
                    {
                        if (errIni >= 0f && x >= errIni && x < errIni + errLargo)
                        {
                            float u = (x - errIni) / errLargo;
                            lateral = errLado * c.Escapatoria * (float)Math.Sin(Math.PI * u);
                            if (co.errorCuando < 0f) co.errorCuando = t;
                        }
                        else lateral = 0f;
                        // Dentro de la vuelta: se corrige para cruzar la meta en su tiempo exacto
                        // (la integración a pasos se desvía unas centésimas).
                        float tiempo = co.fin - t;
                        float falta = (T - acum[Math.Min(nTramos, (int)(x / 5f))]) / escala;
                        if (tiempo > 1f) vel *= Math.Clamp(falta / tiempo, 0.97f, 1.03f);
                    }
                    x += vel * Paso;
                    t += Paso;
                }
                co.retira = t;
                duracion = Math.Max(duracion, co.retira);
                Coches[k] = co;
            }
            Duracion = duracion + 1f;
        }

        /// <summary>Velocidad relativa al irse largo: cae hasta el 35 % a mitad del tramo y se recupera.</summary>
        static float Caida(float u) => 1f - 0.65f * (float)Math.Sin(Math.PI * Math.Clamp(u, 0f, 1f));

        /// <summary>¿Está en pista en este instante?</summary>
        public bool EnPista(int k, float t) => t >= Coches[k].aparece && t < Coches[k].retira;

        /// <summary>Distancia (desde la línea, negativa en la vuelta de lanzamiento) y lateral.</summary>
        public (float d, float lateral) Muestra(int k, float t)
        {
            var co = Coches[k];
            int n = co.grabD.Count;
            float f = Math.Clamp((t - co.aparece) / Paso, 0f, n - 1);
            int i = (int)f; float r = f - i;
            int j = Math.Min(i + 1, n - 1);
            return (co.grabD[i] + (co.grabD[j] - co.grabD[i]) * r, co.grabL[i] + (co.grabL[j] - co.grabL[i]) * r);
        }
    }
}
