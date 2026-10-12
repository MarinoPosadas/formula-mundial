using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace F1.Juego.Editor
{
    /// <summary>
    /// Al abrir el proyecto recién clonado, Unity no sabe qué escena había abierta y arranca con
    /// una vacía: al pulsar Play no pasa nada. Si lo que hay abierto es esa escena vacía sin
    /// título (y sin cambios), se abre la del juego. Solo una vez por sesión del editor, y nunca
    /// toca una escena con nombre o con cambios.
    /// </summary>
    [InitializeOnLoad]
    static class AbrirEscenaDelJuego
    {
        const string Escena = "Assets/Juego/Escenas/Juego.unity";
        const string Clave = "F1.EscenaDelJuegoAbierta";

        static AbrirEscenaDelJuego() => EditorApplication.delayCall += Abrir;

        static void Abrir()
        {
            if (SessionState.GetBool(Clave, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(Clave, true);
            var activa = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount > 1 || !string.IsNullOrEmpty(activa.path) || activa.isDirty) return;
            if (File.Exists(Escena)) EditorSceneManager.OpenScene(Escena);
        }
    }
}
