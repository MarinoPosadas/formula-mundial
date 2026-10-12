using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace F1.Juego.Editor
{
    /// <summary>
    /// F1 ▸ Comprobar escena del juego: que la escena tiene el coche, las once escuderías con su
    /// librea y el catálogo del decorado completo. Sirve por lotes (para la copia pública, donde
    /// el coche se sustituye por otro):
    ///   Unity -batchmode -quit -projectPath ... -executeMethod F1.Juego.Editor.Comprobar.PorLotes
    /// Escribe "COMPROBACION OK" o "COMPROBACION FALLA: ..." en el registro y sale con 0 o 1.
    /// </summary>
    public static class Comprobar
    {
        const string Escena = "Assets/Juego/Escenas/Juego.unity";

        [MenuItem("F1/Comprobar escena del juego")]
        public static void Menu() => Debug.Log(Resultado());

        public static void PorLotes()
        {
            string r = Resultado();
            Debug.Log(r);
            EditorApplication.Exit(r.StartsWith("COMPROBACION OK") ? 0 : 1);
        }

        static string Resultado()
        {
            var escena = EditorSceneManager.OpenScene(Escena, OpenSceneMode.Additive);
            try
            {
                var c = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ControladorJuego>(true)).FirstOrDefault();
                if (c == null) return "COMPROBACION FALLA: la escena no tiene ControladorJuego";
                var so = new SerializedObject(c);
                var coche = so.FindProperty("cochePrefab").objectReferenceValue as GameObject;
                if (coche == null) return "COMPROBACION FALLA: sin coche";
                var mats = so.FindProperty("materialesEscuderia");
                int conTextura = 0;
                for (int i = 0; i < mats.arraySize; i++)
                    if (mats.GetArrayElementAtIndex(i).objectReferenceValue is Material m && m.mainTexture != null) conTextura++;
                var cat = so.FindProperty("catalogo").objectReferenceValue as CatalogoDecorado;
                if (cat == null || cat.farolaCalle == null) return "COMPROBACION FALLA: catálogo sin farola";
                bool casco = coche.GetComponentsInChildren<Renderer>(true).Any(r => r.name == "Casco");
                return conTextura == 11 && casco
                    ? $"COMPROBACION OK: coche {coche.name} con casco, {conTextura} libreas, catálogo completo"
                    : $"COMPROBACION FALLA: coche {coche.name}, casco {casco}, {conTextura} de 11 libreas con textura";
            }
            finally
            {
                EditorSceneManager.CloseScene(escena, true);
            }
        }
    }
}
