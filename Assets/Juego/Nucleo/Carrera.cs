using System;
using System.Collections.Generic;

namespace F1.Nucleo
{
    /// <summary>
    /// Simulación de una carrera corta con salida parada, en pasos de 0,1 s. Es la fuente de
    /// verdad: decide posiciones, tiempos, adelantamientos y abandonos, y deja grabado dónde
    /// estaba cada coche en cada paso para que el 3D lo reproduzca tal cual.
    /// </summary>
    public sealed class Carrera
    {
        public const float Paso = 0.1f;
        /// <summary>% más lento que en clasificación: gasolina y neumáticos.</summary>
        public const float RitmoCarrera = 2.0f;
        /// <summary>Metros de morro a morro por debajo de los que no se puede ir detrás de otro.</summary>
        public const float HuecoMinimo = 7f;
        public const float CarrilAdelantar = 3.5f;
        /// <summary>Intentos de adelantamiento por segundo en condiciones medias (calibrable).</summary>
        public const float Intento = 1.2f;
        /// <summary>Cuántas veces más se intenta adelantar en la primera vuelta.</summary>
        public const float PrimeraVuelta = 2f;

        /// <summary>
        /// % de vuelta de desviación del ritmo de CARRERA respecto al de clasificación (gestión de
        /// gomas, carga de gasolina, constancia). Sin él la carrera copiaba la clasificación: medido en
        /// 240 GP, el de la pole ganaba el 98 % y había 0,03 cambios de líder por carrera.
        /// </summary>
        public static float CaracterCarrera = 0.30f;
        /// <summary>Segundos de desviación del arranque (reflejos) y probabilidad base de una mala salida.</summary>
        public static float VariacionSalida = 0.30f, MalaSalida = 0.10f;
        /// <summary>m/s que hay que ir más rápido que el de delante para intentar pasarle (antes 0,5).</summary>
        public static float UmbralAtaque = 0.2f;
        /// <summary>Lo que da el DRS (fracción de velocidad) y desde cuántos segundos detrás se intenta pasar.</summary>
        public static float DRS = 0.0f, AlcanceAtaque = 0.15f;
        /// <summary>Probabilidad base de ganar la curva en la frenada (con ataque y defensa iguales).</summary>
        public static float Frenada = 0.5f;
        /// <summary>Metros (de morro a morro) desde los que aún se puede tirar por dentro en la frenada.</summary>
        public static float LanceMaximo = 14f;
        /// <summary>
        /// Coche de seguridad: probabilidad de que salga sin motivo visible (restos en pista) y de que
        /// salga tras un abandono por accidente. Mientras está, el líder va al 62 % del ritmo, los demás
        /// le alcanzan en fila, no se adelanta y no hay DRS; a los 30-45 s, relanzada.
        /// </summary>
        public static float ProbCocheSeguridad = 0.18f, CocheSeguridadTrasAccidente = 0.6f;
        /// <summary>Metros tras la salida en los que aún se va en dos columnas, como en la parrilla.</summary>
        public const float DosColumnas = 200f;
        /// <summary>Distancia mínima de morro a morro: nunca menos (un F1 mide 5,6 m).</summary>
        public const float Contacto = 6f;
        /// <summary>
        /// Las averías de un GP de 57 vueltas, repartidas en 3, casi no aparecerían. Se
        /// multiplican para que la carrera corta tenga la misma sal que una larga.
        /// </summary>
        public const float MultiplicadorAverias = 3f;
        const int VueltasDeUnGP = 57;

        public enum Tipo { Adelantamiento, Error, Toque, Abandono, VueltaRapida, Meta, OrdenDeEquipo, Radio, CocheSeguridad, Relanzada }

        /// <summary>Lo que el jefe de escudería le dice a su piloto por radio durante la carrera.</summary>
        public enum OrdenPiloto { Normal, Atacar, Defender, Ahorrar }

        /// <summary>
        /// Efecto de cada orden mientras está activa: ritmo (% de vuelta, negativo = más rápido),
        /// intentos de adelantamiento, probabilidad de error y defensa. Atacar desgasta el motor y
        /// ahorrar lo cuida (se cobra al apuntar el GP).
        /// </summary>
        /// <summary>Neumáticos de salida.</summary>
        public enum Compuesto { Blando, Medio, Duro, Lluvia }

