using System;
using System.Collections.Generic;
using System.Linq;

namespace F1.Nucleo
{
    /// <summary>
    /// Una partida de Campeonato del Mundo: la temporada en curso con su estado completo
    /// (equipos y pilotos con lo que han evolucionado, resultados, puntos y decisiones del
    /// jugador). Se guarda tal cual en JSON: lleva dentro todo lo necesario para continuar.
    /// </summary>
    [Serializable]
    public class Partida
    {
        public int version = 1;
        public int temporada;
        public int semilla;
        public string equipoJugador;
        public int ronda;                              // próxima carrera (0 = la primera)
        public string[] calendario;                    // ids de circuito en orden
        public Equipo[] equipos;                       // con el desarrollo acumulado
        public Piloto[] pilotos;
        public List<ResultadoGP> resultados = new List<ResultadoGP>();

        // Decisiones del jugador para el próximo GP.
        public int puntosDesarrollo;                   // por repartir en esta ronda
        public int invertidoMotor, invertidoAero;      // en toda la temporada (para mostrarlo)
        public string modoMotor = Campeonato.ModoNormal;
        public string carga = Campeonato.CargaMedia;   // configuración de alerones para este GP
        public string prioritario = "";                // piloto con prioridad; vacío = carrera libre
        public float desgasteMotor;                    // 0 = nuevo; las averías crecen con él
        public int motoresUsados = 1;                  // el de la primera carrera cuenta
        public bool motorNuevoEnEsteGP;                // penaliza si pasa de los gratuitos
        /// <summary>Hay un GP empezado en vivo sin apuntar: al cargar se da por corrido sin órdenes.</summary>
        public bool gpEnCurso;
        /// <summary>Vueltas de cada carrera de esta partida (se fija al crearla; 0 en partidas antiguas = 3).</summary>
        public int vueltas = 3;
        public int Vueltas => vueltas > 0 ? vueltas : 3;

        // La directiva y el dinero.
        public float presupuesto = Campeonato.PresupuestoInicial;   // millones de euros
        public float ingresosUltimoGP;
        public int objetivo;                           // puesto de constructores que pide la directiva
        public float confianza = Campeonato.ConfianzaInicial;      // 0-100; acabar el año a 0 es el despido
        public bool despedido;
        public string mensajeDirectiva = "";
        public List<ResumenTemporada> historial = new List<ResumenTemporada>();

        public bool Terminada => ronda >= calendario.Length;
        public Equipo MiEquipo => equipos.First(e => e.id == equipoJugador);
        public Parrilla ComoParrilla() => new Parrilla { temporada = temporada, equipos = equipos, pilotos = pilotos };
    }

    /// <summary>Lo que queda de una temporada acabada.</summary>
    [Serializable]
    public class ResumenTemporada
    {
        public int temporada;
        public string equipo, campeonPiloto, campeonEquipo;
        public int puesto, puntos, objetivo;
    }

    [Serializable]
    public class ResultadoGP
    {
        public int ronda;
        public string circuito;
        public int semilla;
        public FilaResultado[] filas;
        public string vueltaRapida;
        public float tiempoVueltaRapida;
    }

    [Serializable]
    public class FilaResultado
    {
        public string piloto, equipo;
        public int salida, puesto, puntos;
        public bool termino;
        public string motivo;
        public float tiempo, mejorVuelta;
    }

    /// <summary>Clasificación de pilotos o de constructores en un momento de la temporada.</summary>
    public sealed class Puesto
    {
        public string nombre, equipo;
        public int puntos, victorias, podios;
        public int[] porPuesto = new int[23];          // cuántas veces 1.º, 2.º... (para desempatar)
    }

    public static class Campeonato
    {
        public const int PuntosPorRonda = 10;
        /// <summary>Lo que gana un punto de desarrollo: % de vuelta y carácter (motor + / aero -).</summary>
        public const float GananciaPorPunto = 0.0045f, CaracterPorPunto = 0.02f;
        /// <summary>Motores por temporada sin penalización; a partir de ahí, puestos de parrilla.</summary>
        public const int MotoresGratis = 4, PenalizacionMotor = 5;
        /// <summary>Lo que avanza un rival por ronda (media y desviación, % de vuelta).</summary>
        public const float AvanceRivalMedio = 0.035f, AvanceRivalDesviacion = 0.02f;

