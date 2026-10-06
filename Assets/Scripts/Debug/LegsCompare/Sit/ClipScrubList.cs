using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Данные <see cref="ClipScrubStand"/>: цепочка поз аватара сверху вниз (<see cref="chain"/> — как уровни
    /// <c>Legs_Crouch</c> контроллера ног: стоя, присед/колено, промежуточные…) и кандидаты (<see cref="entries"/>) — по манекену
    /// на строку: цепочка аватара, ниже её последней позы — клип кандидата. Кадры хранятся здесь — выбранные точки перехода
    /// переживают пересборку сцены и видны агенту.
    /// </summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Debug/Clip Scrub List", fileName = "ClipScrub")]
    public class ClipScrubList : ScriptableObject
    {
        /// <summary>Поза цепочки аватара: клип на кадре.</summary>
        [Serializable]
        public class KeyPose
        {
            [Tooltip("Название позы (стоя, присед, колено…).")]
            public string label;

            [Tooltip("Клип позы (humanoid).")]
            public AnimationClip clip;

            [Tooltip("Кадр клипа — поза (при частоте кадров клипа).")]
            public float frame;
        }

        [Serializable]
        public class Entry
        {
            [Tooltip("Подпись (пусто — имя клипа).")]
            public string label;

            [Tooltip("Клип кандидата (humanoid) — ниже последней позы цепочки.")]
            public AnimationClip clip;

            [Tooltip("Поза, а не переход: кандидат — неподвижная поза на кадре frame, смешивается с последней позой цепочки по высоте головы (как blend tree Legs_Crouch игры). Выключено — переход: кадр отрезка по высоте головы.")]
            public bool poseBlend;

            [Tooltip("Поза (poseBlend) — кадр позы; переход без следования за камерой — кадр просмотра.")]
            public float frame;

            [Tooltip("Отрезок клипа: начало (включительно). Кандидат — только кадры отрезка; вход — кадр отрезка с самой высокой головой.")]
            public float rangeFrom;

            [Tooltip("Отрезок клипа: конец (включительно). 0 — до конца клипа.")]
            public float rangeTo;
        }

        [Tooltip("Папка кандидатов сидения — выпадающий список клипа строки (фаза кандидата).")]
        public string sitCandidatesFolder = "Assets/Art/Animations/Locomotion/SitCandidates";

        [Tooltip("Папка кандидатов приседа — выпадающий список клипа поз цепочки (кроме «стоя»).")]
        public string crouchCandidatesFolder = "Assets/Art/Animations/Locomotion/CrouchCandidates";

        [Tooltip("Позы аватара сверху вниз — общая часть всех строк (стоя → … → последняя поза перед кандидатом). Голова между позами — смешивание соседних, как 1D blend tree Legs_Crouch.")]
        public List<KeyPose> chain = new List<KeyPose>();

        public List<Entry> entries = new List<Entry>();

        /// <summary>
        /// Сохранённая комбинация: полный снимок строки подбора — своя цепочка поз (клипы и кадры), кандидат (клип, режим,
        /// отрезок или кадр позы) и зона смешивания. Вид стенда «Сохранённые» ставит их в ряд для сравнения.
        /// </summary>
        [Serializable]
        public class Combo
        {
            public string label;
            public List<KeyPose> chain = new List<KeyPose>();
            public Entry entry = new Entry();
            public float blendZone = 0.1f;
        }

        [Tooltip("Сохранённые комбинации — вид стенда «Сохранённые».")]
        public List<Combo> saved = new List<Combo>();

        public static KeyPose Copy(KeyPose k) => new KeyPose { label = k.label, clip = k.clip, frame = k.frame };

        public static Entry Copy(Entry e) => new Entry
        {
            label = e.label, clip = e.clip, poseBlend = e.poseBlend, frame = e.frame, rangeFrom = e.rangeFrom, rangeTo = e.rangeTo,
        };
    }
}
