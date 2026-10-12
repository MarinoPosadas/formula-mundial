using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Banco de pruebas de la fase 2: construye cualquier circuito del calendario, llena la
    /// parrilla y pone un coche a dar vueltas lanzadas al ritmo de la pole de referencia.
    /// No es el juego; es para ver los circuitos y la escala antes de montar la carrera.
    /// </summary>
    public class BancoPruebas : MonoBehaviour
    {
        [SerializeField] GameObject cochePrefab;
        [SerializeField] Material[] materialesEscuderia;
        [SerializeField] CatalogoDecorado catalogo;
        [SerializeField] string circuitoInicial = "it-1922";

        static readonly float[] Velocidades = { 0.25f, 0.5f, 1f, 2f, 4f };

        Calendario calendario;
        int indice;
        DatosCircuito datos;
        Trazado trazado;
        PerfilVelocidad perfil;
        float ajusteRitmo = 1f;           // tiempo de modelo por segundo real
        GameObject pista, coche;
        Transform[] parrilla;
        float tiempo;
        int velocidad = 2, escuderia;
        ConstructorPista.Materiales materiales;
        CamaraCircuito camara;
        Parrilla temporada;
        ReproductorCarrera reproductor;
        Transform[] cochesCarrera;
        int semilla;

        void Start()
        {
            calendario = Calendario.Cargar();
            temporada = JsonUtility.FromJson<Parrilla>(Resources.Load<TextAsset>("Temporada/parrilla_2026").text);
            materiales = CargarMateriales();
            camara = Camera.main.GetComponent<CamaraCircuito>();
            if (camara == null) camara = Camera.main.gameObject.AddComponent<CamaraCircuito>();
            for (int i = 0; i < calendario.carreras.Length; i++)
                if (calendario.carreras[i].circuito == circuitoInicial) indice = i;
            Cargar();
        }

        void Cargar()
        {
            QuitarCarrera();
            if (pista != null) DestruirConMallas(pista);
            if (coche != null) Destroy(coche);
            if (parrilla != null) foreach (var p in parrilla) if (p != null) Destroy(p.gameObject);

            datos = DatosCircuito.Cargar(calendario.carreras[indice].circuito);
            trazado = datos.CrearTrazado();
            perfil = new PerfilVelocidad(trazado, Prestaciones.Referencia);
            ajusteRitmo = datos.poleReferencia > 0f ? perfil.TiempoVuelta / datos.poleReferencia : 1f;
            pista = ConstructorPista.Construir(trazado, materiales, transform, datos.ancho, datos.Urbano, datos.Desierto);
            var entorno = new Entorno(trazado, datos, catalogo, materiales, pista.transform);
            if (catalogo != null) ConstructorDecorado.Construir(trazado, datos, catalogo, materiales, pista.transform, entorno);

            // La parrilla entera con coches quietos, para ver la escala; uno de ellos rueda.
            parrilla = new Transform[cochePrefab != null ? 21 : 0];
            for (int k = 1; k < parrilla.Length + 1 && cochePrefab != null; k++)
            {
                Pose p = ConstructorPista.Parrilla(trazado, k);
                parrilla[k - 1] = CrearCoche(p, k / 2).transform;
            }
            if (cochePrefab != null) coche = CrearCoche(ConstructorPista.Parrilla(trazado, 0), escuderia);
            tiempo = 0f;
            camara.objetivo = coche != null ? coche.transform : null;
            camara.Preparar(trazado, pista.transform);
        }

        GameObject CrearCoche(Pose pose, int equipo) =>
            CrearCoche(pose, materialesEscuderia != null && materialesEscuderia.Length > 0 ? materialesEscuderia[equipo % materialesEscuderia.Length] : null);

        GameObject CrearCoche(Pose pose, Material mat, string casco = null) => FabricaCoches.Crear(cochePrefab, pose, mat, transform, "Coche", casco);

        void Update()
        {
            if (coche == null || reproductor != null) return;
            tiempo += Time.deltaTime * Velocidades[velocidad];
            float t = tiempo * ajusteRitmo;
            float d = perfil.DistanciaTras(t);
            Vector3 pos = trazado.Posicion(d).U(), f = trazado.Tangente(d).U();
            coche.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f, Vector3.up));
        }

        /// <summary>Clasificación y carrera simuladas en este circuito, y a verlas en 3D.</summary>
        void SimularGP()
        {
            if (coche != null) Destroy(coche);
            if (parrilla != null) foreach (var p in parrilla) if (p != null) Destroy(p.gameObject);
            QuitarCarrera();

            var circuito = new Circuito(trazado, datos.poleReferencia, datos.id, temporada.temporada) { Escapatoria = datos.Urbano ? 3.5f : 10f };
            semilla = Random.Range(1, 1000000);
            var azar = new Azar(semilla);
            var q = Clasificacion.Simular(circuito, temporada, azar);
            var carrera = Carrera.Simular(circuito, q, 3, azar);

            cochesCarrera = new Transform[q.Length];
            for (int k = 0; k < q.Length; k++)
            {
                var mat = FabricaCoches.Material(materialesEscuderia, q[k].equipo.material);
                cochesCarrera[k] = CrearCoche(ConstructorPista.Parrilla(trazado, k), mat, q[k].piloto.casco ?? q[k].equipo.color).transform;
                cochesCarrera[k].name = q[k].piloto.corto;
            }
            reproductor = gameObject.AddComponent<ReproductorCarrera>();
            reproductor.Iniciar(carrera, q, cochesCarrera, camara);
        }

        void QuitarCarrera()
        {
            if (reproductor != null) Destroy(reproductor);
            if (cochesCarrera != null) foreach (var c in cochesCarrera) if (c != null) Destroy(c.gameObject);
            reproductor = null; cochesCarrera = null;
        }

        void OnGUI()
        {
            if (datos == null) return;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * Mathf.Max(1f, Screen.height / 720f));
            GUILayout.BeginArea(new Rect(10, 10, 430, 175), GUI.skin.box);
            var c = calendario.carreras[indice];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(40))) { indice = (indice + calendario.carreras.Length - 1) % calendario.carreras.Length; Cargar(); }
            GUILayout.Label($"<b>{c.ronda}. {c.granPremio}</b>", Rico());
            if (GUILayout.Button("▶", GUILayout.Width(40))) { indice = (indice + 1) % calendario.carreras.Length; Cargar(); }
            GUILayout.EndHorizontal();
            GUILayout.Label($"{datos.nombre} · {datos.longitudMedida / 1000f:F3} km · {datos.giro}" +
                            (datos.salidaVerificada ? "" : " · salida sin verificar") +
                            (trazado.Cruces.Count > 0 ? $" · {trazado.Cruces.Count} puente" : ""));
            float vuelta = perfil.TiempoVuelta / ajusteRitmo;
            GUILayout.Label($"Vuelta de referencia {Crono(vuelta)}" + (datos.poleReferencia > 0 ? $" (pole 2025 {Crono(datos.poleReferencia)}; modelo sin ajustar {Crono(perfil.TiempoVuelta)})" : " (sin pole de referencia)"));
            if (reproductor != null)
            {
                GUILayout.Label($"Gran Premio simulado · semilla {semilla}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Otra carrera")) SimularGP();
                if (GUILayout.Button("Volver al banco")) Cargar();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }
            if (coche != null)
            {
                float d = perfil.DistanciaTras(tiempo * ajusteRitmo);
                GUILayout.Label($"Vuelta {1 + Mathf.FloorToInt(tiempo * ajusteRitmo / perfil.TiempoVuelta)} · {Crono(tiempo % vuelta)} · {perfil.VelocidadA(d) * 3.6f:F0} km/h · {d:F0} m");
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Cámara: {camara.NombreModo}")) camara.Siguiente();
            if (GUILayout.Button($"Ritmo x{Velocidades[velocidad]}")) velocidad = (velocidad + 1) % Velocidades.Length;
            if (materialesEscuderia != null && materialesEscuderia.Length > 0 &&
                GUILayout.Button(materialesEscuderia[escuderia].name)) { escuderia = (escuderia + 1) % materialesEscuderia.Length; Cargar(); }
            GUILayout.EndHorizontal();
            if (cochePrefab == null) GUILayout.Label("Falta el coche: F1 ▸ Crear escena de carrera");
            else if (GUILayout.Button("<b>Simular GP</b> (clasificación + 3 vueltas)", new GUIStyle(GUI.skin.button) { richText = true })) SimularGP();
            GUILayout.EndArea();
        }

        static GUIStyle Rico() => new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter };

        static string Crono(float s) => $"{(int)(s / 60)}:{s % 60:00.000}";

        static void DestruirConMallas(GameObject go)
        {
            ConstructorPista.LiberarMallas();
            Destroy(go);
        }

        internal static ConstructorPista.Materiales CargarMateriales()
        {
            Material M(string n, Color c)
            {
                var m = Resources.Load<Material>("Materiales/Pista/" + n);
                if (m == null) { m = new Material(Shader.Find("Standard")) { color = c }; }
                return m;
            }
            // Copias: las texturas se generan aquí y no deben acabar dentro del asset.
            var piano = new Material(M("Piano", Color.white));
            piano.mainTexture = Textura(1, 2, (x, y) => y == 0 ? new Color(0.8f, 0.08f, 0.08f) : Color.white);
            piano.mainTextureScale = new Vector2(1f, 0.5f);   // una franja por metro
            var cuadros = new Material(M("Cuadros", Color.white));
            cuadros.mainTexture = Textura(2, 2, (x, y) => (x + y) % 2 == 0 ? new Color(0.05f, 0.05f, 0.05f) : Color.white);
            var asfalto = new Material(M("Asfalto", new Color(0.2f, 0.2f, 0.21f)));
            asfalto.mainTexture = Ruido(128, 0.82f, 1.0f, 7f);
            var cesped = new Material(M("Cesped", new Color(0.26f, 0.45f, 0.2f)));
            cesped.mainTexture = Ruido(256, 0.8f, 1.05f, 3f);
            var grava = new Material(M("Grava", new Color(0.78f, 0.7f, 0.55f)));
            grava.mainTexture = Ruido(128, 0.8f, 1.05f, 11f);
            var barrera = new Material(M("Barrera", Color.white));
            // Barrera de neumáticos con franjas rojas y blancas (la v de la pared va en 2 m).
            barrera.mainTexture = Textura(2, 1, (x, y) => x == 0 ? new Color(0.82f, 0.1f, 0.1f) : new Color(0.93f, 0.93f, 0.93f));
            return new ConstructorPista.Materiales
            {
                asfalto = asfalto,
                cesped = cesped,
                grava = grava,
                barrera = barrera,
                hormigon = M("Hormigon", new Color(0.72f, 0.72f, 0.7f)),
                acera = Con(M("Acera", new Color(0.6f, 0.6f, 0.6f)), Ruido(128, 0.85f, 1.05f, 5f)),
                arena = Con(M("Arena", new Color(0.84f, 0.73f, 0.55f)), Ruido(256, 0.85f, 1.06f, 13f)),
                agua = M("Agua", new Color(0.12f, 0.36f, 0.55f)),
                montana = M("Montana", new Color(0.33f, 0.43f, 0.32f)),
                montanaAlta = M("MontanaAlta", new Color(0.52f, 0.55f, 0.6f)),
                sueloUrbano = Con(M("SueloUrbano", new Color(0.42f, 0.43f, 0.44f)), Ruido(128, 0.85f, 1.05f, 9f)),
                piano = piano,
                blanco = M("Blanco", Color.white),
                muro = M("Muro", new Color(0.62f, 0.62f, 0.6f)),
                cuadros = cuadros,
            };
        }

        /// <summary>
        /// Ruido suave en escala de grises que casa en los bordes (se multiplica por el color del
        /// material): tres octavas de ruido de valor sobre rejillas periódicas. El de senos hacía
        /// franjas (muaré) al mirar el césped de lejos.
        /// </summary>
        static Texture2D Ruido(int lado, float min, float max, float semilla)
        {
            var rnd = new System.Random((int)(semilla * 1000));
            var t = new Texture2D(lado, lado, TextureFormat.RGBA32, true)
                { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var valores = new float[lado * lado];
            float peso = 0f;
            foreach (var (celdas, w) in new[] { (8, 0.5f), (16, 0.3f), (32, 0.2f) })
            {
                var g = new float[celdas * celdas];
                for (int i = 0; i < g.Length; i++) g[i] = (float)rnd.NextDouble();
                for (int y = 0; y < lado; y++)
                    for (int x = 0; x < lado; x++)
                    {
                        float fx = (float)x / lado * celdas, fy = (float)y / lado * celdas;
                        int x0 = (int)fx, y0 = (int)fy, x1 = (x0 + 1) % celdas, y1 = (y0 + 1) % celdas;
                        float tx = Mathf.SmoothStep(0, 1, fx - x0), ty = Mathf.SmoothStep(0, 1, fy - y0);
                        float a = Mathf.Lerp(g[y0 * celdas + x0], g[y0 * celdas + x1], tx);
                        float b = Mathf.Lerp(g[y1 * celdas + x0], g[y1 * celdas + x1], tx);
                        valores[y * lado + x] += w * Mathf.Lerp(a, b, ty);
                    }
                peso += w;
            }
            for (int i = 0; i < valores.Length; i++)
            {
                float g = Mathf.Lerp(min, max, valores[i] / peso);
                t.SetPixel(i % lado, i / lado, new Color(g, g, g, 1f));
            }
            t.Apply();
            return t;
        }

        static Material Con(Material m, Texture2D t) { var c = new Material(m) { mainTexture = t }; return c; }

        static Texture2D Textura(int w, int h, System.Func<int, int, Color> color)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, color(x, y));
            t.Apply();
            return t;
        }
    }
}