        /// <summary>
        /// Efecto de cada compuesto (% de vuelta): lo que da al salir y lo que pierde por vuelta de
        /// desgaste. Comprimido como las averías: en 3 vueltas pasa lo que en una carrera entera. El
        /// blando sale volando y en la última vuelta va lento; el duro, al revés. Sumadas las tres
        /// vueltas salen parecidas (~0,2 %): la gracia está en CUÁNDO se gana o se pierde.
        /// </summary>
        public static (float inicio, float caida) EfectoGoma(Compuesto g) => g switch
        {
            Compuesto.Blando => (-0.80f, 0.80f),
            Compuesto.Duro => (0.20f, 0.15f),
            Compuesto.Lluvia => (0f, 0.10f),
            _ => (-0.20f, 0.40f),
        };

        /// <summary>Lo que eligen los rivales: blando 35 %, medio 45 %, duro 20 %.</summary>
        static Compuesto SortearGoma(Azar azar)
        {
            float u = azar.Uniforme();
            return u < 0.35f ? Compuesto.Blando : u < 0.80f ? Compuesto.Medio : Compuesto.Duro;
        }

        public static (float ritmo, float intento, float errores, float defensa) EfectoRadio(OrdenPiloto o) => o switch
        {
            // Medido en 300 carreras: con -0,25 % atacar no movía el puesto medio (el tráfico y
            // los errores se comían la ventaja). Con -0,5 % y el doble de intentos, sí.
            OrdenPiloto.Atacar => (-0.5f, 2.0f, 1.5f, 0f),
            OrdenPiloto.Defender => (-0.1f, 0.6f, 1.1f, 0.45f),
            OrdenPiloto.Ahorrar => (0.4f, 0.5f, 0.6f, 0f),
            _ => (0f, 1f, 1f, 0f),
        };

        public sealed class Suceso
        {
            public float t;
            public Tipo tipo;
            public int coche, otro = -1, vuelta;
            public string texto;
            public float valor;               // la vuelta rápida, en segundos
        }

        public sealed class Coche
        {
            public Piloto piloto;
            public Equipo equipo;
            public int salida;                 // puesto en parrilla (0 = pole)
            public float d, v, lateral;        // distancia de carrera (negativa en parrilla), m/s, m
            public int vueltas;
            public float mejorVuelta = float.MaxValue;
            public bool fuera, terminado;
            public float tiempoFinal;
            public string motivo;
            public readonly List<float> grabD = new List<float>(), grabL = new List<float>();

            internal float ritmoBase, ritmoVuelta, arranque, lateralSalida, lateralObjetivo;
            internal float inicioVuelta, meta, adelantandoHasta, perdidaHasta, factorPerdida = 1f;
            internal float errorEn = -1f, toqueEn = -1f, averiaEn = -1f;
            internal int aPor = -1;             // coche al que está adelantando (maniobra en curso)
            internal float ladoManiobra;
            internal bool resuelta;            // la maniobra en curso ya se decidió en la frenada
            internal float ladoError;          // hacia dónde se va largo en un error (0 = no)
            /// <summary>La orden de radio vigente y el tiempo que ha pasado atacando o ahorrando.</summary>
            public OrdenPiloto radio;
            public float segundosAtacando, segundosAhorrando;
            /// <summary>Neumáticos y su desgaste (vueltas equivalentes: atacando gasta más, ahorrando menos).</summary>
            public Compuesto goma;
            public float desgaste;
        }

        public Circuito Circuito { get; private set; }
        /// <summary>Si la carrera es en mojado.</summary>
        public bool Lluvia { get; private set; }
        public int Vueltas { get; private set; }
        public Coche[] Coches { get; private set; }
        public List<Suceso> Sucesos { get; } = new List<Suceso>();
        public int Muestras { get; private set; }
        /// <summary>Maniobras de adelantamiento empezadas (diagnóstico).</summary>
        public int Intentos { get; private set; }
        public float Duracion => Muestras * Paso;

        /// <summary>Orden final: los que acaban por vueltas y tiempo; detrás, los abandonos por distancia.</summary>
        public Coche[] Resultado
        {
            get
            {
                var r = (Coche[])Coches.Clone();
                Array.Sort(r, (a, b) =>
                {
                    if (a.terminado != b.terminado) return a.terminado ? -1 : 1;
                    if (a.terminado) return a.vueltas != b.vueltas ? b.vueltas.CompareTo(a.vueltas) : a.tiempoFinal.CompareTo(b.tiempoFinal);
                    return b.d.CompareTo(a.d);
                });
                return r;
            }
        }

