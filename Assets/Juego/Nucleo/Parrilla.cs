using System;

namespace F1.Nucleo
{
    /// <summary>
    /// Escuderías y pilotos de una temporada, tal como están en
    /// Resources/Temporada/parrilla_&lt;año&gt;.json. Campos públicos para que JsonUtility los lea.
    /// </summary>
    [Serializable]
    public class Parrilla
    {
        public int temporada;
        /// <summary>Nombre del campeonato en los rótulos (la versión pública lleva uno inventado).</summary>
        public string campeonato = "Fórmula 1";
        public Equipo[] equipos;
        public Piloto[] pilotos;

        public Equipo EquipoDe(Piloto p)
        {
            foreach (var e in equipos) if (e.id == p.equipo) return e;
            throw new ArgumentException($"El piloto {p.nombre} es de un equipo que no existe: {p.equipo}");
        }
    }

    [Serializable]
    public class Equipo
    {
        public string id, nombre, material, motor;
        /// <summary>Color de la escudería para la interfaz (#RRGGBB).</summary>
        public string color;
        /// <summary>% de la vuelta respecto a la referencia: 0 el mejor coche, más = más lento.</summary>
        public float ritmo;
        /// <summary>+1 coche de recta (gana en Monza), -1 coche de curva (gana en Mónaco).</summary>
        public float potencia;
        /// <summary>Probabilidad de acabar un GP completo sin averías (unas 57 vueltas).</summary>
        public float fiabilidad;
    }

    [Serializable]
    public class Piloto
    {
        public string nombre, corto, foto, pais, equipo;
        /// <summary>Color del casco (#RRGGBB), aproximado: el diseño real cambia cada temporada.</summary>
        public string casco;
        /// <summary>% de la vuelta respecto a un piloto medio (negativo = más rápido).</summary>
        public float ritmo;
        /// <summary>Desviación típica de vuelta a vuelta, en %.</summary>
        public float regularidad;
        public float salida, ataque, defensa;   // 0..1
        /// <summary>Probabilidad de cometer un error por vuelta.</summary>
        public float errores;
    }

    /// <summary>Generador con semilla: la misma semilla da la misma carrera.</summary>
    /// <summary>
    /// El azar de toda la simulación, con semilla. Por dentro, xoshiro256** sembrado con
    /// SplitMix64. Antes era System.Random, y su algoritmo de Knuth daba secuencias con estructura
    /// según la semilla: medido el 13-10-2026, el de la pole ganaba el 98 % o el 63 % según
    /// se eligieran las semillas (pequeñas y consecutivas, o grandes), con el mismo juego.
    /// </summary>
    public sealed class Azar
    {
        ulong s0, s1, s2, s3;

        public Azar(int semilla)
        {
            ulong x = unchecked((ulong)(uint)semilla * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
            ulong Mezcla()
            {
                unchecked
                {
                    x += 0x9E3779B97F4A7C15UL;
                    ulong z = x;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }
            s0 = Mezcla(); s1 = Mezcla(); s2 = Mezcla(); s3 = Mezcla();
        }

        static ulong Rotar(ulong v, int k) => (v << k) | (v >> (64 - k));

        ulong Siguiente()
        {
            unchecked
            {
                ulong r = Rotar(s1 * 5, 7) * 9;
                ulong t = s1 << 17;
                s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3;
                s2 ^= t;
                s3 = Rotar(s3, 45);
                return r;
            }
        }

        /// <summary>En [0, 1).</summary>
        double Doble() => (Siguiente() >> 11) * (1.0 / (1UL << 53));

        public float Uniforme() => (float)Doble();
        public float Entre(float a, float b) => a + (b - a) * Uniforme();
        public bool Prob(float p) => Doble() < p;

        /// <summary>Normal (Box-Muller) con media y desviación típica.</summary>
        public float Normal(float media = 0f, float desviacion = 1f)
        {
            double u1 = 1.0 - Doble(), u2 = Doble();
            return media + desviacion * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }
    }
}
