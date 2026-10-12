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
    public sealed class Azar
    {
        readonly Random r;
        public Azar(int semilla) { r = new Random(semilla); }

        public float Uniforme() => (float)r.NextDouble();
        public float Entre(float a, float b) => a + (b - a) * Uniforme();
        public bool Prob(float p) => r.NextDouble() < p;

        /// <summary>Normal (Box-Muller) con media y desviación típica.</summary>
        public float Normal(float media = 0f, float desviacion = 1f)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return media + desviacion * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }
    }
}