        /// <summary>
        /// Órdenes de equipo: id de escudería → nombre del piloto con prioridad. Su compañero no
        /// le ataca y, si le alcanza por detrás más rápido, le cede el paso.
        /// </summary>
        Dictionary<string, string> ordenes;

        public static Carrera Simular(Circuito c, Clasificacion.Vuelta[] parrilla, int vueltas, Azar azar,
                                      Dictionary<string, string> ordenes = null, bool enVivo = false, bool lluvia = false)
        {
            var carrera = new Carrera { Circuito = c, Vueltas = vueltas, ordenes = ordenes, Lluvia = lluvia };
            var mojado = Clima.Efecto(lluvia);
            float L = c.Trazado.Longitud;
            var coches = new Coche[parrilla.Length];
            for (int k = 0; k < coches.Length; k++)
            {
                var q = parrilla[k];
                var (d0, lat0) = c.Trazado.Cajon(k);
                var co = new Coche
                {
                    piloto = q.piloto, equipo = q.equipo, salida = k, d = d0,
                    lateral = lat0, lateralSalida = lat0, lateralObjetivo = lat0, meta = vueltas * L,
                    ritmoBase = Clima.Ritmo(c, q.equipo, q.piloto, lluvia) + RitmoCarrera + azar.Normal(0f, Clasificacion.FormaDelDia) + azar.Normal(0f, CaracterCarrera),
                };
                co.ritmoVuelta = co.ritmoBase + azar.Normal(0f, q.piloto.regularidad * mojado.regularidad);
                // Salida: reflejos + algún patinazo de vez en cuando.
                co.arranque = 0.2f + (1f - q.piloto.salida) * 0.3f + Math.Abs(azar.Normal(0f, VariacionSalida));
                if (azar.Prob(MalaSalida + (1f - q.piloto.salida) * MalaSalida)) co.arranque += azar.Entre(0.3f, 1.0f);
                // Toque en la primera vuelta y avería: se decide ahora dónde pasarían.
                if (azar.Prob(0.01f + q.piloto.errores * 0.6f * mojado.errores)) co.toqueEn = azar.Entre(120f, 1200f);
                // El multiplicador está pensado para 3 vueltas: con más, las averías por carrera se
                // quedan como estaban (la carrera corta lleva la sal de una larga; la larga no la dobla).
                float pAveria = (1f - (float)Math.Pow(q.equipo.fiabilidad, (double)vueltas / VueltasDeUnGP)) * MultiplicadorAverias * 3f / vueltas;
                if (azar.Prob(pAveria)) co.averiaEn = azar.Entre(0f, vueltas * L);
                co.errorEn = azar.Prob(q.piloto.errores * mojado.errores) ? azar.Entre(0f, L) : -1f;
                co.goma = SortearGoma(azar);
                if (lluvia) co.goma = Compuesto.Lluvia;
                coches[k] = co;
            }
            carrera.Coches = coches;
            carrera.Iniciar(azar);
            if (azar.Prob(ProbCocheSeguridad)) carrera.scPrevisto = azar.Entre(15f, vueltas * c.Referencia * 0.75f);
            if (!enVivo) carrera.AlFinal();
            return carrera;
        }

        // Estado de la carrera en curso: se puede avanzar a trozos (en vivo, mientras se ve, para
        // que las órdenes por radio cuenten) o de una vez. Sin órdenes, sale lo mismo.
        Azar azar;
        List<int> orden;
        float t, limite, finDespuesDe = float.MaxValue;
        bool hayGanador, cerrada;
        (int coche, float tiempo) vueltaRapida = (-1, float.MaxValue);

        /// <summary>Segundos de carrera ya simulados.</summary>
        public float Tiempo => t;
        /// <summary>Si ya ha acabado (todos en meta o fuera, más la vuelta de enfriamiento).</summary>
        public bool Acabada => cerrada;

        float scPrevisto = -1f, scHasta = -1f;
        bool scActivo;
        /// <summary>Si el coche de seguridad está en pista en el instante simulado.</summary>
        public bool CocheSeguridadEnPista => scActivo;

