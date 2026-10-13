using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// El juego: portada, Gran Premio suelto, Campeonato del Mundo como jefe de escudería y todo
    /// lo que lo rodea (pilotos y escuderías, calendario con sus circuitos, cómo se juega,
    /// opciones). La simulación está en el núcleo; la carrera la enseña el ReproductorCarrera.
    /// </summary>
    public class ControladorJuego : MonoBehaviour
    {
        enum Pantalla { Portada, Pilotos, Calendario, Ficha, ComoSeJuega, Opciones, ElegirEquipo, Temporada, Presentacion, ClasificacionEnPista, Clasificacion, Carrera, Resultado, FinTemporada, Mercado }
        enum Pestana { Pilotos, Constructores, UltimoGP, Calendario }

        [SerializeField] GameObject cochePrefab;
        [SerializeField] Material[] materialesEscuderia;
        [SerializeField] CatalogoDecorado catalogo;

        Pantalla pantalla = Pantalla.Portada;
        Pantalla volverDeFicha = Pantalla.Calendario;
        Pestana pestana = Pestana.Pilotos;
        int paginaAyuda;
        bool confirmarBorrado;
        Parrilla baseTemporada;
        Calendario calendario;
        Partida partida;
        bool enCampeonato;

        ConstructorPista.Materiales materiales;
        CamaraCircuito camara;
        Sonido sonido;
        GameObject pista;
        Transform[] coches;
        ReproductorCarrera reproductor;
        ReproductorClasificacion reproductorQ;
        DatosCircuito datos;
        Circuito circuito;
        CarreraDelCalendario gpActual;
        Clasificacion.Vuelta[] parrilla;
        Carrera carrera;
        bool porApuntar;
        bool lluvia;                          // el tiempo del GP en curso
        Favoritos favoritos;
        // En un GP suelto también se puede llevar el muro de una escudería (radio y orden de equipo).
        string equipoSuelto = "", prioridadSuelta = "";
        string EquipoRadio => enCampeonato ? partida?.equipoJugador : string.IsNullOrEmpty(equipoSuelto) ? null : equipoSuelto;
        string Prioridad { get => enCampeonato ? partida.prioritario ?? "" : prioridadSuelta; set { if (enCampeonato) partida.prioritario = value; else prioridadSuelta = value; } }
        const int SimulacionesFavoritos = 200;
        Vector2 scroll;
        string aviso = "";

        static string RutaPartida => Path.Combine(Application.persistentDataPath, "campeonato_2026.json");

        void Start()
        {
            baseTemporada = JsonUtility.FromJson<Parrilla>(Resources.Load<TextAsset>("Temporada/parrilla_2026").text);
            calendario = Calendario.Cargar();
            materiales = BancoPruebas.CargarMateriales();
            camara = Camera.main.GetComponent<CamaraCircuito>();
            if (camara == null) camara = Camera.main.gameObject.AddComponent<CamaraCircuito>();
            sonido = Camera.main.GetComponent<Sonido>();
            if (sonido == null) sonido = Camera.main.gameObject.AddComponent<Sonido>();
            // De fondo en la portada, un circuito al azar del calendario.
            var c = calendario.carreras[UnityEngine.Random.Range(0, calendario.carreras.Length)];
            MostrarCircuito(c.circuito, CamaraCircuito.Modo.Orbita);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-prueba-humo") >= 0) StartCoroutine(PruebaDeHumo());
        }

        // ── Prueba de humo ───────────────────────────────────────────────────

        /// <summary>
        /// Arrancando el ejecutable con -prueba-humo, el juego se recorre solo: un GP con lluvia en
        /// Spa (favoritos, clasificación en pista, parrilla, carrera y resultado) y otro en seco en
        /// Monza con gomas elegidas. Deja fotos y prueba_humo.txt en persistentDataPath y se cierra.
        /// Sirve para comprobar una build sin tocarla (lo que solo falla fuera del editor).
        /// </summary>
        System.Collections.IEnumerator PruebaDeHumo()
        {
            string carpeta = Application.persistentDataPath;
            var informe = new System.Text.StringBuilder();
            void Apuntar(string x) { informe.AppendLine($"{Time.realtimeSinceStartup:F1} s  {x}"); File.WriteAllText(Path.Combine(carpeta, "prueba_humo.txt"), informe.ToString()); }
            void Foto(string nombre) => ScreenCapture.CaptureScreenshot(Path.Combine(carpeta, $"prueba_{nombre}.png"));
            var finDeFotograma = new WaitForEndOfFrame();
            System.Collections.IEnumerator Esperar(System.Func<bool> hasta, float tope)
            {
                float t0 = Time.realtimeSinceStartup;
                while (!hasta() && Time.realtimeSinceStartup - t0 < tope) yield return null;
            }
            yield return new WaitForSeconds(2f);
            Apuntar("inicio");

            // 1. Lluvia en Spa, con el muro de Willians.
            int intentos = 0;
            do { GranPremioSuelto("be-1925"); intentos++; } while (!lluvia && intentos < 80);
            equipoSuelto = "willians";
            Apuntar($"GP de Bélgica, lluvia={lluvia} tras {intentos} intentos");
            yield return Esperar(() => favoritos != null && favoritos.Hechas >= favoritos.Total, 30f);
            Apuntar($"favoritos {favoritos?.Hechas}/{favoritos?.Total}: {string.Join(", ", favoritos.Orden().Take(3).Select(x => $"{x} {favoritos.Victoria(x) * 100f:F0} %"))}");
            yield return new WaitForSeconds(0.5f); Foto("1_presentacion"); yield return finDeFotograma; yield return null;
            VerClasificacion();
            reproductorQ.VelocidadIndice = 4;
            yield return new WaitForSeconds(5f); Foto("2_clasificacion_lluvia"); yield return finDeFotograma; yield return null;
            yield return Esperar(() => reproductorQ.Terminada, 90f);
            IrAParrilla();
            yield return new WaitForSeconds(1f); Foto("3_parrilla_lluvia"); yield return finDeFotograma; yield return null;
            VerCarrera();
            reproductor.VelocidadIndice = 4;
            yield return new WaitForSeconds(8f); Apuntar(Lluvia.Estado()); Foto("4_carrera_lluvia"); yield return finDeFotograma; yield return null;
            yield return Esperar(() => reproductor.Terminada, 150f);
            Apuntar($"carrera terminada={reproductor.Terminada}, sucesos {carrera.Sucesos.Count}, coche de seguridad {carrera.Sucesos.Exists(x => x.tipo == Carrera.Tipo.CocheSeguridad)}");
            IrAlResultado();
            yield return new WaitForSeconds(1f); Foto("5_resultado"); yield return finDeFotograma; yield return null;
            Continuar();

            // 2. Seco en Monza, con gomas blandas para Willians.
            intentos = 0;
            do { GranPremioSuelto("it-1922"); intentos++; } while (lluvia && intentos < 20);
            equipoSuelto = "willians";
            IrAParrilla();
            for (int i = 0; i < carrera.Coches.Length; i++)
                if (carrera.Coches[i].equipo.id == "willians") carrera.Gomas(i, Carrera.Compuesto.Blando);
            yield return new WaitForSeconds(1f); Foto("6_parrilla_seco"); yield return finDeFotograma; yield return null;
            VerCarrera();
            reproductor.VelocidadIndice = 2;
            yield return new WaitForSeconds(12f); Foto("7_carrera_seco"); yield return finDeFotograma; yield return null;
            Apuntar($"GP de Italia, lluvia={lluvia}, Willians con {string.Join("/", carrera.Coches.Where(x => x.equipo.id == "willians").Select(x => x.goma))}");
            Apuntar("FIN");
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        void Update()
        {
            // El reproductor (torre, sucesos, mandos) solo se ve en la pantalla de carrera.
            if (reproductor != null) reproductor.enabled = pantalla == Pantalla.Carrera;
            if (reproductorQ != null) reproductorQ.enabled = pantalla == Pantalla.ClasificacionEnPista;
            if (sonido == null) return;
            float? velocidad = null; Transform seguido = null;
            if (pantalla == Pantalla.Carrera && reproductor != null) { velocidad = reproductor.VelocidadSeguido; seguido = reproductor.Seguido; }
            if (pantalla == Pantalla.ClasificacionEnPista && reproductorQ != null) { velocidad = reproductorQ.VelocidadSeguido; seguido = reproductorQ.Seguido; }
            sonido.Motor(velocidad, camara.modo == CamaraCircuito.Modo.ABordo || camara.modo == CamaraCircuito.Modo.Detras);
            if (velocidad != null && camara.modo == CamaraCircuito.Modo.Television && seguido != null)
                sonido.Pasada(Vector3.Distance(camara.transform.position, seguido.position));
        }

        // ── Pista y coches ───────────────────────────────────────────────────

        void MostrarCircuito(string id, CamaraCircuito.Modo modo)
        {
            QuitarCoches();
            if (datos != null && datos.id == id && pista != null) { camara.objetivo = null; camara.modo = modo; return; }
            if (pista != null) { ConstructorPista.LiberarMallas(); Destroy(pista); }
            datos = DatosCircuito.Cargar(id);
            gpActual = calendario.carreras.First(c => c.circuito == id);
            var trazado = datos.CrearTrazado();
            circuito = new Circuito(trazado, datos.poleReferencia, datos.id, partida != null ? partida.semilla : 0) { Escapatoria = datos.Urbano ? 3.5f : 10f };
            pista = ConstructorPista.Construir(trazado, materiales, transform, datos.ancho, datos.Urbano, datos.Desierto);
            var entorno = new Entorno(trazado, datos, catalogo, materiales, pista.transform);
            if (catalogo != null) ConstructorDecorado.Construir(trazado, datos, catalogo, materiales, pista.transform, entorno);
            camara.objetivo = null;
            camara.modo = modo;
            camara.Preparar(trazado, pista.transform);
        }

        void QuitarCoches()
        {
            if (reproductor != null) Destroy(reproductor);
            reproductor = null;
            if (reproductorQ != null) Destroy(reproductorQ);
            reproductorQ = null;
            if (coches != null) foreach (var c in coches) if (c != null) Destroy(c.gameObject);
            coches = null;
        }

        void PonerParrilla()
        {
            QuitarCoches();
            coches = new Transform[parrilla.Length];
            for (int k = 0; k < parrilla.Length; k++)
                coches[k] = FabricaCoches.Crear(cochePrefab, ConstructorPista.Parrilla(circuito.Trazado, k),
                    FabricaCoches.Material(materialesEscuderia, parrilla[k].equipo.material), transform, parrilla[k].piloto.corto,
                    parrilla[k].piloto.casco ?? parrilla[k].equipo.color).transform;
            camara.modo = CamaraCircuito.Modo.Persecucion;
            camara.objetivo = coches[0];
        }

        // ── Flujo ────────────────────────────────────────────────────────────

        void GranPremioSuelto(string id)
        {
            enCampeonato = false;
            MostrarCircuito(id, CamaraCircuito.Modo.Orbita);
            // Azar ponderado: forma del día, afinidades y semilla nuevas en cada GP suelto.
            int semilla = UnityEngine.Random.Range(1, int.MaxValue);
            circuito = new Circuito(circuito.Trazado, datos.poleReferencia, datos.id, semilla) { Escapatoria = datos.Urbano ? 3.5f : 10f };
            var azar = new Azar(semilla);
            lluvia = Clima.Lluvia(datos.id, semilla);
            parrilla = Clasificacion.Simular(circuito, baseTemporada, azar, lluvia);
            // En vivo: avanza mientras se ve, para que la radio cuente (sin órdenes sale igual).
            carrera = Carrera.Simular(circuito, parrilla, Opciones.Vueltas, azar, null, enVivo: true, lluvia: lluvia);
            prioridadSuelta = "";
            CalcularFavoritos(baseTemporada, semilla, null);
            pantalla = Pantalla.Presentacion;
        }

        void NuevoCampeonato(string equipo)
        {
            partida = Campeonato.Nueva(baseTemporada, calendario.carreras.Select(c => c.circuito).ToArray(), equipo, UnityEngine.Random.Range(1, int.MaxValue));
            partida.vueltas = Opciones.Vueltas;
            Guardar();
            IrATemporada();
        }

        void IrATemporada()
        {
            enCampeonato = true;
            if (partida.objetivo <= 0) partida.objetivo = Campeonato.ObjetivoDe(partida);   // partidas de antes de la directiva
            if (partida.Terminada || partida.despedido) { pantalla = Pantalla.FinTemporada; return; }
            MostrarCircuito(partida.calendario[partida.ronda], CamaraCircuito.Modo.Orbita);
            pantalla = Pantalla.Temporada;
        }

        /// <summary>
        /// El GP de la ronda. Sin verlo, se simula y se apunta de una vez. Viéndolo, la carrera
        /// avanza mientras se ve (las órdenes por radio cuentan) y se apunta al acabar; la partida
        /// se guarda antes marcando el GP como empezado: si se sale a mitad, al volver se da por
        /// corrido sin órdenes (con la misma semilla), así que salir no sirve para repetirlo.
        /// </summary>
        void CorrerGPDelCampeonato(bool ver)
        {
            if (ver)
            {
                var ordenes = string.IsNullOrEmpty(partida.prioritario) ? null
                    : new Dictionary<string, string> { [partida.equipoJugador] = partida.prioritario };
                lluvia = Clima.Lluvia(circuito.Id, Campeonato.SemillaDe(partida, partida.ronda));
                CalcularFavoritos(Campeonato.ParrillaDelGP(partida, circuito), Campeonato.SemillaDe(partida, partida.ronda), ordenes);
            }
            (parrilla, carrera) = Campeonato.Simular(partida, circuito, 0, enVivo: ver);
            porApuntar = true;
            if (ver) { partida.gpEnCurso = true; Guardar(); pantalla = Pantalla.Presentacion; }
            else { Apuntar(); pantalla = Pantalla.Resultado; }
        }

        /// <summary>
        /// Los favoritos del GP, en segundo plano: 200 GP simulados con otras semillas (nada que
        /// ver con la carrera de verdad). Lo que sabría un apostante.
        /// </summary>
        void CalcularFavoritos(Parrilla p, int semilla, Dictionary<string, string> ordenes)
        {
            favoritos = new Favoritos(p, SimulacionesFavoritos);
            favoritos.Calcular(circuito, p, unchecked(semilla ^ 0x2545F491), ordenes, lluvia, enCampeonato && partida != null ? partida.Vueltas : Opciones.Vueltas);
        }

        /// <summary>Apunta el GP en curso (si falta): acaba de simularlo y guarda.</summary>
        void Apuntar()
        {
            if (!enCampeonato || !porApuntar) return;
            porApuntar = false;
            Campeonato.Registrar(partida, carrera);
            Guardar();
        }

        void IrAlResultado()
        {
            carrera.AlFinal();
            Apuntar();
            pantalla = Pantalla.Resultado;
        }

        /// <summary>
        /// La clasificación en pista: cada coche sale por turno a su vuelta lanzada y marca el
        /// tiempo que ya decidió la simulación. Al acabar (o al saltarla), la parrilla.
        /// </summary>
        void VerClasificacion()
        {
            QuitarCoches();
            var sesion = new SesionClasificacion(circuito, parrilla, new Azar(Mathf.RoundToInt(parrilla[0].segundos * 1000f)));
            coches = new Transform[sesion.Coches.Length];
            for (int k = 0; k < coches.Length; k++)
            {
                var v = sesion.Coches[k].vuelta;
                var go = FabricaCoches.Crear(cochePrefab, ConstructorPista.Situar(circuito.Trazado, -SesionClasificacion.Lanzada, 0f),
                    FabricaCoches.Material(materialesEscuderia, v.equipo.material), transform, v.piloto.corto, v.piloto.casco ?? v.equipo.color);
                go.SetActive(false);
                coches[k] = go.transform;
            }
            reproductorQ = gameObject.AddComponent<ReproductorClasificacion>();
            reproductorQ.VelocidadIndice = Opciones.Ritmo;
            camara.modo = CamaraCircuito.DeOpcion(Opciones.Camara);
            reproductorQ.Iniciar(sesion, coches, camara, enCampeonato ? partida.equipoJugador : null);
            Lluvia.Poner(lluvia, camara.GetComponent<Camera>());
            pantalla = Pantalla.ClasificacionEnPista;
        }

        void IrAParrilla()
        {
            PonerParrilla();
            pantalla = Pantalla.Clasificacion;
        }

        void VerCarrera()
        {
            if (coches == null) PonerParrilla();
            reproductor = gameObject.AddComponent<ReproductorCarrera>();
            reproductor.alturaMandos = 12f;
            reproductor.mostrarResultado = false;
            reproductor.VelocidadIndice = Opciones.Ritmo;
            reproductor.AlSalir = () => sonido.Salida();
            reproductor.favoritos = favoritos != null && favoritos.Hechas >= favoritos.Total ? favoritos : null;
            reproductor.Iniciar(carrera, parrilla, coches, camara);
            Lluvia.Poner(carrera.Lluvia, camara.GetComponent<Camera>());
            camara.modo = CamaraCircuito.DeOpcion(Opciones.Camara);
            pantalla = Pantalla.Carrera;
        }

        void Continuar()
        {
            Lluvia.Poner(false, camara.GetComponent<Camera>());
            QuitarCoches();
            if (enCampeonato) IrATemporada();
            else { pantalla = Pantalla.Calendario; camara.modo = CamaraCircuito.Modo.Orbita; camara.objetivo = null; }
        }

        void Guardar()
        {
            try { File.WriteAllText(RutaPartida, JsonUtility.ToJson(partida)); aviso = ""; }
            catch (Exception e) { aviso = "No se pudo guardar: " + e.Message; }
        }

        bool HayPartida => File.Exists(RutaPartida);

        Partida LeerPartida()
        {
            try { return HayPartida ? JsonUtility.FromJson<Partida>(File.ReadAllText(RutaPartida)) : null; }
            catch { return null; }
        }

        void Cargar()
        {
            partida = LeerPartida();
            if (partida == null) { aviso = "No se pudo cargar el campeonato."; return; }
            if (partida.gpEnCurso && !partida.Terminada)
            {
                // Un GP que se dejó a medias: se da por corrido sin órdenes de radio.
                var d = DatosCircuito.Cargar(partida.calendario[partida.ronda]);
                var c = new Circuito(d.CrearTrazado(), d.poleReferencia, d.id, partida.semilla) { Escapatoria = d.Urbano ? 3.5f : 10f };
                Campeonato.Registrar(partida, Campeonato.Simular(partida, c).carrera);
                Guardar();
                aviso = "El GP que dejaste a medias se ha dado por corrido, sin órdenes de radio.";
            }
            IrATemporada();
        }

        // ── Interfaz ─────────────────────────────────────────────────────────

        void OnGUI()
        {
            Interfaz.Empezar();
            switch (pantalla)
            {
                case Pantalla.Portada: Portada(); break;
                case Pantalla.Pilotos: PilotosYEscuderias(); break;
                case Pantalla.Calendario: PantallaCalendario(); break;
                case Pantalla.Ficha: Ficha(); break;
                case Pantalla.ComoSeJuega: ComoSeJuega(); break;
                case Pantalla.Opciones: PantallaOpciones(); break;
                case Pantalla.ElegirEquipo: ElegirEquipo(); break;
                case Pantalla.Temporada: Temporada(); break;
                case Pantalla.Presentacion: Presentacion(); break;
                case Pantalla.ClasificacionEnPista: ClasificacionEnPista(); break;
                case Pantalla.Clasificacion: PantallaClasificacion(); break;
                case Pantalla.Carrera: PantallaCarrera(); break;
                case Pantalla.Resultado: PantallaResultado(); break;
                case Pantalla.FinTemporada: FinTemporada(); break;
                case Pantalla.Mercado: Mercado(); break;
            }
            if (!string.IsNullOrEmpty(aviso)) GUI.Label(new Rect(20, Interfaz.Alto - 30, 800, 24), $"<color=#f88>{aviso}</color>", Interfaz.Normal);
        }

        float A => Interfaz.Ancho;
        float H => Interfaz.Alto;

        bool Volver(float x, float y, string texto = "Volver") => GUI.Button(new Rect(x, y, 140, 38), texto, Interfaz.Boton);

        void Cabecera(string titulo, string sub = null)
        {
            Interfaz.Rect(new Rect(0, 0, A, sub == null ? 78 : 98), new Color(0.03f, 0.035f, 0.05f, 0.94f));
            Interfaz.Rect(new Rect(0, sub == null ? 78 : 98, A, 3), Interfaz.Rojo);
            GUI.Label(new Rect(36, 18, A - 400, 44), titulo, Interfaz.Grande);
            if (sub != null) GUI.Label(new Rect(36, 58, A - 400, 30), sub, Interfaz.Normal);
        }

        // ── Portada ──────────────────────────────────────────────────────────

        void Portada()
        {
            // Velo oscuro a la izquierda para que se lea el menú sobre el circuito que gira.
            for (int i = 0; i < 12; i++)
                Interfaz.Rect(new Rect(i * 40, 0, 40, H), new Color(0.02f, 0.03f, 0.05f, 0.9f - i * 0.07f));
            Interfaz.Rect(new Rect(0, 0, 6, H), Interfaz.Rojo);

            GUI.Label(new Rect(44, 54, 760, 70), baseTemporada.campeonato.ToUpperInvariant(), Interfaz.Titulo);
            GUI.Label(new Rect(48, 122, 600, 30), $"<color=#e52222>MUNDIAL {calendario.temporada}</color>", Interfaz.Subtitulo);
            GUI.Label(new Rect(48, 150, 420, 40), $"{calendario.carreras.Length} Grandes Premios · {baseTemporada.equipos.Length} escuderías · {baseTemporada.pilotos.Length} pilotos", Interfaz.Pequeno);

            float y = 210, h = 46, sep = 7;
            var guardada = LeerPartida();
            if (guardada != null)
            {
                var e = guardada.equipos.First(x => x.id == guardada.equipoJugador);
                string donde = guardada.Terminada ? "temporada terminada" : $"ronda {guardada.ronda + 1} de {guardada.calendario.Length}";
                if (GUI.Button(new Rect(30, y, 400, h), $"Continuar campeonato   <size=13><color=#bbb>{e.nombre} · {donde}</color></size>", Interfaz.Menu)) Cargar();
                y += h + sep;
            }
            if (GUI.Button(new Rect(30, y, 400, h), "Nuevo campeonato", Interfaz.Menu)) { scroll = Vector2.zero; pantalla = Pantalla.ElegirEquipo; }
            y += h + sep;
            if (GUI.Button(new Rect(30, y, 400, h), "Gran Premio suelto", Interfaz.Menu)) { scroll = Vector2.zero; volverDeFicha = Pantalla.Calendario; pantalla = Pantalla.Calendario; }
            y += h + sep;
            if (GUI.Button(new Rect(30, y, 400, h), "Pilotos y escuderías", Interfaz.Menu)) { scroll = Vector2.zero; pantalla = Pantalla.Pilotos; }
            y += h + sep;
            if (GUI.Button(new Rect(30, y, 400, h), "Cómo se juega", Interfaz.Menu)) { paginaAyuda = 0; pantalla = Pantalla.ComoSeJuega; }
            y += h + sep;
            if (GUI.Button(new Rect(30, y, 400, h), "Opciones", Interfaz.Menu)) pantalla = Pantalla.Opciones;
            y += h + sep;
            if (GUI.Button(new Rect(30, y, 400, h), "Salir", Interfaz.Menu)) Application.Quit();

            GUI.Label(new Rect(48, H - 52, 420, 40), "Circuitos con su trazado real. Los resultados los decide una simulación con azar ponderado; el 3D los reproduce.", Interfaz.Pequeno);
            if (datos != null)
            {
                var r = new Rect(A - 360, H - 64, 340, 46);
                Interfaz.Rect(r, new Color(0, 0, 0, 0.5f));
                Interfaz.BanderaEn(new Rect(r.x + 12, r.y + 12, 33, 22), datos.bandera);
                GUI.Label(new Rect(r.x + 54, r.y + 6, 280, 20), gpActual.granPremio, Interfaz.Normal);
                GUI.Label(new Rect(r.x + 54, r.y + 24, 280, 20), datos.nombre, Interfaz.Pequeno);
            }
        }

        // ── Pilotos y escuderías ─────────────────────────────────────────────

        void PilotosYEscuderias()
        {
            Interfaz.Rect(new Rect(0, 0, A, H), new Color(0.03f, 0.035f, 0.05f, 0.88f));
            Cabecera("Pilotos y escuderías", $"Temporada {baseTemporada.temporada}: ordenadas por el ritmo de su coche al empezar el año (cómo acabaron 2025).");
            var equipos = baseTemporada.equipos.OrderBy(e => e.ritmo).ToArray();
            const float alto = 112f;
            var vista = new Rect(24, 110, A - 48, H - 170);
            scroll = GUI.BeginScrollView(vista, scroll, new Rect(0, 0, vista.width - 20, equipos.Length * alto));
            for (int i = 0; i < equipos.Length; i++)
            {
                var e = equipos[i];
                float y = i * alto;
                var fila = new Rect(0, y, vista.width - 20, alto - 8);
                Interfaz.Rect(fila, Interfaz.FondoClaro);
                Interfaz.Rect(new Rect(0, y, 8, alto - 8), Interfaz.ColorDe(e.color));
                GUI.Label(new Rect(22, y + 10, 220, 28), e.nombre, Interfaz.Subtitulo);
                GUI.Label(new Rect(22, y + 36, 220, 20), $"Motor {e.motor}", Interfaz.Pequeno);
                GUI.Label(new Rect(22, y + 54, 220, 20), $"{i + 1}.º en ritmo · {Caracter(e.potencia)}", Interfaz.Pequeno);
                Interfaz.Barra(new Rect(22, y + 76, 200, 14), "Fiabilidad", Mathf.InverseLerp(0.85f, 0.99f, e.fiabilidad), Interfaz.ColorDe(e.color));
                var pilotos = baseTemporada.pilotos.Where(p => p.equipo == e.id).ToArray();
                for (int k = 0; k < pilotos.Length; k++)
                    TarjetaPiloto(new Rect(260 + k * ((fila.width - 270) / 2f), y + 8, (fila.width - 290) / 2f, alto - 24), pilotos[k], e);
            }
            GUI.EndScrollView();
            if (Volver(24, H - 52)) pantalla = Pantalla.Portada;
        }

        void TarjetaPiloto(Rect r, Piloto p, Equipo e)
        {
            Interfaz.FotoPiloto(new Rect(r.x, r.y, 82, 74), p, e.color);
            Interfaz.BanderaEn(new Rect(r.x + 92, r.y + 2, 30, 20), p.pais);
            GUI.Label(new Rect(r.x + 128, r.y, r.width - 130, 24), p.nombre, Interfaz.Subtitulo);
            // Dos columnas de barras en lo que queda a la derecha de la foto.
            float x = r.x + 92, w = Mathf.Min(220f, (r.width - 92 - 14) / 2f);
            Interfaz.Barra(new Rect(x, r.y + 28, w, 13), "Ritmo", Mathf.InverseLerp(0.25f, -0.3f, p.ritmo), Interfaz.Rojo);
            Interfaz.Barra(new Rect(x, r.y + 43, w, 13), "Salida", p.salida, Interfaz.Gris);
            Interfaz.Barra(new Rect(x, r.y + 58, w, 13), "Ataque", p.ataque, Interfaz.Gris);
            {
                Interfaz.Barra(new Rect(x + w + 14, r.y + 28, w, 13), "Defensa", p.defensa, Interfaz.Gris);
                Interfaz.Barra(new Rect(x + w + 14, r.y + 43, w, 13), "Regularidad", Mathf.InverseLerp(0.18f, 0.07f, p.regularidad), Interfaz.Gris);
                Interfaz.Barra(new Rect(x + w + 14, r.y + 58, w, 13), "Sin errores", Mathf.InverseLerp(0.03f, 0.006f, p.errores), Interfaz.Gris);
            }
        }

        static string Caracter(float potencia) =>
            potencia > 0.3f ? "coche de recta" : potencia < -0.3f ? "coche de curva" : "coche equilibrado";

        static string TipoCircuito(Circuito c) =>
            c.FraccionPotencia > Circuito.FraccionPotenciaMedia + 0.06f ? "de potencia" :
            c.FraccionPotencia < Circuito.FraccionPotenciaMedia - 0.06f ? "de curva lenta" : "mixto";

        // ── Calendario y fichas ──────────────────────────────────────────────

        void PantallaCalendario()
        {
            Interfaz.Rect(new Rect(0, 0, A, H), new Color(0.03f, 0.035f, 0.05f, 0.88f));
            Cabecera($"Calendario {calendario.temporada}", "Elige un circuito para ver su ficha o correr un Gran Premio suelto.");
            int cols = 6;
            float w = (A - 48 - 20 - (cols - 1) * 10) / cols, h = w * 0.51f + 58;
            var vista = new Rect(24, 110, A - 48, H - 170);
            int filas = Mathf.CeilToInt(calendario.carreras.Length / (float)cols);
            scroll = GUI.BeginScrollView(vista, scroll, new Rect(0, 0, vista.width - 20, filas * (h + 10)));
            for (int i = 0; i < calendario.carreras.Length; i++)
            {
                var c = calendario.carreras[i];
                var r = new Rect((i % cols) * (w + 10), (i / cols) * (h + 10), w, h);
                Interfaz.Rect(r, Interfaz.FondoClaro);
                var plano = PlanoImagen(c.circuito);
                var zona = new Rect(r.x + 6, r.y + 6, r.width - 12, r.width * 0.51f - 12);
                Interfaz.Rect(zona, new Color(0.92f, 0.93f, 0.95f));
                if (plano != null) GUI.DrawTexture(zona, plano, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x + 8, zona.yMax + 4, r.width - 50, 20), $"<b>{c.ronda}.</b> {Corto(c.granPremio)}", Interfaz.Celda);
                GUI.Label(new Rect(r.x + 8, zona.yMax + 24, r.width - 50, 20), Fecha(c.fecha), Interfaz.Pequeno);
                Interfaz.BanderaEn(new Rect(r.xMax - 40, zona.yMax + 8, 30, 20), BanderaDe(c.circuito));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { MostrarCircuito(c.circuito, CamaraCircuito.Modo.Orbita); pantalla = Pantalla.Ficha; }
            }
            GUI.EndScrollView();
            if (Volver(24, H - 52)) pantalla = Pantalla.Portada;
        }

        // Plano y bandera de cada circuito (leer el JSON entero en cada fotograma sería un despilfarro).
        static readonly System.Collections.Generic.Dictionary<string, (string plano, string bandera)> cacheCircuitos =
            new System.Collections.Generic.Dictionary<string, (string, string)>();

        static (string plano, string bandera) Info(string id)
        {
            if (cacheCircuitos.TryGetValue(id, out var i)) return i;
            var d = DatosCircuito.Cargar(id);
            string plano = string.IsNullOrEmpty(d.plano) ? (id == "bh-2002" ? "bahrein" : "") : d.plano;
            return cacheCircuitos[id] = (plano, d.bandera);
        }


        /// <summary>
        /// El plano del circuito, dibujado del trazado GPS (los oficiales de la F1 no se pueden
        /// redistribuir): línea negra, salida en rojo y flecha con el sentido de la carrera.
        /// </summary>
        static Texture2D PlanoImagen(string id) => Interfaz.PlanoDibujado(id, () => DatosCircuito.Cargar(id).CrearTrazado());
        static string BanderaDe(string id) => Info(id).bandera;

        static string Corto(string gp) => gp.Replace("Gran Premio de la ", "").Replace("Gran Premio de los ", "").Replace("Gran Premio de ", "");

        int Curvas()
        {
            // Curvas aproximadas: tramos con radio por debajo de 250 m separados al menos 60 m.
            var t = circuito.Trazado; int n = 0; bool dentro = false; float ultima = -1000f;
            for (int i = 0; i < t.Cantidad; i++)
            {
                bool curva = Mathf.Abs(t.Curvatura(i, 4)) > 1f / 250f;
                if (curva && !dentro && t.DistanciaDe(i) - ultima > 60f) { n++; ultima = t.DistanciaDe(i); }
                dentro = curva;
            }
            return n;
        }

        void Ficha()
        {
            var r = new Rect(24, 24, 560, H - 48);
            Interfaz.Fondo_(r);
            Interfaz.BanderaEn(new Rect(r.x + 22, r.y + 22, 42, 28), datos.bandera);
            GUI.Label(new Rect(r.x + 76, r.y + 16, r.width - 90, 36), Corto(gpActual.granPremio), Interfaz.Grande);
            GUI.Label(new Rect(r.x + 76, r.y + 50, r.width - 90, 20), $"Ronda {gpActual.ronda} · {Fecha(gpActual.fecha)} · {datos.localidad}", Interfaz.Pequeno);
            var plano = PlanoImagen(datos.id);
            var zona = new Rect(r.x + 22, r.y + 84, r.width - 44, (r.width - 44) * 0.51f);
            Interfaz.Rect(zona, new Color(0.92f, 0.93f, 0.95f));
            if (plano != null) GUI.DrawTexture(zona, plano, ScaleMode.ScaleToFit);
            else GUI.Label(new Rect(zona.x + 10, zona.y + 10, 300, 20), "<color=#333>(sin plano oficial)</color>", Interfaz.Normal);
            float y = zona.yMax + 14;
            void Dato(string k, string v) { GUI.Label(new Rect(r.x + 24, y, 170, 22), k, Interfaz.Pequeno); GUI.Label(new Rect(r.x + 190, y, r.width - 200, 22), v, Interfaz.Normal); y += 23; }
            Dato("Circuito", datos.nombre);
            Dato("Longitud", $"{datos.longitudMedida / 1000f:F3} km · {Curvas()} curvas aprox. · sentido {datos.giro}");
            Dato("Carácter", $"{(datos.Urbano ? "urbano" : "permanente")}, {TipoCircuito(circuito)} · ancho {datos.ancho:F0} m");
            Dato("Referencia", datos.poleReferencia > 0 ? $"pole 2025: {Crono(datos.poleReferencia)}" : "sin pole de referencia (circuito nuevo)");
            Dato("Carrera", $"{(enCampeonato && partida != null ? partida.Vueltas : Opciones.Vueltas)} vueltas, tras una clasificación a una vuelta");
            GUILayout.BeginArea(new Rect(r.x + 22, r.yMax - 56, r.width - 44, 44));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Volver", Interfaz.Boton, GUILayout.Width(120))) pantalla = volverDeFicha;
            if (GUILayout.Button(camara.modo == CamaraCircuito.Modo.Orbita ? "Vista general" : "Girar alrededor", Interfaz.Boton))
                camara.modo = camara.modo == CamaraCircuito.Modo.Orbita ? CamaraCircuito.Modo.General : CamaraCircuito.Modo.Orbita;
            if (volverDeFicha == Pantalla.Calendario && GUILayout.Button("<b>Correr un Gran Premio aquí</b>", Interfaz.BotonSel)) GranPremioSuelto(datos.id);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ── Cómo se juega ────────────────────────────────────────────────────

        static readonly (string titulo, string texto)[] Ayuda =
        {
            ("El juego",
             "Un Mundial de {campeonato} de 2026 con los 24 circuitos reales: su trazado sale del GPS y su longitud cuadra con la oficial.\n\n" +
             "Cada Gran Premio tiene una <b>clasificación a una vuelta</b> (decide la parrilla) y una <b>carrera de 3, 5 o 10 vueltas</b> (en Opciones) con salida parada.\n\n" +
             "Los resultados los decide una <b>simulación</b>, no los dibujos: el ritmo de cada coche y piloto, su forma del día, lo bien que le sienta el circuito, la salida, el rebufo, los adelantamientos (solo donde la pista lo permite), los errores y las averías. Todo con <b>azar ponderado</b>: el mejor gana más a menudo, pero no siempre.\n\n" +
             "El 3D reproduce exactamente lo que ha pasado en la simulación."),
            ("Gran Premio suelto",
             "Eliges un circuito del calendario y se corre un Gran Premio con la parrilla de 2026. Cada vez sale distinto: la forma del día y las afinidades de los coches con el circuito se sortean de nuevo.\n\n" +
             "Es la manera de ver un circuito en acción, o de probar quién gana dónde."),
            ("Campeonato: eres jefe de escudería",
             "Eliges una escudería y disputas las 24 carreras. Antes de cada Gran Premio decides:\n\n" +
             "• <b>Desarrollo</b>: hasta 10 puntos por ronda entre motor (mejor en recta) y aerodinámica (mejor en curva). Cada punto cuesta dinero, y rinde más cuanto más atrás esté tu coche. Los rivales también desarrollan.\n\n" +
             "• <b>Carga aerodinámica</b> del GP: baja en circuitos de potencia (Monza), alta en los de curva lenta (Mónaco). Acertar da ventaja; fallar la quita.\n\n" +
             "• <b>Modo de motor</b>: el agresivo da ritmo pero desgasta, y el desgaste multiplica las averías en las carreras siguientes. Tienes 4 motores por temporada; desde el quinto, 5 puestos de sanción en parrilla.\n\n" +
             "• <b>Órdenes de equipo</b>: carrera libre o prioridad para uno de tus pilotos (su compañero no le ataca y le deja pasar).\n\n" +
             "• <b>La directiva</b> te pide un puesto de constructores. Su confianza sube y baja con los resultados; acabar el año sin ella es el despido. Cumplir trae una prima.\n\n" +
             "• <b>Dinero</b>: cobras en cada GP (un fijo y una parte por punto) y lo gastas en desarrollo, motores y fichajes.\n\n" +
             "• Al acabar el año, la <b>siguiente temporada</b>: los coches se acercan (el tuyo conserva más de lo desarrollado) y hay <b>mercado de pilotos</b>.\n\n" +
             "La partida se guarda sola tras cada carrera."),
            ("Puntos y clasificaciones",
             "Puntúan los diez primeros: 25, 18, 15, 12, 10, 8, 6, 4, 2 y 1. Desde 2025 no hay punto por la vuelta rápida.\n\n" +
             "Hay dos clasificaciones: <b>pilotos</b> y <b>constructores</b> (la suma de los dos pilotos de cada escudería). Los empates se deshacen por número de victorias, luego de segundos puestos, etc.\n\n" +
             "Con los mejores coches se lucha por el título; con los de atrás, por ser el mejor del resto."),
            ("Durante la carrera",
             "• <b>Cámara</b>: persecución, detrás del coche, a bordo, televisión y vista general. La <b>automática</b> va a la pelea más apretada y, si hay un incidente, a él.\n\n" +
             "• En la <b>torre</b>, pulsa un piloto para seguirlo con la cámara de detrás del coche; «Cámara automática» vuelve a la realización. La letra de color es su goma (B blando, M medio, D duro, LL lluvia).\n\n" +
             "• Arriba, quién sale en cámara; abajo, la pelea que se está viendo y el comentarista; los rótulos grandes avisan de abandonos, salidas de pista y del coche de seguridad.\n\n" +
             "• <b>Ritmo</b> de x0,5 a x8, y puedes saltar al resultado en cualquier momento."),
            ("Radio del muro",
             "Si llevas una escudería, en carrera das órdenes a cada piloto. Para cada uno ves su puesto, cuánto ha gastado de gomas, quién lleva delante y detrás (y si tiene DRS), lo que aconseja el ingeniero y, bajo los botones, lo que hace la orden puesta.\n\n" +
             "<b>Ejemplo 1.</b> Vuelta 2: Sianz a 0,6 s de Alanso, gomas al 40 %. Ataca: con DRS y el doble de intentos, lo normal es que se tire en la siguiente frenada. Si Alanso tiene buena defensa puede fallar y perder un poco; si va con gomas al 85 %, mejor esperar a la última vuelta.\n\n" +
             "<b>Ejemplo 2.</b> Última vuelta: Albin 6.º con Gasli a 0,4 s detrás. Defiende: a Gasli le cuesta mucho más ganarle la frenada y Albin apenas pierde ritmo.\n\n" +
             "<b>Ejemplo 3.</b> Narris va solo, a 3 s del de delante y 4 s del de detrás. Ahorra: pierde unas décimas por vuelta que no le cuestan el puesto, y llega con gomas para atacar al final (y, en el campeonato, cuida el motor).\n\n" +
             "• <b>Ataca</b>: más ritmo y el doble de intentos de adelantar; más errores y gasta gomas (y motor en el campeonato). Con alguien a menos de 1 s delante o en la última vuelta.\n\n" +
             "• <b>Normal</b>: para cuando no pasa nada cerca.\n\n" +
             "• <b>Defiende</b>: cuesta mucho más ganarte la frenada. Con alguien más rápido justo detrás, sobre todo al final.\n\n" +
             "• <b>Ahorra</b>: algo más lento, casi sin errores y cuidando gomas y motor. Si vas solo, tras el coche de seguridad, o para guardar gomas y atacar en la última vuelta.\n\n" +
             "• <b>Orden de equipo</b>: «Manda» a uno y su compañero no le ataca y le deja pasar."),
        };

        static readonly string[] Pestanas = { "El juego", "GP suelto", "Campeonato", "Puntos", "En carrera", "Radio" };

        void ComoSeJuega()
        {
            var r = new Rect(A / 2 - 400, 40, 800, H - 80);
            Interfaz.Fondo_(r, 0.95f);
            GUI.Label(new Rect(r.x + 30, r.y + 18, 600, 36), "Cómo se juega", Interfaz.Grande);
            for (int i = 0; i < Ayuda.Length; i++)
                if (GUI.Button(new Rect(r.x + 30 + i * (r.width - 60) / Ayuda.Length, r.y + 64, (r.width - 60) / Ayuda.Length - 6, 34), Pestanas[i], i == paginaAyuda ? Interfaz.BotonSel : Interfaz.Boton))
                    paginaAyuda = i;
            GUI.Label(new Rect(r.x + 30, r.y + 116, r.width - 60, 30), Ayuda[paginaAyuda].titulo, Interfaz.Subtitulo);
            GUI.Label(new Rect(r.x + 30, r.y + 150, r.width - 60, r.height - 220), Ayuda[paginaAyuda].texto.Replace("{campeonato}", baseTemporada.campeonato), Interfaz.Normal);
            if (Volver(r.x + 30, r.yMax - 54)) pantalla = Pantalla.Portada;
            if (paginaAyuda < Ayuda.Length - 1 && GUI.Button(new Rect(r.xMax - 170, r.yMax - 54, 140, 38), "Siguiente", Interfaz.BotonSel)) paginaAyuda++;
        }

        // ── Opciones ─────────────────────────────────────────────────────────

        void PantallaOpciones()
        {
            var r = new Rect(A / 2 - 380, 50, 760, H - 100);
            Interfaz.Fondo_(r, 0.95f);
            GUI.Label(new Rect(r.x + 30, r.y + 18, 600, 36), "Opciones", Interfaz.Grande);
            float y = r.y + 80;
            GUI.Label(new Rect(r.x + 30, y, 220, 24), "Sonido", Interfaz.Subtitulo);
            if (GUI.Button(new Rect(r.x + 260, y - 4, 140, 34), Opciones.Sonido ? "Activado" : "Silenciado", Opciones.Sonido ? Interfaz.BotonSel : Interfaz.Boton)) Opciones.Sonido = !Opciones.Sonido;
            y += 48;
            GUI.Label(new Rect(r.x + 30, y, 220, 24), "Volumen", Interfaz.Subtitulo);
            GUI.enabled = Opciones.Sonido;
            float vol = GUI.HorizontalSlider(new Rect(r.x + 260, y + 8, 300, 20), Opciones.Volumen, 0f, 1f);
            if (Mathf.Abs(vol - Opciones.Volumen) > 0.005f) Opciones.Volumen = vol;
            GUI.Label(new Rect(r.x + 575, y, 80, 24), $"{Opciones.Volumen * 100:F0} %", Interfaz.Normal);
            GUI.enabled = true;
            y += 48;
            GUI.Label(new Rect(r.x + 30, y, 220, 24), "Ritmo al empezar", Interfaz.Subtitulo);
            string[] ritmos = { "x0,5", "x1", "x2", "x4", "x8" };
            for (int i = 0; i < ritmos.Length; i++)
                if (GUI.Button(new Rect(r.x + 260 + i * 78, y - 4, 72, 34), ritmos[i], Opciones.Ritmo == i ? Interfaz.BotonSel : Interfaz.Boton)) Opciones.Ritmo = i;
            y += 48;
            GUI.Label(new Rect(r.x + 30, y, 220, 24), "Cámara al empezar", Interfaz.Subtitulo);
            string[] camaras = { "Persecución", "Detrás", "A bordo", "Televisión" };
            int[] valores = { 1, 5, 3, 2 };
            for (int i = 0; i < camaras.Length; i++)
                if (GUI.Button(new Rect(r.x + 260 + i * 108, y - 4, 102, 34), camaras[i], Opciones.Camara == valores[i] ? Interfaz.BotonSel : Interfaz.Boton)) Opciones.Camara = valores[i];
            y += 48;
            GUI.Label(new Rect(r.x + 30, y, 220, 24), "Vueltas de carrera", Interfaz.Subtitulo);
            foreach (var (k, n) in new[] { (0, 3), (1, 5), (2, 10) })
                if (GUI.Button(new Rect(r.x + 260 + k * 108, y - 4, 102, 34), $"{n} vueltas", Opciones.Vueltas == n ? Interfaz.BotonSel : Interfaz.Boton)) Opciones.Vueltas = n;
            GUI.Label(new Rect(r.x + 590, y, 150, 30), "<color=#9aa>el campeonato, al crearlo</color>", Interfaz.Pequeno);
            y += 48;
            GUI.Label(new Rect(r.x + 30, y, 220, 24), "Campeonato guardado", Interfaz.Subtitulo);
            GUI.enabled = HayPartida;
            if (!confirmarBorrado && GUI.Button(new Rect(r.x + 260, y - 4, 200, 34), "Borrar campeonato", Interfaz.Boton)) confirmarBorrado = true;
            GUI.enabled = true;
            if (confirmarBorrado)
            {
                GUI.Label(new Rect(r.x + 260, y, 180, 24), "¿Seguro? No se puede deshacer.", Interfaz.Pequeno);
                if (GUI.Button(new Rect(r.x + 450, y - 4, 90, 34), "Borrar", Interfaz.BotonSel)) { try { File.Delete(RutaPartida); } catch { } confirmarBorrado = false; }
                if (GUI.Button(new Rect(r.x + 548, y - 4, 90, 34), "No", Interfaz.Boton)) confirmarBorrado = false;
            }
            y += 60;
            GUI.Label(new Rect(r.x + 30, y, 400, 24), "Créditos", Interfaz.Subtitulo);
            GUI.Label(new Rect(r.x + 30, y + 30, r.width - 60, 230),
                "Trazados GPS de los circuitos: github.com/bacinger/f1-circuits (licencia MIT).\n" +
                "Decorado: kits de Kenney (kenney.nl, CC0). Tipografía: TeX Gyre Adventor (GUST Font License).\n" +
                "Sonidos: generados por síntesis para el juego. Planos: dibujados del trazado GPS. Banderas: flagpedia.net (dominio público).\n" +
                "Fotos de los pilotos: Wikimedia Commons (CC BY, CC BY-SA, CC0 y OGL), de Yu Chu Chin, Liauzh, Steffen Prößdorf, " +
                "Mb2437, Raph_PH, Kuyper, Gabriel Hutchinson, Lauren Hurley (No 10 Downing Street), LucaFazPhoto, United Autosports, " +
                "Rrtb505050, Marc Alvarado, Morio y XaviYuahanda; detalle en Tools/Pilotos/CREDITOS.txt.\n" +
                "Ritmos de los pilotos y coches: estimados a partir de la temporada 2025; los colores de los cascos son aproximados.\n" +
                "Juego de aficionado, sin ánimo de lucro y no oficial: no tiene relación con la Fórmula 1, la FIA ni las escuderías.",
                Interfaz.Pequeno);
            if (Volver(r.x + 30, r.yMax - 54)) { confirmarBorrado = false; pantalla = Pantalla.Portada; }
        }

        // ── Elegir escudería ─────────────────────────────────────────────────

        void ElegirEquipo()
        {
            Interfaz.Rect(new Rect(0, 0, A, H), new Color(0.03f, 0.035f, 0.05f, 0.88f));
            Cabecera("Nuevo campeonato: elige tu escudería", "Con los mejores coches se lucha por el título; con los de atrás, por ser el mejor del resto.");
            var equipos = baseTemporada.equipos.OrderBy(e => e.ritmo).ToArray();
            const float alto = 96f;
            var vista = new Rect(24, 110, A - 48, H - 170);
            scroll = GUI.BeginScrollView(vista, scroll, new Rect(0, 0, vista.width - 20, equipos.Length * alto));
            for (int i = 0; i < equipos.Length; i++)
            {
                var e = equipos[i];
                var fila = new Rect(0, i * alto, vista.width - 20, alto - 8);
                Interfaz.Rect(fila, Interfaz.FondoClaro);
                Interfaz.Rect(new Rect(0, fila.y, 8, fila.height), Interfaz.ColorDe(e.color));
                GUI.Label(new Rect(22, fila.y + 12, 260, 28), e.nombre, Interfaz.Subtitulo);
                GUI.Label(new Rect(22, fila.y + 40, 270, 20), $"{i + 1}.º en ritmo · {Caracter(e.potencia)}", Interfaz.Pequeno);
                string reto = i < 2 ? "Favorito al título" : i < 4 ? "Aspirante a victorias" : i < 7 ? "Luchar por los puntos" : "Sobrevivir al fondo de la parrilla";
                GUI.Label(new Rect(22, fila.y + 60, 270, 20), $"<color=#e55>{reto}</color> <color=#9aa>· la directiva pide {i + 1}.º</color>", Interfaz.Pequeno);
                var pilotos = baseTemporada.pilotos.Where(p => p.equipo == e.id).ToArray();
                for (int k = 0; k < pilotos.Length; k++)
                {
                    float x = 300 + k * 260;
                    Interfaz.FotoPiloto(new Rect(x, fila.y + 8, 76, 70), pilotos[k], e.color);
                    Interfaz.BanderaEn(new Rect(x + 86, fila.y + 14, 30, 20), pilotos[k].pais);
                    GUI.Label(new Rect(x + 86, fila.y + 38, 170, 40), pilotos[k].nombre, Interfaz.Normal);
                }
                if (GUI.Button(new Rect(fila.xMax - 170, fila.y + 26, 150, 38), "Elegir", Interfaz.BotonSel)) NuevoCampeonato(e.id);
            }
            GUI.EndScrollView();
            if (Volver(24, H - 52)) pantalla = Pantalla.Portada;
        }

        // ── Temporada: decisiones del jefe de escudería ─────────────────────

        void Temporada()
        {
            var p = partida;
            var gp = calendario.carreras[p.ronda];
            var mio = p.MiEquipo;
            int rango = p.equipos.OrderBy(e => e.ritmo).ToList().FindIndex(e => e.id == mio.id) + 1;
            var tp = Campeonato.Pilotos(p);
            var tc = Campeonato.Constructores(p);

            var r = new Rect(14, 14, 470, H - 28);
            Interfaz.Fondo_(r);
            Interfaz.Rect(new Rect(r.x + 4, r.y, r.width - 4, 6), Interfaz.ColorDe(mio.color));
            GUI.Label(new Rect(r.x + 20, r.y + 14, 300, 32), mio.nombre, Interfaz.Grande);
            GUI.Label(new Rect(r.x + 250, r.y + 16, 200, 40), $"{tc.FindIndex(x => x.equipo == mio.id) + 1}.º en constructores · {tc.First(x => x.equipo == mio.id).puntos} pts\ncoche {rango}.º en ritmo · {Caracter(mio.potencia)}", Interfaz.Pequeno);
            var mis = p.pilotos.Where(x => x.equipo == mio.id).ToArray();
            for (int k = 0; k < mis.Length; k++)
            {
                float x = r.x + 20 + k * 222;
                Interfaz.FotoPiloto(new Rect(x, r.y + 72, 54, 50), mis[k], mio.color);
                int pos = tp.FindIndex(t => t.nombre == mis[k].nombre) + 1;
                GUI.Label(new Rect(x + 62, r.y + 74, 160, 22), mis[k].corto, Interfaz.Normal);
                GUI.Label(new Rect(x + 62, r.y + 96, 160, 22), $"{pos}.º · {tp[pos - 1].puntos} pts", Interfaz.Pequeno);
            }

            float y = r.y + 134;
            Interfaz.Rect(new Rect(r.x + 20, y, r.width - 40, 1), new Color(1, 1, 1, 0.12f));
            y += 8;
            Interfaz.BanderaEn(new Rect(r.x + 20, y + 3, 30, 20), datos.bandera);
            GUI.Label(new Rect(r.x + 58, y, 330, 24), $"Ronda {p.ronda + 1}/{p.calendario.Length} · {Corto(gp.granPremio)} · {Fecha(gp.fecha)}", Interfaz.Subtitulo);
            GUI.Label(new Rect(r.x + 58, y + 24, 330, 20), $"{datos.nombre} · {datos.longitudMedida / 1000f:F3} km · circuito {TipoCircuito(circuito)}", Interfaz.Pequeno);
            if (GUI.Button(new Rect(r.xMax - 90, y, 70, 26), "Ficha", Interfaz.Boton)) { volverDeFicha = Pantalla.Temporada; pantalla = Pantalla.Ficha; }
            y += 54;

            GUI.Label(new Rect(r.x + 20, y, 440, 20), $"<b>Desarrollo</b> · quedan <b>{p.puntosDesarrollo}</b> puntos · {Campeonato.CostePunto:F1} M c/u <color=#9aa>· rinde x{Campeonato.Rendimiento(p):F1}</color>", Interfaz.Normal);
            y += 24;
            GUI.enabled = p.puntosDesarrollo > 0 && p.presupuesto >= Campeonato.CostePunto;
            if (GUI.Button(new Rect(r.x + 20, y, 130, 32), "+1 motor", Interfaz.Boton)) Campeonato.Invertir(p, true);
            if (GUI.Button(new Rect(r.x + 156, y, 130, 32), "+1 aerodinámica", Interfaz.Boton)) Campeonato.Invertir(p, false);
            if (GUI.Button(new Rect(r.x + 292, y, 158, 32), "Gastar a medias", Interfaz.Boton)) { int n = 0; while (Campeonato.Invertir(p, n++ % 2 == 0)) { } }
            GUI.enabled = true;
            y += 44;

            GUI.Label(new Rect(r.x + 20, y, 430, 20), "<b>Carga aerodinámica</b> para este GP", Interfaz.Normal);
            y += 24;
            Opcion(new Rect(r.x + 20, y, 140, 32), ref p.carga, Campeonato.CargaBaja, "Baja (recta)");
            Opcion(new Rect(r.x + 166, y, 120, 32), ref p.carga, Campeonato.CargaMedia, "Media");
            Opcion(new Rect(r.x + 292, y, 158, 32), ref p.carga, Campeonato.CargaAlta, "Alta (curva)");
            y += 44;

            float mult = Campeonato.MultiplicadorDesgaste(p.desgasteMotor);
            GUI.Label(new Rect(r.x + 20, y, 430, 20), $"<b>Motor</b> · desgaste {p.desgasteMotor:F0} (averías x{mult:F1}) · motores {p.motoresUsados} de {Campeonato.MotoresGratis}", Interfaz.Normal);
            y += 24;
            Opcion(new Rect(r.x + 20, y, 140, 32), ref p.modoMotor, Campeonato.ModoConservador, "Conservador");
            Opcion(new Rect(r.x + 166, y, 120, 32), ref p.modoMotor, Campeonato.ModoNormal, "Normal");
            Opcion(new Rect(r.x + 292, y, 158, 32), ref p.modoMotor, Campeonato.ModoAgresivo, "Agresivo");
            y += 38;
            GUI.enabled = !p.motorNuevoEnEsteGP && p.presupuesto >= Campeonato.CosteMotor;
            string coste = $" · {Campeonato.CosteMotor:F0} M" + (p.motoresUsados >= Campeonato.MotoresGratis ? $", −{Campeonato.PenalizacionMotor} puestos en parrilla" : ", sin sanción");
            if (GUI.Button(new Rect(r.x + 20, y, 430, 30), p.motorNuevoEnEsteGP ? "Motor nuevo montado para este GP" : "Montar motor nuevo" + coste, Interfaz.Boton)) Campeonato.MotorNuevo(p);
            GUI.enabled = true;
            y += 42;

            GUI.Label(new Rect(r.x + 20, y, 430, 20), "<b>Órdenes de equipo</b>", Interfaz.Normal);
            y += 24;
            Opcion(new Rect(r.x + 20, y, 130, 32), ref p.prioritario, "", "Carrera libre");
            for (int k = 0; k < mis.Length; k++)
                Opcion(new Rect(r.x + 156 + k * 150, y, 144, 32), ref p.prioritario, mis[k].nombre, "Prioridad " + mis[k].corto.Split(' ').Last());

            if (GUI.Button(new Rect(r.x + 20, r.yMax - 98, 430, 44), $"Ir al {gp.granPremio}", Interfaz.BotonGrande)) CorrerGPDelCampeonato(true);
            if (GUI.Button(new Rect(r.x + 20, r.yMax - 48, 212, 34), "Simular sin verlo", Interfaz.Boton)) CorrerGPDelCampeonato(false);
            if (GUI.Button(new Rect(r.x + 238, r.yMax - 48, 212, 34), "Guardar y salir", Interfaz.Boton)) { Guardar(); pantalla = Pantalla.Portada; }

            Tablas(new Rect(A - 470, 14, 456, H - 28));
            PanelDirectiva(new Rect(r.xMax + 14, 14, A - 470 - 28 - r.xMax, 330));
        }

        /// <summary>La directiva (objetivo, confianza, su opinión) y el dinero.</summary>
        void PanelDirectiva(Rect r)
        {
            if (r.width < 220f) return;
            var p = partida;
            Interfaz.Fondo_(r, 0.9f);
            int pos = Campeonato.PuestoConstructores(p);
            GUI.Label(new Rect(r.x + 16, r.y + 10, r.width - 24, 24), $"DIRECTIVA · TEMPORADA {p.temporada}", Interfaz.Subtitulo);
            GUI.Label(new Rect(r.x + 16, r.y + 38, r.width - 24, 22), $"Objetivo <b>{p.objetivo}.º</b> · ahora <b>{pos}.º</b>", Interfaz.Normal);
            GUI.Label(new Rect(r.x + 16, r.y + 64, 90, 20), "Confianza", Interfaz.Pequeno);
            var barra = new Rect(r.x + 96, r.y + 68, r.width - 150, 10);
            Interfaz.Rect(barra, new Color(1, 1, 1, 0.08f));
            Color c = p.confianza >= 60f ? new Color(0.35f, 0.8f, 0.4f) : p.confianza >= 30f ? new Color(0.95f, 0.75f, 0.2f) : Interfaz.Rojo;
            Interfaz.Rect(new Rect(barra.x, barra.y, barra.width * p.confianza / 100f, barra.height), c);
            GUI.Label(new Rect(barra.xMax + 8, r.y + 62, 50, 20), $"{p.confianza:F0}", Interfaz.Normal);
            GUI.Label(new Rect(r.x + 16, r.y + 90, r.width - 28, 70), $"<i>{p.mensajeDirectiva}</i>", Interfaz.Normal);

            float y = r.y + 168;
            Interfaz.Rect(new Rect(r.x + 16, y, r.width - 32, 1), new Color(1, 1, 1, 0.12f));
            GUI.Label(new Rect(r.x + 16, y + 10, r.width - 24, 24), $"PRESUPUESTO  <b>{p.presupuesto:F1} M</b>", Interfaz.Subtitulo);
            GUI.Label(new Rect(r.x + 16, y + 38, r.width - 28, 60),
                (p.ronda > 0 ? $"Último GP: +{p.ingresosUltimoGP:F1} M. " : "") +
                $"Cada GP: {Campeonato.IngresoBase:F1} M fijos y {Campeonato.IngresoPorPunto:F2} M por punto. Cumplir el objetivo del año trae una prima.", Interfaz.Pequeno);
            if (p.ronda == 0 && GUI.Button(new Rect(r.x + 16, r.yMax - 46, r.width - 32, 34), "Mercado de pilotos", Interfaz.Boton))
            { scroll = Vector2.zero; mercadoMio = ""; pantalla = Pantalla.Mercado; }
        }

        // ── Mercado de pilotos (antes del primer GP de cada temporada) ─────────

        string mercadoMio = "", mercadoAviso = "";

        void Mercado()
        {
            var p = partida;
            Interfaz.Rect(new Rect(0, 0, A, H), new Color(0.03f, 0.035f, 0.05f, 0.9f));
            Cabecera("Mercado de pilotos", $"Temporada {p.temporada} · presupuesto {p.presupuesto:F1} M. Elige a cuál de tus pilotos sustituyes y a quién fichas: el tuyo se va a su equipo.");
            var mios = p.pilotos.Where(x => x.equipo == p.equipoJugador).ToArray();
            var mio = p.MiEquipo;
            for (int k = 0; k < mios.Length; k++)
            {
                var caja = new Rect(24 + k * 300, 100, 290, 84);
                bool sel = mercadoMio == mios[k].nombre;
                Interfaz.Rect(caja, sel ? new Color(0.5f, 0.08f, 0.1f, 0.9f) : Interfaz.FondoClaro);
                Interfaz.FotoPiloto(new Rect(caja.x + 8, caja.y + 8, 70, 66), mios[k], mio.color);
                GUI.Label(new Rect(caja.x + 88, caja.y + 10, 200, 22), $"<b>{mios[k].nombre}</b>", Interfaz.Normal);
                Interfaz.Barra(new Rect(caja.x + 88, caja.y + 36, 190, 14), "Ritmo", Nivel(mios[k]), Interfaz.ColorDe(mio.color));
                if (GUI.Button(caja, GUIContent.none, GUIStyle.none)) mercadoMio = sel ? "" : mios[k].nombre;
            }
            GUI.Label(new Rect(640, 110, A - 660, 60), string.IsNullOrEmpty(mercadoMio) ? "Pulsa uno de tus pilotos para ver a quién puedes fichar en su lugar." : $"<color=#e55>{mercadoAviso}</color>", Interfaz.Normal);

            if (!string.IsNullOrEmpty(mercadoMio))
            {
                var actual = mios.First(x => x.nombre == mercadoMio);
                var otros = p.pilotos.Where(x => x.equipo != p.equipoJugador).OrderBy(x => x.ritmo).ToArray();
                const float fila = 40f;
                var vista = new Rect(24, 200, A - 48, H - 260);
                scroll = GUI.BeginScrollView(vista, scroll, new Rect(0, 0, vista.width - 20, otros.Length * fila));
                for (int i = 0; i < otros.Length; i++)
                {
                    var o = otros[i];
                    var e = p.equipos.First(x => x.id == o.equipo);
                    float y = i * fila;
                    Interfaz.Rect(new Rect(0, y, vista.width - 20, fila - 4), new Color(1, 1, 1, i % 2 == 0 ? 0.04f : 0.02f));
                    Interfaz.FotoPiloto(new Rect(6, y + 2, 34, 32), o, e.color);
                    GUI.Label(new Rect(50, y + 8, 220, 22), o.nombre, Interfaz.Normal);
                    GUI.Label(new Rect(270, y + 8, 160, 22), $"<color=#{ColorUtility.ToHtmlStringRGB(Interfaz.ColorDe(e.color))}>{e.nombre}</color>", Interfaz.Normal);
                    Interfaz.Barra(new Rect(440, y + 10, 260, 14), "Ritmo", Nivel(o), Interfaz.ColorDe(e.color));
                    float coste = Campeonato.CosteFichaje(actual, o);
                    GUI.Label(new Rect(720, y + 8, 110, 22), $"<b>{coste:F1} M</b>", Interfaz.Normal);
                    GUI.enabled = p.presupuesto >= coste;
                    if (GUI.Button(new Rect(vista.width - 180, y + 3, 150, fila - 10), "Fichar", Interfaz.Boton))
                    {
                        if (Campeonato.Fichar(p, actual.nombre, o.nombre)) { mercadoAviso = $"{o.nombre} ficha por {mio.nombre}; {actual.nombre} se va a {e.nombre}."; mercadoMio = ""; Guardar(); }
                    }
                    GUI.enabled = true;
                }
                GUI.EndScrollView();
            }
            if (GUI.Button(new Rect(A - 300, H - 56, 276, 42), "Empezar la temporada", Interfaz.BotonGrande)) { mercadoAviso = ""; IrATemporada(); }
        }

        /// <summary>El ritmo del piloto como barra de 0 a 1 (0,28 % más rápido que la media, lleno).</summary>
        static float Nivel(Piloto x) => Mathf.Clamp01(0.55f - x.ritmo * 1.6f);

        void Opcion(Rect r, ref string valor, string esta, string rotulo)
        {
            if (GUI.Button(r, rotulo, valor == esta ? Interfaz.BotonSel : Interfaz.Boton)) valor = esta;
        }

        static string Fecha(string iso)
        {
            if (!DateTime.TryParse(iso, out var f)) return iso;
            string[] meses = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
            return $"{f.Day} de {meses[f.Month - 1]}";
        }

        void Tablas(Rect r)
        {
            Interfaz.Fondo_(r);
            string[] nombres = { "Pilotos", "Constructores", "Último GP", "Calendario" };
            for (int i = 0; i < nombres.Length; i++)
                if (GUI.Button(new Rect(r.x + 14 + i * 108, r.y + 12, 104, 30), nombres[i], (int)pestana == i ? Interfaz.BotonSel : Interfaz.Boton))
                    pestana = (Pestana)i;
            float y = r.y + 54; const float f = 21f;
            switch (pestana)
            {
                case Pestana.Pilotos:
                    var tp = Campeonato.Pilotos(partida);
                    for (int i = 0; i < tp.Count; i++, y += f)
                    {
                        var e = partida.equipos.First(x => x.id == tp[i].equipo);
                        var pi = partida.pilotos.First(x => x.nombre == tp[i].nombre);
                        bool mio = e.id == partida.equipoJugador;
                        if (mio) Interfaz.Rect(new Rect(r.x + 6, y - 1, r.width - 12, f), new Color(1, 1, 1, 0.07f));
                        GUI.Label(new Rect(r.x + 16, y, 26, f), $"{i + 1}", Interfaz.Celda);
                        Interfaz.Rect(new Rect(r.x + 42, y + 3, 4, f - 7), Interfaz.ColorDe(e.color));
                        Interfaz.BanderaEn(new Rect(r.x + 52, y + 3, 22, 14), pi.pais);
                        GUI.Label(new Rect(r.x + 80, y, 190, f), (mio ? "<b>" : "") + tp[i].nombre + (mio ? "</b>" : ""), Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 270, y, 100, f), $"<color=#9aa>{e.nombre}</color>", Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 380, y, 60, f), $"<b>{tp[i].puntos}</b>", Interfaz.Celda);
                    }
                    break;
                case Pestana.Constructores:
                    var tc = Campeonato.Constructores(partida);
                    for (int i = 0; i < tc.Count; i++, y += 30)
                    {
                        var e = partida.equipos.First(x => x.id == tc[i].equipo);
                        bool mio = e.id == partida.equipoJugador;
                        Interfaz.Rect(new Rect(r.x + 10, y - 2, r.width - 20, 27), mio ? new Color(1, 1, 1, 0.1f) : new Color(1, 1, 1, 0.03f));
                        Interfaz.Rect(new Rect(r.x + 10, y - 2, 6, 27), Interfaz.ColorDe(e.color));
                        GUI.Label(new Rect(r.x + 26, y + 3, 30, 22), $"{i + 1}", Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 56, y + 3, 200, 22), (mio ? "<b>" : "") + e.nombre + (mio ? "</b>" : ""), Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 260, y + 3, 90, 22), tc[i].victorias > 0 ? $"<color=#9aa>{tc[i].victorias} vict.</color>" : "", Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 370, y + 3, 70, 22), $"<b>{tc[i].puntos}</b>", Interfaz.Celda);
                    }
                    break;
                case Pestana.UltimoGP:
                    if (partida.resultados.Count == 0) { GUI.Label(new Rect(r.x + 16, y, 400, 24), "Aún no se ha corrido ninguna carrera.", Interfaz.Normal); break; }
                    var u = partida.resultados[partida.resultados.Count - 1];
                    GUI.Label(new Rect(r.x + 16, y, 400, 24), $"<b>{calendario.carreras[u.ronda].granPremio}</b>", Interfaz.Celda); y += 24;
                    foreach (var fr in u.filas)
                    {
                        var e = partida.equipos.First(x => x.id == fr.equipo);
                        string t = fr.termino ? (fr.puesto == 1 ? Crono(fr.tiempo) : $"+{fr.tiempo - u.filas[0].tiempo:F3}") : fr.motivo;
                        Interfaz.Rect(new Rect(r.x + 42, y + 3, 4, f - 7), Interfaz.ColorDe(e.color));
                        GUI.Label(new Rect(r.x + 16, y, 26, f), $"{fr.puesto}", Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 54, y, 190, f), fr.piloto, Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 250, y, 100, f), $"<color=#9aa>{t}</color>", Interfaz.Celda);
                        if (fr.puntos > 0) GUI.Label(new Rect(r.x + 380, y, 60, f), $"<b>{fr.puntos}</b>", Interfaz.Celda);
                        y += f;
                    }
                    break;
                case Pestana.Calendario:
                    foreach (var c in calendario.carreras)
                    {
                        var res = partida.resultados.FirstOrDefault(x => x.ronda == c.ronda - 1);
                        string ganador = res != null ? res.filas[0].piloto : c.ronda - 1 == partida.ronda ? "<color=#e55><b>← próxima</b></color>" : "";
                        GUI.Label(new Rect(r.x + 16, y, 30, f), $"{c.ronda}", Interfaz.Celda);
                        Interfaz.BanderaEn(new Rect(r.x + 44, y + 3, 22, 14), BanderaDe(c.circuito));
                        GUI.Label(new Rect(r.x + 74, y, 180, f), Corto(c.granPremio), Interfaz.Celda);
                        GUI.Label(new Rect(r.x + 260, y, 190, f), $"<color=#9aa>{ganador}</color>", Interfaz.Celda);
                        y += f;
                    }
                    break;
            }
        }

        // ── El Gran Premio ───────────────────────────────────────────────────

        void Presentacion()
        {
            var r = new Rect(24, 24, 560, H - 48);
            PanelFavoritos(new Rect(r.xMax + 16, 24, Mathf.Min(470f, A - r.xMax - 40), 440));
            Interfaz.Fondo_(r);
            GUI.Label(new Rect(r.x + 22, r.y + 14, 400, 20), enCampeonato ? $"CAMPEONATO · RONDA {gpActual.ronda} DE {calendario.carreras.Length}" : "GRAN PREMIO SUELTO", Interfaz.Pequeno);
            Interfaz.BanderaEn(new Rect(r.x + 22, r.y + 42, 42, 28), datos.bandera);
            GUI.Label(new Rect(r.x + 76, r.y + 36, r.width - 90, 36), Corto(gpActual.granPremio), Interfaz.Grande);
            GUI.Label(new Rect(r.x + 76, r.y + 70, r.width - 90, 20), $"{datos.nombre} · {datos.longitudMedida / 1000f:F3} km · {Curvas()} curvas aprox. · previsión: {(lluvia ? "<color=#8cf><b>LLUVIA</b></color>" : "<b>seco</b>")}", Interfaz.Pequeno);
            var plano = PlanoImagen(datos.id);
            var zona = new Rect(r.x + 22, r.y + 100, r.width - 44, (r.width - 44) * 0.51f);
            Interfaz.Rect(zona, new Color(0.92f, 0.93f, 0.95f));
            if (plano != null) GUI.DrawTexture(zona, plano, ScaleMode.ScaleToFit);
            float y = zona.yMax + 16;
            GUI.Label(new Rect(r.x + 22, y, r.width - 44, 90),
                $"Circuito {(datos.Urbano ? "urbano" : "permanente")} y {TipoCircuito(circuito)}. La pole de 2025 fue de <b>{(datos.poleReferencia > 0 ? Crono(datos.poleReferencia) : "—")}</b>.\n" +
                $"Primero la clasificación a una vuelta, que decide la parrilla; después, {carrera.Vueltas} vueltas con salida parada." +
                (lluvia ? " <color=#8cf>Con lluvia cuenta más el piloto que el coche.</color>" : ""), Interfaz.Normal);
            if (!enCampeonato) ElegirMuro(new Rect(r.x + 22, r.yMax - 228, r.width - 44, 76));
            if (GUI.Button(new Rect(r.x + 22, r.yMax - 144, r.width - 44, 44), "Ver la clasificación en pista", Interfaz.BotonGrande)) VerClasificacion();
            if (GUI.Button(new Rect(r.x + 22, r.yMax - 94, r.width - 44, 38), "Ir a la parrilla de salida (sin ver la clasificación)", Interfaz.Boton)) IrAParrilla();
            if (GUI.Button(new Rect(r.x + 22, r.yMax - 48, r.width - 44, 34), "Saltar al resultado final (sin ver la carrera)", Interfaz.Boton)) IrAlResultado();
        }

        void ClasificacionEnPista()
        {
            // El reproductor pinta la torre de tiempos y sus mandos.
            if (reproductorQ != null && reproductorQ.Terminada)
            {
                if (GUI.Button(new Rect(A / 2 - 130, H - 64, 260, 46), "Ver la parrilla", Interfaz.BotonGrande)) IrAParrilla();
            }
            else if (GUI.Button(new Rect(A / 2 - 100, H - 52, 200, 38), "Saltar a la parrilla", Interfaz.Boton)) IrAParrilla();
        }

        static GUIStyle botonMuro, botonMuroSel;

        /// <summary>GP suelto: qué escudería lleva el jugador desde el muro (ninguna = solo mirar).</summary>
        void ElegirMuro(Rect r)
        {
            botonMuro ??= new GUIStyle(Interfaz.Boton) { fontSize = 11, padding = new RectOffset(4, 4, 4, 4) };
            botonMuroSel ??= new GUIStyle(Interfaz.BotonSel) { fontSize = 11, padding = new RectOffset(4, 4, 4, 4) };
            GUI.Label(new Rect(r.x, r.y, r.width, 18), "Tu muro en este GP (para dar órdenes por radio a sus dos pilotos):", Interfaz.Pequeno);
            var opciones = new[] { ("", "Ninguno") }.Concat(baseTemporada.equipos.Select(e => (e.id, e.nombre))).ToArray();
            float w = (r.width - 5 * 4) / 6f;
            for (int k = 0; k < opciones.Length; k++)
            {
                var b = new Rect(r.x + (k % 6) * (w + 4), r.y + 20 + (k / 6) * 28, w, 25);
                bool sel = equipoSuelto == opciones[k].Item1;
                if (GUI.Button(b, opciones[k].Item2, sel ? botonMuroSel : botonMuro)) equipoSuelto = opciones[k].Item1;
                if (k > 0) Interfaz.Rect(new Rect(b.x, b.yMax - 3, b.width, 3), Interfaz.ColorDe(baseTemporada.equipos[k - 1].color));
            }
        }

        string EstrellasDe(string piloto) =>
            favoritos != null && favoritos.Hechas >= favoritos.Total ? " <size=10>" + Interfaz.Estrellas(favoritos.Estrellas(piloto)) + "</size>" : "";

        /// <summary>Los favoritos del GP: estrellas y opciones de victoria y podio.</summary>
        void PanelFavoritos(Rect r)
        {
            if (favoritos == null || r.width < 300f) return;
            Interfaz.Fondo_(r, 0.9f);
            bool listo = favoritos.Hechas >= favoritos.Total;
            GUI.Label(new Rect(r.x + 18, r.y + 12, r.width - 30, 26), "FAVORITOS", Interfaz.Subtitulo);
            GUI.Label(new Rect(r.x + 18, r.y + 36, r.width - 30, 20),
                listo ? $"Lo que sale de simular este GP {favoritos.Total} veces" : $"Simulando el GP… {favoritos.Hechas * 100 / favoritos.Total} %", Interfaz.Pequeno);
            if (!listo) return;
            var orden = favoritos.Orden();
            float y = r.y + 66;
            for (int k = 0; k < 8 && k < orden.Count; k++, y += 44)
            {
                var p = (partida != null && enCampeonato ? partida.pilotos : baseTemporada.pilotos).First(x => x.nombre == orden[k]);
                var e = (partida != null && enCampeonato ? partida.equipos : baseTemporada.equipos).First(x => x.id == p.equipo);
                bool mio = enCampeonato && p.equipo == partida.equipoJugador;
                if (mio) Interfaz.Rect(new Rect(r.x + 6, y - 2, r.width - 12, 42), new Color(1, 1, 1, 0.08f));
                Interfaz.FotoPiloto(new Rect(r.x + 18, y, 42, 38), p, e.color);
                GUI.Label(new Rect(r.x + 70, y, 220, 22), $"<b>{p.nombre}</b>  {Interfaz.Estrellas(favoritos.Estrellas(p.nombre))}", Interfaz.Normal);
                GUI.Label(new Rect(r.x + 70, y + 19, 220, 20), $"<color=#{ColorUtility.ToHtmlStringRGB(Interfaz.ColorDe(e.color))}>{e.nombre}</color>", Interfaz.Pequeno);
                GUI.Label(new Rect(r.xMax - 170, y + 2, 160, 20), $"gana <b>{favoritos.Victoria(p.nombre) * 100f:F0} %</b>", Interfaz.Normal);
                GUI.Label(new Rect(r.xMax - 170, y + 20, 160, 20), $"podio {favoritos.Podio(p.nombre) * 100f:F0} %  ·  pole {favoritos.Pole(p.nombre) * 100f:F0} %", Interfaz.Pequeno);
            }
            if (enCampeonato)
            {
                // Tus pilotos, aunque no estén entre los ocho primeros.
                var mios = partida.pilotos.Where(x => x.equipo == partida.equipoJugador).Select(x => x.corto.Split(' ').Last() +
                    $" gana {favoritos.Victoria(x.nombre) * 100f:F0} %, podio {favoritos.Podio(x.nombre) * 100f:F0} %");
                GUI.Label(new Rect(r.x + 18, r.yMax - 28, r.width - 30, 22), "<b>Tus pilotos:</b> " + string.Join("  ·  ", mios), Interfaz.Pequeno);
            }
        }

        static readonly Carrera.Compuesto[] Secos = { Carrera.Compuesto.Blando, Carrera.Compuesto.Medio, Carrera.Compuesto.Duro };

        /// <summary>
        /// Con qué gomas salen tus pilotos (si llevas un muro). Solo antes de que empiece la carrera:
        /// en la parrilla. Los rivales ya han elegido y se ve en la lista.
        /// </summary>
        void NeumaticosDeSalida(Rect r)
        {
            if (EquipoRadio == null || r.width < 300f || carrera.Tiempo > 0f) return;
            Interfaz.Fondo_(r, 0.9f);
            GUI.Label(new Rect(r.x + 16, r.y + 10, r.width - 24, 24), "NEUMÁTICOS DE SALIDA", Interfaz.Subtitulo);
            if (carrera.Lluvia)
            {
                GUI.Label(new Rect(r.x + 16, r.y + 40, r.width - 28, 60), "Carrera en mojado: todos salen con gomas de lluvia.", Interfaz.Normal);
                return;
            }
            GUI.Label(new Rect(r.x + 16, r.y + 36, r.width - 28, 54),
                $"{ReproductorCarrera.Goma(Carrera.Compuesto.Blando)} vuela al salir y cae en la última vuelta.  " +
                $"{ReproductorCarrera.Goma(Carrera.Compuesto.Medio)} equilibrado.  " +
                $"{ReproductorCarrera.Goma(Carrera.Compuesto.Duro)} lento al salir, constante. Atacar por radio las gasta antes.", Interfaz.Pequeno);
            float y = r.y + 96;
            string[] nombres = { "Blando", "Medio", "Duro" };
            for (int i = 0; i < carrera.Coches.Length; i++)
            {
                var co = carrera.Coches[i];
                if (co.equipo.id != EquipoRadio) continue;
                GUI.Label(new Rect(r.x + 16, y + 4, 140, 22), $"<b>{co.piloto.corto}</b>", Interfaz.Celda);
                for (int k = 0; k < Secos.Length; k++)
                    if (GUI.Button(new Rect(r.x + 150 + k * 84, y, 80, 28), nombres[k], co.goma == Secos[k] ? Interfaz.BotonSel : Interfaz.Boton))
                        carrera.Gomas(i, Secos[k]);
                y += 36;
            }
            GUI.Label(new Rect(r.x + 16, y + 2, r.width - 28, 40), "<color=#9aa>Si no eliges, salen con lo que ha decidido el equipo.</color>", Interfaz.Pequeno);
        }

        void PantallaClasificacion()
        {
            var r = new Rect(14, 14, 560, H - 28);
            Interfaz.Fondo_(r);
            GUI.Label(new Rect(r.x + 22, r.y + 14, r.width - 40, 30), $"{Corto(gpActual.granPremio)} · Parrilla de salida", Interfaz.Grande);
            float pole = parrilla[0].segundos;
            // Los tres primeros, con su foto.
            for (int i = 0; i < 3; i++)
            {
                var v = parrilla[i];
                float x = r.x + 22 + i * 176;
                Interfaz.FotoPiloto(new Rect(x, r.y + 56, 64, 58), v.piloto, v.equipo.color);
                GUI.Label(new Rect(x + 72, r.y + 54, 104, 22), $"<b>{i + 1}.º</b>", Interfaz.Normal);
                GUI.Label(new Rect(x + 72, r.y + 72, 104, 20), $"<b>{v.piloto.corto.Split(' ').Last()}</b>", Interfaz.Celda);
                GUI.Label(new Rect(x + 72, r.y + 90, 104, 20), i == 0 ? Crono(v.segundos) : $"+{v.segundos - pole:F3}", Interfaz.Pequeno);
            }
            float y = r.y + 126; const float f = 20f;
            for (int i = 3; i < parrilla.Length; i++, y += f)
            {
                var v = parrilla[i];
                bool mio = enCampeonato && v.equipo.id == partida.equipoJugador;
                if (mio) Interfaz.Rect(new Rect(r.x + 8, y - 1, r.width - 16, f), new Color(1, 1, 1, 0.08f));
                GUI.Label(new Rect(r.x + 22, y, 26, f), $"{i + 1}", Interfaz.Celda);
                Interfaz.Rect(new Rect(r.x + 48, y + 3, 4, f - 7), Interfaz.ColorDe(v.equipo.color));
                Interfaz.BanderaEn(new Rect(r.x + 58, y + 3, 22, 14), v.piloto.pais);
                GUI.Label(new Rect(r.x + 86, y, 184, f), (mio ? "<b>" : "") + v.piloto.nombre + (mio ? "</b>" : "") + EstrellasDe(v.piloto.nombre), Interfaz.Celda);
                GUI.Label(new Rect(r.x + 270, y, 110, f), $"<color=#9aa>{v.equipo.nombre}</color>", Interfaz.Celda);
                GUI.Label(new Rect(r.x + 390, y, 80, f), $"+{v.segundos - pole:F3}", Interfaz.Celda);
                if (v.error) GUI.Label(new Rect(r.x + 458, y, 100, f), "<color=#f96>se fue largo</color>", Interfaz.Celda);
                GUI.Label(new Rect(r.x + 372, y, 18, f), ReproductorCarrera.Goma(carrera.Coches[System.Array.FindIndex(carrera.Coches, co => co.piloto == v.piloto)].goma), Interfaz.Celda);
            }
            NeumaticosDeSalida(new Rect(r.xMax + 16, 14, Mathf.Min(420f, A - r.xMax - 300f), 230));
            if (GUI.Button(new Rect(r.x + 22, r.yMax - 98, r.width - 44, 44), "Ver la carrera", Interfaz.BotonGrande)) VerCarrera();
            if (GUI.Button(new Rect(r.x + 22, r.yMax - 48, r.width - 44, 34), "Saltar al resultado final", Interfaz.Boton)) IrAlResultado();
        }

        void PantallaCarrera()
        {
            // El reproductor pinta la torre, los sucesos y sus mandos; aquí solo el botón de seguir.
            if (reproductor != null && reproductor.Terminada)
            {
                if (GUI.Button(new Rect(A / 2 - 130, H - 64, 260, 46), "Ver el resultado", Interfaz.BotonGrande)) IrAlResultado();
                return;
            }
            if (GUI.Button(new Rect(A / 2 - 115, H - 52, 230, 38), "Saltar al resultado final", Interfaz.Boton)) IrAlResultado();
            if (EquipoRadio != null && !carrera.Acabada) Radio();
        }

        static readonly Carrera.OrdenPiloto[] Ordenes =
            { Carrera.OrdenPiloto.Atacar, Carrera.OrdenPiloto.Normal, Carrera.OrdenPiloto.Defender, Carrera.OrdenPiloto.Ahorrar };

        /// <summary>
        /// El muro: órdenes por radio a tus dos pilotos y la orden de equipo, en plena carrera.
        /// Atacar: más ritmo y más intentos de adelantar, pero más errores y desgaste de motor.
        /// Defender: más difícil de pasar. Ahorrar: más lento, cuida el motor y casi no falla.
        /// </summary>
        bool radioPlegada;

        void Radio()
        {
            var r = new Rect(12, 62, 372, radioPlegada ? 40 : 422);
            Interfaz.Fondo_(r, 0.88f);
            GUI.Label(new Rect(r.x + 14, r.y + 8, 300, 22), "RADIO DEL MURO", Interfaz.Subtitulo);
            if (GUI.Button(new Rect(r.xMax - 84, r.y + 7, 72, 24), radioPlegada ? "Abrir" : "Plegar", Interfaz.Boton)) radioPlegada = !radioPlegada;
            if (radioPlegada) return;
            float y = r.y + 36;
            string[] nombres = { "Ataca", "Normal", "Defiende", "Ahorra" };
            for (int i = 0; i < carrera.Coches.Length; i++)
            {
                var co = carrera.Coches[i];
                if (co.equipo.id != EquipoRadio) continue;
                bool corre = !co.fuera && !co.terminado;
                Interfaz.Rect(new Rect(r.x + 14, y + 4, 4, 16), Interfaz.ColorDe(co.equipo.color));
                string estado = co.fuera ? " <color=#e66>fuera</color>" : co.terminado ? " <color=#9aa>en meta</color>" : $"  <color=#9aa>{carrera.Puesto(i)}.º</color>";
                GUI.Label(new Rect(r.x + 24, y + 2, 200, 20), $"<b>{co.piloto.corto}</b>{estado}", Interfaz.Celda);
                if (corre)
                {
                    // Las gomas, en claro: cuánto se han gastado, de verde a rojo.
                    float gastadas = Mathf.Clamp(co.desgaste * 33f, 0f, 99f);
                    string col = gastadas < 50f ? "6c6" : gastadas < 80f ? "fd6" : "f66";
                    GUI.Label(new Rect(r.x + 200, y + 3, 170, 20), $"<size=11>gomas {ReproductorCarrera.Goma(co.goma)}: <color=#{col}>{gastadas:F0} % gastadas</color></size>", Interfaz.Celda);
                    var hu = carrera.Huecos(i);
                    string Quien(int k, float s) => k < 0 || s > 9f ? "nadie cerca" : $"{carrera.Coches[k].piloto.corto} a {s:F1} s";
                    GUI.Label(new Rect(r.x + 24, y + 20, r.width - 34, 18),
                        $"<size=11><color=#9aa>Delante: {Quien(hu.quienDelante, hu.delante)}{(hu.delante < 1f ? " <color=#6c6>(DRS)</color>" : "")} · Detrás: {Quien(hu.quienDetras, hu.detras)}</color></size>", Interfaz.Celda);
                    GUI.Label(new Rect(r.x + 24, y + 37, r.width - 34, 34), $"<size=11><color=#8cf>Ingeniero: «{Consejo(i, hu)}»</color></size>", Interfaz.Pequeno);
                }
                y += 72;
                GUI.enabled = corre;
                for (int k = 0; k < Ordenes.Length; k++)
                    if (GUI.Button(new Rect(r.x + 14 + k * 87, y, 83, 26), new GUIContent(nombres[k], AyudaRadio[k]), co.radio == Ordenes[k] ? Interfaz.BotonSel : Interfaz.Boton))
                        carrera.Radio(i, Ordenes[k]);
                GUI.enabled = true;
                if (corre) GUI.Label(new Rect(r.x + 14, y + 28, r.width - 24, 30), $"<size=11><color=#ccc>Ahora: {Efecto(co.radio)}</color></size>", Interfaz.Pequeno);
                y += 58;
            }
            // Orden de equipo: quién tiene prioridad (el otro no le ataca y le cede el paso).
            var mis = carrera.Coches.Where(x => x.equipo.id == EquipoRadio).Select(x => x.piloto).ToArray();
            GUI.Label(new Rect(r.x + 14, y, 200, 18), "Orden de equipo", Interfaz.Pequeno);
            y += 18;
            string actual = Prioridad;
            if (GUI.Button(new Rect(r.x + 14, y, 110, 26), new GUIContent("Libre", "Libre: tus dos pilotos pueden pelearse entre ellos."), actual == "" ? Interfaz.BotonSel : Interfaz.Boton))
            { Prioridad = ""; carrera.Prioridad(EquipoRadio, null); }
            for (int k = 0; k < mis.Length && k < 2; k++)
                if (GUI.Button(new Rect(r.x + 128 + k * 116, y, 112, 26),
                    new GUIContent("Manda " + mis[k].corto.Split(' ').Last(), $"Manda {mis[k].corto}: su compañero no le ataca y, si le alcanza más rápido, le deja pasar. Útil para jugarte un podio o el campeonato con uno."),
                    actual == mis[k].nombre ? Interfaz.BotonSel : Interfaz.Boton))
                { Prioridad = mis[k].nombre; carrera.Prioridad(EquipoRadio, mis[k].nombre); }
            // Qué hace la orden que hay bajo el ratón (o una pista si no hay ninguna).
            y += 34;
            string ayuda = string.IsNullOrEmpty(GUI.tooltip) ? "Pasa el ratón por una orden para ver qué hace y cuándo conviene." : GUI.tooltip;
            GUI.Label(new Rect(r.x + 14, y, r.width - 26, r.yMax - y - 6), $"<size=11>{ayuda}</size>", Interfaz.Pequeno);
        }

        /// <summary>
        /// Lo que aconsejaría el ingeniero con lo que tiene el piloto alrededor: de lo urgente
        /// (coche de seguridad, compañero delante, DRS, alguien encima) a lo tranquilo (va solo).
        /// </summary>
        string Consejo(int i, (float delante, float detras, int quienDelante, int quienDetras) hu)
        {
            var co = carrera.Coches[i];
            string N(int k) => carrera.Coches[k].piloto.corto;
            bool ultima = co.vueltas >= carrera.Vueltas - 1;
            float gastadas = Mathf.Clamp(co.desgaste * 33f, 0f, 99f);
            if (carrera.CocheSeguridadEnPista) return "Coche de seguridad: no se adelanta. Ahorra gomas y motor para la relanzada.";
            if (hu.quienDelante >= 0 && hu.delante < 1f && carrera.Coches[hu.quienDelante].equipo.id == co.equipo.id)
                return $"Delante va tu compañero ({N(hu.quienDelante)}). Si atacas, os jugáis el puesto entre vosotros; si no quieres, pulsa «Manda».";
            if (hu.quienDelante >= 0 && hu.delante < 1f)
                return gastadas < 75f || ultima
                    ? $"Ataca: tiene DRS sobre {N(hu.quienDelante)}{(ultima ? " y es la última vuelta" : "")}."
                    : $"Tiene a {N(hu.quienDelante)} cerca, pero con las gomas al {gastadas:F0} % atacar es arriesgado; mejor en la última vuelta.";
            if (hu.quienDetras >= 0 && hu.detras < 1f)
                return $"Defiende: {N(hu.quienDetras)} está a {hu.detras:F1} s{(ultima ? " en la última vuelta" : "")}.";
            if (ultima && hu.delante < 2.5f) return "Última vuelta y el de delante no está lejos: si quieres ese puesto, Ataca ahora.";
            if (hu.delante > 2f && hu.detras > 2f) return "Va solo (a más de 2 s de todos): Ahorra; no pierde puestos y cuida gomas y motor.";
            if (gastadas > 80f) return "Gomas muy gastadas: Ahorra para que no se caigan del todo.";
            return "Nada urgente: Normal.";
        }

        /// <summary>Lo que hace la orden, en cifras que se entienden (segundos por vuelta en este circuito).</summary>
        string Efecto(Carrera.OrdenPiloto o)
        {
            var e = Carrera.EfectoRadio(o);
            float s = carrera.Circuito.Referencia * Mathf.Abs(e.ritmo) / 100f;
            string motor = enCampeonato ? " · gasta motor" : "";
            return o switch
            {
                Carrera.OrdenPiloto.Atacar => $"−{s:F1} s/vuelta · ×2 intentos de adelantar · +50 % errores · gomas ×1,5{motor}",
                Carrera.OrdenPiloto.Defender => $"−{s:F1} s/vuelta · mucho más difícil de pasar · +10 % errores",
                Carrera.OrdenPiloto.Ahorrar => $"+{s:F1} s/vuelta · −40 % errores · gomas ×0,6{(enCampeonato ? " · cuida el motor" : "")}",
                _ => "sin cambios",
            };
        }

        static readonly string[] AyudaRadio =
        {
            "<b>Ataca</b>: más ritmo y el doble de intentos de adelantar, pero más errores, gasta las gomas antes y, en el campeonato, el motor. Úsala con alguien a menos de 1 s delante (DRS) o en la última vuelta.",
            "<b>Normal</b>: ni gana ni pierde. Para cuando no pasa nada cerca.",
            "<b>Defiende</b>: al de detrás le cuesta mucho más ganarte la frenada; casi sin riesgo. Úsala con alguien más rápido a menos de 1 s detrás, sobre todo en la última vuelta.",
            "<b>Ahorra</b>: algo más lento, pero casi no falla, las gomas duran más y cuida el motor. Úsala si vas solo, detrás del coche de seguridad, o para guardar gomas y atacar al final.",
        };

        void PantallaResultado()
        {
            Apuntar();
            var r = carrera.Resultado;
            var caja = new Rect(A / 2 - 330, 14, 660, H - 28);
            Interfaz.Fondo_(caja, 0.95f);
            GUI.Label(new Rect(caja.x + 24, caja.y + 14, 600, 32), $"{Corto(gpActual.granPremio)} · Resultado", Interfaz.Grande);

            // Podio: el ganador en medio y más alto.
            int[] orden = { 1, 0, 2 };
            float[] altura = { 64, 86, 52 };
            for (int k = 0; k < 3; k++)
            {
                int p = orden[k];
                if (p >= r.Length) continue;
                var co = r[p];
                float x = caja.x + 120 + k * 150, baseY = caja.y + 218;
                Interfaz.FotoPiloto(new Rect(x + 20, baseY - altura[k] - 86, 84, 78), co.piloto, co.equipo.color);
                Interfaz.Rect(new Rect(x, baseY - altura[k], 124, altura[k]), Interfaz.ColorDe(co.equipo.color, Interfaz.Gris) * new Color(1, 1, 1, 0.85f));
                GUI.Label(new Rect(x + 8, baseY - altura[k] + 6, 110, 30), $"<b>{p + 1}.º</b>", Interfaz.Grande);
                GUI.Label(new Rect(x, baseY + 4, 124, 20), $"<b>{co.piloto.corto}</b>", Interfaz.Celda);
            }
            float y = caja.y + 246; const float f = 17.5f;
            float t0 = r[0].tiempoFinal;
            for (int i = 0; i < r.Length; i++, y += f)
            {
                var co = r[i];
                bool mio = enCampeonato && co.equipo.id == partida.equipoJugador;
                if (mio) Interfaz.Rect(new Rect(caja.x + 8, y - 1, caja.width - 16, f), new Color(1, 1, 1, 0.08f));
                string t = !co.terminado ? (co.motivo ?? "Abandono") : i == 0 ? Crono(co.tiempoFinal) : $"+{co.tiempoFinal - t0:F3}";
                int pts = co.terminado ? Puntos.PorPuesto(i + 1) : 0;
                GUI.Label(new Rect(caja.x + 24, y, 26, f), $"{i + 1}", Interfaz.Celda);
                Interfaz.Rect(new Rect(caja.x + 50, y + 3, 4, f - 7), Interfaz.ColorDe(co.equipo.color));
                Interfaz.BanderaEn(new Rect(caja.x + 60, y + 3, 22, 14), co.piloto.pais);
                GUI.Label(new Rect(caja.x + 88, y, 190, f), (mio ? "<b>" : "") + co.piloto.nombre + (mio ? "</b>" : ""), Interfaz.Celda);
                GUI.Label(new Rect(caja.x + 280, y, 110, f), $"<color=#9aa>{co.equipo.nombre}</color>", Interfaz.Celda);
                GUI.Label(new Rect(caja.x + 400, y, 90, f), t, Interfaz.Celda);
                GUI.Label(new Rect(caja.x + 492, y, 70, f), $"<color=#789>sale {co.salida + 1}</color>", Interfaz.Celda);
                if (pts > 0) GUI.Label(new Rect(caja.x + 570, y, 70, f), $"<b>{pts} pts</b>", Interfaz.Celda);
            }
            if (GUI.Button(new Rect(caja.x + 24, caja.yMax - 54, caja.width - 48, 42), "Continuar", Interfaz.BotonGrande)) Continuar();
        }

        void FinTemporada()
        {
            var tp = Campeonato.Pilotos(partida);
            var tc = Campeonato.Constructores(partida);
            int mio = tc.FindIndex(x => x.equipo == partida.equipoJugador) + 1;
            var campeon = partida.pilotos.First(x => x.nombre == tp[0].nombre);
            var eqCampeon = partida.equipos.First(x => x.id == campeon.equipo);
            var r = new Rect(A / 2 - 330, 30, 660, H - 60);
            Interfaz.Fondo_(r, 0.95f);
            GUI.Label(new Rect(r.x + 24, r.y + 16, 600, 20), $"MUNDIAL {partida.temporada} · CAMPEÓN", Interfaz.Pequeno);
            Interfaz.FotoPiloto(new Rect(r.x + 24, r.y + 44, 110, 100), campeon, eqCampeon.color);
            GUI.Label(new Rect(r.x + 150, r.y + 46, 480, 36), campeon.nombre, Interfaz.Grande);
            GUI.Label(new Rect(r.x + 150, r.y + 84, 480, 40), $"{eqCampeon.nombre} · {tp[0].puntos} puntos y {tp[0].victorias} victorias.\nConstructores: <b>{tc[0].nombre}</b> ({tc[0].puntos}).", Interfaz.Normal);
            GUI.Label(new Rect(r.x + 24, r.y + 156, 600, 24), $"<b>{partida.MiEquipo.nombre}</b> acaba <b>{mio}.º</b> de constructores con {tc[mio - 1].puntos} puntos.", Interfaz.Subtitulo);
            float y = r.y + 196;
            for (int i = 0; i < Math.Min(10, tp.Count); i++)
            {
                var e = partida.equipos.First(x => x.id == tp[i].equipo);
                Interfaz.Rect(new Rect(r.x + 50, y + i * 21 + 3, 4, 14), Interfaz.ColorDe(e.color));
                GUI.Label(new Rect(r.x + 24, y + i * 21, 26, 21), $"{i + 1}", Interfaz.Celda);
                GUI.Label(new Rect(r.x + 60, y + i * 21, 200, 21), tp[i].nombre, Interfaz.Celda);
                GUI.Label(new Rect(r.x + 262, y + i * 21, 50, 21), $"<b>{tp[i].puntos}</b>", Interfaz.Celda);
            }
            for (int i = 0; i < tc.Count; i++)
            {
                var e = partida.equipos.First(x => x.id == tc[i].equipo);
                bool yo = e.id == partida.equipoJugador;
                Interfaz.Rect(new Rect(r.x + 360, y + i * 21 + 3, 4, 14), Interfaz.ColorDe(e.color));
                GUI.Label(new Rect(r.x + 336, y + i * 21, 26, 21), $"{i + 1}", Interfaz.Celda);
                GUI.Label(new Rect(r.x + 370, y + i * 21, 160, 21), (yo ? "<b>" : "") + e.nombre + (yo ? "</b>" : ""), Interfaz.Celda);
                GUI.Label(new Rect(r.x + 540, y + i * 21, 60, 21), $"<b>{tc[i].puntos}</b>", Interfaz.Celda);
            }
            float yd = y + 11 * 21 + 10;
            GUI.Label(new Rect(r.x + 24, yd, r.width - 48, 44), $"<i>{partida.mensajeDirectiva}</i>", Interfaz.Normal);
            yd += 46;
            foreach (var h in partida.historial.Skip(Math.Max(0, partida.historial.Count - 4)))
            {
                string ok = h.puesto <= h.objetivo ? "<color=#6c6>cumplido</color>" : "<color=#e66>fallado</color>";
                GUI.Label(new Rect(r.x + 24, yd, r.width - 48, 20), $"<color=#9aa>{h.temporada}</color>  {h.puesto}.º (objetivo {h.objetivo}.º, {ok}) · campeón {h.campeonPiloto}, {h.campeonEquipo}", Interfaz.Pequeno);
                yd += 19;
            }
            if (partida.despedido)
            {
                if (GUI.Button(new Rect(r.x + 24, r.yMax - 54, r.width - 48, 42), "Fin: volver a la portada", Interfaz.BotonGrande)) pantalla = Pantalla.Portada;
                return;
            }
            if (GUI.Button(new Rect(r.x + 24, r.yMax - 54, r.width - 260, 42), $"Siguiente temporada ({partida.temporada + 1})", Interfaz.BotonGrande))
            {
                Campeonato.NuevaTemporada(partida);
                Guardar();
                scroll = Vector2.zero; mercadoMio = ""; mercadoAviso = "";
                MostrarCircuito(partida.calendario[0], CamaraCircuito.Modo.Orbita);
                gpActual = calendario.carreras[0];
                pantalla = Pantalla.Mercado;
            }
            if (GUI.Button(new Rect(r.xMax - 228, r.yMax - 54, 204, 42), "Volver a la portada", Interfaz.Boton)) pantalla = Pantalla.Portada;
        }

        static string Crono(float s) => $"{(int)(s / 60)}:{s % 60:00.000}";
    }
}
