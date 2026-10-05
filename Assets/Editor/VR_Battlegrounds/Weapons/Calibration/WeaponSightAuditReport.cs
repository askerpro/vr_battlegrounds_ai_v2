using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Снимок сохранённых ассетов; не доказательство runtime-попадания или наличия прицела.</summary>
    [Serializable]
    public sealed class WeaponSightAuditReport
    {
        public int schemaVersion = 1;
        public string observedAtUtc;
        public bool passed;
        public bool calibrationAccepted;
        public int registryCount;
        public float defaultZeroDistance;
        public AssetIdentity registry;
        public AssetIdentity settings;
        public List<string> failures = new List<string>();
        public List<WeaponEntry> weapons = new List<WeaponEntry>();

        [Serializable]
        public sealed class AssetIdentity
        {
            public string path;
            public string guid;
            public long localFileId;
            public string dependencyHash;
        }

        [Serializable]
        public sealed class WeaponEntry
        {
            public int registryIndex;
            public string weaponId;
            public AssetIdentity info;
            public AssetIdentity prefab;
            public string status = "NeedsReferenceReview";
            public Vector3 rootScale;
            public List<Node> nodes = new List<Node>();
            public List<Shot> shots = new List<Shot>();
            public List<Trigger> triggers = new List<Trigger>();
            public List<Geometry> geometry = new List<Geometry>();
            public List<Profile> profiles = new List<Profile>();
            public List<string> componentTypes = new List<string>();
        }

        [Serializable]
        public sealed class Node
        {
            public AssetIdentity identity;
            public string semanticPath;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        [Serializable]
        public sealed class Shot
        {
            public string sourceComponentPath;
            public int shotTypeIndex;
            public Node origin;
            public Node tip;
            public Vector3 prefabPosition;
            public Vector3 prefabDirection;
            public bool automaticTrajectory;
            public float speed;
            public AssetIdentity projectile;
        }

        [Serializable]
        public sealed class Trigger
        {
            public string firearmPath;
            public int triggerIndex;
            public int shotTypeIndex;
        }

        [Serializable]
        public sealed class Geometry
        {
            public string nodePath;
            public string rendererType;
            public AssetIdentity mesh;
            public int vertexCount;
            public int subMeshCount;
            public Bounds localBounds;
            public bool isReadable;
            public List<AssetIdentity> materials = new List<AssetIdentity>();
        }

        [Serializable]
        public sealed class Profile
        {
            public AssetIdentity asset;
            public string sightId;
            public int triggerIndex;
            public int shotTypeIndex;
            public bool usesCustomDistance;
            public float zeroDistance;
            public string status = "NeedsReferenceReview";
        }
    }
}