        void EmpezarCocheSeguridad(int lider)
        {
            if (scActivo) return;
            scActivo = true;
            scPrevisto = -1f;
            scHasta = t + azar.Entre(30f, 45f);
            foreach (var co in Coches) co.aPor = -1;             // nadie sigue adelantando
            Anotar(t, Tipo.CocheSeguridad, lider, -1, Coches[lider].vueltas + 1, null);
        }

        void Iniciar(Azar a)
        {
            azar = a;
            orden = new List<int>();
            for (int i = 0; i < Coches.Length; i++) orden.Add(i);
            limite = Vueltas * Circuito.Referencia * 1.8f + 60f;
        }

        /// <summary>Simula hasta el instante indicado (o hasta que acabe).</summary>
        public void Avanzar(float hasta)
        {
            while (!cerrada && t < hasta)
            {
                if (t >= limite || t >= finDespuesDe) { Cerrar(); break; }
                Paso1();
            }
        }

        public void AlFinal() => Avanzar(float.MaxValue);

        void Cerrar()
        {
            cerrada = true;
            if (vueltaRapida.coche >= 0)
                Sucesos.Add(new Suceso { t = 0f, tipo = Tipo.VueltaRapida, coche = vueltaRapida.coche, valor = vueltaRapida.tiempo });
        }

        /// <summary>
        /// Segundos (aproximados) al coche de delante y al de detrás en el instante simulado, para
        /// decidir la radio. float.MaxValue si no hay nadie.
        /// </summary>
        public (float delante, float detras, int quienDelante, int quienDetras) Huecos(int coche)
        {
            var co = Coches[coche];
            float v = Math.Max(20f, co.v), delante = float.MaxValue, detras = float.MaxValue;
            int qd = -1, qt = -1;
            for (int i = 0; i < Coches.Length; i++)
            {
                var o = Coches[i];
                if (o == co || o.fuera || o.terminado) continue;
                float dd = o.d - co.d;
                if (dd > 0f && dd / v < delante) { delante = dd / v; qd = i; }
                else if (dd <= 0f && -dd / v < detras) { detras = -dd / v; qt = i; }
            }
            return (delante, detras, qd, qt);
        }

        /// <summary>Puesto de un coche en el instante simulado (los que ya han acabado van delante).</summary>
        public int Puesto(int coche)
        {
            var co = Coches[coche];
            int p = 1;
            foreach (var o in Coches)
                if (o != co && !o.fuera && (o.terminado && !co.terminado || o.d > co.d)) p++;
            return p;
        }

        /// <summary>Elige los neumáticos de salida de un coche; solo antes de que empiece la carrera.</summary>
        public bool Gomas(int coche, Compuesto g)
        {
            if (t > 0f || Lluvia) return false;
            Coches[coche].goma = g;
            return true;
        }

        public void Radio(int coche, OrdenPiloto o)
        {
            if (cerrada || Coches[coche].radio == o) return;
            Coches[coche].radio = o;
            Anotar(t, Tipo.Radio, coche, -1, Coches[coche].vueltas + 1, o.ToString());
        }

        /// <summary>Orden de equipo en vivo: prioridad para un piloto (null = carrera libre).</summary>
        public void Prioridad(string equipo, string piloto)
        {
            ordenes ??= new Dictionary<string, string>();
            if (piloto == null) ordenes.Remove(equipo); else ordenes[equipo] = piloto;
        }

