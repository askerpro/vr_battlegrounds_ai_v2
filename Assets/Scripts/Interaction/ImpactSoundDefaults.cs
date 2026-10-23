using System;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Чей набор ударов по умолчанию берёт <see cref="ImpactSound" /> без своих клипов. Значения сериализованы в префабах —
    /// новые только в конец.
    /// </summary>
    public enum ImpactSoundKind { Weapon, Magazine }

    /// <summary>
    /// Звуки удара о мир по умолчанию для <see cref="ImpactSound" /> (решение пользователя 2026-10-23: дефолты — данные в
    /// ассете, не константы в коде): набор для оружия (тяжёлый металл) и для магазинов (лёгкий). Предмет без своих клипов
    /// звучит набором своего вида (<see cref="ImpactSound.Kind" />) в момент удара, поэтому подбор на слух — правка этого
    /// ассета, без пересохранения префабов. Свои клипы предмета — оверрайд.
    ///
    /// <para>
    /// Ассет — <c>Resources/ImpactSoundDefaults.asset</c>, как <see cref="MagazineAnchorSoundDefaults" />: удар — свойство
    /// предмета (оружие, магазин, патрон), у магазина нет хоста с категорией, а ссылка в каждом из десятков префабов была бы
    /// ещё одной копией одного и того же. Нет ассета — предмет звучит только своими клипами.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Audio/Impact Sound Defaults")]
    public sealed class ImpactSoundDefaults : ScriptableObject
    {
        [Tooltip("Падение оружия: варианты удара корпуса о мир (чередуются случайно).")]
        [SerializeField] private AudioClip[] _weapon = new AudioClip[0];

        [Tooltip("Падение магазина (и выдаваемого патрона): варианты удара о мир.")]
        [SerializeField] private AudioClip[] _magazine = new AudioClip[0];

        /// <summary>Набор вида (не null; пустой — у вида дефолта нет).</summary>
        public AudioClip[] For(ImpactSoundKind kind)
        {
            AudioClip[] clips = kind == ImpactSoundKind.Magazine ? _magazine : _weapon;
            return clips ?? Array.Empty<AudioClip>();
        }

        private static ImpactSoundDefaults s_instance;

        /// <summary>Настройки из Resources или null, если ассета нет.</summary>
        public static ImpactSoundDefaults Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<ImpactSoundDefaults>(nameof(ImpactSoundDefaults));
                return s_instance;
            }
        }
    }
}
