using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Звуки ствола из набора пака Hands (<c>Sounds/&lt;N&gt;_Set</c>): что резать. Режет <see cref="HandsPackAudio" /> тем же
    /// инструментом, что звуки KINEMATION (<see cref="KinemationAudio" />). Пусто — звука из пака нет: вставка и снятие магазина —
    /// запасная пара категории ствола (<c>WeaponFeedbackDefaults</c>) и дефолт роли гнезда, затвор/помпа — дефолт категории
    /// хоста WeaponSystem; подставляются в момент проигрывания, в префаб не пишутся.
    /// </summary>
    public sealed class HandsPackSoundRecipe
    {
        public string Shot;          // выстрел: Sounds/1Assault_Rifle_Set/Rifle_Shot_1.mp3
        public float ShotLength = 1.5f;
        public string Reload;        // перезарядка набора одним файлом: из неё режутся снятие магазина и (SlideFromReload) затвор
        public string ReloadClip;    // клип пака той же перезарядки: по ходу деталей в нём — время событий звука
        public bool SlideFromReload; // затвор назад/вперёд — куски Reload по ходу ActionPart в ReloadClip
        public string Bolt;          // отдельный звук затвора/помпы: делится по самой длинной паузе на «назад» и «вперёд»
        public string Load;          // заряжание патрона в окно приёма (ручное заряжание)
    }

    /// <summary>
    /// Нарезка звуков стволов пака Hands под Quest (моно, короткий хвост) — аналог <c>KinemationWeaponBuilder.MakeSounds</c>.
    /// Куски пишутся в <c>Assets/Audio/SFX/Weapons/HandsPack/&lt;ствол&gt;/</c> (папка ствола — в откате пересборки,
    /// <see cref="HandsPackWeaponBuilder.RebuildOutputs" />); файлы пака не меняются. Время события перезарядки — по ходу детали
    /// в клипе пака (<see cref="HandsPackWeapon.Excursion" />): звук пак играет с начала клипа.
    /// </summary>
    public static class HandsPackAudio
    {
        public const string Pack = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/";
        public const string Root = "Assets/Audio/SFX/Weapons/HandsPack";

        private const float MagazineCut = 0.6f;
        private const float BoltCut = 0.45f;
        private const float LoadLength = 1f;

        public static string Folder(string weapon) => $"{Root}/{weapon}";

        /// <summary>Исходные звуки пака рецепта (входы пересборки и preflight).</summary>
        public static IEnumerable<string> Sources(HandsPackWeaponRecipe r)
        {
            HandsPackSoundRecipe s = r.PackSounds;
            if (s == null) return Array.Empty<string>();
            return new[] { s.Shot, s.Reload, s.Bolt, s.Load }.Where(p => !string.IsNullOrEmpty(p)).Distinct();
        }

        /// <summary>
        /// Режет звуки рецепта и записывает пути кусков в рецепт (<see cref="HandsPackWeaponRecipe.ShotAudio" />,
        /// <see cref="HandsPackWeaponRecipe.TakeOutAudio" />, затвор/помпа, <see cref="HandsPackWeaponRecipe.LoadAudio" />).
        /// <paramref name="timing" /> — свой экземпляр модели: импорт новых WAV обновляет базу ассетов (как у KINEMATION).
        /// Где звука в наборе нет — поле рецепта не меняется, отчёт называет дефолт.
        /// </summary>
        public static string MakeSounds(HandsPackWeaponRecipe r, HandsPackWeapon timing)
        {
            HandsPackSoundRecipe s = r.PackSounds;
            if (s == null) return $"{r.Name}: звуков пака нет — прежние поля рецепта.";
            string folder = Folder(r.Name);
            var report = new List<string>();

            if (!string.IsNullOrEmpty(s.Shot))
            {
                if (!string.IsNullOrEmpty(r.ShotAudio))
                    throw new InvalidOperationException($"{r.Name}: выстрел задан и готовым клипом ({r.ShotAudio}), и звуком пака ({s.Shot})");
                r.ShotAudio = AssetDatabase.GetAssetPath(KinemationAudio.Shot(s.Shot, s.ShotLength, folder, $"{r.Name}_Shot"));
                report.Add("выстрел " + Short(s.Shot));
            }
            else report.Add("выстрел " + (string.IsNullOrEmpty(r.ShotAudio) ? "НЕТ" : r.ShotAudio));

            // Снятие магазина: момент, когда магазин ушёл из гнезда в клипе перезарядки. Внутренний магазин (трубка) не снимается.
            if (!r.MagazineIsInternal)
            {
                float leave = -1f, seat = -1f;
                if (!string.IsNullOrEmpty(s.Reload)) (leave, seat) = timing.Excursion(r.MagazinePart, s.ReloadClip);
                if (leave >= 0f)
                {
                    r.TakeOutAudio = Audible(KinemationAudio.CutAtEvent(s.Reload, leave, MagazineCut, folder, $"{r.Name}_MagOut"), "снятие магазина", report);
                    if (r.TakeOutAudio != null) report.Add($"снятие магазина {Short(s.Reload)} @{leave:F2} с");
                }
                else report.Add("снятие магазина — дефолт категории ствола" +
                                (string.IsNullOrEmpty(s.Reload) ? " (в наборе нет перезарядки)" : $" (магазин в {s.ReloadClip} неподвижен)"));
                // Вставка — момент, когда магазин встал в гнездо в том же клипе (решение пользователя 2026-10-23).
                if (seat >= 0f && seat > leave)
                {
                    r.InsertAudio = Audible(KinemationAudio.CutAtEvent(s.Reload, seat, MagazineCut, folder, $"{r.Name}_MagIn"), "вставка магазина", report);
                    if (r.InsertAudio != null) report.Add($"вставка магазина {Short(s.Reload)} @{seat:F2} с");
                }
                else report.Add("вставка магазина — дефолт категории ствола" + (string.IsNullOrEmpty(s.Reload) ? " (в наборе нет перезарядки)" : ""));
            }

            if (r.Action != HandsPackWeaponRecipe.ActionKind.None)
            {
                string action = r.Action == HandsPackWeaponRecipe.ActionKind.Pump ? "помпа" : "затвор";
                if (!string.IsNullOrEmpty(s.Bolt))
                {
                    (AudioClip back, AudioClip forward) = KinemationAudio.SplitAtGap(s.Bolt, folder, $"{r.Name}_BoltBack", $"{r.Name}_BoltForward");
                    r.SlideBackAudio = AssetDatabase.GetAssetPath(back);
                    r.SlideForwardAudio = AssetDatabase.GetAssetPath(forward);
                    report.Add($"{action} назад/вперёд — {Short(s.Bolt)} по паузе");
                }
                else if (s.SlideFromReload && !string.IsNullOrEmpty(s.Reload))
                {
                    (float back, float forward) = timing.Excursion(r.ActionPart, s.ReloadClip);
                    if (forward >= 0f)
                    {
                        // Затвор ушёл и вернулся один раз (оттяжка и отпускание рукоятки) — «назад» и «вперёд» по краям хода;
                        // клип с уже открытым затвором — один щелчок на оба, как у KINEMATION.
                        if (back < 0.05f) back = forward;
                        // Время хода берётся из клипа набора, а звук — из перезарядки набора: у стволов, чей клип не совпадает с
                        // записью (MP5K на Reload.mp3 автоматного набора), кусок может прийтись на паузу — тогда дефолт категории.
                        r.SlideBackAudio = Audible(KinemationAudio.CutAtEvent(s.Reload, back, BoltCut, folder, $"{r.Name}_BoltBack"), action + " назад", report);
                        r.SlideForwardAudio = Audible(KinemationAudio.CutAtEvent(s.Reload, forward, BoltCut, folder, $"{r.Name}_BoltForward"), action + " вперёд", report);
                        report.Add($"{action} назад/вперёд — {Short(s.Reload)} @{back:F2}/{forward:F2} с");
                    }
                    else report.Add($"{action} — дефолт категории хоста ({r.ActionPart} в {s.ReloadClip} неподвижен)");
                }
                else report.Add($"{action} — дефолт категории хоста (в наборе нет звука {action}а)");
            }

            if (!string.IsNullOrEmpty(s.Load))
            {
                r.LoadAudio = AssetDatabase.GetAssetPath(KinemationAudio.Shot(s.Load, LoadLength, folder, $"{r.Name}_Load"));
                report.Add("заряжание " + Short(s.Load));
            }

            string text = $"[HandsPackAudio] {r.Name}: " + string.Join("; ", report);
            GameLog.WeaponSystem.Info(text);
            return text;
        }

        /// <summary>
        /// Путь нарезки или null, если кусок — почти тишина (порог <see cref="EditorTools.Audio.SfxClipLevel.NearSilenceDbfs" />, как у
        /// проверки хоста WeaponSystem): такой кусок удаляется, поле остаётся пустым — звучит дефолт категории.
        /// </summary>
        private static string Audible(AudioClip clip, string what, List<string> report)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (!EditorTools.Audio.SfxClipLevel.TryPeakDbfs(clip, out float peak, out _) || peak >= EditorTools.Audio.SfxClipLevel.NearSilenceDbfs)
                return path;
            string name = clip.name; // после удаления ассета клип уничтожен
            AssetDatabase.DeleteAsset(path);
            report.Add($"{what} — дефолт категории (кусок {name} почти тишина, пик {peak:0.#} dBFS)");
            return null;
        }

        private static string Short(string path) => path.StartsWith(Pack, StringComparison.Ordinal) ? path.Substring(Pack.Length) : path;
    }
}