        void Paso1()
        {
            var c = Circuito;
            float L = c.Trazado.Longitud;
            {
                orden.Sort((a, b) => Coches[b].d.CompareTo(Coches[a].d));
                int lider = orden.Find(i => !Coches[i].fuera && !Coches[i].terminado);
                if (!scActivo && scPrevisto > 0f && t >= scPrevisto && !hayGanador) EmpezarCocheSeguridad(lider);
                if (scActivo && t >= scHasta)
                {
                    scActivo = false;
                    Anotar(t, Tipo.Relanzada, lider, -1, Coches[lider].vueltas + 1, null);
                }
                var antes = new float[Coches.Length];
                for (int i = 0; i < Coches.Length; i++) antes[i] = Coches[i].d;

                for (int n = 0; n < orden.Count; n++)
                {
                    var co = Coches[orden[n]];
                    if (co.fuera) { Parar(co); continue; }
                    if (co.terminado) { Enfriar(co); continue; }
                    if (t < co.arranque) continue;

                    var radio = EfectoRadio(co.radio);
                    if (co.radio == OrdenPiloto.Atacar) co.segundosAtacando += Paso;
                    else if (co.radio == OrdenPiloto.Ahorrar) co.segundosAhorrando += Paso;
                    var goma = EfectoGoma(co.goma);
                    if (co.d > 0f)
                        // En "vueltas de una carrera de 3": con más vueltas cada una gasta menos, y el
                        // desgaste total de la carrera es el mismo sea cual sea su duración.
                        co.desgaste += co.v * Paso / L * 3f / Vueltas * (co.radio == OrdenPiloto.Atacar ? 1.5f : co.radio == OrdenPiloto.Ahorrar ? 0.6f : 1f);
                    float ritmo = co.ritmoVuelta + radio.ritmo + goma.inicio + goma.caida * co.desgaste;
                    float objetivo = c.Velocidad(co.d, ritmo);
                    if (scActivo)
                    {
                        // Detrás del coche de seguridad: el líder a su ritmo; los demás, algo más
                        // deprisa hasta pegarse al de delante (el tope de seguimiento hace el resto).
                        float vsc = c.Velocidad(co.d, ritmo) * 0.62f;
                        objetivo = Math.Min(objetivo, orden[n] == lider ? vsc : vsc * 1.35f);
                    }
                    if (t < co.perdidaHasta) objetivo *= co.factorPerdida;

                    // El de delante: en carrera se va en fila india; en la salida, en dos columnas
                    // (el de la otra columna no estorba). El que estás adelantando tampoco.
                    int yo = orden[n];
                    if (co.aPor >= 0 && (t > co.adelantandoHasta || co.d > Coches[co.aPor].d + Contacto || Coches[co.aPor].fuera))
                        co.aPor = -1;                       // maniobra acabada (pasó o desistió)
                    bool enColumnas = co.d < DosColumnas;
                    Coche delante = null;
                    int iDelante = -1;
                    for (int m = n - 1; m >= 0; m--)
                    {
                        int j = orden[m];
                        var o = Coches[j];
                        // El que estás adelantando no estorba... si ya vas por otro lado.
                        if (o.fuera || o.terminado || (j == co.aPor && Math.Abs(o.lateral - co.lateral) >= 1.8f)) continue;
                        if (antes[j] - co.d > 150f) break;
                        if (enColumnas && o.d < DosColumnas && o.salida % 2 != co.salida % 2) continue;
                        delante = o; iDelante = j; break;
                    }
                    // Hueco con las posiciones de ANTES del paso: el de delante ya se ha movido en
                    // este paso y medirlo así le daba al de detrás ~7 m de más.
                    float hueco = delante != null ? antes[iDelante] - co.d : float.MaxValue;
                    bool zona = c.ZonaDeAdelantamiento(co.d);
                    if (delante != null && hueco < 60f && zona && !scActivo) objetivo *= 1.012f;   // rebufo
                    // DRS: a menos de un segundo del de delante, en zona de adelantamiento y desde la
                    // segunda vuelta. Sin él los huecos de la salida (~1 s) no se cerraban en 3 vueltas.
                    if (delante != null && hueco < co.v * 1f && zona && co.vueltas >= 1 && !scActivo) objetivo *= 1f + DRS;

                    float vNueva = co.v < objetivo
                        ? Math.Min(objetivo, co.v + Aceleracion(co.v) * Paso)
                        : objetivo;

                    // Atacando, el piloto se lanza desde más lejos (rebufo largo); el tope de
                    // velocidad tras el de delante sigue siendo el de siempre.
                    float alcance = AlcanceAtaque + (co.radio == OrdenPiloto.Atacar ? 0.2f : 0f);
                    bool cerca = delante != null && hueco < HuecoMinimo + co.v * 0.15f;
                    if (delante != null && hueco < HuecoMinimo + co.v * alcance)
                    {
                        // ¿Se lanza? Solo en recta y si de verdad va más rápido.
                        // Órdenes de equipo entre compañeros.
                        bool companeros = delante.equipo.id == co.equipo.id && ordenes != null &&
                                          ordenes.TryGetValue(co.equipo.id, out var prioritario);
                        bool noAtacar = companeros && ordenes[co.equipo.id] == delante.piloto.nombre;
                        bool leCeden = companeros && ordenes[co.equipo.id] == co.piloto.nombre;
                        if (leCeden && !scActivo && co.aPor < 0 && zona && !enColumnas && objetivo > delante.v + 0.3f)
                        {
                            co.aPor = iDelante;
                            co.adelantandoHasta = t + 5f;
                            co.ladoManiobra = delante.lateral >= 0f ? -1f : 1f;
                            Anotar(t, Tipo.OrdenDeEquipo, yo, iDelante, co.vueltas + 1, null);
                        }
                        if (co.aPor < 0 && !scActivo && !noAtacar && zona && !enColumnas && objetivo > delante.v + UmbralAtaque)
                        {
                            float defensa = delante.piloto.defensa + EfectoRadio(delante.radio).defensa;
                            float habilidad = Math.Clamp(0.4f + co.piloto.ataque - 0.6f * defensa, 0.05f, 1.2f);
                            float empuje = Math.Clamp((objetivo - delante.v) / 4f, 0.2f, 1.5f);
                            // En la primera vuelta van en grupo y se arriesga más.
                            float primera = co.vueltas == 0 ? PrimeraVuelta : 1f;
                            if (azar.Prob(Intento * primera * habilidad * empuje * radio.intento * Paso))
                            {
                                co.aPor = iDelante;
                                co.adelantandoHasta = t + 6f;
                                co.resuelta = false;
                                Intentos++;
                                co.ladoManiobra = delante.lateral >= 0f ? -1f : 1f;
                            }
                        }
                        if (cerca && co.aPor != iDelante)
                            vNueva = Math.Min(vNueva, delante.v + (hueco - HuecoMinimo) * 0.8f);
                    }
                    // La maniobra se decide en la FRENADA: al acabarse la recta, si va emparejado
                    // (por el otro carril y a menos de un coche), gana la curva o no según su ataque
                    // frente a la defensa del otro. Antes había que pasar en recta a pura velocidad:
                    // 255 intentos por carrera y 15 adelantamientos (6 %).
                    if (co.aPor >= 0 && !co.resuelta && !zona)
                    {
                        var rival = Coches[co.aPor];
                        float dd = antes[co.aPor] - co.d;
                        bool emparejado = dd < LanceMaximo && Math.Abs(rival.lateral - co.lateral) >= 1.8f;
                        co.resuelta = true;
                        if (!emparejado) co.aPor = -1;           // no llegó: se queda detrás
                        else
                        {
                            float defensa = rival.piloto.defensa + EfectoRadio(rival.radio).defensa;
                            float p = Math.Clamp(Frenada + 0.6f * (co.piloto.ataque - defensa) - 0.03f * Math.Max(0f, dd - Contacto), 0.10f, 0.85f);
                            if (azar.Prob(p))
                            {
                                rival.perdidaHasta = t + 1.5f; rival.factorPerdida = 0.88f;   // cierra la curva por fuera
                                co.adelantandoHasta = t + 3f;
                            }
                            else
                            {
                                co.aPor = -1;
                                co.perdidaHasta = t + 1f; co.factorPerdida = 0.92f;           // levanta el pie
                            }
                        }
                    }
                    // Tope duro contra CUALQUIER coche que ocupe tu carril en los próximos metros, no
                    // solo contra el primero: el más cercano puede ir abierto y el que de verdad
                    // tienes encima, un poco más allá.
                    foreach (var o in Coches)
                    {
                        if (o == co || o.fuera || o.terminado) continue;
                        float dd = o.d - co.d;
                        if (dd <= 0f || dd > Contacto + 15f || Math.Abs(o.lateral - co.lateral) >= 1.8f) continue;
                        vNueva = Math.Min(vNueva, Math.Max(0f, dd - Contacto) / Paso);
                    }

                    co.v = Math.Max(0f, vNueva);
                    co.d += co.v * Paso;

                    // Lateral, solo para el 3D: de la parrilla a la trazada; a un lado al adelantar.
                    if (co.aPor >= 0)
                    {
                        // Por el lado contrario al que va AHORA el adelantado (puede haberse abierto él).
                        float suyo = Coches[co.aPor].lateral;
                        // Solo se elige lado mientras aún vas detrás: ya emparejados, cambiar es cruzarse.
                        if (Math.Abs(suyo) > 1f && Coches[co.aPor].d - co.d > Contacto) co.ladoManiobra = suyo > 0f ? -1f : 1f;
                        co.lateralObjetivo = co.ladoManiobra * CarrilAdelantar;
                    }
                    else
                    {
                        float quiero = co.d < DosColumnas ? co.lateralSalida * Math.Clamp(1f - Math.Max(0f, co.d) / DosColumnas, 0f, 1f) : 0f;
                        // No se vuelve a la trazada con otro coche al lado (al fundirse las dos
                        // columnas, o justo después de adelantar).
                        foreach (var o in Coches)
                            if (o != co && !o.fuera && !o.terminado && Math.Abs(o.d - co.d) < Contacto + 1f && Math.Abs(o.lateral - co.lateral) > 1.5f)
                            { quiero = co.lateral; break; }
                        co.lateralObjetivo = quiero;
                    }
                    // Se va largo: hacia la escapatoria mientras dura la pérdida (luego vuelve si
                    // nadie le tapa el hueco).
                    if (co.ladoError != 0f && t < co.perdidaHasta && co.aPor < 0) co.lateralObjetivo = co.ladoError * Circuito.Escapatoria * 0.8f;
                    else co.ladoError = 0f;
                    // Ningún movimiento lateral puede meterte en el carril de un coche que tengas al lado.
                    float nuevo = co.lateral + (co.lateralObjetivo - co.lateral) * Math.Min(1f, Paso * 2f);
                    foreach (var o in Coches)
                    {
                        if (o == co || o.fuera || Math.Abs(o.d - co.d) >= Contacto) continue;
                        if (Math.Abs(nuevo - o.lateral) < 1.8f && Math.Abs(co.lateral - o.lateral) >= 1.8f) { nuevo = co.lateral; break; }
                    }
                    co.lateral = nuevo;

                    Incidencias(co, t, azar);
                    if (co.fuera) continue;

                    // Paso por meta.
                    if (co.d >= (co.vueltas + 1) * L)
                    {
                        float tLinea = t + Paso - (co.d - (co.vueltas + 1) * L) / Math.Max(1f, co.v);
                        float tv = tLinea - co.inicioVuelta;
                        co.vueltas++;
                        co.inicioVuelta = tLinea;
                        if (tv < co.mejorVuelta) co.mejorVuelta = tv;
                        if (co.vueltas > 1 && tv < vueltaRapida.tiempo) vueltaRapida = (orden[n], tv);   // la de salida no cuenta
                        co.ritmoVuelta = co.ritmoBase + azar.Normal(0f, co.piloto.regularidad * Clima.Efecto(Lluvia).regularidad);
                        co.errorEn = azar.Prob(co.piloto.errores * EfectoRadio(co.radio).errores * Clima.Efecto(Lluvia).errores) ? co.vueltas * L + azar.Entre(0f, L) : -1f;

                        if (co.d >= co.meta)
                        {
                            co.terminado = true;
                            co.tiempoFinal = tLinea;
                            Anotar(tLinea, Tipo.Meta, orden[n], -1, co.vueltas, null);
                            if (!hayGanador)
                            {
                                hayGanador = true;
                                // El resto acaba en su siguiente paso por meta (no hace falta haber
                                // completado todas las vueltas si te han doblado).
                                foreach (var o in Coches)
                                    if (!o.terminado && !o.fuera) o.meta = (float)Math.Ceiling(o.d / L) * L;
                            }
                        }
                    }
                }

                DetectarAdelantamientos(antes, t);
                for (int i = 0; i < Coches.Length; i++) { Coches[i].grabD.Add(Coches[i].d); Coches[i].grabL.Add(Coches[i].lateral); }
                Muestras++;
                t += Paso;

                if (finDespuesDe == float.MaxValue && Array.TrueForAll(Coches, x => x.terminado || x.fuera))
                    finDespuesDe = t + 4f;     // unos segundos de vuelta de enfriamiento
            }

        }

