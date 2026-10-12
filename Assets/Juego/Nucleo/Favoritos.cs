using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace F1.Nucleo
{
    /// <summary>
    /// Quién parte como favorito en un GP: se simula el GP entero (clasificación y carrera)
    /// muchas veces con otras semillas y se cuentan victorias, podios y poles. No sabe nada de la
    /// carrera de verdad (su semilla es otra): es lo que sabría un apostante antes de empezar.
    /// Se calcula en segundo plano; Hechas dice cuántas van.
    /// </summary>
    public sealed class Favoritos
    {
        public string[] Pilotos { get; }
        public int Total { get; }
        public int Hechas => hechas;

        readonly int[] victorias, podios, poles;
        readonly Dictionary<string, int> indice = new Dictionary<string, int>();
        int hechas;

        public Favoritos(Parrilla p, int total)
        {
            Total = total;
            Pilotos = new string[p.pilotos.Length];
            for (int i = 0; i < Pilotos.Length; i++) { Pilotos[i] = p.pilotos[i].nombre; indice[Pilotos[i]] = i; }
            victorias = new int[Pilotos.Length]; podios = new int[Pilotos.Length]; poles = new int[Pilotos.Length];
        }

        /// <summary>Lanza el cálculo en segundo plano (en paralelo, una simulación por tarea).</summary>
        public Task Calcular(Circuito c, Parrilla p, int semilla, Dictionary<string, string> ordenes = null)
        {
            return Task.Run(() => Parallel.For(0, Total, n =>
            {
                var azar = new Azar(unchecked(semilla * 31 + n * 7919 + 101));
                var q = Clasificacion.Simular(c, p, azar);
                var r = Carrera.Simular(c, q, 3, azar, ordenes).Resultado;
                lock (victorias)
                {
                    poles[indice[q[0].piloto.nombre]]++;
                    if (r[0].terminado) victorias[indice[r[0].piloto.nombre]]++;
                    for (int k = 0; k < 3 && k < r.Length; k++) if (r[k].terminado) podios[indice[r[k].piloto.nombre]]++;
                }
                Interlocked.Increment(ref hechas);
            }));
        }

        float Parte(int[] cuenta, string piloto) =>
            indice.TryGetValue(piloto, out int i) && hechas > 0 ? cuenta[i] / (float)hechas : 0f;

        public float Victoria(string piloto) => Parte(victorias, piloto);
        public float Podio(string piloto) => Parte(podios, piloto);
        public float Pole(string piloto) => Parte(poles, piloto);

        /// <summary>Estrellas de favorito: 3 si gana una de cada 4, 2 si una de cada 10, 1 si podio a menudo.</summary>
        public int Estrellas(string piloto)
        {
            float v = Victoria(piloto);
            return v >= 0.25f ? 3 : v >= 0.10f ? 2 : v >= 0.02f || Podio(piloto) >= 0.15f ? 1 : 0;
        }

        /// <summary>Los pilotos ordenados de más a menos favorito.</summary>
        public List<string> Orden()
        {
            var l = new List<string>(Pilotos);
            l.Sort((a, b) =>
            {
                int x = Victoria(b).CompareTo(Victoria(a));
                return x != 0 ? x : Podio(b).CompareTo(Podio(a));
            });
            return l;
        }
    }
}
