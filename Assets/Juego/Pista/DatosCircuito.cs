using System;
using F1.Nucleo;
using UnityEngine;

namespace F1.Juego
{
    /// <summary>Un circuito tal como lo escribe Tools/Circuitos/generar_circuitos.py.</summary>
    [Serializable]
    public class DatosCircuito
    {
        public string id, nombre, granPremio, localidad, bandera, plano, giro;
        public int longitudOficial;
        public float longitudMedida, radioMinimo;
        public float poleReferencia;   // segundos; 0 = sin referencia (Madrid)
        public string tipo;            // "urbano" o "permanente"
        public float ancho = 13f;      // ancho de pista en metros

        public bool Urbano => tipo == "urbano";

        // Entorno de fondo, aproximado a la geografía real (direcciones en grados desde el norte).
        public string suelo = "hierba";   // "hierba" o "desierto"
        public float montanas;            // altura en m; 0 = no hay
        public float montanasDir = -1f;   // -1 = alrededor
        public float mar = -1f;           // -1 = no hay
        public float ciudad = -1f;        // -1 = no hay; 999 = alrededor
        public bool Desierto => suelo == "desierto";
        public bool salidaVerificada;
        public float[] x, z;

        public Trazado CrearTrazado() => new Trazado(x, z);

        public static DatosCircuito Cargar(string id)
        {
            var texto = Resources.Load<TextAsset>("Circuitos/" + id);
            if (texto == null) throw new ArgumentException($"No existe Resources/Circuitos/{id}.json");
            return JsonUtility.FromJson<DatosCircuito>(texto.text);
        }
    }

    [Serializable]
    public class CarreraDelCalendario
    {
        public int ronda;
        public string circuito, granPremio, fecha;
    }

    [Serializable]
    public class Calendario
    {
        public int temporada;
        public CarreraDelCalendario[] carreras;

        public static Calendario Cargar(int temporada = 2026)
        {
            var texto = Resources.Load<TextAsset>($"Circuitos/calendario_{temporada}");
            if (texto == null) throw new ArgumentException($"No existe el calendario de {temporada}");
            return JsonUtility.FromJson<Calendario>(texto.text);
        }
    }

    /// <summary>Conversión entre los vectores del núcleo (System.Numerics) y los de Unity.</summary>
    public static class Vec
    {
        public static Vector3 U(this System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