        public const string ModoConservador = "conservador", ModoNormal = "normal", ModoAgresivo = "agresivo";

        /// <summary>
        /// El dinero (millones de euros). Desarrollar cuesta CostePunto por punto (hasta
        /// PuntosPorRonda por ronda, lo que da de sí la fábrica) y un motor nuevo, CosteMotor. Se
        /// cobra en cada GP: un fijo más una parte por punto de constructores. Calibrado con el arnés de
        /// temporadas (ver README).
        /// </summary>
        public const float PresupuestoInicial = 6f, CostePunto = 0.3f, CosteMotor = 3f, IngresoBase = 2.5f, IngresoPorPunto = 0.08f;
        /// <summary>La confianza de la directiva al empezar (0-100).</summary>
        public const float ConfianzaInicial = 60f;
        public const string CargaBaja = "baja", CargaMedia = "media", CargaAlta = "alta";

        /// <summary>
        /// La carga aerodinámica del GP mueve el carácter del coche solo en esa carrera: baja =
        /// coche de recta, alta = coche de curva. Acertar en un circuito extremo vale ~0,2 % de
        /// vuelta; fallar cuesta lo mismo.
        /// </summary>
        public static float EfectoCarga(string carga) => carga switch { CargaBaja => 0.8f, CargaAlta => -0.8f, _ => 0f };

        /// <summary>La carga que conviene en un circuito (lo que eligen los rivales cuando aciertan).</summary>
        public static string CargaIdeal(Circuito c) =>
            c.FraccionPotencia > Circuito.FraccionPotenciaMedia + 0.06f ? CargaBaja :
            c.FraccionPotencia < Circuito.FraccionPotenciaMedia - 0.06f ? CargaAlta : CargaMedia;

        /// <summary>Con qué probabilidad un rival acierta con la carga (si no, va con la media).</summary>
        public const float AciertoRivales = 0.6f;

        /// <summary>
        /// Efecto de cada modo de motor: ritmo (% de vuelta) y desgaste que añade el GP. En 3
        /// vueltas el riesgo inmediato de avería es despreciable (el agresivo daba 154 puntos
        /// frente a 111 sin coste): el precio está en el desgaste, que se arrastra.
        /// </summary>
        public static (float ritmo, float desgaste) EfectoModo(string modo) => modo switch
        {
            ModoConservador => (0.08f, 2f),
            ModoAgresivo => (-0.10f, 8f),
            _ => (0f, 4f),
        };

        /// <summary>Cuánto multiplica las averías un motor gastado: x2 a 30, x5 a 60, x10 a 90.</summary>
        public static float MultiplicadorDesgaste(float desgaste) => 1f + (desgaste / 30f) * (desgaste / 30f);

        /// <summary>Monta un motor nuevo para el próximo GP (desgaste 0).</summary>
        public static bool MotorNuevo(Partida p)
        {
            if (p.motorNuevoEnEsteGP || p.presupuesto < CosteMotor) return false;
            p.presupuesto -= CosteMotor;
            p.motorNuevoEnEsteGP = true;
            p.motoresUsados++;
            p.desgasteMotor = 0f;
            return true;
        }

        public static bool PenalizaMotor(Partida p) => p.motorNuevoEnEsteGP && p.motoresUsados > MotoresGratis;

        public static Partida Nueva(Parrilla base_, string[] calendario, string equipoJugador, int semilla)
        {
            if (base_.equipos.All(e => e.id != equipoJugador)) throw new ArgumentException($"No existe el equipo {equipoJugador}");
            var p = new Partida
            {
                temporada = base_.temporada,
                semilla = semilla,
                equipoJugador = equipoJugador,
                calendario = (string[])calendario.Clone(),
                equipos = base_.equipos.Select(Copiar).ToArray(),
                pilotos = base_.pilotos.Select(Copiar).ToArray(),
                puntosDesarrollo = PuntosPorRonda,
                presupuesto = PresupuestoInicial,
                confianza = ConfianzaInicial,
            };
            p.objetivo = ObjetivoDe(p);
            p.mensajeDirectiva = $"La directiva espera acabar {p.objetivo}.º en constructores.";
            return p;
        }

        // ── Decisiones del jugador ───────────────────────────────────────────

