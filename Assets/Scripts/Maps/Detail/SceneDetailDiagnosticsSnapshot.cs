using System;
using UnityEngine;

namespace VrBattlegrounds.Maps.Detail
{
    /// <summary>
    /// Отсоединённый снимок данных применителя. Unity-ссылки служат только для сопоставления;
    /// наличие рендерера в effective не доказывает выбранный камерой LOD или draw.
    /// </summary>
    [Serializable]
    public sealed class SceneDetailDiagnosticsSnapshot
    {
        public int schemaVersion = 1;
        public string scenePath;
        public string sceneName;
        public SceneDetailApplier applier;
        public SceneDetailProfile profile;
        public int observationFrame;
        public double observationRealtime;
        public float midStart;
        public float farStart;
        public SceneDetailProfile.PlaneRule mid;
        public SceneDetailProfile.PlaneRule far;
        public int globalTextureMipmapLimit;
        public float lodBias;
        public Group[] groups;
        public RendererState[] renderers;
        public TextureGroupState[] textureGroups;
        public string[] limitations;
        public bool complete;

        [Serializable]
        public sealed class Group
        {
            public LODGroup group;
            public float arenaEdgeDistance;
            public string band;
            public SceneDetailProfile.PlaneRule rule;
            public Level[] original;
            public Level[] effective;
            public int[] effectiveToOriginal;
            public bool mappingVerified;
            public string reason;
        }

        [Serializable]
        public sealed class Level
        {
            public float screenRelativeTransitionHeight;
            public float fadeTransitionWidth;
            public Renderer[] renderers;
        }

        [Serializable]
        public sealed class RendererState
        {
            public Renderer renderer;
            public bool baselineKnown;
            public bool baselineEnabled;
            public bool currentEnabled;
            public bool hasManagedDistance;
            public float arenaEdgeDistance;
            public float size;
        }

        [Serializable]
        public sealed class TextureGroupState
        {
            public string name;
            public bool exists;
            public bool baselineKnown;
            public TextureMipmapLimitSettings baseline;
            public TextureMipmapLimitSettings current;
        }
    }
}
