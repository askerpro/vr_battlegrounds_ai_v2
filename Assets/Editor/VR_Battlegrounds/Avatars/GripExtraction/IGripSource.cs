using System;
using System.Collections.Generic;
using UltimateXR.Core;
using VrBattlegrounds.Editor.Gameplay;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Точка хвата, которую знает источник: поза, предмет, корпус-опора и точка хвата SDK.</summary>
    public sealed class GripSourcePoint
    {
        /// <summary>Имя позы — имя ассета; SDK ищет позу у аватара по нему.</summary>
        public string Pose;

        /// <summary>hands | kinemation (позже avatar-record, manual).</summary>
        public string SourceKind;

        /// <summary>Путь префаба предмета.</summary>
        public string Weapon;

        /// <summary>Корпус предмета в префабе — опора кадра источника.</summary>
        public string BodyPath;

        public int GrabPoint;

        /// <summary>Рука источника; вторая рука позы — её зеркало.</summary>
        public UxrHandSide SourceSide;

        /// <summary>Папка позы прежнего импортёра (профиль MEF пишет туда же).</summary>
        public string LegacyPoseFolder;

        /// <summary>Папка align под <c>Grabs</c>: <c>Grabs/&lt;AlignSource&gt;/&lt;поза&gt;/&lt;аватар&gt;/Left|Right</c>.</summary>
        public string AlignSource;

        internal HandsPackPoseRecipe PackRecipe;
        internal KinemationWeaponRecipe KinemationRecipe;

        /// <summary>Ключ руки в параметрах запроса: <c>&lt;поза&gt;/Left|Right</c>.</summary>
        public string HandKey(UxrHandSide side) => $"{Pose}/{side}";
    }

    /// <summary>
    /// Источник нейтрального хвата (<see cref="GripSample" />). Реализации: <see cref="PackClipGripSource" /> — кадр
    /// клипа пака (Hands, KINEMATION). Точки расширения (ещё не реализованы):
    /// <list type="bullet">
    /// <item>AvatarRecordGripSource — существующая запись хвата другого аватара на предмете: кадр — кисть
    /// аватара-источника, поставленная по его align и позе (этап R3: записи Cyborg → MEF_cyborg_hands);</item>
    /// <item>ManualGripSource — ручные точки подушечек (<c>grip-authoring</c>), заполняет <see cref="GripSample.Pads" />.</item>
    /// </list>
    /// </summary>
    public interface IGripSource : IDisposable
    {
        /// <summary>Имя источника в запросе (<c>source</c>).</summary>
        string Id { get; }

        /// <summary>Точки по селекторам запроса; неизвестный селектор — ошибка.</summary>
        IReadOnlyList<GripSourcePoint> Select(IReadOnlyCollection<string> selectors);

        /// <summary>Кадр точки (рука <see cref="GripSourcePoint.SourceSide" />).</summary>
        GripSample Sample(GripSourcePoint point);
    }
}