        /// <summary>Invierte puntos de esta ronda: en motor (recta) o en aerodinámica (curva).</summary>
        public static bool Invertir(Partida p, bool motor)
        {
            if (p.puntosDesarrollo <= 0 || p.presupuesto < CostePunto) return false;
            p.presupuesto -= CostePunto;
            var e = p.MiEquipo;
            e.ritmo -= GananciaPorPunto * Rendimiento(p);
            e.potencia = Math.Clamp(e.potencia + (motor ? CaracterPorPunto : -CaracterPorPunto), -1.5f, 1.5f);
            if (motor) p.invertidoMotor++; else p.invertidoAero++;
            p.puntosDesarrollo--;
            return true;
        }

        /// <summary>
        /// Cuánto rinde un punto de desarrollo según el puesto del coche por ritmo: ×0,6 el mejor,
        /// ×1,4 el peor. Cuanto más arriba, más cuesta mejorar; sin esto, gastando todo se ganaba el
        /// Mundial al tercer año con un Willians y el Cadillan no salía nunca del fondo.
        /// </summary>
        public static float Rendimiento(Partida p)
        {
            int puesto = ObjetivoDe(p);
            return 0.6f + 0.8f * (puesto - 1) / Math.Max(1, p.equipos.Length - 1);
        }

        public static string[] MisPilotos(Partida p) =>
            p.pilotos.Where(x => x.equipo == p.equipoJugador).Select(x => x.nombre).ToArray();

        // ── Un Gran Premio ───────────────────────────────────────────────────

        /// <summary>Hash estable (string.GetHashCode cambia de una ejecución a otra en .NET).</summary>
        static int Hash(string s)
        {
            unchecked { int h = 23; foreach (char ch in s ?? "") h = h * 31 + ch; return h; }
        }

        public static int SemillaDe(Partida p, int ronda) => unchecked(p.semilla * 7919 + ronda * 104729 + 17);

        /// <summary>
        /// La parrilla tal como sale a pista en este GP: el estado de la temporada más el modo
        /// de motor del jugador. Es una copia: el modo no se acumula de una carrera a otra.
        /// </summary>
        public static Parrilla ParrillaDelGP(Partida p, Circuito c = null)
        {
            var equipos = p.equipos.Select(Copiar).ToArray();
            foreach (var e in equipos)
            {
                if (e.id == p.equipoJugador) { e.potencia += EfectoCarga(p.carga); continue; }
                if (c == null) continue;
                var az = new Azar(SemillaDe(p, p.ronda) ^ Hash(e.id));
                if (az.Prob(AciertoRivales)) e.potencia += EfectoCarga(CargaIdeal(c));
            }
            var (ritmo, _) = EfectoModo(p.modoMotor);
            var mio = equipos.First(e => e.id == p.equipoJugador);
            mio.ritmo += ritmo;
            mio.fiabilidad = 1f - Math.Min(0.99f, (1f - mio.fiabilidad) * MultiplicadorDesgaste(p.desgasteMotor));
            return new Parrilla { temporada = p.temporada, equipos = equipos, pilotos = p.pilotos.Select(Copiar).ToArray() };
        }

        /// <summary>
        /// Simula clasificación y carrera del GP de esta ronda (sin apuntar nada todavía). En
        /// vivo, la carrera queda en la parrilla y avanza mientras se ve (para la radio).
        /// </summary>
        public static (Clasificacion.Vuelta[] q, Carrera carrera) Simular(Partida p, Circuito c, int vueltas = 0, bool enVivo = false)
        {
            if (vueltas <= 0) vueltas = p.Vueltas;
            var parrilla = ParrillaDelGP(p, c);
            var azar = new Azar(SemillaDe(p, p.ronda));
            bool lluvia = Clima.Lluvia(c.Id, SemillaDe(p, p.ronda));
            var q = Clasificacion.Simular(c, parrilla, azar, lluvia);
            if (PenalizaMotor(p)) q = Penalizar(q, p.equipoJugador, PenalizacionMotor);
            var ordenes = string.IsNullOrEmpty(p.prioritario) ? null
                : new Dictionary<string, string> { [p.equipoJugador] = p.prioritario };
            return (q, Carrera.Simular(c, q, vueltas, azar, ordenes, enVivo, lluvia));
        }

