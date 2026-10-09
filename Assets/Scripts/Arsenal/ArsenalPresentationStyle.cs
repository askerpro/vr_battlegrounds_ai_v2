using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Абсолютная поза target в slot-local frame. Масштаб предмета здесь не авторится.</summary>
    [Serializable]
    public struct ArsenalPresentationPose
    {
        public Vector3 Position;
        public Vector3 EulerAngles;
        public Quaternion Rotation => Quaternion.Euler(EulerAngles);
        public ArsenalPresentationPose(Vector3 position, Quaternion rotation)
        { Position = position; EulerAngles = rotation.eulerAngles; }
    }

    public enum ArsenalSupportAnchorKind { Weapon, Magazine }

    [Serializable]
    public struct ArsenalSupportPose
    {
        public string Role;
        public ArsenalSupportAnchorKind AnchorKind;
        public ArsenalPresentationPose SlotPose;
    }

    /// <summary>
    ///     Внешний вид арсенала: модуль опор и материал подсказки возврата. Раскладку в слоте задаёт ассет
    ///     <see cref="ArsenalSlotLayout" />, геометрию рядов — корпус (<see cref="ArsenalSlotRow" />).
    /// </summary>
    [CreateAssetMenu(fileName = "ArsenalPresentation", menuName = "VR Battlegrounds/Arsenal/Presentation Style")]
    public sealed class ArsenalPresentationStyle : ScriptableObject
    {
        [SerializeField] private GameObject _supportModule;
        [SerializeField] private Material _returnReadyMaterial;
        public GameObject SupportModule => _supportModule;
        public Material ReturnReadyMaterial => _returnReadyMaterial;
    }
}
