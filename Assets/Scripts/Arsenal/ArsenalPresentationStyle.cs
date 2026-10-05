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

    /// <summary>Единственный автор композиции: target poses SDK anchors и карточки по физической зоне.</summary>
    [CreateAssetMenu(fileName = "ArsenalPresentation", menuName = "VR Battlegrounds/Arsenal/Presentation Style")]
    public sealed class ArsenalPresentationStyle : ScriptableObject
    {
        [Serializable]
        public sealed class ZoneDefaults
        {
            public ArsenalPresentationZone Zone;
            public ArsenalPresentationPose ItemTarget;
            public ArsenalPresentationPose MagazineTarget;
            public ArsenalPresentationPose CardTarget;
            public Vector2 CardSize = new Vector2(.15f, .16f);
            public float CardFontSize = .16f;
            public List<ArsenalSupportPose> Supports = new List<ArsenalSupportPose>();
        }

        [Serializable]
        public sealed class WeaponException
        {
            public WeaponInfo Weapon;
            public ArsenalPresentationZone Zone;
            public bool OverrideItem;
            public ArsenalPresentationPose ItemTarget;
            public bool OverrideMagazine;
            public ArsenalPresentationPose MagazineTarget;
            public bool OverrideCard;
            public ArsenalPresentationPose CardTarget;
            public Vector2 CardSize = new Vector2(.15f, .16f);
            public float CardFontSize = .16f;
            public bool OverrideSupports;
            public List<ArsenalSupportPose> Supports = new List<ArsenalSupportPose>();
        }

        [SerializeField] private GameObject _supportModule;
        [SerializeField] private Material _returnReadyMaterial;
        [SerializeField] private List<ZoneDefaults> _zones = new List<ZoneDefaults>();
        [SerializeField] private List<WeaponException> _exceptions = new List<WeaponException>();
        public GameObject SupportModule => _supportModule;
        public Material ReturnReadyMaterial => _returnReadyMaterial;
        public IReadOnlyList<ZoneDefaults> Zones => _zones;
        public IReadOnlyList<WeaponException> Exceptions => _exceptions;
    }
}
