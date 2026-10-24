using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>
    /// Монитор попаданий на «Обзоре» — там, где на карте есть стрельбище (<see cref="ShootingRangePhotoCapture"/>,
    /// сейчас лобби): фото последнего своего попадания в щит с меткой, урон, оружие, дистанция и смещение
    /// от центра; до первого попадания — снимок мишени без метки. Только рисует данные захвата.
    /// </summary>
    public sealed class OverviewRangeMonitor
    {
        /// <summary>Сторона снимка — в высотах строки темы.</summary>
        public const int PictureRows = 4;
        public const string Hint = "Попадите в щит — здесь появится точка попадания";

        private ShootingRangePhotoCapture _capture;
        private RawImage _picture;
        private TMP_Text _text;
        private int _revision = -1;

        /// <summary>Есть что показать: стрельбище на карте и графическое устройство для снимков.</summary>
        public static bool Available => ShootingRangePhotoCapture.Active != null && ShootingRangePhotoCapture.CanRender;

        /// <summary>Блок «Стрельбище» в конец <paramref name="content"/>; прежний блок уже снят MenuKit.Clear.</summary>
        public void Build(Transform content)
        {
            _capture = ShootingRangePhotoCapture.Active;
            _revision = -1;
            MenuKit.Section(content, "Стрельбище");
            RectTransform row = MenuKit.Row(content, MenuKit.PictureSize(PictureRows));
            _picture = MenuKit.Picture(row, null, PictureRows);
            _text = MenuKit.Label(row, Hint);
            Tick();
        }

        /// <summary>Блок снят вместе с содержимым экрана.</summary>
        public void Forget()
        {
            _capture = null;
            _picture = null;
            _text = null;
        }

        /// <summary>Каждый кадр: перерисовка только при новом снимке.</summary>
        public void Tick()
        {
            if (_picture == null || _capture == null) return;
            Texture2D photo = _capture.Revision > 0 ? _capture.Photo : null;
            Texture2D idle = photo == null ? _capture.IdlePhoto() : null;
            int revision = photo != null ? _capture.Revision : idle != null ? 0 : -1;
            if (revision == _revision) return;
            _revision = revision;

            Texture image = photo != null ? photo : idle;
            _picture.texture = image;
            _picture.enabled = image != null;
            _text.text = photo != null
                ? Caption(_capture.Damage, _capture.Zone, _capture.WeaponName, _capture.Distance, _capture.OffsetMillimetres)
                : Hint;
        }

        /// <summary>Подпись попадания: урон и зона щита крупно, ниже оружие, дистанция и смещение от центра щита.</summary>
        public static string Caption(float damage, HitZone zone, string weapon, float distance, Vector2 offsetMillimetres) =>
            "<size=150%>Урон " + Mathf.RoundToInt(damage) + "</size> · " + ZoneName(zone) +
            " ×" + HitZoneDamage.Multiplier(zone).ToString("0.##") + "\n" +
            (string.IsNullOrEmpty(weapon) ? "" : weapon + " · ") + distance.ToString("F1") + " м\n" +
            "X " + offsetMillimetres.x.ToString("+0;-0;0") + " · Y " + offsetMillimetres.y.ToString("+0;-0;0") + " мм";

        public static string ZoneName(HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return "голова";
                case HitZone.Arm: return "рука";
                case HitZone.Leg: return "нога";
                default: return "торс";
            }
        }
    }
}