        /// <summary>Apunta el resultado, reparte puntos, hace evolucionar a los rivales y pasa de ronda.</summary>
        public static ResultadoGP Registrar(Partida p, Carrera carrera)
        {
            carrera.AlFinal();
            var r = carrera.Resultado;
            var vr = carrera.Sucesos.FirstOrDefault(s => s.tipo == Carrera.Tipo.VueltaRapida);
            var gp = new ResultadoGP
            {
                ronda = p.ronda,
                circuito = p.calendario[p.ronda],
                semilla = SemillaDe(p, p.ronda),
                vueltaRapida = vr != null ? carrera.Coches[vr.coche].piloto.nombre : "",
                tiempoVueltaRapida = vr != null ? vr.valor : 0f,
                filas = r.Select((co, i) => new FilaResultado
                {
                    piloto = co.piloto.nombre, equipo = co.equipo.id, salida = co.salida + 1, puesto = i + 1,
                    termino = co.terminado, motivo = co.motivo ?? "",
                    puntos = co.terminado ? Puntos.PorPuesto(i + 1) : 0,
                    tiempo = co.terminado ? co.tiempoFinal : 0f,
                    mejorVuelta = co.mejorVuelta < float.MaxValue ? co.mejorVuelta : 0f,
                }).ToArray(),
            };
            p.resultados.Add(gp);

            // Los rivales evolucionan entre carreras (el jugador ya lo hizo al invertir).
            var azar = new Azar(SemillaDe(p, p.ronda) ^ 0x5f3759df);
            foreach (var e in p.equipos.Where(e => e.id != p.equipoJugador))
            {
                e.ritmo -= Math.Max(0f, azar.Normal(AvanceRivalMedio, AvanceRivalDesviacion));
                e.potencia = Math.Clamp(e.potencia + azar.Normal(0f, 0.05f), -1.5f, 1.5f);
            }

            p.desgasteMotor = Math.Max(0f, p.desgasteMotor + EfectoModo(p.modoMotor).desgaste + DesgasteRadio(p, carrera));
            p.gpEnCurso = false;
            p.ronda++;
            p.puntosDesarrollo = PuntosPorRonda;      // lo no gastado se pierde
            p.modoMotor = ModoNormal;
            p.carga = CargaMedia;
            p.motorNuevoEnEsteGP = false;
            Directiva(p, gp);
            return gp;
        }

        /// <summary>
        /// Lo que la radio gasta o ahorra de motor: una carrera entera atacando con los dos
        /// coches suma ~4,5 en Mónaco (como pasar de modo normal a agresivo); ahorrando, resta la mitad.
        /// </summary>
        public static float DesgasteRadio(Partida p, Carrera carrera)
        {
            float s = 0f;
            foreach (var co in carrera.Coches)
                if (co.equipo.id == p.equipoJugador) s += co.segundosAtacando * 0.01f - co.segundosAhorrando * 0.005f;
            return s * 3f / carrera.Vueltas;            // lo mismo por carrera, dure lo que dure
        }

        // ── La directiva, el dinero y las temporadas ──────────────────────────

        /// <summary>El puesto que pide la directiva: el que le toca al coche por ritmo.</summary>
        public static int ObjetivoDe(Partida p) =>
            p.equipos.OrderBy(e => e.ritmo).ToList().FindIndex(e => e.id == p.equipoJugador) + 1;

        public static int PuestoConstructores(Partida p) =>
            Constructores(p).FindIndex(x => x.equipo == p.equipoJugador) + 1;

