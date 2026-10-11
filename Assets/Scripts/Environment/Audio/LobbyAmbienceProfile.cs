using System;
using UnityEngine;

namespace VrBattlegrounds.LobbyAudio
{
    public enum LobbyAmbientKind { Helicopter, Truck, Gunfire, Soldiers }
    public enum LobbyAmbientMode { Point, Path, TwoDimensional }

    [CreateAssetMenu(menuName = "VR Battlegrounds/Lobby/Профиль эмбиента")]
    public sealed class LobbyAmbienceProfile : ScriptableObject
    {
        public AudioClip forest;
        [Range(0, 1)] public float forestVolume = .08f;
        public Vector2 globalPause = new Vector2(18, 35);
        public Episode[] episodes = Array.Empty<Episode>();

        [Serializable]
        public sealed class Episode
        {
            public LobbyAmbientKind kind;
            public LobbyAmbientMode mode;
            public AudioClip[] clips = Array.Empty<AudioClip>();
            public Vector2 cooldown = new Vector2(60, 120);
            public Vector2Int repeats = new Vector2Int(1, 1);
            [Min(.1f)] public float duration = 12;
            [Range(0, 1)] public float volume = .25f;
            [Min(0)] public float fade = 2;
            [Min(.1f)] public float minDistance = 5;
            [Min(1)] public float maxDistance = 120;
        }
    }
}