        void Incidencias(Coche co, float t, Azar azar)
        {
            int i = Array.IndexOf(Coches, co);
            if (co.toqueEn >= 0f && co.d >= co.toqueEn)
            {
                co.toqueEn = -1f;
                if (azar.Prob(0.35f))
                {
                    Abandonar(co, t, "Accidente");
                    if (azar.Prob(CocheSeguridadTrasAccidente) && !hayGanador)
                        EmpezarCocheSeguridad(orden.Find(k => !Coches[k].fuera && !Coches[k].terminado));
                    return;
                }
                co.perdidaHasta = t + azar.Entre(2f, 5f);
                co.factorPerdida = 0.3f;
                co.ritmoBase += azar.Entre(0.3f, 1.5f);      // daños en el alerón
                Anotar(t, Tipo.Toque, i, -1, co.vueltas + 1, null);
            }
            if (co.averiaEn >= 0f && co.d >= co.averiaEn) { Abandonar(co, t, "Avería"); return; }
            if (co.errorEn >= 0f && co.d >= co.errorEn)
            {
                co.errorEn = -1f;
                co.perdidaHasta = t + azar.Entre(1.5f, 4f);
                co.factorPerdida = azar.Entre(0.45f, 0.7f);
                var tr = Circuito.Trazado;
                float dv = co.d % tr.Longitud;
                int k = Math.Clamp((int)(dv / tr.Longitud * tr.Cantidad), 0, tr.Cantidad - 1);
                co.ladoError = tr.Curvatura((k + 8) % tr.Cantidad, 6) > 0f ? 1f : -1f;   // hacia fuera de la curva
                Anotar(t, Tipo.Error, i, -1, co.vueltas + 1, null);
            }
        }

