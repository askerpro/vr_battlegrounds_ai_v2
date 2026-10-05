using System;
using System.Linq;
using UnityEngine;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Weapons.Sights
{
    /// <summary>Редакторские данные отдельного стенда. Сцена не входит в Build Settings; SDK не запускается.</summary>
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class ManualSightCalibrationSession : MonoBehaviour
    {
        public WeaponInfo Weapon;
        public GameObject Instance;
        public Transform Placement;
        public Transform Target;
        public UxrProjectileSource Source;
        public float Distance = 15;
        public int ShotTypeIndex;
        public string InputPath;
        public string OutputPath;
        public string InputHash;
        public string SavedPose;
        public Vector3 RootPosition;
        public Quaternion RootRotation;
        public Vector3 RootScale;
        public Vector3 MuzzlePosition;
        public Vector3 MuzzleForward;
        public float SavedDistance;
        public bool OutputExisted;
        public string OutputHash;
        public bool ProfileExisted;
        public string ProfileHash;
        public ProtectedPose[] ProtectedNodes;
        public ProtectedShot[] ProtectedShots;

        [Serializable] public sealed class ProtectedPose
        { public Transform Node; public Transform Parent; public Vector3 Position; public Quaternion Rotation; public Vector3 Scale; }
        [Serializable] public sealed class ProtectedShot
        { public UxrProjectileSource Source; public int Index; public int Count; public Transform Muzzle; }

        public Transform Muzzle => Source != null && ShotTypeIndex >= 0 && ShotTypeIndex < Source.ShotTypes.Count
            ? Source.ShotTypes[ShotTypeIndex].ShotSource : null;

        public bool TryMarkers(out SightAlignmentMarker rear, out SightAlignmentMarker front, out string reason)
        {
            rear = front = null; reason = "";
            if (Instance == null) { reason = "Выберите оружие."; return false; }
            var markers = Instance.GetComponentsInChildren<SightAlignmentMarker>(true);
            var rears = markers.Where(m => m.Role == SightAlignmentRole.Rear).ToArray();
            var fronts = markers.Where(m => m.Role == SightAlignmentRole.Front).ToArray();
            if (markers.Length != 2 || rears.Length != 1 || fronts.Length != 1)
            { reason = "Нужен ровно один маркер Rear (целик) и один Front (мушка)."; return false; }
            rear = rears[0]; front = fronts[0]; return true;
        }

    }
}
