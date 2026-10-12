using System.Collections.Generic;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// El aspecto común de todas las pantallas: Century Gothic, paneles oscuros con acento rojo,
    /// y las piezas que se repiten (fotos de pilotos, banderas, planos, barras de valor).
    /// Todo en coordenadas de 1280 x 720 que se escalan a la pantalla.
    /// </summary>
    public static class Interfaz
    {
        public static readonly Color Fondo = new Color(0.05f, 0.06f, 0.09f, 0.92f);
        public static readonly Color FondoClaro = new Color(0.11f, 0.12f, 0.16f, 0.95f);
        public static readonly Color Rojo = new Color(0.88f, 0.1f, 0.12f);
        public static readonly Color Texto = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color Gris = new Color(0.62f, 0.65f, 0.7f);

        public static float Ancho { get; private set; } = 1280f;
        public static float Alto { get; private set; } = 720f;

        static GUIStyle iniciales;
        public static GUIStyle Titulo, Grande, Subtitulo, Normal, Pequeno, Boton, BotonGrande, BotonSel, Menu, Panel, Celda;
        static Font normal, negrita;
        static readonly Dictionary<Color, Texture2D> lisas = new Dictionary<Color, Texture2D>();
        static readonly Dictionary<string, Texture2D> imagenes = new Dictionary<string, Texture2D>();

        /// <summary>Al principio de cada OnGUI: escala y estilos.</summary>
        public static void Empezar()
        {
            float escala = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            escala = Mathf.Max(0.5f, escala);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * escala);
            Ancho = Screen.width / escala; Alto = Screen.height / escala;
            if (Titulo != null) return;

            normal = Resources.Load<Font>("Fuentes/texgyreadventor-regular");
            negrita = Resources.Load<Font>("Fuentes/texgyreadventor-bold");
            GUIStyle Texto_(int tam, bool bold, Color c) => new GUIStyle(GUI.skin.label)
            {
                font = bold ? negrita : normal, fontSize = tam, richText = true, wordWrap = true,
                normal = { textColor = c }, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 2, 2),
            };
            Titulo = Texto_(52, true, Color.white);
            Grande = Texto_(26, true, Color.white);
            Subtitulo = Texto_(18, true, Texto);
            Normal = Texto_(14, false, Texto);
            Pequeno = Texto_(12, false, Gris);
            Celda = Texto_(13, false, Texto); Celda.wordWrap = false; Celda.clipping = TextClipping.Clip;

            Boton = new GUIStyle(GUI.skin.button)
            {
                font = negrita, fontSize = 14, richText = true, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 7, 7),
                normal = { background = Lisa(new Color(0.16f, 0.17f, 0.22f)), textColor = Texto },
                hover = { background = Lisa(new Color(0.24f, 0.25f, 0.31f)), textColor = Color.white },
                active = { background = Lisa(Rojo), textColor = Color.white },
            };
            BotonSel = new GUIStyle(Boton) { normal = { background = Lisa(Rojo), textColor = Color.white }, hover = { background = Lisa(new Color(0.95f, 0.2f, 0.2f)), textColor = Color.white } };
            BotonGrande = new GUIStyle(BotonSel) { fontSize = 18, padding = new RectOffset(16, 16, 12, 12) };
            Menu = new GUIStyle(Boton)
            {
                fontSize = 19, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(22, 10, 10, 10),
                normal = { background = Lisa(new Color(1f, 1f, 1f, 0.04f)), textColor = Texto },
                hover = { background = Lisa(new Color(0.88f, 0.1f, 0.12f, 0.85f)), textColor = Color.white },
            };
            Panel = new GUIStyle { normal = { background = Lisa(Fondo) }, padding = new RectOffset(18, 18, 14, 14) };
        }

        public static Texture2D Lisa(Color c)
        {
            if (lisas.TryGetValue(c, out var t) && t != null) return t;
            t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply();
            return lisas[c] = t;
        }

        public static void Rect(Rect r, Color c) => GUI.DrawTexture(r, Lisa(c));

        public static Color ColorDe(string hex, Color siFalla = default)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : (siFalla == default ? Gris : siFalla);
        }

        public static Texture2D Imagen(string ruta)
        {
            if (string.IsNullOrEmpty(ruta)) return null;
            if (imagenes.TryGetValue(ruta, out var t)) return t;
            return imagenes[ruta] = Resources.Load<Texture2D>(ruta);
        }

        public static Texture2D Foto(Nucleo.Piloto p) => p == null ? null : Imagen("Pilotos/" + p.foto);
        public static Texture2D Bandera(string pais) => Imagen("Flags/" + pais);

        /// <summary>
        /// Plano dibujado a partir del trazado (para los circuitos sin plano oficial): línea
        /// negra gruesa sobre blanco, como los oficiales, y la salida marcada en rojo.
        /// </summary>
        public static Texture2D PlanoDibujado(string clave, System.Func<Nucleo.Trazado> trazado)
        {
            string k = "dibujado/" + clave;
            if (imagenes.TryGetValue(k, out var hecho)) return hecho;
            const int W = 800, H = 408, grosor = 7;
            var t = trazado();
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < t.Cantidad; i++) { var p = t.Punto(i); minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X); minZ = Mathf.Min(minZ, p.Z); maxZ = Mathf.Max(maxZ, p.Z); }
            float esc = Mathf.Min((W - 80) / (maxX - minX), (H - 60) / (maxZ - minZ));
            float ox = (W - (maxX - minX) * esc) / 2, oz = (H - (maxZ - minZ) * esc) / 2;
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            void Punto(int x, int y, Color32 c, int r)
            {
                for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < W && yy >= 0 && yy < H) px[yy * W + xx] = c;
                }
            }
            Vector2 A(int i) { var p = t.Punto(i); return new Vector2(ox + (p.X - minX) * esc, oz + (p.Z - minZ) * esc); }
            for (int i = 0; i < t.Cantidad; i++)
            {
                Vector2 a = A(i), b = A(i + 1);
                int pasos = Mathf.CeilToInt(Vector2.Distance(a, b)) + 1;
                for (int s = 0; s <= pasos; s++) { var q = Vector2.Lerp(a, b, s / (float)pasos); Punto((int)q.x, (int)q.y, new Color32(20, 20, 22, 255), grosor / 2); }
            }
            // Salida: punto rojo y flecha con el sentido de la carrera (unos 250 m de trazado).
            var rojo = new Color32(220, 30, 50, 255);
            var salida = A(0);
            int ade = 1;
            while (ade < t.Cantidad / 3 && Vector2.Distance(A(ade), salida) < 46f) ade++;
            Vector2 dir = (A(ade) - salida).normalized, normal = new Vector2(-dir.y, dir.x);
            Vector2 lado = normal * 16f;
            Vector2 desde = salida + lado, hasta = desde + dir * 40f;
            void Linea(Vector2 a, Vector2 b, int r)
            {
                int n = Mathf.CeilToInt(Vector2.Distance(a, b)) + 1;
                for (int s = 0; s <= n; s++) { var q = Vector2.Lerp(a, b, s / (float)n); Punto((int)q.x, (int)q.y, rojo, r); }
            }
            Linea(desde, hasta, 2);
            Linea(hasta, hasta - dir * 12f + normal * 8f, 2);
            Linea(hasta, hasta - dir * 12f - normal * 8f, 2);
            Punto((int)salida.x, (int)salida.y, rojo, grosor);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            return imagenes[k] = tex;
        }

        /// <summary>Foto del piloto con su marco del color de la escudería.</summary>
        public static void FotoPiloto(Rect r, Nucleo.Piloto p, string colorEquipo)
        {
            Rect(r, ColorDe(colorEquipo));
            var foto = Foto(p);
            var dentro = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            if (foto != null) GUI.DrawTexture(dentro, foto, ScaleMode.ScaleAndCrop);
            else
            {
                // Sin foto (la versión pública no lleva fotos): las iniciales sobre el color del equipo.
                Rect(dentro, FondoClaro);
                iniciales ??= new GUIStyle(Grande) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                iniciales.fontSize = Mathf.Clamp((int)(r.height * 0.42f), 9, 40);
                var partes = (p?.nombre ?? "?").Split(' ');
                string ini = partes.Length > 1 ? $"{partes[0][0]}{partes[partes.Length - 1][0]}" : partes[0].Substring(0, 1);
                GUI.Label(dentro, $"<color=#{ColorUtility.ToHtmlStringRGB(ColorDe(colorEquipo))}>{ini}</color>", iniciales);
            }
        }

        public static void BanderaEn(Rect r, string pais)
        {
            var t = Bandera(pais);
            if (t != null) GUI.DrawTexture(r, t, ScaleMode.ScaleToFit);
        }

        /// <summary>Barra de 0 a 1 con su rótulo (para los atributos).</summary>
        public static void Barra(Rect r, string rotulo, float valor, Color color)
        {
            GUI.Label(new Rect(r.x, r.y - 1, 84, r.height + 2), rotulo, Pequeno);
            var b = new Rect(r.x + 86, r.y + r.height * 0.3f, r.width - 86, r.height * 0.45f);
            Rect(b, new Color(1, 1, 1, 0.08f));
            Rect(new Rect(b.x, b.y, b.width * Mathf.Clamp01(valor), b.height), color);
        }

        /// <summary>
        /// El rótulo del piloto que sigue la cámara, como en la tele: icono de cámara, foto,
        /// nombre, escudería con su color y una línea de datos (puesto, diferencia, velocidad).
        /// </summary>
        public static void PanelSeguido(Rect r, Nucleo.Piloto p, Nucleo.Equipo e, string datos, string modo)
        {
            Color c = ColorDe(e.color);
            Rect(r, new Color(Fondo.r, Fondo.g, Fondo.b, 0.9f));
            Rect(new Rect(r.x, r.y, 6, r.height), c);
            FotoPiloto(new Rect(r.x + 12, r.y + 6, r.height - 12, r.height - 12), p, e.color);
            float x = r.x + r.height + 6;
            GUI.Label(new Rect(x, r.y + 5, r.width - (x - r.x) - 8, 22), $"<size=11><color=#e33>● CÁMARA</color>  <color=#9aa>{modo}</color></size>", Normal);
            GUI.Label(new Rect(x, r.y + 20, r.width - (x - r.x) - 8, 26), $"<b>{p.nombre}</b>  <size=13><color=#{ColorUtility.ToHtmlStringRGB(c)}>{e.nombre}</color></size>", Subtitulo);
            GUI.Label(new Rect(x, r.y + 44, r.width - (x - r.x) - 8, 20), datos, Pequeno);
        }

        /// <summary>
        /// Rótulo grande de una incidencia (abandono, salida de pista, toque), en rojo, con la
        /// foto del piloto. 'alfa' para que aparezca y se vaya suave.
        /// </summary>
        public static void Incidencia(Rect r, string titulo, Nucleo.Piloto p, Nucleo.Equipo e, string detalle, float alfa)
        {
            var antes = GUI.color;
            GUI.color = new Color(1, 1, 1, alfa);
            Rect(r, new Color(0.45f, 0.04f, 0.05f, 0.92f));
            Rect(new Rect(r.x, r.y, r.width, 3), Rojo);
            FotoPiloto(new Rect(r.x + 8, r.y + 8, r.height - 16, r.height - 16), p, e.color);
            float x = r.x + r.height;
            GUI.Label(new Rect(x, r.y + 6, r.width - (x - r.x) - 8, 26), $"<b>{titulo}</b>", Subtitulo);
            GUI.Label(new Rect(x, r.y + 30, r.width - (x - r.x) - 8, 22), $"<b>{p.nombre}</b> · {e.nombre} · {detalle}", Normal);
            GUI.color = antes;
        }

        /// <summary>Estrellas de favorito (0 a 3) en texto.</summary>
        public static string Estrellas(int n) => n <= 0 ? "" : $"<color=#fc4>{new string('★', n)}</color>";

        /// <summary>Panel oscuro con una franja roja a la izquierda.</summary>
        public static void Fondo_(Rect r, float alfa = 0.92f)
        {
            Rect(r, new Color(Fondo.r, Fondo.g, Fondo.b, alfa));
            Rect(new Rect(r.x, r.y, 4, r.height), Rojo);
        }
    }

    /// <summary>Opciones del jugador (se guardan en PlayerPrefs; si fallan, valores por defecto).</summary>
    public static class Opciones
    {
        public static bool Sonido { get => Leer("sonido", 1) == 1; set => Guardar("sonido", value ? 1 : 0); }
        public static float Volumen { get => Leer("volumen", 70) / 100f; set => Guardar("volumen", Mathf.RoundToInt(value * 100)); }
        public static int Ritmo { get => Leer("ritmo", 1); set => Guardar("ritmo", value); }        // índice de x0,5 x1 x2 x4 x8
        public static int Camara { get => Leer("camara", 1); set => Guardar("camara", value); }    // modo de CamaraCircuito

        static int Leer(string k, int porDefecto)
        {
            try { return PlayerPrefs.GetInt("F1." + k, porDefecto); } catch { return porDefecto; }
        }

        static void Guardar(string k, int v)
        {
            try { PlayerPrefs.SetInt("F1." + k, v); PlayerPrefs.Save(); } catch { }
        }
    }
}
