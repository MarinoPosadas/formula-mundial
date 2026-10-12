using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace F1.Juego.Editor
{
    /// <summary>
    /// F1 ▸ Crear escena de carrera: materiales de pista en Resources (para que viajen en la
    /// build con su shader) y la escena Carrera con la cámara y el banco de pruebas montados.
    /// Se puede repetir: rehace la escena y deja los materiales que ya existan.
    /// </summary>
    public static class CrearEscenaCarrera
    {
        const string CarpetaMateriales = "Assets/Resources/Materiales/Pista";
        const string Escena = "Assets/Juego/Escenas/Carrera.unity";
        // El F1 de verdad (3.644 triángulos) con las 11 libreas 2026 generadas a partir de su
        // textura (Assets/Coches/F1_2026/Libreas). Sustituye al coche de juguete de FormulaCar.
        // Con casco y visera (Tools/Blender/casco.py los añade al F1_2026.fbx).
        const string Coche = "Assets/Coches/F1_2026/F1_2026_piloto.fbx";
        const string Libreas = "Assets/Coches/F1_2026/Libreas";
        const string MaterialesEscuderia = "Assets/Coches/F1_2026/Materiales";
        const string Catalogo = "Assets/Juego/Decorado/Catalogo.asset";
        const string Kenney = "Assets/ModelosRacing/Racing Kit - Kenney";
        const string Ciudad = "Assets/Decorado/Ciudad";

        const string EscenaJuego = "Assets/Juego/Escenas/Juego.unity";

        [MenuItem("F1/Crear escena del juego")]
        public static void CrearJuego() => Montar(EscenaJuego, typeof(ControladorJuego), primera: true);

        [MenuItem("F1/Crear escena de carrera")]
        public static void Crear() => Montar(Escena, typeof(BancoPruebas), primera: false);

        static void Montar(string rutaEscena, System.Type componente, bool primera)
        {
            Directory.CreateDirectory(CarpetaMateriales);
            Directory.CreateDirectory(Path.GetDirectoryName(Escena));
            Material("Asfalto", new Color(0.2f, 0.2f, 0.21f), 0.15f);
            Material("Cesped", new Color(0.26f, 0.45f, 0.2f), 0.05f);
            Material("Piano", Color.white, 0.2f);
            Material("Blanco", new Color(0.92f, 0.92f, 0.92f), 0.2f);
            Material("Muro", new Color(0.62f, 0.62f, 0.6f), 0.1f);
            Material("Cuadros", Color.white, 0.2f);
            Material("Grava", new Color(0.78f, 0.7f, 0.55f), 0.05f);
            Material("Hormigon", new Color(0.72f, 0.72f, 0.7f), 0.1f);
            Material("Barrera", Color.white, 0.25f);
            Material("Acera", new Color(0.6f, 0.6f, 0.6f), 0.1f);
            Material("SueloUrbano", new Color(0.42f, 0.43f, 0.44f), 0.1f);
            Material("Arena", new Color(0.84f, 0.73f, 0.55f), 0.05f);
            Material("Agua", new Color(0.12f, 0.36f, 0.55f), 0.85f);
            Material("Montana", new Color(0.33f, 0.43f, 0.32f), 0.05f);
            Material("MontanaAlta", new Color(0.52f, 0.55f, 0.6f), 0.05f);
            MaterialesDeLibrea();
            AssetDatabase.SaveAssets();

            // Si la escena ya está abierta, se actualizan sus referencias ahí mismo (no se puede
            // guardar una escena nueva encima de una abierta). Si no, se crea en modo aditivo:
            // así no se toca la escena que haya abierta ni se pierden sus cambios.
            var abierta = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(rutaEscena);
            bool yaAbierta = abierta.IsValid() && abierta.isLoaded;
            var escena = yaAbierta ? abierta : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            var raices = escena.GetRootGameObjects();
            MonoBehaviour banco = raices.Select(g => g.GetComponent(componente) as MonoBehaviour).FirstOrDefault(c => c != null);
            if (banco == null)
            {
                var luz = raices.Select(g => g.GetComponent<Light>()).FirstOrDefault(l => l != null);
                if (luz != null) luz.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var cam = raices.Select(g => g.GetComponent<Camera>()).First(c => c != null);
                if (cam.GetComponent<CamaraCircuito>() == null) cam.gameObject.AddComponent<CamaraCircuito>();
                cam.farClipPlane = 16000f;
                banco = (MonoBehaviour)new GameObject(componente.Name).AddComponent(componente);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(banco.gameObject, escena);
            }
            var so = new SerializedObject(banco);
            so.FindProperty("cochePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Coche);
            var mats = AssetDatabase.FindAssets("t:Material", new[] { MaterialesEscuderia })
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null)
                .OrderBy(m => m.name).ToArray();
            so.FindProperty("catalogo").objectReferenceValue = CrearCatalogo();
            var lista = so.FindProperty("materialesEscuderia");
            lista.arraySize = mats.Length;
            for (int i = 0; i < mats.Length; i++) lista.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            bool hayCoche = so.FindProperty("cochePrefab").objectReferenceValue != null;

            EditorSceneManager.MarkSceneDirty(escena);
            bool guardada = EditorSceneManager.SaveScene(escena, rutaEscena);
            if (!yaAbierta) EditorSceneManager.CloseScene(escena, true);
            if (!guardada) { Debug.LogError($"[F1] No se pudo guardar {rutaEscena}"); return; }
            var enBuild = EditorBuildSettings.scenes.ToList();
            enBuild.RemoveAll(s => s.path == rutaEscena);
            enBuild.Insert(primera ? 0 : Mathf.Min(1, enBuild.Count), new EditorBuildSettingsScene(rutaEscena, true));
            EditorBuildSettings.scenes = enBuild.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[F1] Escena {rutaEscena} {(yaAbierta ? "actualizada" : "creada")}: coche {(hayCoche ? "sí" : "NO")}, {mats.Length} escuderías.");
        }

        /// <summary>El catálogo de piezas del decorado (se rehace cada vez: es solo un índice).</summary>
        static CatalogoDecorado CrearCatalogo()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Catalogo));
            var c = AssetDatabase.LoadAssetAtPath<CatalogoDecorado>(Catalogo);
            if (c == null) { c = ScriptableObject.CreateInstance<CatalogoDecorado>(); AssetDatabase.CreateAsset(c, Catalogo); }
            GameObject K(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Kenney}/{n}.fbx");
            c.garaje = K("pitsGarage"); c.oficinaBoxes = K("pitsOfficeRoof");
            c.gradaCubierta = K("grandStandCovered"); c.grada = K("grandStand");
            c.porticoSalida = K("overheadLights");
            c.valla = K("billboard"); c.vallaBaja = K("billboardLow");
            c.torreRoja = K("bannerTowerRed"); c.torreVerde = K("bannerTowerGreen");
            c.farolaCircuito = K("lightPostModern"); c.banderaCuadros = K("flagCheckers");
            c.arbolGrande = K("treeLarge"); c.arbolPequeno = K("treeSmall"); c.carpa = K("tent");
            c.edificios = AssetDatabase.FindAssets("t:GameObject", new[] { Ciudad })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(g => g != null).OrderBy(g => g.name).ToArray();
            c.farolaCalle = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decorado/Calles/Farola.fbx");
            EditorUtility.SetDirty(c);
            int faltan = new[] { c.garaje, c.oficinaBoxes, c.gradaCubierta, c.porticoSalida, c.valla, c.torreRoja, c.arbolGrande, c.farolaCalle }.Count(x => x == null);
            Debug.Log($"[F1] Catálogo: {c.edificios.Length} edificios; piezas que faltan: {faltan}");
            return c;
        }

        /// <summary>Un material por librea, con el nombre de la escudería (el que usa la parrilla).</summary>
        static void MaterialesDeLibrea()
        {
            Directory.CreateDirectory(MaterialesEscuderia);
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { Libreas }))
            {
                string rutaTex = AssetDatabase.GUIDToAssetPath(g);
                string nombre = Path.GetFileNameWithoutExtension(rutaTex);
                string ruta = $"{MaterialesEscuderia}/{nombre}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(ruta) != null) continue;
                var m = new Material(Shader.Find("Standard")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(rutaTex) };
                m.SetFloat("_Glossiness", 0.55f);
                m.SetFloat("_Metallic", 0.1f);
                AssetDatabase.CreateAsset(m, ruta);
            }
        }

        static void Material(string nombre, Color color, float brillo)
        {
            string ruta = $"{CarpetaMateriales}/{nombre}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(ruta) != null) return;
            var m = new Material(Shader.Find("Standard")) { color = color };
            m.SetFloat("_Glossiness", brillo);
            AssetDatabase.CreateAsset(m, ruta);
        }
    }
}
