using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Жетон готовности — сетевой предмет, который стена арсенала выдаёт в рантайме из префаба
    /// (<c>Assets/Prefabs/Arsenal/DogTag/DogTag.prefab</c>) и вешает на крючок своей панели
    /// (<see cref="DogTagController"/>).
    ///
    /// <para>
    /// <b>Почему жетон больше не вшит в станцию.</b> Пока он лежал внутри <c>DogTagPanel</c>, у каждой
    /// станции на каждой сцене был свой экземпляр со своими <c>UniqueId</c> UltimateXR, и любая правка
    /// компонентов жетона (точки хвата, поза) требовала правок всех сцен. Выданный префаб один на всех:
    /// идентичность ему выравнивает <c>NetworkUxrIdentity</c> по <c>netId</c>, как оружию стены.
    /// </para>
    ///
    /// <para>
    /// Сам компонент — только метка: по ней панель проверяет, что ей выдали именно жетон, а сценарии
    /// и проверки отличают жетон от оружия и магазинов среди заспавненных предметов.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UxrGrabbableObject))]
    public class DogTagItem : MonoBehaviour
    {
        private UxrGrabbableObject _grabbable;

        /// <summary>Хватаемый компонент жетона.</summary>
        public UxrGrabbableObject Grabbable
        {
            get
            {
                if (_grabbable == null) _grabbable = GetComponent<UxrGrabbableObject>();
                return _grabbable;
            }
        }
    }
}