        /// <summary>
        /// Tras cada GP: se cobra (un fijo y una parte por punto) y la directiva ajusta su confianza
        /// según cómo vaya el equipo frente al objetivo (hasta ±3 por carrera; +3 más si se gana).
        /// Al acabar la temporada, el balance: +15 si se cumple (y una prima), −10 por puesto de
        /// menos; acabar a 0 de confianza es el despido.
        /// </summary>
        static void Directiva(Partida p, ResultadoGP gp)
        {
            int pts = gp.filas.Where(f => f.equipo == p.equipoJugador).Sum(f => f.puntos);
            p.ingresosUltimoGP = IngresoBase + IngresoPorPunto * pts;
            p.presupuesto += p.ingresosUltimoGP;
            if (p.objetivo <= 0) p.objetivo = ObjetivoDe(p);
            int pos = PuestoConstructores(p);
            float cambio = Math.Clamp(p.objetivo - pos, -3f, 3f) + (gp.filas[0].equipo == p.equipoJugador && gp.filas[0].termino ? 3f : 0f);
            p.confianza = Math.Clamp(p.confianza + cambio, 0f, 100f);
            p.mensajeDirectiva =
                pos < p.objetivo ? $"La directiva está encantada: vais {pos}.º y el objetivo era {p.objetivo}.º." :
                pos == p.objetivo ? $"La directiva está satisfecha: {pos}.º, lo que se esperaba." :
                p.confianza < 30f ? $"La directiva pierde la paciencia: vais {pos}.º y se esperaba {p.objetivo}.º." :
                $"La directiva pide más: vais {pos}.º y se esperaba {p.objetivo}.º.";
            if (!p.Terminada) return;

            // Balance de la temporada.
            var tp = Pilotos(p); var tc = Constructores(p);
            p.historial.Add(new ResumenTemporada
            {
                temporada = p.temporada, equipo = p.equipoJugador, puesto = pos,
                puntos = tc.First(x => x.equipo == p.equipoJugador).puntos, objetivo = p.objetivo,
                campeonPiloto = tp[0].nombre, campeonEquipo = tc[0].nombre,
            });
            if (pos <= p.objetivo)
            {
                p.confianza = Math.Min(100f, p.confianza + 15f);
                p.presupuesto += 3f * (p.objetivo - pos + 1);
                p.mensajeDirectiva = $"Objetivo cumplido: {pos}.º (se pedía {p.objetivo}.º). La directiva te renueva y añade {3f * (p.objetivo - pos + 1):F0} M al presupuesto.";
            }
            else
            {
                p.confianza = Math.Max(0f, p.confianza - 10f * (pos - p.objetivo));
                p.despedido = p.confianza <= 0f;
                p.mensajeDirectiva = p.despedido
                    ? $"La directiva te despide: {pos}.º cuando se pedía {p.objetivo}.º."
                    : $"Objetivo fallado: {pos}.º (se pedía {p.objetivo}.º). Sigues, pero la confianza baja a {p.confianza:F0}.";
            }
        }

        /// <summary>
        /// La temporada siguiente: los coches se acercan unos a otros (los rivales más que el tuyo,
        /// que conserva más de lo que has desarrollado), con algo de azar por el cambio de año; los
        /// pilotos varían un poco. Motores y desgaste, a cero; objetivo nuevo; el dinero sigue.
        /// </summary>
        public static void NuevaTemporada(Partida p)
        {
            p.temporada++;
            p.semilla = unchecked(p.semilla * 31 + 7919);       // si no, las carreras se repetirían
            var azar = new Azar(p.semilla);
            float media = p.equipos.Average(e => e.ritmo);
            foreach (var e in p.equipos)
            {
                bool mio = e.id == p.equipoJugador;
                e.ritmo = media + (e.ritmo - media) * (mio ? 0.85f : 0.7f) + (mio ? 0f : azar.Normal(0f, 0.12f));
                e.potencia = Math.Clamp(e.potencia * 0.8f + azar.Normal(0f, 0.15f), -1.5f, 1.5f);
            }
            float mejor = p.equipos.Min(e => e.ritmo);
            foreach (var e in p.equipos) e.ritmo -= mejor;             // el mejor coche vuelve a 0
            foreach (var x in p.pilotos)
            {
                x.ritmo += azar.Normal(0f, 0.04f);
                x.errores = Math.Clamp(x.errores + azar.Normal(0f, 0.003f), 0.004f, 0.04f);
            }
            p.ronda = 0;
            p.resultados.Clear();
            p.puntosDesarrollo = PuntosPorRonda;
            p.invertidoMotor = p.invertidoAero = 0;
            p.modoMotor = ModoNormal; p.carga = CargaMedia; p.prioritario = "";
            p.desgasteMotor = 0f; p.motoresUsados = 1; p.motorNuevoEnEsteGP = false; p.gpEnCurso = false;
            // Lo que pide la directiva: entre lo conseguido el año pasado y lo que vale el coche (si se
            // calculase solo por el coche, no desarrollar saldría gratis: el objetivo bajaría con él).
            int anterior = p.historial.Count > 0 ? p.historial[p.historial.Count - 1].puesto : ObjetivoDe(p);
            p.objetivo = (anterior + ObjetivoDe(p)) / 2;
            p.mensajeDirectiva = $"Temporada {p.temporada}: la directiva espera acabar {p.objetivo}.º en constructores.";
        }

