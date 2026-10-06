using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Кандидаты позы сидения для <see cref="SitCandidateStand"/>: клип и кадр, который станет статической позой
    /// <c>Legs_Crouch</c> = 2. Стоя и на колене — клипы набора винтовки, как в контроллере ног.
    /// </summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Debug/Sit Candidate List", fileName = "SitCandidates")]
    public class SitCandidateList : ScriptableObject
    {
        [Serializable]
        public class Candidate
        {
            [Tooltip("Подпись над манекеном.")]
            public string label;

            [Tooltip("Клип (humanoid), из которого берётся поза.")]
            public AnimationClip clip;

            [Tooltip("Кадр клипа (при частоте кадров клипа) — поза сидения.")]
            public float frame;
        }

        [Tooltip("Поза стоя (Legs_Crouch = 0) — покой набора винтовки.")]
        public AnimationClip stand;

        [Tooltip("Поза на колене (Legs_Crouch = 1) — присед набора винтовки.")]
        public AnimationClip kneel;

        [Tooltip("Кандидаты сидения — по манекену на каждого.")]
        public List<Candidate> candidates = new List<Candidate>();
    }
}
