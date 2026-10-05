using System;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    [Serializable] public sealed class MapBodyPoseTemplate
    {
        public Vector3 eyeOffset, muzzleOffset;
        public ImpactBodySample[] body = Array.Empty<ImpactBodySample>();
    }

    /// <summary>Числовой профиль оценки. Калибровка не выводится из демонстрационных размеров.</summary>
    [Serializable] public sealed class MapBodyProfileData
    {
        public string id;
        public int version = 1;
        public bool calibrated;
        public float radius = .5f, speed = 1;
        public MapBodyPoseTemplate standing = new MapBodyPoseTemplate();
        public MapBodyPoseTemplate crouching = new MapBodyPoseTemplate();
    }

    public sealed class MapBodyProfile : ScriptableObject
    {
        public MapBodyProfileData data = new MapBodyProfileData();
    }
}
