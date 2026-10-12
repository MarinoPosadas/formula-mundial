using UnityEngine;

namespace F1.Juego
{
    /// <summary>Crea coches a escala real con los colores de su escudería.</summary>
    public static class FabricaCoches
    {
        public const float LargoCoche = 5.63f;    // un F1 de 2026 mide ~5,6 m
        /// <summary>El F1_2026.fbx tiene el morro hacia -Z; Unity avanza hacia +Z.</summary>
        public const float GiroModelo = 180f;

        /// <summary>Crea un coche con su librea; el casco del piloto, del color indicado (#RRGGBB).</summary>
        public static GameObject Crear(GameObject prefab, Pose pose, Material mat, Transform padre, string nombre = "Coche", string colorCasco = null)
        {
            var raiz = new GameObject(nombre);
            raiz.transform.SetParent(padre, false);
            var modelo = Object.Instantiate(prefab, raiz.transform);
            modelo.transform.localPosition = Vector3.zero;
            modelo.transform.localRotation = Quaternion.Euler(0f, GiroModelo, 0f);

            // Escala y altura a partir de lo que mide el modelo de verdad.
            Bounds b = Medir(modelo);
            float largo = Mathf.Max(b.size.x, b.size.z);
            modelo.transform.localScale *= largo > 0.01f ? LargoCoche / largo : 1f;
            b = Medir(modelo);
            modelo.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

            if (mat != null)
                foreach (var r in modelo.GetComponentsInChildren<Renderer>())
                {
                    // El coche (ruedas incluidas) usa un único atlas: cada escudería es el mismo
                    // atlas con sus colores. El casco y la visera del piloto van aparte.
                    Material m = r.name == "Casco" ? Casco(mat, colorCasco) : r.name == "Visera" ? Visera(mat) : mat;
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = m;
                    r.sharedMaterials = ms;
                }
            raiz.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return raiz;
        }

        static readonly System.Collections.Generic.Dictionary<string, Material> cascos = new System.Collections.Generic.Dictionary<string, Material>();
        static Material visera;

        static Material Casco(Material base_, string hex)
        {
            hex ??= "#DDDDDD";
            if (cascos.TryGetValue(hex, out var m) && m != null) return m;
            ColorUtility.TryParseHtmlString(hex, out var c);
            m = new Material(base_) { mainTexture = null, color = c, name = "Casco " + hex };
            m.SetFloat("_Glossiness", 0.75f);
            return cascos[hex] = m;
        }

        static Material Visera(Material base_)
        {
            if (visera != null) return visera;
            visera = new Material(base_) { mainTexture = null, color = new Color(0.02f, 0.02f, 0.03f), name = "Visera" };
            visera.SetFloat("_Glossiness", 0.95f);
            return visera;
        }

        public static Material Material(Material[] materiales, string nombre) =>
            materiales == null ? null : System.Array.Find(materiales, m => m != null && m.name == nombre);

        static Bounds Medir(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center -= go.transform.parent.position;
            return b;
        }
    }
}
