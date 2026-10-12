using System;
using System.Linq;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Enseña la clasificación en pista: los coches salen por turno a su vuelta lanzada y la
    /// torre de tiempos se reordena a medida que cruzan la meta. Los tiempos son los que ya
    /// decidió la simulación (SesionClasificacion solo los reparte en el tiempo).
    /// </summary>
    public class ReproductorClasificacion : MonoBehaviour
    {
        static readonly float[] Velocidades = { 0.5f, 1f, 2f, 4f, 8f };

        SesionClasificacion sesion;
        Transform[] coches;               // en el orden de la sesión (orden de salida a pista)
        CamaraCircuito camara;
        float t;
        int velocidad = 1, seguido = -1;  // -1 = automático
        string equipoJugador;

        public bool Terminada => sesion != null && t > sesion.Duracion;
        public float? VelocidadSeguido { get; private set; }
        public Transform Seguido { get; private set; }
        public int VelocidadIndice { get => velocidad; set => velocidad = Mathf.Clamp(value, 0, Velocidades.Length - 1); }

        public void Iniciar(SesionClasificacion s, Transform[] cochesEnOrdenDeSalida, CamaraCircuito cam, string equipoJugador = null)
        {
            sesion = s; coches = cochesEnOrdenDeSalida; camara = cam; this.equipoJugador = equipoJugador;
            Colocar();
        }

        void Update()
        {
            if (sesion == null) return;
            t += Time.deltaTime * Velocidades[velocidad];
            Colocar();
        }

        /// <summary>
        /// A quién sigue la cámara: al que se ha elegido o, si no, al que antes vaya a cruzar la
        /// meta de los que están en su vuelta lanzada (así se ve cada tiempo al marcarse).
        /// </summary>
        int Objetivo()
        {
            if (seguido >= 0 && sesion.EnPista(seguido, t)) return seguido;
            int mejor = -1;
            for (int k = 0; k < coches.Length; k++)
                if (sesion.EnPista(k, t) && t < sesion.Coches[k].fin + 1.5f && (mejor < 0 || sesion.Coches[k].fin < sesion.Coches[mejor].fin)) mejor = k;
            if (mejor >= 0) return mejor;
            for (int k = coches.Length - 1; k >= 0; k--) if (sesion.EnPista(k, t)) return k;
            return coches.Length - 1;
        }

        void Colocar()
        {
            var trazado = sesion.Circuito.Trazado;
            for (int k = 0; k < coches.Length; k++)
            {
                bool en = sesion.EnPista(k, t);
                if (coches[k].gameObject.activeSelf != en) coches[k].gameObject.SetActive(en);
                if (!en) continue;
                var (d, lat) = sesion.Muestra(k, t);
                var (d2, lat2) = sesion.Muestra(k, t + 0.2f);
                Pose p = ConstructorPista.Situar(trazado, d, lat);
                Vector3 hacia = ConstructorPista.Situar(trazado, d2 + 0.5f, lat2).position - p.position;
                coches[k].SetPositionAndRotation(p.position,
                    hacia.sqrMagnitude > 0.01f ? Quaternion.LookRotation(new Vector3(hacia.x, 0f, hacia.z), Vector3.up) : p.rotation);
            }
            int o = Objetivo();
            Seguido = sesion.EnPista(o, t) ? coches[o] : null;
            camara.objetivo = Seguido;
            if (Seguido == null || Terminada) VelocidadSeguido = null;
            else VelocidadSeguido = (sesion.Muestra(o, t).d - sesion.Muestra(o, Mathf.Max(0f, t - 0.2f)).d) / 0.2f;
        }

        void OnGUI()
        {
            if (sesion == null) return;
            Interfaz.Empezar();
            float ancho = Interfaz.Ancho;
            int n = coches.Length;

            // Orden provisional: los que ya tienen tiempo, por tiempo; luego los que están en
            // vuelta (por lo que llevan recorrido) y al final los que aún no han salido.
            var idx = Enumerable.Range(0, n).ToArray();
            int Estado(int k) => t >= sesion.Coches[k].fin ? 0 : t >= sesion.Coches[k].aparece ? 1 : 2;
            Array.Sort(idx, (a, b) =>
            {
                int ea = Estado(a), eb = Estado(b);
                if (ea != eb) return ea.CompareTo(eb);
                if (ea == 0) return sesion.Coches[a].vuelta.segundos.CompareTo(sesion.Coches[b].vuelta.segundos);
                if (ea == 1) return sesion.Muestra(b, t).d.CompareTo(sesion.Muestra(a, t).d);
                return a.CompareTo(b);
            });
            float pole = float.MaxValue;
            for (int k = 0; k < n; k++) if (Estado(k) == 0) pole = Mathf.Min(pole, sesion.Coches[k].vuelta.segundos);
            int sig = Objetivo();

            const float fila = 21f;
            var torre = new Rect(ancho - 292, 12, 280, 40 + fila * n);
            Interfaz.Fondo_(torre, 0.85f);
            int conTiempo = idx.Count(k => Estado(k) == 0);
            GUI.Label(new Rect(torre.x + 14, torre.y + 8, 260, 24), Terminada ? "CLASIFICACIÓN · FINAL" : $"CLASIFICACIÓN · {conTiempo}/{n}", Interfaz.Subtitulo);
            for (int p = 0; p < n; p++)
            {
                int k = idx[p];
                var co = sesion.Coches[k];
                var v = co.vuelta;
                float y = torre.y + 36 + p * fila;
                bool sel = k == sig;
                bool mio = equipoJugador != null && v.equipo.id == equipoJugador;
                if (sel)
                {
                    var ce = Interfaz.ColorDe(v.equipo.color);
                    Interfaz.Rect(new Rect(torre.x + 4, y, torre.width - 8, fila - 1), new Color(ce.r, ce.g, ce.b, 0.35f));
                    Interfaz.Rect(new Rect(torre.x - 10, y + 4, 8, fila - 9), Interfaz.Rojo);
                }
                Interfaz.Rect(new Rect(torre.x + 34, y + 3, 4, fila - 7), Interfaz.ColorDe(v.equipo.color));
                int e = Estado(k);
                GUI.Label(new Rect(torre.x + 10, y + 2, 24, fila), e == 0 ? $"{p + 1}" : "", Interfaz.Celda);
                string nombre = (mio || sel ? "<b>" : "") + v.piloto.corto + (mio || sel ? "</b>" : "");
                if (GUI.Button(new Rect(torre.x + 44, y + 2, 120, fila), nombre, Interfaz.Celda) && e != 2)
                {
                    if (seguido == k) seguido = -1;
                    else { seguido = k; camara.modo = CamaraCircuito.Modo.Detras; }
                }
                string tiempo;
                if (e == 0) tiempo = v.segundos <= pole ? Crono(v.segundos) : $"+{v.segundos - pole:F3}";
                else if (e == 1) tiempo = t < co.inicio ? "<color=#9aa>lanzada</color>" : $"<color=#fd6>{Crono(t - co.inicio, 1)}</color>";
                else tiempo = "<color=#678>en boxes</color>";
                GUI.Label(new Rect(torre.x + 168, y + 2, 76, fila), tiempo, Interfaz.Celda);
                // Recién marcado: el puesto con el que entra.
                if (e == 0 && t - co.fin < 4f) GUI.Label(new Rect(torre.x + 246, y + 2, 32, fila), v.segundos <= pole ? "<color=#c6f>POLE</color>" : "<color=#6c6>●</color>", Interfaz.Celda);
            }

            // El piloto que sigue la cámara.
            if (Seguido != null)
            {
                var co = sesion.Coches[sig];
                string datos = t < co.inicio ? "vuelta de lanzamiento" :
                    t < co.fin ? $"vuelta lanzada: {Crono(t - co.inicio, 1)}  ·  {(VelocidadSeguido ?? 0f) * 3.6f:F0} km/h" :
                    $"<b>{Crono(co.vuelta.segundos)}</b>  ·  {Array.IndexOf(idx, sig) + 1}.º provisional";
                Interfaz.PanelSeguido(new Rect(ancho - 292 - 14 - 400, 12, 400, 66), co.vuelta.piloto, co.vuelta.equipo, datos,
                    camara.NombreModo + (seguido < 0 ? " · automática" : ""));
            }

            // Avisos de la sesión, abajo a la izquierda: quién se va largo y quién marca la pole.
            var avisos = new System.Collections.Generic.List<(float cuando, string texto)>();
            foreach (var co in sesion.Coches)
            {
                if (co.errorCuando >= 0f && co.errorCuando <= t && co.errorCuando > t - 8f)
                    avisos.Add((co.errorCuando, $"<color=#f96><b>{co.vuelta.piloto.corto}</b> se va largo y pierde {co.vuelta.segundos - co.vuelta.segundos / (1f + co.vuelta.perdida / 100f):F1} s</color>"));
                if (co.fin <= t && co.fin > t - 8f && co.vuelta.segundos <= PoleHasta(co.fin))
                    avisos.Add((co.fin, $"<b>{co.vuelta.piloto.corto}</b> marca la pole provisional: {Crono(co.vuelta.segundos)}"));
            }
            avisos.Sort((a, b) => b.cuando.CompareTo(a.cuando));
            for (int k = 0; k < Mathf.Min(4, avisos.Count); k++)
            {
                var r = new Rect(12, Interfaz.Alto - 44 - k * 30, 470, 26);
                Interfaz.Rect(r, new Color(0, 0, 0, 0.55f));
                Interfaz.Rect(new Rect(r.x, r.y, 3, r.height), Interfaz.Rojo);
                GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 12, r.height), avisos[k].texto, Interfaz.Celda);
            }

            // Mandos.
            GUILayout.BeginArea(new Rect(12, 12, 560, 40));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Cámara: {camara.NombreModo}", Interfaz.Boton)) camara.Siguiente();
            if (GUILayout.Button($"Ritmo x{Velocidades[velocidad]}", Interfaz.Boton)) velocidad = (velocidad + 1) % Velocidades.Length;
            if (GUILayout.Button("Cámara automática", Interfaz.Boton)) seguido = -1;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>El mejor tiempo marcado hasta ese instante (incluido).</summary>
        float PoleHasta(float instante)
        {
            float mejor = float.MaxValue;
            foreach (var co in sesion.Coches) if (co.fin <= instante) mejor = Mathf.Min(mejor, co.vuelta.segundos);
            return mejor;
        }

        static string Crono(float s, int decimales = 3) =>
            decimales == 3 ? $"{(int)(s / 60)}:{s % 60:00.000}" : $"{(int)(s / 60)}:{s % 60:00.0}";
    }
}
