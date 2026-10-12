using System.Collections.Generic;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Tres maneras de mirar la carrera: el circuito entero desde arriba, detrás del coche
    /// seguido y cámaras de televisión fijas junto a la pista que van pasándose el coche.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CamaraCircuito : MonoBehaviour
    {
        public enum Modo { General, Persecucion, Television, ABordo, Orbita, Detras }

        /// <summary>El orden del botón «Cámara»: de la más cercana a la más lejana.</summary>
        static readonly Modo[] Ciclo = { Modo.Persecucion, Modo.Detras, Modo.ABordo, Modo.Television, Modo.General };

        /// <summary>El modo que se guarda en Opciones (1 persecución, 2 tele, 3 a bordo, 5 detrás).</summary>
        public static Modo DeOpcion(int i) => i == 2 || i == 3 || i == 5 ? (Modo)i : Modo.Persecucion;

        public Modo modo = Modo.Persecucion;
        public Transform objetivo;

        [SerializeField] float suavidad = 6f;
        [SerializeField] float separacionTele = 350f;

        Camera cam;
        Trazado trazado;
        readonly List<Vector3> camarasTele = new List<Vector3>();
        List<Bulto> bultos = new List<Bulto>();       // edificios y demás que pueden tapar al coche
        float alcancePersecucion = 1f;                 // fracción de la distancia de persecución que está libre (suavizada)
        Vector3 centro;
        float distanciaGeneral, aspectoGeneral, angulo = 20f;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.farClipPlane = 16000f;   // las montañas llegan a ~10 km
            cam.nearClipPlane = 0.3f;
        }

        /// <param name="decorado">La pista con su decorado: las cámaras de tele no se ponen dentro ni detrás de un edificio.</param>
        public void Preparar(Trazado t, Transform decorado = null)
        {
            trazado = t;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            for (int i = 0; i < t.Cantidad; i++) { var p = t.Punto(i).U(); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            centro = (min + max) / 2f;
            distanciaGeneral = 0f;   // se recalcula al encuadrar

            // Cámaras de tele: cada separacionTele metros, por fuera de la curva y en alto.
            camarasTele.Clear();
            bultos = Bultos(decorado);
            for (float d = 0f; d < t.Longitud; d += separacionTele)
            {
                int i = Mathf.RoundToInt(d / t.Longitud * t.Cantidad);
                float k = t.Curvatura(i, 6);
                float lado = k > 0f ? 1f : -1f;      // curva a la izquierda: fuera es la derecha
                camarasTele.Add(SitioDeCamara(t, d, lado, bultos));
            }
            if (modo == Modo.General) Colocar(1f);
        }

        /// <summary>
        /// Lo que puede tapar una cámara de tele: edificios, boxes, gradas, pórticos, árboles…
        /// (todo lo que levanta más de 2,5 m y no es una malla del tamaño del circuito).
        /// </summary>
        static List<Bulto> Bultos(Transform decorado)
        {
            var lista = new List<Bulto>();
            if (decorado == null) return lista;
            foreach (var r in decorado.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                if (b.size.y > 2.5f && Mathf.Max(b.size.x, b.size.z) < 400f) lista.Add(new Bulto(r));
            }
            return lista;
        }

        /// <summary>
        /// Un objeto que puede tapar, con su caja GIRADA (la de la malla en sus ejes). La caja
        /// alineada con el mundo de un edificio girado invadía la pista de Mónaco y la cámara
        /// creía tener algo delante: 92 saltos por vuelta.
        /// </summary>
        sealed class Bulto
        {
            public readonly Bounds mundo, local;
            readonly Matrix4x4 aLocal, aMundo;

            public Bulto(Renderer r)
            {
                mundo = r.bounds;
                local = r.localBounds;
                aMundo = r.transform.localToWorldMatrix;
                aLocal = r.transform.worldToLocalMatrix;
            }

            public bool Contiene(Vector3 p) => mundo.Contains(p) && local.Contains(aLocal.MultiplyPoint3x4(p));

            /// <summary>Distancia (en el mundo) a la que el rayo entra en el objeto, si entra antes de 'hasta'.</summary>
            public bool Corta(Ray rayo, float hasta, out float distancia)
            {
                distancia = 0f;
                if (!mundo.IntersectRay(rayo, out float d0) || d0 > hasta) return false;
                var o = aLocal.MultiplyPoint3x4(rayo.origin);
                var rl = new Ray(o, aLocal.MultiplyVector(rayo.direction));
                if (!local.IntersectRay(rl, out float dl)) return false;
                distancia = Vector3.Distance(rayo.origin, aMundo.MultiplyPoint3x4(rl.GetPoint(dl)));
                return distancia < hasta;
            }
        }

        /// <summary>
        /// Busca dónde poner la cámara de tele del punto d: por fuera de la curva, a 28 m y 9 m
        /// de alto si se puede; si ahí hay un edificio, o uno tapa la pista, prueba más lejos,
        /// más cerca, más alto y al otro lado. La de meta caía dentro de los boxes.
        /// </summary>
        static Vector3 SitioDeCamara(Trazado t, float d, float lado, List<Bulto> bultos)
        {
            Vector3 c = t.Posicion(d).U(), f = t.Tangente(d).U(), r = Vector3.Cross(Vector3.up, f).normalized;
            var cerca = bultos.FindAll(b => (b.mundo.center - c).sqrMagnitude < 260f * 260f);
            var vistos = new Vector3[5];
            for (int k = 0; k < vistos.Length; k++) vistos[k] = t.Posicion(d + (k - 2) * 45f).U() + Vector3.up;
            Vector3 mejor = c + r * (lado * 28f) + Vector3.up * 9f;
            int mejorLibres = -1;
            foreach (float alto in new[] { 9f, 16f, 26f, 40f })
                foreach (float lateral in new[] { 28f, 40f, 18f })
                    foreach (float l in new[] { lado, -lado })
                    {
                        Vector3 q = c + r * (l * lateral) + Vector3.up * alto;
                        if (cerca.Exists(b => { var e = b.mundo; e.Expand(3f); return e.Contains(q) && b.Contiene(q); })) continue;
                        int libres = 0;
                        foreach (var v in vistos)
                        {
                            var rayo = new Ray(q, v - q);
                            float largo = Vector3.Distance(q, v);
                            if (!cerca.Exists(b => b.Corta(rayo, largo - 2f, out _))) libres++;
                        }
                        if (libres == vistos.Length) return q;
                        if (libres > mejorLibres) { mejorLibres = libres; mejor = q; }
                    }
            return mejor;
        }

        /// <summary>¿Hay algo entre a y b?</summary>
        bool Tapado(Vector3 a, Vector3 b)
        {
            var rayo = new Ray(a, b - a);
            float largo = Vector3.Distance(a, b);
            foreach (var x in bultos)
                if (x.Corta(rayo, largo - 1f, out _) && !x.Contiene(b)) return true;
            return false;
        }

        /// <summary>La posición de cámara más lejana del coche entre él y 'deseada' que no tenga nada delante.</summary>
        Vector3 SinTapar(Vector3 coche, Vector3 deseada)
        {
            var rayo = new Ray(coche, deseada - coche);
            float largo = Vector3.Distance(coche, deseada), hasta = largo;
            foreach (var x in bultos)
                if (x.Corta(rayo, hasta, out float d) && !x.Contiene(coche)) hasta = d;
            return hasta >= largo ? deseada : coche + rayo.direction * Mathf.Max(2.5f, hasta - 1.5f);
        }

        bool CabeTodo(float margen)
        {
            for (int i = 0; i < trazado.Cantidad; i += 4)
            {
                Vector3 v = cam.WorldToViewportPoint(trazado.Punto(i).U());
                if (v.z <= 0f || v.x < margen || v.x > 1f - margen || v.y < margen || v.y > 1f - margen) return false;
            }
            return true;
        }

        public void Siguiente() => modo = Ciclo[(System.Array.IndexOf(Ciclo, modo) + 1) % Ciclo.Length];

        public string NombreModo => modo switch
        {
            Modo.General => "vista general",
            Modo.Persecucion => "persecución",
            Modo.ABordo => "a bordo",
            Modo.Detras => "detrás del coche",
            Modo.Orbita => "órbita",
            _ => "televisión",
        };

        void LateUpdate()
        {
            if (trazado == null) return;
            // Neblina de distancia salvo en la vista general (empañaría el circuito).
            bool niebla = modo == Modo.Orbita || (modo != Modo.General && objetivo != null);
            if (RenderSettings.fog != niebla) Entorno.Niebla(niebla);
            float dt = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : 0f;
            Colocar(1f - Mathf.Exp(-suavidad * dt));
        }

        void Colocar(float mezcla)
        {
            if (modo == Modo.Orbita)
            {
                // Fondo de los menús: un giro lento alrededor del circuito, alto y en diagonal.
                cam.fieldOfView = 45f;
                if (distanciaGeneral <= 0f) { var m = modo; modo = Modo.General; Colocar(1f); modo = m; }
                angulo += Time.unscaledDeltaTime * 3f;
                Quaternion giroOrbita = Quaternion.Euler(32f, angulo, 0f);
                transform.position = centro - giroOrbita * Vector3.forward * distanciaGeneral * 0.85f;
                transform.rotation = giroOrbita;
                return;
            }

            if (modo == Modo.General || objetivo == null)
            {
                // Desde el sur, mirando hacia el norte y 60 grados hacia abajo. La distancia se
                // busca alejando la cámara hasta que todo el trazado cabe con margen: con
                // perspectiva, lo cercano se ve más grande y una fórmula con la caja se queda corta.
                cam.fieldOfView = 40f;
                Quaternion giro = Quaternion.Euler(60f, 0f, 0f);
                transform.rotation = giro;
                if (distanciaGeneral <= 0f || !Mathf.Approximately(aspectoGeneral, cam.aspect))
                {
                    aspectoGeneral = cam.aspect;
                    for (distanciaGeneral = 200f; distanciaGeneral < 20000f; distanciaGeneral *= 1.04f)
                    {
                        transform.position = centro - giro * Vector3.forward * distanciaGeneral;
                        if (CabeTodo(0.06f)) break;
                    }
                }
                transform.position = centro - giro * Vector3.forward * distanciaGeneral;
                return;
            }

            if (modo == Modo.Persecucion)
            {
                cam.fieldOfView = 55f;
                Vector3 quiero = objetivo.position - objetivo.forward * 13f + Vector3.up * 4f;
                Vector3 pos = Vector3.Lerp(transform.position, quiero, mezcla);
                // Si un edificio se mete entre el coche y la cámara (Mónaco), la cámara se acerca
                // deprisa; cuando el hueco se abre, se aleja despacio (sin saltos).
                Vector3 ojo = objetivo.position + Vector3.up * 1.2f;
                float largo = Vector3.Distance(ojo, pos);
                float libre = largo > 0.01f ? Vector3.Distance(ojo, SinTapar(ojo, pos)) / largo : 1f;
                alcancePersecucion = Mathf.MoveTowards(alcancePersecucion, libre, Time.unscaledDeltaTime * (libre < alcancePersecucion ? 3f : 0.6f));
                transform.position = ojo + (pos - ojo) * alcancePersecucion;
                transform.rotation = Quaternion.LookRotation(objetivo.position + objetivo.forward * 8f + Vector3.up * 1f - transform.position);
                return;
            }

            if (modo == Modo.Detras)
            {
                // Fija al coche, detrás y por encima de la cabina: se ve su carrera tal cual.
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.1f;
                // A 4 m del coche y sobre la pista no le tapa nada: sin comprobar edificios (la
                // comprobación, con un pórtico o un edificio girado, la hacía saltar).
                transform.position = objetivo.TransformPoint(new Vector3(0f, 2.3f, -4.2f));
                transform.rotation = Quaternion.LookRotation(objetivo.TransformPoint(new Vector3(0f, 1.1f, 16f)) - transform.position, Vector3.up);
                return;
            }

            if (modo == Modo.ABordo)
            {
                // Justo encima del casco (más atrás, el cajón de admisión tapa la vista): morro y pista.
                cam.fieldOfView = 62f;
                cam.nearClipPlane = 0.05f;
                transform.position = objetivo.TransformPoint(new Vector3(0f, 1.32f, 0.05f));
                transform.rotation = Quaternion.LookRotation(objetivo.forward * 20f + Vector3.down * 2.0f, Vector3.up);
                return;
            }
            cam.nearClipPlane = 0.3f;

            // Televisión: la cámara fija más cercana al coche que lo VEA (en un urbano la más
            // cercana suele tener un edificio delante), con zoom para encuadrarlo. Si ninguna lo
            // ve, la del helicóptero.
            Vector3 mejor = Vector3.zero;
            float mejorD = float.MaxValue;
            Vector3 coche = objetivo.position + Vector3.up;
            foreach (var c in camarasTele)
            {
                float dd = (c - objetivo.position).sqrMagnitude;
                if (dd < mejorD && dd < 600f * 600f && !Tapado(c, coche)) { mejorD = dd; mejor = c; }
            }
            if (mejorD == float.MaxValue)
            {
                mejor = objetivo.position - objetivo.forward * 40f + Vector3.up * 70f;
                mejorD = (mejor - objetivo.position).sqrMagnitude;
            }
            transform.position = mejor;
            transform.rotation = Quaternion.LookRotation(objetivo.position - mejor);
            float distancia = Mathf.Sqrt(mejorD);
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(9f / distancia) * Mathf.Rad2Deg, 4f, 60f);
        }
    }
}
