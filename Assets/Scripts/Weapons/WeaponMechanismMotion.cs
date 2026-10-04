using System;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Механические позы пака относительно корпуса. Только данные, без событий FPS и gameplay.</summary>
    public sealed class WeaponMechanismMotion : ScriptableObject
    {
        [Serializable] public struct Key
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
        }
        [Serializable] public sealed class Track
        {
            public string Part;
            public string SourcePath;
            public int SourceBoneIndex;
            public bool AnimateRotation = true;
            public Key[] Keys;

            public Pose Evaluate(float time)
            {
                if (Keys == null || Keys.Length == 0) return Pose.identity;
                int low = 0, high = Keys.Length - 1;
                if (time <= Keys[0].Time) return new Pose(Keys[0].Position, Keys[0].Rotation);
                if (time >= Keys[high].Time) return new Pose(Keys[high].Position, Keys[high].Rotation);
                while (high - low > 1) { int mid = (high + low) / 2; if (Keys[mid].Time <= time) low = mid; else high = mid; }
                float t = Mathf.InverseLerp(Keys[low].Time, Keys[high].Time, time);
                return new Pose(Vector3.Lerp(Keys[low].Position, Keys[high].Position, t), Quaternion.Slerp(Keys[low].Rotation, Keys[high].Rotation, t));
            }
        }
        [Serializable] public sealed class Cycle
        {
            public string SourceGuid;
            public long SourceLocalId;
            public float Duration;
            public bool HoldEnd;
            public Track[] Tracks;
        }
        [SerializeReference] public Cycle Fire;
        [SerializeReference] public Cycle Empty;
        [SerializeReference] public Cycle Manual;
    }
}
