using System;
using System.Linq;
using F1.Juego;
using F1.Nucleo;
using NUnit.Framework;
using UnityEngine;

namespace F1.Tests
{
    /// <summary>
    /// Lo que el simulador tiene que cumplir siempre. Las cifras de calibración (quién gana,
    /// cuántos adelantamientos) se miden aparte con muchas carreras; aquí van las reglas.
    /// </summary>
    public class SimuladorTests
    {
        static Parrilla parrilla;

        static Parrilla CargarParrilla() =>
            parrilla ??= JsonUtility.FromJson<Parrilla>(Resources.Load<TextAsset>("Temporada/parrilla_2026").text);

        static Circuito CargarCircuito(string id)
        {
            var d = DatosCircuito.Cargar(id);
            return new Circuito(d.CrearTrazado(), d.poleReferencia, d.id, 2026);
        }

        static Carrera Correr(string id, int semilla, out Clasificacion.Vuelta[] q)
        {
            var c = CargarCircuito(id);
            var az = new Azar(semilla);
            q = Clasificacion.Simular(c, CargarParrilla(), az);
            return Carrera.Simular(c, q, 3, az);
        }

        [Test]
        public void LaParrillaTiene22PilotosEn11Equipos()
        {
            var p = CargarParrilla();
            Assert.AreEqual(22, p.pilotos.Length);
            Assert.AreEqual(11, p.equipos.Length);
            foreach (var e in p.equipos) Assert.AreEqual(2, p.pilotos.Count(x => x.equipo == e.id), e.nombre);
        }

        [Test]
        public void LaMismaSemillaDaLaMismaCarrera()
        {
            var a = Correr("it-1922", 42, out _).Resultado;
            var b = Correr("it-1922", 42, out _).Resultado;
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].piloto.nombre, b[i].piloto.nombre, $"puesto {i + 1}");
                Assert.AreEqual(a[i].tiempoFinal, b[i].tiempoFinal, 1e-4, $"puesto {i + 1}");
            }
        }

        [TestCase("mc-1929", 1)]
        [TestCase("it-1922", 2)]
        [TestCase("jp-1962", 3)]
        [TestCase("sg-2008", 4)]
        public void TodosAcabanOAbandonanYConTiemposRazonables(string id, int semilla)
        {
            var ca = Correr(id, semilla, out _);
            foreach (var co in ca.Coches)
                Assert.IsTrue(co.terminado || co.fuera, $"{co.piloto.corto} ni acabó ni abandonó");
            var g = ca.Resultado[0];
            // 3 vueltas a ritmo de carrera (+2 %) más la salida parada: entre +1 % y +8 % de 3 referencias;
            // con coche de seguridad (30-45 s al 62 %), hasta +25 %.
            float r = g.tiempoFinal / (3f * ca.Circuito.Referencia);
            bool sc = ca.Sucesos.Exists(x => x.tipo == Carrera.Tipo.CocheSeguridad);
            Assert.That(r, Is.InRange(1.01f, sc ? 1.25f : 1.08f), $"ganador en {g.tiempoFinal:F1} s{(sc ? " (con coche de seguridad)" : "")}");
            Assert.AreEqual(3, g.vueltas);
        }

        [TestCase("mc-1929", 5)]
        [TestCase("hu-1986", 6)]
        [TestCase("it-1922", 7)]
        public void NuncaHayDosCochesEnElMismoSitio(string id, int semilla)
        {
            var ca = Correr(id, semilla, out _);
            float L = ca.Circuito.Trazado.Longitud;
            int solapes = 0;
            for (int m = 0; m < ca.Muestras; m++)
                for (int i = 0; i < ca.Coches.Length; i++)
                    for (int j = i + 1; j < ca.Coches.Length; j++)
                    {
                        var A = ca.Coches[i]; var B = ca.Coches[j];
                        float dA = A.grabD[m], dB = B.grabD[m];
                        // Pasada la salida (se cuenta solo carrera lanzada) y antes de la meta.
                        if (dA < Carrera.DosColumnas + 300f || dB < Carrera.DosColumnas + 300f || dA > 3 * L || dB > 3 * L) continue;
                        if (A.fuera || B.fuera) continue;
                        if (Math.Abs(dA - dB) < 3f && Math.Abs(A.grabL[m] - B.grabL[m]) < 1.2f) solapes++;
                    }
            Assert.AreEqual(0, solapes);
        }

        [Test]
        public void LaClasificacionVaDeMasRapidoAMasLento()
        {
            Correr("be-1925", 9, out var q);
            for (int i = 1; i < q.Length; i++) Assert.LessOrEqual(q[i - 1].segundos, q[i].segundos);
        }

        [Test]
        public void PuntosOficiales()
        {
            int[] esperado = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1, 0 };
            for (int i = 0; i < esperado.Length; i++) Assert.AreEqual(esperado[i], Puntos.PorPuesto(i + 1));
        }

        [Test]
        public void ElPuenteDeSuzukaEstaEnLaRectaDeAtras()
        {
            var t = DatosCircuito.Cargar("jp-1962").CrearTrazado();
            Assert.AreEqual(1, t.Cruces.Count);
            var (a, b) = t.Cruces[0];
            int alto = t.Punto(a).Y > t.Punto(b).Y ? a : b;
            Assert.That(t.DistanciaDe(alto), Is.InRange(4300f, 4900f), "la recta de atrás, camino de la 130R");
            Assert.AreEqual(Trazado.AlturaPuente, t.Punto(alto).Y, 0.01f);
        }

        [Test]
        public void LaClasificacionEnPistaCruzaLaMetaEnSuTiempo()
        {
            foreach (var id in new[] { "mc-1929", "be-1925" })
            {
                var c = CargarCircuito(id);
                var azar = new Azar(4);
                var q = Clasificacion.Simular(c, CargarParrilla(), azar);
                var s = new SesionClasificacion(c, q, azar);
                float L = c.Trazado.Longitud;
                for (int k = 0; k < s.Coches.Length; k++)
                {
                    var co = s.Coches[k];
                    Assert.AreEqual(co.vuelta.segundos, co.fin - co.inicio, 1e-3f);
                    Assert.AreEqual(L, s.Muestra(k, co.fin).d, 10f, $"{id}: {co.vuelta.piloto.nombre} cruza la meta a destiempo");
                }
                // Nunca dos coches a menos de 20 m en el mismo carril.
                for (float t = 0f; t < s.Duracion; t += 0.2f)
                    for (int a = 0; a < s.Coches.Length; a++)
                        for (int b = a + 1; b < s.Coches.Length; b++)
                        {
                            if (!s.EnPista(a, t) || !s.EnPista(b, t)) continue;
                            var A = s.Muestra(a, t); var B = s.Muestra(b, t);
                            float dd = Mathf.Abs(Mathf.Repeat(A.d - B.d + L / 2f, L) - L / 2f);
                            if (Mathf.Abs(A.lateral - B.lateral) < 2f) Assert.Greater(dd, 20f, $"{id} t={t}");
                        }
            }
        }
    }
}
