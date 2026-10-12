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

        public enum Tipo { Adelantamiento, Error, Toque, Abandono, VueltaRapida, Meta, OrdenDeEquipo, Radio }

        /// <summary>Lo que el jefe de escudería le dice a su piloto por radio durante la carrera.</summary>
        public enum OrdenPiloto { Normal, Atacar, Defender, Ahorrar }

        /// <summary>
        /// Efecto de cada orden mientras está activa: ritmo (% de vuelta, negativo = más rápido),
        /// intentos de adelantamiento, probabilidad de error y defensa. Atacar desgasta el motor y
        /// ahorrar lo cuida (se cobra al apuntar el GP).
        /// </summary>
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
            internal float ladoError;          // hacia dónde se va largo en un error (0 = no)
            /// <summary>La orden de radio vigente y el tiempo que ha pasado atacando o ahorrando.</summary>
            public OrdenPiloto radio;
            public float segundosAtacando, segundosAhorrando;
        }

        public Circuito Circuito { get; private set; }
        public int Vueltas { get; private set; }
        public Coche[] Coches { get; private set; }
        public List<Suceso> Sucesos { get; } = new List<Suceso>();
        public int Muestras { get; private set; }
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
                                      Dictionary<string, string> ordenes = null, bool enVivo = false)
        {
            var carrera = new Carrera { Circuito = c, Vueltas = vueltas, ordenes = ordenes };
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
                    ritmoBase = c.Ritmo(q.equipo, q.piloto) + RitmoCarrera + azar.Normal(0f, Clasificacion.FormaDelDia),
                };
                co.ritmoVuelta = co.ritmoBase + azar.Normal(0f, q.piloto.regularidad);
                // Salida: reflejos + algún patinazo de vez en cuando.
                co.arranque = 0.2f + (1f - q.piloto.salida) * 0.3f + Math.Abs(azar.Normal(0f, 0.10f));
                if (azar.Prob(0.05f + (1f - q.piloto.salida) * 0.05f)) co.arranque += azar.Entre(0.3f, 1.0f);
                // Toque en la primera vuelta y avería: se decide ahora dónde pasarían.
                if (azar.Prob(0.01f + q.piloto.errores * 0.6f)) co.toqueEn = azar.Entre(120f, 1200f);
                float pAveria = (1f - (float)Math.Pow(q.equipo.fiabilidad, (double)vueltas / VueltasDeUnGP)) * MultiplicadorAverias;
                if (azar.Prob(pAveria)) co.averiaEn = azar.Entre(0f, vueltas * L);
                co.errorEn = azar.Prob(q.piloto.errores) ? azar.Entre(0f, L) : -1f;
                coches[k] = co;
            }
            carrera.Coches = coches;
            carrera.Iniciar(azar);
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

        /// <summary>Orden por radio a un piloto, desde este instante.</summary>
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
                    float ritmo = co.ritmoVuelta + radio.ritmo;
                    float objetivo = c.Velocidad(co.d, ritmo);
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
                    if (delante != null && hueco < 60f && zona) objetivo *= 1.012f;   // rebufo

                    float vNueva = co.v < objetivo
                        ? Math.Min(objetivo, co.v + Aceleracion(co.v) * Paso)
                        : objetivo;

                    // Atacando, el piloto se lanza desde más lejos (rebufo largo); el tope de
                    // velocidad tras el de delante sigue siendo el de siempre.
                    float alcance = co.radio == OrdenPiloto.Atacar ? 0.35f : 0.15f;
                    bool cerca = delante != null && hueco < HuecoMinimo + co.v * 0.15f;
                    if (delante != null && hueco < HuecoMinimo + co.v * alcance)
                    {
                        // ¿Se lanza? Solo en recta y si de verdad va más rápido.
                        // Órdenes de equipo entre compañeros.
                        bool companeros = delante.equipo.id == co.equipo.id && ordenes != null &&
                                          ordenes.TryGetValue(co.equipo.id, out var prioritario);
                        bool noAtacar = companeros && ordenes[co.equipo.id] == delante.piloto.nombre;
                        bool leCeden = companeros && ordenes[co.equipo.id] == co.piloto.nombre;
                        if (leCeden && co.aPor < 0 && zona && !enColumnas && objetivo > delante.v + 0.3f)
                        {
                            co.aPor = iDelante;
                            co.adelantandoHasta = t + 5f;
                            co.ladoManiobra = delante.lateral >= 0f ? -1f : 1f;
                            Anotar(t, Tipo.OrdenDeEquipo, yo, iDelante, co.vueltas + 1, null);
                        }
                        if (co.aPor < 0 && !noAtacar && zona && !enColumnas && objetivo > delante.v + 0.5f)
                        {
                            float defensa = delante.piloto.defensa + EfectoRadio(delante.radio).defensa;
                            float habilidad = Math.Clamp(0.4f + co.piloto.ataque - 0.6f * defensa, 0.05f, 1.2f);
                            float empuje = Math.Clamp((objetivo - delante.v) / 4f, 0.2f, 1.5f);
                            // En la primera vuelta van en grupo y se arriesga más.
                            float primera = co.vueltas == 0 ? PrimeraVuelta : 1f;
                            if (azar.Prob(Intento * primera * habilidad * empuje * radio.intento * Paso))
                            {
                                co.aPor = iDelante;
                                co.adelantandoHasta = t + 4f;
                                co.ladoManiobra = delante.lateral >= 0f ? -1f : 1f;
                            }
                        }
                        if (cerca && co.aPor != iDelante)
                            vNueva = Math.Min(vNueva, delante.v + (hueco - HuecoMinimo) * 0.8f);
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
                        co.ritmoVuelta = co.ritmoBase + azar.Normal(0f, co.piloto.regularidad);
                        co.errorEn = azar.Prob(co.piloto.errores * EfectoRadio(co.radio).errores) ? co.vueltas * L + azar.Entre(0f, L) : -1f;

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
                if (azar.Prob(0.35f)) { Abandonar(co, t, "Accidente"); return; }
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
