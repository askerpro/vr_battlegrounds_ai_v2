using System.Collections.Generic;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Ствол из пака Low Poly Weapons VOL.2: что взять из пака и числа механики, которых у пака нет (клипов нет).
    /// Остальное — общий рецепт <see cref="HandsPackWeaponRecipe" /> (доноры, теги, магазин), сборка — тем же
    /// <see cref="HandsPackWeaponBuilder" />.
    ///
    /// <para>
    /// Все векторы — в осях корпуса префаба пака (его корня), в единицах меша; у пака масштаб корня 1, единицы — метры.
    /// </para>
    /// </summary>
    public sealed class LowPolyWeaponRecipe
    {
        public HandsPackWeaponRecipe Weapon;

        public string PackPrefab;     // путь внутри пака без .prefab: Prefabs/Sniper/sniper_03
        public Vector3 Forward, Up;   // ствол и верх — оси меша (±X/±Y/±Z)
        public float BoltTravel;      // полный ход затвора, м
        public Vector3 BoltAxis;      // направление хода затвора назад — ось меша
        public float TriggerDeg;      // угол спуска до выстрела, °; знак — направление поворота
        public int TriggerAxis;       // локальная ось спуска 0/1/2 (X/Y/Z), как _triggerRotationAxis UxrFirearmWeapon
        public Vector3 PalmGrip, PalmSupport; // центры ладоней: правая на рукояти (точка 0), левая — вторая рука (точка 1)
    }

    /// <summary>
    /// Собирает стволы пака Low Poly Weapons VOL.2: геометрия — <see cref="LowPolyWeapon" />, сборка и магазин —
    /// <see cref="HandsPackWeaponBuilder" />, общий конец (прицел, хост WeaponSystem, баланс, теги) —
    /// <see cref="WeaponRebuildFinish" />. Звуков и поз рук у пака нет: выстрел — свой клип рецепта, затвор и магазин —
    /// от доноров; исходных поз <c>_orig</c> нет, хваты аватаров переносятся из текущего префаба как есть.
    /// Регистрация (WeaponInfo, реестр, сеть, стена, баланс) — шаги скилла <c>/add-weapon</c>, не сборщика.
    /// </summary>
    public static class LowPolyWeaponBuilder
    {
        public const string Pack = "Assets/ThirdParty/Low Poly Weapons VOL.2/";
        private const string HandsSounds = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/";

        /// <summary>
        /// Дальнобойная винтовка (роль AWP по месту в линейке, решение пользователя): Barrett M82 — самозарядная .50 BMG,
        /// магазин на 10. Модель пака — <c>sniper_03</c> (в FBX «M107»): корпус на корне, затвор <c>Bolt</c> с рукояткой
        /// справа, спуск <c>Trigger</c>, магазин <c>Magazin</c> с патронами <c>Bullets</c>. Ствол пака — по −Z (вершины FBX:
        /// дульный тормоз у z −0,70, приклад у +0,54, рукоять и спуск позади магазина), верх +Y.
        /// Без клипов затвора хост WeaponSystem — профиль ReturnToRest (строка в <see cref="WeaponSystemAuthoring.Weapons" />).
        /// </summary>
        public static LowPolyWeaponRecipe Barrett
        {
            get
            {
                HandsPackWeaponRecipe r = HandsPackWeaponBuilder.RifleDonor(new HandsPackWeaponRecipe
                {
                    Name = "Barrett", PrefabFolder = "Barrett", BodyPart = "sniper_03",
                    PoseClip = null, ActionClip = null, RestClip = null, // клипов у пака нет
                    TargetLength = 1.24f, // длина модели пака в префабе — длина прототипа (решение пользователя)
                    StaticParts = new string[0],
                    TriggerPart = "Trigger", ActionPart = "Bolt",
                    // Рукоятка затвора — выступ меша Bolt справа (вершины x −0,065…−0,029, z −0,313…−0,304 в осях Bolt).
                    ActionGripContact = new Vector3(-0.047f, 0.018f, -0.309f), // МЕРИТЬ: точка контакта на рукоятке затвора
                    SupportGrip = true,
                    UxrTag = "BackWeapon", MagazinePart = "Magazin", MagazineExtraParts = new[] { "Bullets" },
                    MagazineTag = "MagBarrett", MagazineCapacity = 10,
                    // Свой выстрел: снайперский набор пака Hands ни одним стволом не занят (WeaponShotSoundTests — без общего клипа).
                    ShotAudio = HandsSounds + "11Sniper_Rifle_Set/Sniper_Rifle_Shot_2.mp3"
                });
                // MuzzlePart не задан: дульный тормоз — часть корпуса, срез — передний край его вершин.
                r.ShotCycle = UxrShotCycle.SemiAutomatic; // самозарядная (донор M16 — автоматический огонь)
                return new LowPolyWeaponRecipe
                {
                    Weapon = r, PackPrefab = "Prefabs/Sniper/sniper_03",
                    Forward = Vector3.back, Up = Vector3.up, // по вершинам FBX; проверить на виде модели
                    BoltTravel = 0.08f,                      // МЕРИТЬ: ход затвора, м
                    BoltAxis = Vector3.forward,              // МЕРИТЬ: «назад» при стволе по −Z — это +Z
                    TriggerDeg = -15f,                       // МЕРИТЬ: низ спуска уходит назад (+Z) при повороте −15° вокруг X
                    TriggerAxis = 0,                         // МЕРИТЬ: ось поворота спуска — X (поперёк ствола)
                    PalmGrip = new Vector3(-0.025f, -0.09f, 0.19f),   // МЕРИТЬ: правая ладонь на пистолетной рукояти позади спуска
                    PalmSupport = new Vector3(0f, -0.015f, -0.16f)    // МЕРИТЬ: левая ладонь под цевьём, на 0,35 м впереди рукояти
                };
            }
        }

        public static IEnumerable<LowPolyWeaponRecipe> All => new[] { Barrett };

        /// <summary>Рецепт по имени ствола (<see cref="HandsPackWeaponRecipe.Name" />); null — нет.</summary>
        public static LowPolyWeaponRecipe Find(string name) => All.FirstOrDefault(r => r.Weapon.Name == name);

        public static string PackPrefabPath(LowPolyWeaponRecipe k) => Pack + k.PackPrefab + ".prefab";

        public static string PrefabPath(LowPolyWeaponRecipe k) => $"Assets/Prefabs/Weapons/{k.Weapon.PrefabFolder}/{k.Weapon.Name}.prefab";

        /// <summary>Собственный магазин ствола (как его называет <see cref="HandsPackWeaponBuilder" />).</summary>
        public static string MagazinePath(LowPolyWeaponRecipe k) =>
            $"Assets/Prefabs/Weapons/{k.Weapon.PrefabFolder}/{k.Weapon.MagazineName ?? k.Weapon.Name + "_mag"}.prefab";

        public static LowPolyWeapon Model(LowPolyWeaponRecipe k) => new LowPolyWeapon(k);

        public static string Report(LowPolyWeaponRecipe k)
        {
            using LowPolyWeapon model = Model(k);
            return model.Report();
        }

        /// <summary>Вход для <c>execute_code</c>: полная пересборка ствола по имени (<c>Barrett</c>).</summary>
        public static GameObject Build(string name) =>
            Build(Find(name) ?? throw new System.ArgumentException($"Нет рецепта Low Poly '{name}'. Есть: {string.Join(", ", All.Select(r => r.Weapon.Name))}"));

        /// <summary>
        /// Полная пересборка из пака: перенос хватов текущего префаба → геометрия, магазин, точка отдачи, области
        /// подсветки и хост WeaponSystem (<see cref="HandsPackWeaponBuilder.Build(HandsPackWeaponRecipe, IWeaponModel, bool)" />)
        /// → возврат хватов → подсветка магазина → прицел, хост, баланс, теги (<see cref="WeaponRebuildFinish" />). Всё или
        /// ничего. <paramref name="poses" /> — только <see cref="WeaponGripPoseMode.KeepCurrent" />.
        /// </summary>
        public static GameObject Build(LowPolyWeaponRecipe k, WeaponGripPoseMode poses = WeaponGripPoseMode.KeepCurrent)
        {
            WeaponGripCarryOver.RequireBuilderMode(poses, "LowPolyWeaponBuilder");
            string path = PrefabPath(k);
            string magazine = MagazinePath(k);
            WeaponRebuildFinish.Run("LowPolyWeaponBuilder", path, magazine, RebuildOutputs(k), () =>
            {
                HandsPackWeaponRecipe r = k.Weapon;
                // Хваты снимаются до сборки: сборщик перезаписывает оружие и магазин.
                WeaponGripCarryOver.Snapshot grips = WeaponGripCarryOver.Capture(path, magazine);
                using (LowPolyWeapon model = Model(k))
                    HandsPackWeaponBuilder.Build(r, model, configureHost: true);
                WeaponGripCarryOver.Restore(grips, poses, HandsPackWeaponBuilder.MagazineBaseGeometry(r), HandsPackWeaponBuilder.SdkSampleAvatars);
                // Подсветка выдаваемого отдельно магазина — как у BuildFull пака Hands.
                WeaponInteractionReview.Apply(System.Array.Empty<string>(), new[] { magazine });
                GameLog.Debug.Info($"[LowPolyWeaponBuilder] {path}: собран из {PackPrefabPath(k)}");
            });
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Что пишет <see cref="Build(LowPolyWeaponRecipe, WeaponGripPoseMode)" /> помимо префабов ствола — для отката.</summary>
        public static IEnumerable<string> RebuildOutputs(LowPolyWeaponRecipe k) =>
            new[] { "Assets/Art/Weapons/Interaction/" + k.Weapon.Name };
    }
}
