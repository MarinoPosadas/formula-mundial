using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Que se vea que llueve: menos luz, neblina gris más cercana y gotas que acompañan a la
    /// cámara. Solo aspecto; lo que cambia la carrera lo decide la simulación (Clima).
    /// </summary>
    public static class Lluvia
    {
        static Light sol;
        static float luzSeco = -1f;
        static Color ambienteSeco;
        static float intensidadSeco;
        static GameObject gotas;
        static CameraClearFlags fondoSeco;
        static Color colorSeco;
        static bool guardado;

        public static bool Activa { get; private set; }

        public static void Poner(bool lluvia, Camera cam)
        {
            if (sol == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sol = l; break; }
            if (luzSeco < 0f && sol != null) { luzSeco = sol.intensity; ambienteSeco = RenderSettings.ambientLight; intensidadSeco = RenderSettings.ambientIntensity; }

            Activa = lluvia;
            if (sol != null) sol.intensity = lluvia ? luzSeco * 0.45f : luzSeco;
            RenderSettings.ambientLight = lluvia ? ambienteSeco * 0.65f : ambienteSeco;
            // La escena toma el ambiente del cielo (modo Skybox): ahí el color no cuenta, la intensidad sí.
            RenderSettings.ambientIntensity = lluvia ? intensidadSeco * 0.55f : intensidadSeco;
            Entorno.Niebla(RenderSettings.fog);           // repinta la niebla con el color que toca

            // El cielo no lo tapa la niebla: con lluvia, un fondo gris liso del mismo color.
            if (cam != null)
            {
                if (!guardado) { fondoSeco = cam.clearFlags; colorSeco = cam.backgroundColor; guardado = true; }
                cam.clearFlags = lluvia ? CameraClearFlags.SolidColor : fondoSeco;
                cam.backgroundColor = lluvia ? new Color(0.52f, 0.55f, 0.6f) : colorSeco;
            }
            if (lluvia && gotas == null && cam != null) gotas = CrearGotas(cam.transform);
            if (gotas != null) gotas.SetActive(lluvia);
        }

        /// <summary>Para la prueba de humo: cómo está de verdad la lluvia en el ejecutable.</summary>
        public static string Estado()
        {
            var ps = gotas != null ? gotas.GetComponent<ParticleSystem>() : null;
            var r = gotas != null ? gotas.GetComponent<ParticleSystemRenderer>() : null;
            return $"lluvia: activa={Activa} sol={(sol != null ? sol.intensity.ToString("F2") + "/" + luzSeco.ToString("F2") : "no hay")} " +
                   $"ambiente={RenderSettings.ambientLight} modoAmbiente={RenderSettings.ambientMode} niebla={RenderSettings.fog} {RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance} " +
                   $"gotas={(gotas != null ? gotas.activeInHierarchy.ToString() : "no hay")} particulas={(ps != null ? ps.particleCount : -1)} " +
                   $"material={(r != null && r.sharedMaterial != null ? r.sharedMaterial.shader.name + " soportado=" + r.sharedMaterial.shader.isSupported : "ninguno")}";
        }

        static GameObject CrearGotas(Transform cam)
        {
            var go = new GameObject("Lluvia");
            go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, 12f, 14f);   // por delante: las más cercanas a la cámara parecían barras
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.startLifetime = 1.1f; m.startSpeed = 30f; m.startSize = 0.045f;
            m.maxParticles = 12000; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = new Color(0.85f, 0.88f, 0.95f, 0.7f);
            var e = ps.emission; e.rateOverTime = 9000f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60f, 1f, 60f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);          // caen hacia abajo
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2.5f; r.velocityScale = 0.03f;
            // Sprites/Default viaja siempre en la build (un shader de partículas podría no estar).
            r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.85f, 0.9f, 1f, 0.5f) };
            ps.Play();
            return go;
        }
    }
}