        /// <summary>Lo que cuesta fichar a 'nuevo' en lugar de 'actual': más cuanto mejor sea.</summary>
        public static float CosteFichaje(Piloto actual, Piloto nuevo) => Math.Max(1f, 2f + (actual.ritmo - nuevo.ritmo) * 40f);

        /// <summary>
        /// Fichaje entre temporadas (antes del primer GP): el piloto nuevo llega a tu equipo y el tuyo
        /// se va al suyo. Se paga del presupuesto.
        /// </summary>
        public static bool Fichar(Partida p, string miPiloto, string otro)
        {
            if (p.ronda != 0) return false;
            var a = p.pilotos.FirstOrDefault(x => x.nombre == miPiloto && x.equipo == p.equipoJugador);
            var b = p.pilotos.FirstOrDefault(x => x.nombre == otro && x.equipo != p.equipoJugador);
            if (a == null || b == null) return false;
            float coste = CosteFichaje(a, b);
            if (p.presupuesto < coste) return false;
            p.presupuesto -= coste;
            (a.equipo, b.equipo) = (b.equipo, a.equipo);
            if (p.prioritario == miPiloto) p.prioritario = "";
            p.objetivo = ObjetivoDe(p);
            return true;
        }

        /// <summary>Retrasa en la parrilla a los coches de un equipo (penalización por motor).</summary>
        static Clasificacion.Vuelta[] Penalizar(Clasificacion.Vuelta[] q, string equipo, int puestos)
        {
            var lista = q.ToList();
            // Del peor al mejor, para que no se adelanten entre ellos al moverlos.
            for (int i = lista.Count - 1; i >= 0; i--)
                if (lista[i].equipo.id == equipo)
                {
                    var v = lista[i]; lista.RemoveAt(i);
                    lista.Insert(Math.Min(lista.Count, i + puestos), v);
                }
            return lista.ToArray();
        }

        // ── Clasificaciones ──────────────────────────────────────────────────

        public static List<Puesto> Pilotos(Partida p) => Tabla(p, porEquipos: false);
        public static List<Puesto> Constructores(Partida p) => Tabla(p, porEquipos: true);

        static List<Puesto> Tabla(Partida p, bool porEquipos)
        {
            // Todos aparecen desde el principio, aunque no hayan puntuado.
            var t = new Dictionary<string, Puesto>();
            if (porEquipos) foreach (var e in p.equipos) t[e.id] = new Puesto { nombre = e.nombre, equipo = e.id };
            else foreach (var pi in p.pilotos) t[pi.nombre] = new Puesto { nombre = pi.nombre, equipo = pi.equipo };
            foreach (var gp in p.resultados)
                foreach (var f in gp.filas)
                {
                    var k = porEquipos ? f.equipo : f.piloto;
                    if (!t.TryGetValue(k, out var pu)) t[k] = pu = new Puesto { nombre = k, equipo = f.equipo };
                    pu.puntos += f.puntos;
                    if (f.termino && f.puesto <= 22) pu.porPuesto[f.puesto]++;
                    if (f.termino && f.puesto == 1) pu.victorias++;
                    if (f.termino && f.puesto <= 3) pu.podios++;
                }
            // Desempate oficial: más victorias, luego más segundos puestos, etc.
            return t.Values.OrderByDescending(x => x.puntos).ThenBy(x => x, Comparer<Puesto>.Create(Desempate)).ToList();
        }

        static int Desempate(Puesto a, Puesto b)
        {
            for (int i = 1; i < a.porPuesto.Length; i++)
                if (a.porPuesto[i] != b.porPuesto[i]) return b.porPuesto[i].CompareTo(a.porPuesto[i]);
            return string.CompareOrdinal(a.nombre, b.nombre);
        }

        static Equipo Copiar(Equipo e) => new Equipo
        {
            id = e.id, nombre = e.nombre, material = e.material, motor = e.motor, color = e.color,
            ritmo = e.ritmo, potencia = e.potencia, fiabilidad = e.fiabilidad,
        };

        static Piloto Copiar(Piloto x) => new Piloto
        {
            nombre = x.nombre, corto = x.corto, foto = x.foto, pais = x.pais, equipo = x.equipo, casco = x.casco,
            ritmo = x.ritmo, regularidad = x.regularidad, salida = x.salida, ataque = x.ataque,
            defensa = x.defensa, errores = x.errores,
        };
    }
}
