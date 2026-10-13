using System;
using System.Collections.Generic;
using System.Linq;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Reproduce en 3D una carrera ya simulada: cada coche va exactamente donde lo dejó la
    /// simulación en cada instante. Aquí no se decide nada; solo se enseña.
    /// </summary>
    public class ReproductorCarrera : MonoBehaviour
    {
        static readonly float[] Velocidades = { 0.5f, 1f, 2f, 4f, 8f };

        Carrera carrera;
        Clasificacion.Vuelta[] parrilla;
        Transform[] coches;
        CamaraCircuito camara;
        float t = -3f;                // unos segundos con la parrilla quieta antes de apagar el semáforo
        int velocidad = 1, seguido = -1;   // -1 = el líder

        /// <summary>Dónde van los mandos (cámara, ritmo): bajo el panel del banco de pruebas o arriba del todo.</summary>
        public float alturaMandos = 195f;

        public bool Terminada => carrera != null && carrera.Acabada && t > carrera.Duracion;

        /// <summary>Lo que la simulación va por delante de lo que se ve (si se simula en vivo).</summary>
        const float Adelanto = 1f;

        /// <summary>Se llama cuando se apaga el semáforo (para la señal de salida).</summary>
        public System.Action AlSalir;
        /// <summary>Velocidad (m/s) del coche que sigue la cámara, o null si no hay carrera en marcha.</summary>
        public float? VelocidadSeguido { get; private set; }
        public Transform Seguido => carrera == null ? null : coches[Objetivo(Mathf.Max(0f, t))];
        /// <summary>Favoritos del GP (estrellas en la torre); null si no se han calculado.</summary>
        public Favoritos favoritos;

        /// <summary>La goma en una letra de su color (B roja, M amarilla, D blanca, LL azul).</summary>
        public static string Goma(Carrera.Compuesto g) => g switch
        {
            Carrera.Compuesto.Blando => "<color=#ff4040><b>B</b></color>",
            Carrera.Compuesto.Duro => "<color=#f2f2f2><b>D</b></color>",
            Carrera.Compuesto.Lluvia => "<color=#4aa8ff><b>LL</b></color>",
            _ => "<color=#ffd84a><b>M</b></color>",
        };

        /// <summary>¿Está el coche de seguridad en pista en este instante (de los que se ven)?</summary>
        bool SeguridadEnPista(float ahora)
        {
            var u = carrera.Sucesos.FindLast(x => (x.tipo == Carrera.Tipo.CocheSeguridad || x.tipo == Carrera.Tipo.Relanzada) && x.t <= ahora);
            return u != null && u.tipo == Carrera.Tipo.CocheSeguridad;
        }

        static bool EsIncidencia(Carrera.Tipo x) => x == Carrera.Tipo.Error || x == Carrera.Tipo.Toque || x == Carrera.Tipo.Abandono;

        /// <summary>
        /// La incidencia más reciente alrededor de 'ahora' (de 1 s antes a 4 s después): la
        /// realización corta a ella un segundo antes de que pase, como en la tele.
        /// </summary>
        Carrera.Suceso Incidencia(float ahora, float antes = 1f, float despues = 4f)
        {
            Carrera.Suceso mejor = null;
            foreach (var x in carrera.Sucesos)
                if (EsIncidencia(x.tipo) && x.t <= ahora + antes && x.t > ahora - despues && (mejor == null || x.t > mejor.t)) mejor = x;
            return mejor;
        }

        /// <summary>A quién enseña la cámara: al elegido; si no, a la incidencia del momento o al líder.</summary>
        int Objetivo(float ahora)
        {
            if (seguido >= 0) return seguido;
            if (ahora > 0f)
            {
                var inc = Incidencia(ahora);
                if (inc != null) return inc.coche;
                var b = Pelea(ahora);
                if (b.atacante >= 0) return b.atacante;
            }
            return OrdenActual()[0];
        }

        // ── Realización: la pelea más apretada ────────────────────────────

        int peleaAtacante = -1;
        float peleaDesde;
        int peleaFrame = -1;
        (int atacante, int delantero, int puesto, float hueco) peleaCache = (-1, -1, 0, 0f);

        /// <summary>
        /// La pelea que enseña la cámara automática: la pareja más junta (menos de 1 s), con
        /// preferencia por los puestos de delante. Se queda en ella mientras siga viva (hasta 1,5 s y
        /// 25 s como mucho) para no saltar de una a otra. El líder solo ya no es lo que se ve.
        /// </summary>
        (int atacante, int delantero, int puesto, float hueco) Pelea(float ahora)
        {
            if (peleaFrame == Time.frameCount) return peleaCache;
            peleaFrame = Time.frameCount;
            var orden = OrdenActual();
            // ¿Sigue viva la de ahora?
            if (peleaAtacante >= 0)
            {
                int p = Array.IndexOf(orden, peleaAtacante);
                if (p > 0 && !Fuera(peleaAtacante, ahora) && Meta(peleaAtacante, ahora) < 0f && ahora - peleaDesde < 25f)
                {
                    float h = Hueco(orden[p - 1], peleaAtacante, ahora);
                    if (h < 1.5f) return peleaCache = (peleaAtacante, orden[p - 1], p, h);
                }
                peleaAtacante = -1;
            }
            int mejor = -1;
            float puntos = float.MaxValue, mejorHueco = 0f;
            for (int p = 1; p < Mathf.Min(orden.Length, 14); p++)
            {
                int a = orden[p], d = orden[p - 1];
                if (Fuera(a, ahora) || Fuera(d, ahora) || Meta(a, ahora) >= 0f || Meta(d, ahora) >= 0f) continue;
                float h = Hueco(d, a, ahora);
                if (h > 1f) continue;
                float valor = h + 0.05f * p;
                if (valor < puntos) { puntos = valor; mejor = p; mejorHueco = h; }
            }
            if (mejor < 0) return peleaCache = (-1, -1, 0, 0f);
            peleaAtacante = orden[mejor];
            peleaDesde = ahora;
            return peleaCache = (peleaAtacante, orden[mejor - 1], mejor, mejorHueco);
        }

        // ── El comentarista ───────────────────────────────────────────────

        /// <summary>
        /// Una frase para los momentos que importan: nuevo líder, coche de seguridad, relanzada,
        /// última vuelta, errores de los de delante y el ganador. Sale de los sucesos (lo que ha
        /// pasado en la simulación), no se inventa nada.
        /// </summary>
        int comentarioFrame = -1;
        (float cuando, string texto) comentarioCache;

        (float cuando, string texto) Comentario(float ahora)
        {
            if (comentarioFrame == Time.frameCount) return comentarioCache;
            comentarioFrame = Time.frameCount;
            return comentarioCache = CalcularComentario(ahora);
        }

        (float cuando, string texto) CalcularComentario(float ahora)
        {
            string N(int i) => carrera.Coches[i].piloto.corto;
            float L = carrera.Circuito.Trazado.Longitud;
            float mejorT = -1f;
            string mejorTexto = null;
            void Si(float cuando, string texto)
            {
                if (cuando <= ahora && cuando > ahora - 5f && cuando > mejorT) { mejorT = cuando; mejorTexto = texto; }
            }
            foreach (var x in carrera.Sucesos)
            {
                if (x.t > ahora || x.t < ahora - 5f) continue;
                switch (x.tipo)
                {
                    case Carrera.Tipo.Adelantamiento:
                        if (LiderEn(x.t + 0.3f) == x.coche && LiderEn(x.t - 1.5f) == x.otro)
                            Si(x.t, $"¡{N(x.coche)} se pone líder! Le ha ganado la frenada a {N(x.otro)}.");
                        break;
                    case Carrera.Tipo.CocheSeguridad:
                        Si(x.t, "Coche de seguridad. El pelotón se agrupa: se acabaron las ventajas.");
                        break;
                    case Carrera.Tipo.Relanzada:
                        Si(x.t, $"¡Relanzada! {N(LiderEn(x.t))} tiene que defenderse otra vez.");
                        break;
                    case Carrera.Tipo.Error:
                        int pe = PuestoEn(x.coche, x.t);
                        if (pe <= 5) Si(x.t, $"¡{N(x.coche)} se va largo! Pierde tiempo cuando iba {pe}.º.");
                        break;
                    case Carrera.Tipo.Meta:
                        if (carrera.Coches[x.coche] == carrera.Resultado[0]) Si(x.t, $"¡{N(x.coche)} gana el Gran Premio!");
                        break;
                }
            }
            // Última vuelta: cuando el líder cruza la meta y empieza la última.
            float ultima = (carrera.Vueltas - 1) * L;
            for (float tv = Mathf.Max(0.5f, ahora - 5f); tv <= ahora; tv += 0.5f)
            {
                int l = LiderEn(tv);
                if (carrera.Muestra(l, tv).d >= ultima && carrera.Muestra(l, tv - 0.5f).d < ultima)
                {
                    var orden = OrdenActual();
                    if (orden.Length > 1)
                        Si(tv, $"Última vuelta: {N(orden[0])} lidera con {Hueco(orden[0], orden[1], ahora):F1} s sobre {N(orden[1])}.");
                }
            }
            return (mejorT, mejorTexto);
        }

        int LiderEn(float tiempo)
        {
            int mejor = 0;
            float d = float.MinValue;
            for (int i = 0; i < carrera.Coches.Length; i++)
            {
                if (Fuera(i, tiempo)) continue;
                float di = carrera.Muestra(i, Mathf.Max(0f, tiempo)).d;
                if (di > d) { d = di; mejor = i; }
            }
            return mejor;
        }

        int PuestoEn(int coche, float tiempo)
        {
            float d = carrera.Muestra(coche, Mathf.Max(0f, tiempo)).d;
            int p = 1;
            for (int i = 0; i < carrera.Coches.Length; i++)
                if (i != coche && !Fuera(i, tiempo) && carrera.Muestra(i, Mathf.Max(0f, tiempo)).d > d) p++;
            return p;
        }
        public int VelocidadIndice { get => velocidad; set => velocidad = Mathf.Clamp(value, 0, Velocidades.Length - 1); }

        public void Iniciar(Carrera c, Clasificacion.Vuelta[] q, Transform[] cochesEnEscena, CamaraCircuito cam)
        {
            carrera = c; parrilla = q; coches = cochesEnEscena; camara = cam;
            carrera.Avanzar(Adelanto);
            camara.modo = CamaraCircuito.Modo.Persecucion;
            Colocar();
        }

        void Update()
        {
            if (carrera == null) return;
            float antes = t;
            t += Time.deltaTime * Velocidades[velocidad];
            carrera.Avanzar(Mathf.Max(0f, t) + Adelanto);
            if (antes < 0f && t >= 0f) AlSalir?.Invoke();
            Colocar();
            if (t < 0f || Terminada) VelocidadSeguido = null;
            else
            {
                int i = Objetivo(t);
                VelocidadSeguido = (carrera.Muestra(i, t).d - carrera.Muestra(i, Mathf.Max(0f, t - 0.2f)).d) / 0.2f;
            }
        }

        void Colocar()
        {
            float ahora = Mathf.Max(0f, t);
            var trazado = carrera.Circuito.Trazado;
            for (int i = 0; i < coches.Length; i++)
            {
                var (d, lat) = carrera.Muestra(i, ahora);
                var (d2, lat2) = carrera.Muestra(i, ahora + 0.2f);
                Pose p = ConstructorPista.Situar(trazado, d, lat);
                // Mirar hacia donde va a estar: así el coche gira al cambiar de carril.
                Vector3 hacia = ConstructorPista.Situar(trazado, d2 + 0.5f, lat2).position - p.position;
                coches[i].SetPositionAndRotation(p.position,
                    hacia.sqrMagnitude > 0.01f ? Quaternion.LookRotation(new Vector3(hacia.x, 0f, hacia.z), Vector3.up) : p.rotation);
            }
            camara.objetivo = coches[Objetivo(ahora)];
        }

        /// <summary>Orden en este instante: por distancia recorrida; los abandonos, al final.</summary>
        int[] OrdenActual()
        {
            float ahora = Mathf.Max(0f, t);
            var idx = Enumerable.Range(0, coches.Length).ToArray();
            Array.Sort(idx, (a, b) =>
            {
                // Quien ya ha cruzado la meta, por su paso por meta (la vuelta de enfriamiento
                // descoloca las distancias); luego los que corren, por distancia; los abandonos, al final.
                float ma = Meta(a, ahora), mb = Meta(b, ahora);
                if ((ma >= 0f) != (mb >= 0f)) return ma >= 0f ? -1 : 1;
                if (ma >= 0f) return ma.CompareTo(mb);
                bool fa = Fuera(a, ahora), fb = Fuera(b, ahora);
                if (fa != fb) return fa ? 1 : -1;
                return carrera.Muestra(b, ahora).d.CompareTo(carrera.Muestra(a, ahora).d);
            });
            return idx;
        }

        /// <summary>
        /// Segundos que van del líder a un coche: cuánto hace que el líder pasó por donde está
        /// ahora el coche (como los cronos de la tele). Antes se dividía la distancia por la
        /// velocidad y las diferencias no siempre crecían hacia abajo.
        /// </summary>
        float Hueco(int lider, int coche, float ahora)
        {
            float d = carrera.Muestra(coche, ahora).d;
            float lo = 0f, hi = ahora;
            if (carrera.Muestra(lider, lo).d >= d) return ahora;
            for (int k = 0; k < 24; k++)
            {
                float m = (lo + hi) * 0.5f;
                if (carrera.Muestra(lider, m).d < d) lo = m; else hi = m;
            }
            return ahora - hi;
        }

        /// <summary>Instante en que cruzó la meta final, o -1 si aún no.</summary>
        float Meta(int coche, float ahora)
        {
            var co = carrera.Coches[coche];
            return co.terminado && co.tiempoFinal <= ahora ? co.tiempoFinal : -1f;
        }

        bool Fuera(int coche, float ahora) =>
            carrera.Sucesos.Any(s => s.tipo == Carrera.Tipo.Abandono && s.coche == coche && s.t <= ahora);

        /// <summary>El juego pinta su propio resultado (con podio); el banco de pruebas, este.</summary>
        public bool mostrarResultado = true;

        void OnGUI()
        {
            if (carrera == null) return;
            Interfaz.Empezar();
            float ancho = Interfaz.Ancho, alto = Interfaz.Alto;
            float ahora = Mathf.Max(0f, t);
            var orden = OrdenActual();
            float L = carrera.Circuito.Trazado.Longitud;
            int sigue = Objetivo(ahora);

            // Torre de posiciones: color de escudería, nombre, diferencia y puestos ganados.
            const float fila = 21f;
            var torre = new Rect(ancho - 262, 12, 250, 40 + fila * coches.Length);
            Interfaz.Fondo_(torre, 0.85f);
            int vueltaLider = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, carrera.Muestra(orden[0], ahora).d) / L) + 1, 1, carrera.Vueltas);
            GUI.Label(new Rect(torre.x + 14, torre.y + 8, 220, 24),
                t < 0f ? "PARRILLA" : Terminada ? "BANDERA A CUADROS" :
                SeguridadEnPista(ahora) ? $"<color=#fd3>COCHE DE SEGURIDAD</color> <size=12>V{vueltaLider}</size>" : $"VUELTA {vueltaLider}/{carrera.Vueltas}", Interfaz.Subtitulo);
            for (int p = 0; p < orden.Length; p++)
            {
                int i = orden[p];
                var co = carrera.Coches[i];
                float y = torre.y + 36 + p * fila;
                bool sel = i == sigue;
                if (sel)
                {
                    // La fila que sigue la cámara: fondo del color de su escudería y una marca roja.
                    var ce = Interfaz.ColorDe(co.equipo.color);
                    Interfaz.Rect(new Rect(torre.x + 4, y, torre.width - 8, fila - 1), new Color(ce.r, ce.g, ce.b, 0.35f));
                    Interfaz.Rect(new Rect(torre.x - 10, y + 4, 8, fila - 9), Interfaz.Rojo);
                }
                Interfaz.Rect(new Rect(torre.x + 34, y + 3, 4, fila - 7), Interfaz.ColorDe(co.equipo.color));
                GUI.Label(new Rect(torre.x + 10, y + 2, 24, fila), $"{p + 1}", Interfaz.Celda);
                string estrellas = favoritos != null ? " " + Interfaz.Estrellas(favoritos.Estrellas(co.piloto.nombre)) : "";
                if (GUI.Button(new Rect(torre.x + 44, y + 2, 122, fila), $"{(sel ? "<b>" : "")}{co.piloto.corto}{(sel ? "</b>" : "")}<size=9>{estrellas}</size> <size=10>{Goma(co.goma)}</size>", Interfaz.Celda))
                {
                    // Elegir un piloto: la cámara de detrás del coche, para ver su carrera.
                    if (seguido == i) seguido = -1;
                    else { seguido = i; camara.modo = CamaraCircuito.Modo.Detras; }
                }
                string tras = "";
                if (Fuera(i, ahora)) tras = "<color=#e66>fuera</color>";
                else if (p > 0)
                {
                    float mi = Meta(i, ahora), m0 = Meta(orden[0], ahora);
                    if (mi >= 0f && m0 >= 0f) tras = $"+{mi - m0:F1}";
                    else tras = $"+{Hueco(orden[0], i, ahora):F1}";
                }
                string cambio = co.salida > p ? $"<color=#6c6>▲{co.salida - p}</color>" : co.salida < p ? $"<color=#e66>▼{p - co.salida}</color>" : "";
                GUI.Label(new Rect(torre.x + 166, y + 2, 50, fila), tras, Interfaz.Celda);
                GUI.Label(new Rect(torre.x + 216, y + 2, 32, fila), cambio, Interfaz.Celda);
            }

            // El piloto que sigue la cámara, arriba entre los mandos y la torre.
            {
                var co = carrera.Coches[sigue];
                int puesto = Array.IndexOf(orden, sigue) + 1;
                float hueco = puesto > 1 ? Hueco(orden[0], sigue, ahora) : 0f;
                string datos = Fuera(sigue, ahora) ? "<color=#e66>fuera de carrera</color>" :
                    $"<b>{puesto}.º</b>{(puesto > 1 ? $"  a {hueco:F1} s del líder" : "  en cabeza")}  ·  sale {co.salida + 1}.º  ·  {(VelocidadSeguido ?? 0f) * 3.6f:F0} km/h";
                string modo = camara.NombreModo + (seguido < 0 ? " · automática" : "");
                Interfaz.PanelSeguido(new Rect(ancho - 262 - 14 - 450, 12, 450, 66), co.piloto, co.equipo, datos, modo);
            }

            // Incidencia del momento: rótulo grande debajo, 4 s.
            var inc = t >= 0f ? Incidencia(ahora, 0f, 4f) : null;
            if (inc != null)
            {
                var co = carrera.Coches[inc.coche];
                string titulo = inc.tipo == Carrera.Tipo.Abandono ? "ABANDONO" : inc.tipo == Carrera.Tipo.Toque ? "TOQUE" : "SE VA LARGO";
                string detalle = inc.tipo == Carrera.Tipo.Abandono ? $"{inc.texto} · vuelta {inc.vuelta}" :
                                 inc.tipo == Carrera.Tipo.Toque ? $"alerón dañado · vuelta {inc.vuelta}" : $"pierde tiempo · vuelta {inc.vuelta}";
                float edad = ahora - inc.t, alfa = Mathf.Clamp01(Mathf.Min(edad * 4f, (4f - edad) * 2f));
                Interfaz.Incidencia(new Rect(ancho - 262 - 14 - 450, 86, 450, 60), titulo, co.piloto, co.equipo, detalle, alfa);
            }

            // Coche de seguridad: rótulo amarillo al salir y al relanzar (4 s).
            var sc = carrera.Sucesos.FindLast(x => (x.tipo == Carrera.Tipo.CocheSeguridad || x.tipo == Carrera.Tipo.Relanzada) && x.t <= ahora && x.t > ahora - 4f);
            if (sc != null && t >= 0f)
            {
                var caja = new Rect(ancho - 262 - 14 - 450, inc != null ? 152 : 86, 450, 46);
                Interfaz.Rect(caja, new Color(0.16f, 0.14f, 0.02f, 0.92f));
                Interfaz.Rect(new Rect(caja.x, caja.y, caja.width, 3), new Color(1f, 0.85f, 0.15f));
                GUI.Label(new Rect(caja.x + 14, caja.y + 10, caja.width - 20, 28), sc.tipo == Carrera.Tipo.CocheSeguridad
                    ? "<color=#fd3><b>COCHE DE SEGURIDAD</b></color>  <size=13>en pista: no se adelanta</size>"
                    : "<color=#fd3><b>RELANZADA</b></color>  <size=13>¡vuelven a correr!</size>", Interfaz.Subtitulo);
            }

            // La pelea que sigue la cámara automática y el comentarista, abajo en el centro.
            if (t >= 0f && !Terminada)
            {
                (int atacante, int delantero, int puesto, float hueco) b = seguido < 0 && Incidencia(ahora) == null ? Pelea(ahora) : (-1, -1, 0, 0f);
                float yAbajo = alto - 70f;
                if (b.atacante >= 0)
                {
                    var caja = new Rect(ancho / 2f - 230f, yAbajo - 40f, 460f, 34f);
                    Interfaz.Rect(caja, new Color(0.02f, 0.03f, 0.05f, 0.85f));
                    Interfaz.Rect(new Rect(caja.x, caja.y, 5f, caja.height), Interfaz.ColorDe(carrera.Coches[b.atacante].equipo.color));
                    Interfaz.Rect(new Rect(caja.xMax - 5f, caja.y, 5f, caja.height), Interfaz.ColorDe(carrera.Coches[b.delantero].equipo.color));
                    GUI.Label(new Rect(caja.x + 14, caja.y + 8, caja.width - 28, 22),
                        $"<color=#e33><b>LUCHA POR EL {b.puesto}.º</b></color>   <b>{carrera.Coches[b.atacante].piloto.corto}</b> a {b.hueco:F1} s de <b>{carrera.Coches[b.delantero].piloto.corto}</b>", Interfaz.Normal);
                    yAbajo -= 44f;
                }
                var com = Comentario(ahora);
                if (com.texto != null)
                {
                    var caja = new Rect(ancho / 2f - 300f, yAbajo - 40f, 600f, 34f);
                    Interfaz.Rect(caja, new Color(1f, 1f, 1f, 0.92f));
                    GUI.Label(new Rect(caja.x + 14, caja.y + 7, caja.width - 28, 24), $"<color=#111><i>«{com.texto}»</i></color>", Interfaz.Normal);
                }
            }

            // Sucesos recientes, abajo a la izquierda.
            // La radio se apunta en el tiempo de la simulación (que va por delante): se enseña en el acto.
            float Cuando(Carrera.Suceso s) => s.tipo == Carrera.Tipo.Radio ? s.t - Adelanto : s.t;
            var recientes = carrera.Sucesos.Where(s => Cuando(s) <= ahora && Cuando(s) > ahora - 8f && s.tipo != Carrera.Tipo.Meta && s.tipo != Carrera.Tipo.VueltaRapida).Reverse().Take(4).ToList();
            for (int k = 0; k < recientes.Count; k++)
            {
                var r = new Rect(12, alto - 44 - k * 30, 470, 26);
                Interfaz.Rect(r, new Color(0, 0, 0, 0.55f));
                Interfaz.Rect(new Rect(r.x, r.y, 3, r.height), Interfaz.Rojo);
                GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 12, r.height), Texto(recientes[k]), Interfaz.Celda);
            }

            // Mandos.
            GUILayout.BeginArea(new Rect(12, alturaMandos, 520, 40));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Cámara: {camara.NombreModo}", Interfaz.Boton)) camara.Siguiente();
            if (GUILayout.Button($"Ritmo x{Velocidades[velocidad]}", Interfaz.Boton)) velocidad = (velocidad + 1) % Velocidades.Length;
            if (GUILayout.Button("Cámara automática", Interfaz.Boton)) seguido = -1;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (Terminada && mostrarResultado) Resultados(ancho, alto);
        }

        void Resultados(float ancho, float alto)
        {
            var r = carrera.Resultado;
            var vr = carrera.Sucesos.FirstOrDefault(s => s.tipo == Carrera.Tipo.VueltaRapida);
            float x = 450f, w = Mathf.Max(380f, ancho - 280f - x);
            var caja = new Rect(x, 40, w, 50 + 20 * r.Length);
            Interfaz.Fondo_(caja);
            GUI.Label(new Rect(x + 16, 48, w - 20, 24), "RESULTADO" + (vr != null ? $"   <size=12>vuelta rápida: {carrera.Coches[vr.coche].piloto.corto} {Crono(vr.valor)}</size>" : ""), Interfaz.Subtitulo);
            float t0 = r[0].tiempoFinal;
            for (int p = 0; p < r.Length; p++)
            {
                var co = r[p];
                float y = 78 + p * 20;
                string tiempo = !co.terminado ? (co.motivo ?? "Abandono") : p == 0 ? Crono(co.tiempoFinal) : co.vueltas < r[0].vueltas ? $"+{r[0].vueltas - co.vueltas} v" : $"+{co.tiempoFinal - t0:F3}";
                int pts = co.terminado ? Puntos.PorPuesto(p + 1) : 0;
                Interfaz.Rect(new Rect(x + 40, y + 3, 4, 14), Interfaz.ColorDe(co.equipo.color));
                GUI.Label(new Rect(x + 16, y, 24, 20), $"{p + 1}", Interfaz.Celda);
                GUI.Label(new Rect(x + 50, y, 130, 20), co.piloto.corto, Interfaz.Celda);
                GUI.Label(new Rect(x + 180, y, 100, 20), $"<color=#9aa>{co.equipo.nombre}</color>", Interfaz.Celda);
                GUI.Label(new Rect(x + 280, y, 80, 20), tiempo, Interfaz.Celda);
                GUI.Label(new Rect(x + 360, y, 60, 20), $"<color=#789>sale {co.salida + 1}</color>", Interfaz.Celda);
                if (pts > 0) GUI.Label(new Rect(x + 420, y, 60, 20), $"<b>{pts} pts</b>", Interfaz.Celda);
            }
        }

        string Texto(Carrera.Suceso s)
        {
            string a = carrera.Coches[s.coche].piloto.corto;
            switch (s.tipo)
            {
                case Carrera.Tipo.Adelantamiento: return $"Vuelta {s.vuelta}: <b>{a}</b> adelanta a {carrera.Coches[s.otro].piloto.corto}";
                case Carrera.Tipo.Error: return $"Vuelta {s.vuelta}: <b>{a}</b> se va largo";
                case Carrera.Tipo.Toque: return $"Vuelta {s.vuelta}: <b>{a}</b> toca y daña el alerón";
                case Carrera.Tipo.Abandono: return $"Vuelta {s.vuelta}: <color=#e66><b>{a}</b> abandona ({s.texto})</color>";
                case Carrera.Tipo.OrdenDeEquipo: return $"Vuelta {s.vuelta}: orden de equipo, {carrera.Coches[s.otro].piloto.corto} deja pasar a <b>{a}</b>";
                case Carrera.Tipo.Radio: return $"<color=#8cf>Radio a <b>{a}</b>: {Radio(s.texto)}</color>";
                case Carrera.Tipo.CocheSeguridad: return $"Vuelta {s.vuelta}: <color=#fd3><b>coche de seguridad</b></color>";
                case Carrera.Tipo.Relanzada: return $"Vuelta {s.vuelta}: <color=#fd3><b>relanzada</b></color>";
                default: return "";
            }
        }

        public static string Radio(string orden) => orden switch
        {
            nameof(Carrera.OrdenPiloto.Atacar) => "«¡Ataca, a por él!»",
            nameof(Carrera.OrdenPiloto.Defender) => "«Defiende la posición»",
            nameof(Carrera.OrdenPiloto.Ahorrar) => "«Ahorra motor, levanta el pie»",
            _ => "«Ritmo normal»",
        };

        static string Crono(float s) => $"{(int)(s / 60)}:{s % 60:00.000}";
    }
}
