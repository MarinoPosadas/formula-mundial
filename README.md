# F1_0

Juego de Fórmula 1 en Unity 6000.3.9f1 (Built-in Render Pipeline): Mundial 2026 con los 24
circuitos reales, clasificación a una vuelta y carrera a 3 vueltas. El resultado lo decide un
simulador (C# puro, con semilla); el 3D solo lo reproduce.

## Para jugar

Abre el proyecto con Unity 6000.3.9f1, abre `Assets/Juego/Escenas/Juego.unity` y pulsa Play. (Al abrir
el proyecto por primera vez se abre sola, si Unity arranca con una escena vacía.)

## Escenas

| Escena | Qué es |
|---|---|
| `Assets/Juego/Escenas/Juego.unity` | **El juego** (primera de la build): menú, Gran Premio suelto y Campeonato como jefe de escudería. |
| `Assets/Juego/Escenas/Carrera.unity` | Banco de pruebas: recorrer los 24 circuitos y simular GP sueltos con datos de depuración. |

Las dos escenas nuevas se rehacen con **F1 ▸ Crear escena del juego** y **F1 ▸ Crear escena de
carrera** (se crean en modo aditivo: no tocan la escena que haya abierta).

## Cómo se juega

- **Gran Premio suelto**: eliges circuito; forma del día, afinidades y semilla nuevas cada vez.
- **Campeonato**: eliges escudería y, antes de cada GP, decides:
  - **Desarrollo**: 10 puntos por ronda entre motor y aerodinámica (lo que no se gasta se pierde;
    los rivales desarrollan solos).
  - **Carga aerodinámica** del GP: baja (recta), media, alta (curva). Acertar con el circuito vale
    ~0,2 % de vuelta; los rivales aciertan el 60 %.
  - **Modo de motor**: conservador / normal / agresivo. El precio del agresivo es el desgaste, que
    se arrastra y multiplica las averías. Motor nuevo: 4 gratis; desde el quinto, 5 puestos de
    parrilla.
  - **Órdenes de equipo**: carrera libre o prioridad para uno de tus pilotos.
- **Clasificación en pista**: los 22 salen por turno a su vuelta lanzada (los más lentos primero,
  con algo de mezcla) y la torre se reordena al cruzar cada uno; cada vuelta dura exactamente lo
  que decidió la simulación. La tanda cabe en una vuelta para que nadie se cruce. Después, la
  parrilla y la carrera.
  Quien falla en su vuelta (sale como «se fue largo») se abre en una frenada y pierde ahí el
  tiempo que decidió la simulación. Desde la presentación también se puede ir directo a la
  parrilla o al resultado final.
- **Favoritos**: al presentar el GP se simula 200 veces en segundo plano (con otras semillas) y
  salen estrellas y opciones de victoria, podio y pole; las estrellas siguen en la parrilla y la torre.
- **Cámaras**: persecución, detrás del coche (al elegir un piloto en la torre), a bordo,
  televisión y general. Ninguna se queda tras un edificio: la de persecución se acerca y la de
  tele cambia a otra que vea el coche (o a la del helicóptero). Un rótulo arriba dice a quién se
  sigue, con su escudería, puesto, diferencia y velocidad.
- **Incidencias**: rótulo grande (abandono, se va largo, toque) y la cámara automática corta al
  coche un segundo antes. El que se va largo se abre hacia la escapatoria también en carrera.
- **Radio del muro**, en la carrera del campeonato o, en un GP suelto, de la escudería que se elija en la presentación: a cada uno de tus pilotos, *ataca* (más
  ritmo y el doble de intentos de adelantar, pero más errores y desgaste de motor), *defiende*
  (más difícil de pasar), *ahorra* (más lento, casi no falla y cuida el motor) o *normal*; y la
  orden de equipo se puede cambiar en marcha. Medido en 300 GP con un solo piloto: atacando gana
  0,14 puestos de media y adelanta un 44 % más; una carrera entera atacando con los dos coches
  gasta tanto motor como el modo agresivo.
- La carrera que se ve se simula en vivo, un segundo por delante de la imagen; sin órdenes sale
  idéntica a la simulada de golpe (lo comprueba un test).
- La partida se guarda en `Application.persistentDataPath/campeonato_2026.json`. Al empezar a ver
  un GP se guarda marcado como empezado: si se sale a mitad, al cargar se da por corrido sin
  órdenes (misma semilla), así que salir no sirve para repetirlo.

## Código (`Assets/Juego`)

- `Nucleo` (`F1.Nucleo`, sin Unity): `Trazado`, `PerfilVelocidad`, `Circuito`, `Parrilla`,
  `Clasificacion`, `Carrera`, `Campeonato`/`Partida`.
- `Pista` (`F1.Juego`): `ConstructorPista`, `ConstructorDecorado`, `ConstructorEntorno`,
  `CamaraCircuito`, `FabricaCoches`, `ReproductorClasificacion`, `ReproductorCarrera`, `Sonido`,
  `Interfaz`, `ControladorJuego`, `BancoPruebas`.
- `Editor`: los menús de F1.
- `Tests` (`F1.Tests`, 20 tests): simulador y campeonato. Test Runner ▸ EditMode.

## Datos

- **Circuitos** (`Resources/Circuitos/<id>.json` + `calendario_2026.json`): salen de
  `Tools/Circuitos`. Forma y escala del GPS real (github.com/bacinger/f1-circuits, MIT; longitudes a
  ±1 % de las oficiales); salida y sentido leídos en los planos oficiales con `encajar.py` (el
  primer punto del GPS no siempre es la salida, y Singapur viene al revés). Baréin y Yeda no tienen
  plano. Cada circuito lleva la pole de 2025 como referencia de ritmo.
- **Parrilla** (`Resources/Temporada/parrilla_2026.json`): escuderías (ritmo, carácter,
  fiabilidad, color) y pilotos (ritmo, regularidad, salida, ataque, defensa, errores), puestos a
  mano desde cómo acabó 2025.

- **Fotos de los pilotos**: no se incluyen en esta versión; el marco lleva las iniciales.

## Calibración (medida con arneses fuera de Unity)

- Perfil de velocidad: un solo juego de parámetros da las poles de 2025 con error medio 0 % y
  dispersión 3 %; cada circuito se ajusta a su pole. El agarre a alta velocidad es un parámetro
  efectivo (~7,8 g), más que un F1 real: compensa curvas que el GPS suavizado cierra de más.
- Carrera (480 GP): McLorin 52 % de victorias, Rad Bull 32 %, Marcedes 12 %, Ferrani 3 %; 9
  adelantamientos por carrera; 0 solapes; el poleman gana el 85 % (3 vueltas).
- Campeonato (Willians, 30 temporadas): sin decisiones 21 puntos; desarrollando 95; acertando la
  carga 119; agresivo gestionando motores 129; fallando la carga 77.

## Licencia

El código es de Marino Posadas, con licencia MIT (`LICENSE`). Lo que no es código propio
lleva su propia licencia (tabla siguiente).

## Créditos y licencias de lo que no es código propio

| Qué | De dónde | Licencia |
|---|---|---|
| Trazados de los circuitos (`Tools/Circuitos/fuentes`) | github.com/bacinger/f1-circuits | MIT (con su `LICENSE.md`) |
| Edificios, gradas, boxes, vallas, árboles y farola (`Assets/ModelosRacing`, `Assets/Decorado`) | Kenney (kenney.nl): Racing Kit, City Kit Commercial, City Kit Roads | CC0 |
| Tipografía (`Resources/Fuentes`) | TeX Gyre Adventor (GUST, vía CTAN) | GUST Font License (con su texto) |
| Fotos de los pilotos | no se incluyen en esta versión (salen las iniciales) | — |
| Sonidos (`Resources/Sonido`) | generados con `Tools/Sonido/sintetizar.py` | propios |
| Planos de los circuitos | dibujados en el juego a partir del trazado | propios |
| Coche de la versión privada (`Assets/Coches/F1_2026`) | origen desconocido (FBX de Blender 2.72, de un paquete de 2014) | **no va en la versión pública** |
| Coche de la versión pública (`Assets/Coches/Monoplaza`) | «f2002» de som_natalino (som-natalino.itch.io/low), piloto y libreas con `Tools/Coche` | CC0 |
| Banderas (`Resources/Flags`) | flagpedia.net / flagcdn.com (de los vectores de Wikimedia Commons) | dominio público |

Juego de aficionado, sin ánimo de lucro y **no oficial**: no tiene relación con la Fórmula 1, la
FIA, Formula One Management ni ninguna escudería. Los nombres de pilotos, escuderías y Grandes
Premios se usan solo para describir la temporada que se simula.

En esta versión los nombres de pilotos y escuderías son inventados.

## Pendiente

- Interfaz definitiva (ahora IMGUI); ruedas que giren.
