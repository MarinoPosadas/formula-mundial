using System;

namespace F1.Nucleo
{
    /// <summary>
    /// Un circuito tal como lo necesita el simulador: el trazado, el perfil de velocidad y el
    /// tiempo de referencia. La referencia es la pole real: la vuelta del mejor coche con un
    /// piloto medio. Todos los ritmos (coche, piloto, forma) son % sobre ella.
    /// </summary>
    public sealed class Circuito
    {
        /// <summary>
        /// Fracción de vuelta a fondo en el circuito medio del calendario 2026 (medida: 0,327; de
        /// Mónaco 0,13 a Monza 0,54). Tiene que ser la media de verdad: con 0,45 el término
        /// favorecía a los coches de curva en casi todos los circuitos.
        /// </summary>
        public const float FraccionPotenciaMedia = 0.327f;
        /// <summary>Desviación típica (% de vuelta) de lo bien que le sienta cada circuito a cada coche.</summary>
        public const float DesviacionAfinidad = 0.15f;
        /// <summary>Cuánto pesa (en % de vuelta) el carácter del coche por cada punto de fracción.</summary>
        public const float PesoPotencia = 1.2f;

        public Trazado Trazado { get; }
        public PerfilVelocidad Perfil { get; }
        /// <summary>Segundos de la vuelta de referencia.</summary>
        public float Referencia { get; }
        /// <summary>Segundos del modelo por segundo real (para pasar del perfil al reloj de la carrera).</summary>
        public float Ajuste { get; }
        /// <summary>Fracción del tiempo de vuelta por encima del 85 % de la punta.</summary>
        public float FraccionPotencia { get; }

        /// <summary>Identificador del circuito (el del GeoJSON) y semilla de la temporada.</summary>
        public string Id { get; }
        public int SemillaTemporada { get; }
        /// <summary>Metros que se abre quien se va largo: a la escapatoria en un permanente; poco en un urbano (muro al lado).</summary>
        public float Escapatoria { get; set; } = 10f;

        public Circuito(Trazado t, float poleReferencia, string id = "", int semillaTemporada = 0)
        {
            Id = id ?? "";
            SemillaTemporada = semillaTemporada;
            Trazado = t;
            Perfil = new PerfilVelocidad(t, Prestaciones.Referencia);
            Referencia = poleReferencia > 0f ? poleReferencia : Perfil.TiempoVuelta;
            Ajuste = Perfil.TiempoVuelta / Referencia;

            float umbral = 0.85f * Perfil.VelocidadMaxima, aFondo = 0f;
            for (int i = 0; i < t.Cantidad; i++)
            {
                float ds = (i + 1 == t.Cantidad ? t.Longitud : t.DistanciaDe(i + 1)) - t.DistanciaDe(i);
                float v = Perfil.VelocidadEn(i);
                if (v >= umbral) aFondo += ds / v;
            }
            FraccionPotencia = aFondo / Perfil.TiempoVuelta;
        }

        /// <summary>Ritmo (% sobre la referencia) de un piloto con su coche en este circuito.</summary>
        public float Ritmo(Equipo e, Piloto p) =>
            e.ritmo + p.ritmo - e.potencia * (FraccionPotencia - FraccionPotenciaMedia) * PesoPotencia + Afinidad(e);

        /// <summary>
        /// Lo bien o mal que le sienta este circuito a este coche, más allá de recta o curva.
        /// Fija para una temporada (sale de la semilla, el circuito y el equipo).
        /// </summary>
        public float Afinidad(Equipo e)
        {
            unchecked
            {
                int h = 17;
                foreach (char ch in Id) h = h * 31 + ch;
                foreach (char ch in e.id ?? "") h = h * 37 + ch;
                h = h * 41 + SemillaTemporada;
                return new Azar(h).Normal(0f, DesviacionAfinidad);
            }
        }

        /// <summary>Velocidad real (m/s) en un punto para un coche que va a un ritmo dado.</summary>
        public float Velocidad(float distancia, float ritmoPorcentaje) =>
            Perfil.VelocidadA(distancia) * Ajuste / (1f + ritmoPorcentaje / 100f);

        /// <summary>¿Se puede adelantar aquí? Donde se va casi a fondo (rectas y frenadas al final).</summary>
        public bool ZonaDeAdelantamiento(float distancia) =>
            Perfil.VelocidadA(distancia) >= 0.8f * Perfil.VelocidadMaxima;

        public float Segundos(float ritmoPorcentaje) => Referencia * (1f + ritmoPorcentaje / 100f);
    }

    /// <summary>Puntos del Mundial: los diez primeros, sin punto por la vuelta rápida (desde 2025).</summary>
    public static class Puntos
    {
        static readonly int[] Tabla = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
        public static int PorPuesto(int puesto) => puesto >= 1 && puesto <= Tabla.Length ? Tabla[puesto - 1] : 0;
    }

    /// <summary>Clasificación a una vuelta lanzada: cada piloto tiene un intento.</summary>
    public static class Clasificacion
    {
        public const float FormaDelDia = 0.12f;      // % de desviación típica: cómo sale el día

        public sealed class Vuelta
        {
            public Piloto piloto;
            public Equipo equipo;
            public float ritmo;       // % sobre la referencia
            public float segundos;
            public bool error;        // se fue largo en algún punto de la vuelta
            public float perdida;     // % de vuelta que le costó el error (ya incluido en ritmo)
        }

        public static Vuelta[] Simular(Circuito c, Parrilla parrilla, Azar azar)
        {
            var res = new Vuelta[parrilla.pilotos.Length];
            for (int i = 0; i < res.Length; i++)
            {
                var p = parrilla.pilotos[i];
                var e = parrilla.EquipoDe(p);
                float ritmo = c.Ritmo(e, p) + azar.Normal(0f, FormaDelDia) + azar.Normal(0f, p.regularidad);
                bool error = azar.Prob(p.errores * 2f);           // en clasificación se arriesga más
                float perdida = error ? azar.Entre(0.3f, 2.0f) : 0f;
                ritmo += perdida;
                res[i] = new Vuelta { piloto = p, equipo = e, ritmo = ritmo, segundos = c.Segundos(ritmo), error = error, perdida = perdida };
            }
            Array.Sort(res, (a, b) => a.segundos.CompareTo(b.segundos));
            return res;
        }
    }
}