        void Abandonar(Coche co, float t, string motivo)
        {
            co.fuera = true;
            co.motivo = motivo;
            co.lateralObjetivo = (co.lateral >= 0f ? 1f : -1f) * 10f;   // a la escapatoria
            Anotar(t, Tipo.Abandono, Array.IndexOf(Coches, co), -1, co.vueltas + 1, motivo);
        }

        void Parar(Coche co)
        {
            co.v = Math.Max(0f, co.v - 25f * Paso);
            co.d += co.v * Paso;
            co.lateral += (co.lateralObjetivo - co.lateral) * Math.Min(1f, Paso * 1.2f);
        }

        void Enfriar(Coche co)
        {
            co.v += (Circuito.Velocidad(co.d, 40f) - co.v) * Math.Min(1f, Paso * 0.5f);
            co.d += co.v * Paso;
        }

        static float Aceleracion(float v)
        {
            var p = Prestaciones.Referencia;
            return p.AceleracionA(v);
        }

        // Orden "firme" entre cada par: solo cambia cuando uno saca más de 5 m al otro. Así no
        // cuenta como adelantamiento ir rueda con rueda intercambiando centímetros.
        sbyte[,] delanteFirme;

        void DetectarAdelantamientos(float[] antes, float t)
        {
            int n = Coches.Length;
            if (delanteFirme == null) delanteFirme = new sbyte[n, n];
            for (int a = 0; a < n; a++)
            {
                var A = Coches[a];
                for (int b = a + 1; b < n; b++)
                {
                    var B = Coches[b];
                    float dd = A.d - B.d;
                    sbyte ahora = dd > 5f ? (sbyte)1 : dd < -5f ? (sbyte)-1 : (sbyte)0;
                    if (ahora == 0) continue;
                    sbyte previo = delanteFirme[a, b];
                    delanteFirme[a, b] = ahora;
                    // Cuenta si los dos estaban en carrera, ya en movimiento y fuera de la parrilla.
                    if (previo == 0 || previo == ahora || A.fuera || B.fuera || A.terminado || B.terminado || A.d < 0f || B.d < 0f) continue;
                    if (ahora == 1) Anotar(t, Tipo.Adelantamiento, a, b, A.vueltas + 1, null);
                    else Anotar(t, Tipo.Adelantamiento, b, a, B.vueltas + 1, null);
                }
            }
        }

        void Anotar(float t, Tipo tipo, int coche, int otro, int vuelta, string texto) =>
            Sucesos.Add(new Suceso { t = t, tipo = tipo, coche = coche, otro = otro, vuelta = vuelta, texto = texto });

        /// <summary>Distancia y lateral grabados de un coche en un instante (interpolado).</summary>
        public (float d, float lateral) Muestra(int coche, float segundos)
        {
            var co = Coches[coche];
            float f = Math.Clamp(segundos / Paso, 0f, Muestras - 1);
            int i = (int)f; float r = f - i;
            int j = Math.Min(i + 1, Muestras - 1);
            return (co.grabD[i] + (co.grabD[j] - co.grabD[i]) * r, co.grabL[i] + (co.grabL[j] - co.grabL[i]) * r);
        }
    }
}
