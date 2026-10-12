using System.Linq;
using F1.Juego;
using F1.Nucleo;
using NUnit.Framework;
using UnityEngine;

namespace F1.Tests
{
    public class CampeonatoTests
    {
        static Parrilla Base() =>
            JsonUtility.FromJson<Parrilla>(Resources.Load<TextAsset>("Temporada/parrilla_2026").text);

        static string[] CalendarioIds() => Calendario.Cargar().carreras.Select(c => c.circuito).ToArray();

        static Circuito Cir(string id)
        {
            var d = DatosCircuito.Cargar(id);
            return new Circuito(d.CrearTrazado(), d.poleReferencia, d.id, 2026);
        }

        [Test]
        public void UnaTemporadaEntera()
        {
            var p = Campeonato.Nueva(Base(), CalendarioIds(), "willians", 7);
            Assert.AreEqual(24, p.calendario.Length);
            while (!p.Terminada)
            {
                var (_, carrera) = Campeonato.Simular(p, Cir(p.calendario[p.ronda]));
                var gp = Campeonato.Registrar(p, carrera);
                int acaban = gp.filas.Count(f => f.termino);
                int esperado = new[] { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 }.Take(acaban).Sum();
                Assert.AreEqual(esperado, gp.filas.Sum(f => f.puntos), $"ronda {gp.ronda + 1}");
            }
            Assert.AreEqual(24, p.resultados.Count);
            var pilotos = Campeonato.Pilotos(p);
            Assert.AreEqual(22, pilotos.Count);
            for (int i = 1; i < pilotos.Count; i++) Assert.GreaterOrEqual(pilotos[i - 1].puntos, pilotos[i].puntos);
            Assert.AreEqual(pilotos.Sum(x => x.puntos), Campeonato.Constructores(p).Sum(x => x.puntos));
        }

        [Test]
        public void GuardarYCargarNoPierdeNada()
        {
            var p = Campeonato.Nueva(Base(), CalendarioIds(), "ferrani", 11);
            for (int i = 0; i < 3; i++) { Campeonato.Invertir(p, true); Campeonato.Registrar(p, Campeonato.Simular(p, Cir(p.calendario[p.ronda])).carrera); }
            p.modoMotor = Campeonato.ModoAgresivo; p.carga = Campeonato.CargaAlta;

            var copia = JsonUtility.FromJson<Partida>(JsonUtility.ToJson(p));
            Assert.AreEqual(p.ronda, copia.ronda);
            Assert.AreEqual(p.resultados.Count, copia.resultados.Count);
            Assert.AreEqual(p.MiEquipo.ritmo, copia.MiEquipo.ritmo, 1e-6);
            Assert.AreEqual(p.desgasteMotor, copia.desgasteMotor, 1e-6);
            // Y la siguiente carrera sale igual en la original y en la cargada.
            var c = Cir(p.calendario[p.ronda]);
            var a = Campeonato.Simular(p, c).carrera.Resultado;
            var b = Campeonato.Simular(copia, c).carrera.Resultado;
            CollectionAssert.AreEqual(a.Select(x => x.piloto.nombre), b.Select(x => x.piloto.nombre));
        }

        [Test]
        public void InvertirGastaPuntosYMejoraElCoche()
        {
            var p = Campeonato.Nueva(Base(), CalendarioIds(), "hoas", 3);
            float ritmo = p.MiEquipo.ritmo, potencia = p.MiEquipo.potencia;
            for (int i = 0; i < Campeonato.PuntosPorRonda; i++) Assert.IsTrue(Campeonato.Invertir(p, motor: true));
            Assert.IsFalse(Campeonato.Invertir(p, true), "no quedan puntos");
            Assert.Less(p.MiEquipo.ritmo, ritmo);
            Assert.Greater(p.MiEquipo.potencia, potencia);
        }

        [Test]
        public void ElQuintoMotorPenalizaEnParrilla()
        {
            var p = Campeonato.Nueva(Base(), CalendarioIds(), "mclorin", 5);
            var c = Cir(p.calendario[p.ronda]);
            var sin = Campeonato.Simular(p, c).q;
            p.motoresUsados = Campeonato.MotoresGratis;     // ya gastados los gratuitos
            Campeonato.MotorNuevo(p);
            Assert.IsTrue(Campeonato.PenalizaMotor(p));
            var con = Campeonato.Simular(p, c).q;
            int Mejor(Clasificacion.Vuelta[] q) => System.Array.FindIndex(q, v => v.equipo.id == "mclorin");
            Assert.GreaterOrEqual(Mejor(con), Mejor(sin) + Campeonato.PenalizacionMotor - 1);
        }

        [Test]
        public void ElPrioritarioNoEsAtacadoPorSuCompanero()
        {
            // Con órdenes, el compañero nunca adelanta al prioritario.
            var p = Campeonato.Nueva(Base(), CalendarioIds(), "mclorin", 21);
            p.prioritario = "Nolan Narris";
            for (int r = 0; r < 6; r++)
            {
                var (_, ca) = Campeonato.Simular(p, Cir(p.calendario[p.ronda]));
                Assert.IsFalse(ca.Sucesos.Any(s => s.tipo == Carrera.Tipo.Adelantamiento &&
                    ca.Coches[s.coche].piloto.nombre == "Onofre Paistri" && ca.Coches[s.otro].piloto.nombre == "Nolan Narris" &&
                    s.t > 20f), $"ronda {r + 1}");   // tras la salida (el arranque no son órdenes)
                Campeonato.Registrar(p, ca);
                p.prioritario = "Nolan Narris";
            }
        }

        [Test]
        public void EnVivoSinOrdenesSaleIgualQueDeGolpe()
        {
            var a = Campeonato.Nueva(Base(), CalendarioIds(), "willians", 11);
            var b = Campeonato.Nueva(Base(), CalendarioIds(), "willians", 11);
            var c = Cir(a.calendario[0]);
            var (_, golpe) = Campeonato.Simular(a, c);
            var (_, vivo) = Campeonato.Simular(b, c, 3, enVivo: true);
            Assert.IsFalse(vivo.Acabada);
            for (float t = 0f; !vivo.Acabada; t += 0.37f) vivo.Avanzar(t);   // a trozos, como los fotogramas
            var ra = golpe.Resultado; var rb = vivo.Resultado;
            for (int i = 0; i < ra.Length; i++)
            {
                Assert.AreEqual(ra[i].piloto.nombre, rb[i].piloto.nombre, $"puesto {i + 1}");
                Assert.AreEqual(ra[i].tiempoFinal, rb[i].tiempoFinal, 1e-4f);
            }
        }

        [Test]
        public void LaRadioCuestaOAhorraMotor()
        {
            float Desgaste(Carrera.OrdenPiloto o)
            {
                var p = Campeonato.Nueva(Base(), CalendarioIds(), "willians", 3);
                var (_, carrera) = Campeonato.Simular(p, Cir(p.calendario[0]), 3, enVivo: true);
                for (int i = 0; i < carrera.Coches.Length; i++)
                    if (carrera.Coches[i].equipo.id == "willians") carrera.Radio(i, o);
                Campeonato.Registrar(p, carrera);
                Assert.IsFalse(p.gpEnCurso);
                return p.desgasteMotor;
            }
            float normal = Desgaste(Carrera.OrdenPiloto.Normal);
            Assert.Greater(Desgaste(Carrera.OrdenPiloto.Atacar), normal + 1f);
            Assert.Less(Desgaste(Carrera.OrdenPiloto.Ahorrar), normal - 0.5f);
        }
    }
}
