"""
Sonidos del juego generados por síntesis (sin grabaciones de terceros, sin licencias que mirar).

    python Tools/Sonido/sintetizar.py            -> escribe Assets/Resources/Sonido/*.wav

- Motor.wav / MotorFrenando.wav: bucles de 0,4 s de un V6 turbo a ~300 Hz de encendido; el
  juego les sube y baja el tono con la velocidad (pitch 0,55-2). Encajan sin chasquido: el
  tono cabe un número entero de veces en el bucle y el ruido se funde en el empalme.
- Salida.wav: los motores subiendo de vueltas al apagarse el semáforo.
- Pasada1-3.wav: un coche que pasa junto a la cámara (Doppler y distancia).

El volumen se iguala (RMS) al de los sonidos que había antes, para no tocar los niveles del juego.
"""
import os
import wave

import numpy as np

SR = 44100
AQUI = os.path.dirname(os.path.abspath(__file__))
SALIDA = os.path.normpath(os.path.join(AQUI, "..", "..", "Assets", "Resources", "Sonido"))
azar = np.random.default_rng(2026)


def paso_bajo(x, corte):
    """Filtro de un polo (suaviza el ruido)."""
    a = np.exp(-2 * np.pi * corte / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def tono_motor(fase, brillo, armonicos=28):
    """Suma de armónicos sobre una fase (en ciclos): la base del sonido del motor."""
    y = np.zeros_like(fase)
    for k in range(1, armonicos + 1):
        amp = 1.0 / k ** brillo
        if k % 3 == 0:
            amp *= 1.6          # el V6 refuerza los múltiplos de 3
        y += amp * np.sin(2 * np.pi * k * fase + azar.uniform(0, 2 * np.pi))
    return y


def motor_bucle(f0, dur, brillo, ruido, petardeo=0.0):
    n = int(round(dur * SR))
    ciclos = round(f0 * dur)                    # número entero de ciclos: el bucle encaja
    t = np.arange(n) / SR
    fase = t * ciclos / dur
    y = tono_motor(fase, brillo)
    # Aspereza: modulación a un tercio del encendido (cada bancada), también entera.
    y *= 1 + 0.25 * np.sin(2 * np.pi * fase / 3)
    # Ruido de admisión y escape, fundido en el empalme para que el bucle no chasquee.
    extra = int(0.05 * SR)
    r = paso_bajo(azar.normal(0, 1, n + extra), 3500)
    mezcla = np.linspace(0, 1, extra)
    r[:extra] = r[:extra] * mezcla + r[n:] * (1 - mezcla)
    y += ruido * r[:n] / np.std(r)
    if petardeo > 0:
        # Al levantar el pie, petardeos sueltos del escape.
        for _ in range(int(petardeo * dur * 30)):
            i = azar.integers(0, n - 800)
            y[i:i + 800] += azar.uniform(1.5, 3) * np.exp(-np.arange(800) / 120) * azar.normal(0, 1, 800)
    return np.tanh(y / np.max(np.abs(y)) * 1.6)


def salida(dur=2.4):
    """Al apagarse el semáforo: varios motores subiendo de vueltas, algo desafinados."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    y = np.zeros(n)
    for k in range(6):
        f = 210 + 320 * (1 - np.exp(-t / (0.7 + 0.08 * k))) + azar.uniform(-12, 12)
        fase = np.cumsum(f) / SR
        y += tono_motor(fase, 1.0, 18) * (0.8 + 0.2 * azar.random())
    y += 2.5 * paso_bajo(azar.normal(0, 1, n), 2500)       # ruedas y escape
    env = np.minimum(1, t / 0.08) * np.minimum(1, (dur - t) / 0.4)
    return np.tanh(y / np.max(np.abs(y)) * 2.0) * env


def pasada(v, f0, d, dur=2.6):
    """Un coche que pasa a v m/s a d metros de la cámara, en mitad del sonido."""
    n = int(dur * SR)
    t = np.arange(n) / SR - dur / 2
    x = v * t
    r = np.sqrt(x ** 2 + d ** 2)
    radial = v * x / r                          # + alejándose, - acercándose
    f = f0 * 343.0 / (343.0 + radial)           # Doppler
    fase = np.cumsum(f) / SR
    y = tono_motor(fase, 0.95, 24)
    y += 1.2 * paso_bajo(azar.normal(0, 1, n), 4000)
    y = np.tanh(y / np.max(np.abs(y)) * 1.5)
    amp = d / r
    env = np.minimum(1, (t + dur / 2) / 0.15) * np.minimum(1, (dur / 2 - t) / 0.3)
    return y * amp * env


def rms_de(ruta):
    with wave.open(ruta) as w:
        x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(float) / 32768
    return float(np.sqrt(np.mean(x ** 2)))


def guardar(nombre, y, rms):
    y = y * (rms / np.sqrt(np.mean(y ** 2)))
    y = np.clip(y, -0.99, 0.99)
    with wave.open(os.path.join(SALIDA, nombre), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((y * 32767).astype(np.int16).tobytes())
    print(f"{nombre}: {len(y) / SR:.2f} s, RMS {rms:.3f}")


if __name__ == "__main__":
    # Niveles de referencia: los que tenían los sonidos anteriores (medidos el 12-10-2026).
    # (AccelerationHigh -15,9 dB, DecelerationHigh -26,9, Inicial -14,9, pasadas -11 a -21).
    RMS_MOTOR, RMS_FRENA, RMS_SALIDA, RMS_PASADA = 0.160, 0.045, 0.180, 0.160
    guardar("Motor.wav", motor_bucle(300, 0.4, 0.9, 0.35), RMS_MOTOR)
    guardar("MotorFrenando.wav", motor_bucle(300, 0.4, 1.4, 0.55, petardeo=1.0), RMS_FRENA)
    guardar("Salida.wav", salida(), RMS_SALIDA)
    for i, (v, f0, d) in enumerate([(78, 520, 18), (86, 560, 24), (70, 480, 14)], 1):
        guardar(f"Pasada{i}.wav", pasada(v, f0, d), RMS_PASADA)
