using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// El sonido de la carrera: el motor del coche que sigue la cámara (un bucle corto al que se
    /// le sube el tono con la velocidad), la señal de salida y las pasadas por delante de la
    /// cámara de televisión. Respeta las Opciones (sonido sí/no y volumen).
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class Sonido : MonoBehaviour
    {
        AudioSource motor, efectos;
        AudioClip acelera, frena, salida;
        AudioClip[] pasadas;
        float velocidadAnterior;
        float distanciaAnterior = float.MaxValue;
        bool acercandose;

        void Awake()
        {
            // Generados por síntesis con Tools/Sonido/sintetizar.py (sin grabaciones de terceros).
            acelera = Resources.Load<AudioClip>("Sonido/Motor");
            frena = Resources.Load<AudioClip>("Sonido/MotorFrenando");
            salida = Resources.Load<AudioClip>("Sonido/Salida");
            pasadas = new[] { Resources.Load<AudioClip>("Sonido/Pasada1"), Resources.Load<AudioClip>("Sonido/Pasada2"), Resources.Load<AudioClip>("Sonido/Pasada3") };
            motor = gameObject.AddComponent<AudioSource>();
            motor.loop = true; motor.playOnAwake = false; motor.clip = acelera; motor.spatialBlend = 0f;
            efectos = gameObject.AddComponent<AudioSource>();
            efectos.playOnAwake = false; efectos.spatialBlend = 0f;
        }

        /// <summary>Llamar cada fotograma durante la carrera; velocidad en m/s; null = sin carrera.</summary>
        public void Motor(float? velocidad, bool aBordo)
        {
            float vol = Opciones.Sonido ? Opciones.Volumen : 0f;
            if (velocidad == null || vol <= 0f || Time.timeScale == 0f)
            {
                if (motor.isPlaying) motor.Stop();
                velocidadAnterior = 0f;
                return;
            }
            float v = velocidad.Value;
            // Frenando, el bucle de deceleración; acelerando o a fondo, el de aceleración.
            var clip = v < velocidadAnterior - 0.3f ? frena : acelera;
            if (motor.clip != clip) { motor.clip = clip; motor.Play(); }
            if (!motor.isPlaying) motor.Play();
            motor.pitch = Mathf.Lerp(motor.pitch, 0.55f + v / 92f * 1.45f, 0.25f);
            motor.volume = vol * (aBordo ? 0.8f : 0.45f) * Mathf.Clamp01(0.25f + v / 40f);
            velocidadAnterior = v;
        }

        public void Salida()
        {
            if (Opciones.Sonido && salida != null) efectos.PlayOneShot(salida, Opciones.Volumen);
        }

        /// <summary>Cámara de televisión: suena una pasada cuando el coche pasa cerca y se aleja.</summary>
        public void Pasada(float distanciaACamara)
        {
            if (!Opciones.Sonido) return;
            if (distanciaACamara < distanciaAnterior) acercandose = true;
            else if (acercandose && distanciaAnterior < 45f)
            {
                acercandose = false;
                var c = pasadas[Random.Range(0, pasadas.Length)];
                if (c != null) efectos.PlayOneShot(c, Opciones.Volumen * 0.8f);
            }
            distanciaAnterior = distanciaACamara;
        }
    }
}
