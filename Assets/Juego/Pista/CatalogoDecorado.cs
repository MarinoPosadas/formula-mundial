using UnityEngine;

namespace F1.Juego
{
    /// <summary>
    /// Las piezas con las que se viste un circuito (kits de Kenney, todos CC0). Lo
    /// rellena el menú F1 ▸ Crear escena…; al estar referenciado desde la escena, viaja en la build.
    /// </summary>
    [CreateAssetMenu(menuName = "F1/Catálogo de decorado")]
    public class CatalogoDecorado : ScriptableObject
    {
        public GameObject garaje, oficinaBoxes, gradaCubierta, grada, porticoSalida;
        public GameObject valla, vallaBaja, torreRoja, torreVerde, farolaCircuito, banderaCuadros;
        public GameObject arbolGrande, arbolPequeno, carpa;
        public GameObject[] edificios;
        public GameObject farolaCalle;

        /// <summary>Giro (grados) que hay que dar a cada tipo de pieza para que su frente mire a la pista.</summary>
        public float giroKenney = 0f, giroEdificios = 0f, giroFarolaCalle = 0f;
    }
}
