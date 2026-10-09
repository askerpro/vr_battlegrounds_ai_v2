using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Audio;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.EditorTools.Audio;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;
using WS = VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Единственный writer хоста оружия <see cref="WeaponSystem"/> (этапы D и waves-f WeaponSystem): профиль, механизм
    /// (<see cref="WeaponMechanismRig"/>), оверрайды звука и вибрации ствола, ссылка на дефолты категории
    /// (<see cref="WeaponFeedbackDefaults"/>) и подсказка. Переносит данные из прежних компонентов —
    /// <c>AutomaticWeaponSlideFeedback</c> (ручка, порог, пружина, звуки затвора), <c>WeaponMechanismVisuals</c> (корпус,
    /// клипы, детали, фабрика Action-привязок), <c>UxrShotgunPump</c> (помпа и её звуки), <c>WeaponChamberingReminder</c>
    /// (подсветка) — и удаляет их вместе с исчезнувшими <c>WeaponTriggerAttemptRouter</c>/<c>WeaponAttemptFeedback</c>
    /// (отсутствующие скрипты). Повторный запуск на переведённом префабе берёт данные из самого хоста и ничего не меняет.
    ///
    /// <para>
    /// <b>Дефолты по категориям</b> (решение пользователя 2026-10-09). Ствол хранит ссылку на ассет своей категории и только
    /// оверрайды; подставляет дефолт исполнитель в момент проигрывания. Writer дефолты не копирует, а оверрайд, равный
    /// дефолту (прежняя копия отказа S2 и вибраций пилотов), снимает. Категория — явная таблица <see cref="Weapons"/>:
    /// в <c>WeaponInfo</c> есть только «винтовка/пистолет», дробовика там нет (SRM12 и Herrington — Rifle), а признак
    /// <c>ShotgunPellets</c> есть не у всех дробовиков. Preflight сверяет «пистолет» таблицы с <c>WeaponInfo.Category</c>.
    /// </para>
    /// <para>
    /// Звуки переносятся по смыслу события: у помпы <c>_audioSlide</c> — оттяжка, <c>_audioSlideBack</c> — возврат; у затвора
    /// сборщиков <c>_audioSlideBack</c> — оттяжка, <c>_audioSlideForward</c> — возврат. Прежний AWSF играл эти поля наоборот
    /// (одно имя поля — два смысла); хост больше не зависит от имени поля.
    /// </para>
    /// </summary>
    public static class WeaponSystemAuthoring
    {
        public const string DataFolder = "Assets/Data/WeaponSystem";
        public const string ProfilesFolder = DataFolder + "/Profiles";

        /// <summary>Съёмный магазин + патронник, ручное досылание, затвор остаётся сзади после последнего патрона (F1).</summary>
        public const string DetachableHoldOpenProfile = ProfilesFolder + "/DetachableHoldOpenReadiness.asset";
        /// <summary>Съёмный магазин + патронник, ручное досылание, затвор возвращается в покой (F2, F3).</summary>
        public const string DetachableReturnToRestProfile = ProfilesFolder + "/DetachableReturnToRestReadiness.asset";
        /// <summary>Съёмный магазин + патронник без ручного хода, досылание при вставке магазина (F4).</summary>
        public const string DetachableNoActionProfile = ProfilesFolder + "/DetachableNoActionReadiness.asset";

        /// <summary>Звук отказа до конца паузы между выстрелами (решение S2) — дефолт всех категорий.</summary>
        public const string RefusalClip = "Assets/Audio/SFX/Weapons/UI_Error_Subtle_Deep_stereo.wav";
        /// <summary>Сухой щелчок — тот же клип, что «нет патронов» у спусков SDK всех стволов.</summary>
        public const string DryFireClip = "Assets/ThirdParty/UltimateXR/Samples/FullScene/Audio/TriggerNoAmmo.wav";

        /// <summary>Ствол арсенала: имя корня (= рецепт сборщика), префаб, волна, профиль волны (null — свой у пилота), категория.</summary>
        public sealed class Entry
        {
            public string Name, Prefab, Wave, Profile;
            public WeaponFeedbackCategory Category;
        }

        private static Entry E(string name, string wave, string profile, WeaponFeedbackCategory category) =>
            new Entry { Name = name, Prefab = $"Assets/Prefabs/Weapons/{name}/{name}.prefab", Wave = wave, Profile = profile, Category = category };

        /// <summary>Обзорная копия ствола (стена лобби): та же волна и категория, что у исходного (план, вопрос В10).</summary>
        private static Entry Review(string name, string wave, string profile, WeaponFeedbackCategory category) =>
            new Entry { Name = name + "_SightReview", Prefab = $"Assets/Prefabs/Weapons/SightReview/{name}_SightReview.prefab",
                Wave = wave, Profile = profile, Category = category };

        /// <summary>
        /// Все стволы арсенала (план п. 5): волна, профиль и категория отклика — одна таблица. Новый ствол без строки
        /// writer не настроит. F5 (MagazineOnly, legacy-помпа) — ждёт порта учёта без патронника.
        /// </summary>
        public static readonly Entry[] Weapons =
        {
            E("Herrington", "D", null, WeaponFeedbackCategory.Shotgun),
            E("FabarmSDASS", "D", null, WeaponFeedbackCategory.Shotgun),
            E("BrowningHiPower", "F1", DetachableHoldOpenProfile, WeaponFeedbackCategory.Pistol),
            E("Viper", "F1", DetachableHoldOpenProfile, WeaponFeedbackCategory.Pistol),
            E("TR15", "F1", DetachableHoldOpenProfile, WeaponFeedbackCategory.Rifle),
            E("AK105", "F2", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("AR15", "F2", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("Mk14", "F2", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("MKR9", "F2", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("SRM12", "F2", DetachableReturnToRestProfile, WeaponFeedbackCategory.Shotgun),
            E("PPK", "F3", DetachableReturnToRestProfile, WeaponFeedbackCategory.Pistol),
            E("MP5K", "F3", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("Scar", "F3", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("Uzi", "F3", DetachableReturnToRestProfile, WeaponFeedbackCategory.Pistol),
            E("SniperRifle", "F3", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("Gun", "F4", DetachableNoActionProfile, WeaponFeedbackCategory.Pistol),
            E("Machinegun", "F4", DetachableNoActionProfile, WeaponFeedbackCategory.Rifle),
            Review("Viper", "F1", DetachableHoldOpenProfile, WeaponFeedbackCategory.Pistol),
            Review("TR15", "F1", DetachableHoldOpenProfile, WeaponFeedbackCategory.Rifle),
            Review("MKR9", "F2", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            Review("SRM12", "F2", DetachableReturnToRestProfile, WeaponFeedbackCategory.Shotgun),
            Review("SniperRifle", "F3", DetachableReturnToRestProfile, WeaponFeedbackCategory.Rifle),
            E("Revolver", "F5", null, WeaponFeedbackCategory.Pistol),
            E("R08", "F5", null, WeaponFeedbackCategory.Pistol),
            E("Shotgun", "F5", null, WeaponFeedbackCategory.Shotgun),
        };

        /// <summary>Строка таблицы по имени корня/рецепта; null — ствола в таблице нет.</summary>
        public static Entry EntryFor(string weaponName) => Array.Find(Weapons, entry => entry.Name == weaponName);

        /// <summary>Профиль волны для сборщиков (рецепт без явного профиля); null — ствол не на волне F1–F4.</summary>
        public static string ProfilePathFor(string weaponName)
        {
            Entry entry = EntryFor(weaponName);
            return entry != null && entry.Wave != "F5" ? entry.Profile : null;
        }

        // ── Дефолты категорий и профили волн ───────────────────────────────────────────────

        private sealed class DefaultsSpec
        {
            public WeaponFeedbackCategory Category;
            public string Path, Back, Forward, Why;
        }

        /// <summary>
        /// Начальные звуки механизма категорий — клипы, уже назначенные в проекте типичному стволу категории:
        /// автомат — AK105 (затвор Kinemation, эталон автомата), дробовик — помпа <c>UxrShotgunPump</c> (FABARM и SDK Shotgun),
        /// пистолет — Viper (стартовый пистолет у всех игроков). Извлечения патрона и задержки затвора в проекте не назначено
        /// ни одному стволу: это необязательные слои поверх оттяжки и позы, в дефолтах пусто.
        /// </summary>
        private static readonly DefaultsSpec[] DefaultsSpecs =
        {
            new DefaultsSpec { Category = WeaponFeedbackCategory.Rifle, Path = DataFolder + "/RifleFeedbackDefaults.asset",
                Back = "Assets/Audio/SFX/Weapons/Kinemation/AK105/AK105_BoltBack.wav",
                Forward = "Assets/Audio/SFX/Weapons/Kinemation/AK105/AK105_BoltForward.wav", Why = "AK105" },
            new DefaultsSpec { Category = WeaponFeedbackCategory.Shotgun, Path = DataFolder + "/ShotgunFeedbackDefaults.asset",
                Back = "Assets/ThirdParty/UltimateXR/Samples/FullScene/Audio/ShotgunPump01.mp3",
                Forward = "Assets/ThirdParty/UltimateXR/Samples/FullScene/Audio/ShotgunPump02.mp3", Why = "помпа FABARM/SDK Shotgun" },
            new DefaultsSpec { Category = WeaponFeedbackCategory.Pistol, Path = DataFolder + "/PistolFeedbackDefaults.asset",
                Back = "Assets/Audio/SFX/Weapons/Kinemation/Viper/Viper_BoltBack.wav",
                Forward = "Assets/Audio/SFX/Weapons/Kinemation/Viper/Viper_BoltForward.wav", Why = "Viper" },
        };

        private const string Srm12Sfx = "Assets/Audio/SFX/Weapons/Kinemation/SRM12/";
        private static readonly (string Field, string Clip)[] Srm12Action =
        {
            ("_actionBack", Srm12Sfx + "SRM12_BoltBack_Cut.wav"),
            ("_actionForwardChambered", Srm12Sfx + "SRM12_BoltForward_Cut.wav"),
            ("_actionForwardEmpty", Srm12Sfx + "SRM12_BoltForward_Cut.wav"),
        };

        /// <summary>
        /// Свои клипы механизма ствола поверх перенесённых из прежних компонентов (имя корня → поле <c>_audio</c> → клип).
        /// Нужны, когда звук пака испорчен: у SRM12 <c>BoltBack.wav</c> почти тишина, а <c>BoltForward.wav</c> — весь цикл;
        /// нарезка — <c>Tools/Audio/recipes/srm12-bolt.json</c> (решение пользователя 2026-10-09). Обзорная копия — те же клипы.
        /// </summary>
        private static readonly Dictionary<string, (string Field, string Clip)[]> ClipOverrides =
            new Dictionary<string, (string Field, string Clip)[]>
            {
                ["SRM12"] = Srm12Action,
                ["SRM12_SightReview"] = Srm12Action,
            };

        private static readonly (string Path, WeaponPhysicalCapability Physical, WeaponChamberPolicy Policy, WeaponEmptyPose Pose)[] ProfileSpecs =
        {
            (DetachableHoldOpenProfile, WeaponPhysicalCapability.ActionTravel, WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.HoldOpen),
            (DetachableReturnToRestProfile, WeaponPhysicalCapability.ActionTravel, WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest),
            (DetachableNoActionProfile, WeaponPhysicalCapability.NoAction, WeaponChamberPolicy.AutoOnMagazineInsert, WeaponEmptyPose.ReturnToRest),
        };

        /// <summary>Ассет дефолтов категории с диска (null — ещё не создан).</summary>
        public static WeaponFeedbackDefaults LoadDefaults(WeaponFeedbackCategory category) =>
            AssetDatabase.LoadAssetAtPath<WeaponFeedbackDefaults>(Array.Find(DefaultsSpecs, spec => spec.Category == category).Path);

        /// <summary>
        /// Ассеты дефолтов и профилей: существующие — с диска (не перезаписываются), отсутствующие — новые с начальными
        /// значениями. <paramref name="create"/>=false — временные копии для preflight (их уничтожает <see cref="DestroyTransient"/>).
        /// </summary>
        private static Dictionary<string, Object> EnsureData(bool create, List<string> created, List<Object> transient)
        {
            var result = new Dictionary<string, Object>();
            if (create) { EnsureFolder("Assets/Data", "WeaponSystem"); EnsureFolder(DataFolder, "Profiles"); }
            foreach (var spec in ProfileSpecs)
            {
                var profile = AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(spec.Path);
                if (profile == null)
                {
                    CheckAbsent(spec.Path);
                    profile = ScriptableObject.CreateInstance<WeaponReadinessProfile>();
                    var data = new SerializedObject(profile);
                    data.FindProperty("_ammoCapability").intValue = (int)WeaponAmmoCapability.DetachableMagazineChamber;
                    data.FindProperty("_physicalCapability").intValue = (int)spec.Physical;
                    data.FindProperty("_chamberPolicy").intValue = (int)spec.Policy;
                    data.FindProperty("_emptyPose").intValue = (int)spec.Pose;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    Keep(profile, spec.Path, create, created, transient);
                }
                if (!profile.TryValidate(out string error) || profile.AmmoCapability != WeaponAmmoCapability.DetachableMagazineChamber ||
                    profile.PhysicalCapability != spec.Physical || profile.ChamberPolicy != spec.Policy || profile.EmptyPose != spec.Pose)
                    throw new InvalidOperationException($"Профиль {spec.Path} не совпадает с волной: {error}");
                result[spec.Path] = profile;
            }
            foreach (DefaultsSpec spec in DefaultsSpecs)
            {
                var defaults = AssetDatabase.LoadAssetAtPath<WeaponFeedbackDefaults>(spec.Path);
                if (defaults == null)
                {
                    CheckAbsent(spec.Path);
                    defaults = ScriptableObject.CreateInstance<WeaponFeedbackDefaults>();
                    var data = new SerializedObject(defaults);
                    data.FindProperty("_category").intValue = (int)spec.Category;
                    SerializedProperty audio = data.FindProperty("_audio");
                    SetClip(audio, "_actionBack", spec.Back);
                    SetClip(audio, "_actionForwardChambered", spec.Forward);
                    SetClip(audio, "_actionForwardEmpty", spec.Forward);
                    SetClip(audio, "_dryFire", DryFireClip);
                    SetClip(audio, "_refusal", RefusalClip);
                    // Контракт haptics-api, ревизия 2: отказы — сила ≈0,4, коротко; ход механизма — ≈0,25 (значения пилотов drive).
                    SerializedProperty haptics = data.FindProperty("_haptics");
                    SetHaptic(haptics, "_notReady", UxrHapticClipType.RumbleFreqNormal, 0.4f, 0.08f);
                    SetHaptic(haptics, "_faulted", UxrHapticClipType.RumbleFreqNormal, 0.4f, 0.15f);
                    SetHaptic(haptics, "_rateOfFire", UxrHapticClipType.RumbleFreqLow, 0.4f, 0.06f);
                    SetHaptic(haptics, "_actionRear", UxrHapticClipType.Click, 0.25f, -1f);
                    data.ApplyModifiedPropertiesWithoutUndo();
                    defaults.name = Path.GetFileNameWithoutExtension(spec.Path);
                    Keep(defaults, spec.Path, create, created, transient);
                }
                if (defaults.Category != spec.Category) throw new InvalidOperationException($"{spec.Path}: категория {defaults.Category}, нужна {spec.Category}");
                result[spec.Category.ToString()] = defaults;
            }
            return result;
        }

        private static void Keep(Object asset, string path, bool create, List<string> created, List<Object> transient)
        {
            if (!create) { transient.Add(asset); return; }
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            created.Add(path);
        }

        private static void DestroyTransient(List<Object> transient)
        {
            foreach (Object asset in transient) if (asset != null) Object.DestroyImmediate(asset);
            transient.Clear();
        }

        private static void SetClip(SerializedProperty audio, string name, string path)
        {
            audio.FindPropertyRelative(name + "._clip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(path) ?? throw new InvalidOperationException("Нет звука " + path);
            audio.FindPropertyRelative(name + "._volume").floatValue = 1f;
        }

        private static void SetHaptic(SerializedProperty haptics, string name, UxrHapticClipType type, float amplitude, float seconds)
        {
            SerializedProperty clip = haptics.FindPropertyRelative(name);
            clip.FindPropertyRelative("_fallbackClipType").intValue = (int)type;
            clip.FindPropertyRelative("_fallbackAmplitude").floatValue = amplitude;
            clip.FindPropertyRelative("_fallbackDurationSeconds").floatValue = seconds;
            clip.FindPropertyRelative("_hapticMode").intValue = (int)UxrHapticMode.Mix;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        // ── Writer ──────────────────────────────────────────────────────────────────────────

        private sealed class Source
        {
            public Transform Body, Contact;
            public UxrGrabbableObject Handle;
            public ChamberActionBinding[] Bindings = Array.Empty<ChamberActionBinding>();
            public WeaponMechanismRig.LocalPose[] LocalRest = Array.Empty<WeaponMechanismRig.LocalPose>();
            public float Gate = 0.7f, Speed = 1.5f, EmptyRearTime = -1f;
            public WS.WeaponReleasedAction Released = WS.WeaponReleasedAction.Spring;
            public WeaponMechanismMotion Motion;
            public WeaponMechanismRig.Part[] Parts = Array.Empty<WeaponMechanismRig.Part>();
            public UxrGrabbableObjectAnchor MagazineAnchor;
            public GameObject HintVisual, HintProximity;
        }

        /// <summary>
        /// Записать хост по профилю и прежним компонентам (или по самому хосту) и удалить прежние компоненты.
        /// <paramref name="defaults"/> = null — ассет категории ствола из таблицы <see cref="Weapons"/> (по имени корня).
        /// </summary>
        public static void Configure(GameObject root, WeaponReadinessProfile profile, WeaponFeedbackDefaults defaults = null)
        {
            if (root == null || profile == null || !profile.TryValidate(out _) || profile.AmmoCapability == WeaponAmmoCapability.LegacyAmmo)
                throw new InvalidOperationException("Нужен профиль готовности на учёте (не LegacyAmmo).");
            if (root.GetComponent<UxrFirearmWeapon>() == null) throw new InvalidOperationException("Нет UxrFirearmWeapon.");
            if (defaults == null)
            {
                Entry entry = EntryFor(root.name) ?? throw new InvalidOperationException(
                    $"{root.name}: нет строки в таблице WeaponSystemAuthoring.Weapons (категория отклика).");
                defaults = LoadDefaults(entry.Category) ?? throw new InvalidOperationException(
                    $"Нет ассета дефолтов категории {entry.Category} — сначала миграция волны (создаёт ассеты).");
            }
            var pump = root.GetComponent<UxrShotgunPump>();
            if (pump != null && pump.enabled)
                throw new InvalidOperationException("Включённый UxrShotgunPump — legacy-помпа (волна F5), переводится отдельно.");
            var feedback = root.GetComponent<AutomaticWeaponSlideFeedback>();
            var visuals = root.GetComponent<WeaponMechanismVisuals>();
            var reminder = root.GetComponent<WeaponChamberingReminder>();
            var host = root.GetComponent<WeaponSystem>();
            bool travel = profile.PhysicalCapability == WeaponPhysicalCapability.ActionTravel;
            if (!travel && (feedback != null || pump != null))
                throw new InvalidOperationException("Профиль NoAction, а на стволе есть ручной Action (затвор/помпа).");

            Source source = host != null ? FromHost(host) : new Source();
            if (visuals != null) ReadVisuals(visuals, source);
            if (travel)
            {
                if (pump != null) ReadPumpGeometry(root, pump, feedback, visuals, source);
                else if (visuals != null)
                {
                    if (!visuals.TryBuildPreparedActionMapping(profile, out ChamberActionBinding[] bindings, out float emptyTime, out string error))
                        throw new InvalidOperationException(error);
                    source.Bindings = bindings;
                    source.EmptyRearTime = emptyTime;
                    source.Handle = feedback != null ? feedback.Slide : source.Handle;
                    source.LocalRest = LocalRest(bindings, visuals);
                }
                else if (feedback != null) ReadSlideGeometry(root, feedback, profile, source);
                else if (source.Handle == null || source.Bindings.Length == 0)
                    throw new InvalidOperationException("Нет ни прежних компонентов Action, ни механизма хоста.");
                if (feedback != null) ReadFeedback(feedback, source);
                else if (pump != null) ReadPumpNumbers(pump, source);
            }
            else
            {
                source.Handle = null; source.Bindings = Array.Empty<ChamberActionBinding>();
                source.LocalRest = Array.Empty<WeaponMechanismRig.LocalPose>(); source.Contact = null;
            }
            if (reminder != null)
            {
                var data = new SerializedObject(reminder);
                source.HintVisual = data.FindProperty("_visual").objectReferenceValue as GameObject;
                source.HintProximity = data.FindProperty("_proximitySignal").objectReferenceValue as GameObject;
            }

            host = host != null ? host : root.AddComponent<WeaponSystem>();
            var settings = new SerializedObject(host);
            settings.FindProperty("_triggerIndex").intValue = 0;
            settings.FindProperty("_profile").objectReferenceValue = profile;
            settings.FindProperty("_feedbackDefaults").objectReferenceValue = defaults;
            WriteRig(settings, source);
            WriteAudio(settings, pump, feedback);
            if (ClipOverrides.TryGetValue(root.name, out var overrides))
                foreach (var (field, clip) in overrides) SetClip(settings.FindProperty("_audio"), field, clip);
            DropCopiesOfDefaults(settings, defaults);
            settings.FindProperty("_hintVisual").objectReferenceValue = source.HintVisual;
            settings.FindProperty("_hintProximity").objectReferenceValue = source.HintProximity;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (!host.Rig.TryPrepare(travel, travel && profile.EmptyPose == WeaponEmptyPose.HoldOpen, out string rigError))
                throw new InvalidOperationException("Механизм не проходит проверку: " + rigError);

            // Прежние писатели позы/звука/отклика: на стволе на машине их быть не может (класс 1 плана).
            if (reminder != null) Object.DestroyImmediate(reminder, true);
            if (feedback != null) Object.DestroyImmediate(feedback, true);
            if (visuals != null) Object.DestroyImmediate(visuals, true);
            if (pump != null) Object.DestroyImmediate(pump, true);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root); // Router и AttemptFeedback (скрипты удалены)
            if (!host.TryValidateConfiguration(out string hostError))
                throw new InvalidOperationException("Хост не проходит проверку настройки: " + hostError);
            EditorUtility.SetDirty(host);
        }

        private static Source FromHost(WeaponSystem host)
        {
            WeaponMechanismRig rig = host.Rig;
            return new Source
            {
                Body = rig.Body, Handle = rig.Handle, Contact = rig.ContactPart,
                Bindings = rig.ActionBindings ?? Array.Empty<ChamberActionBinding>(),
                LocalRest = rig.ActionLocalRest ?? Array.Empty<WeaponMechanismRig.LocalPose>(),
                Gate = rig.ExtractionGate, Speed = rig.SpringReturnSpeed, EmptyRearTime = rig.EmptyRearTime, Released = rig.ReleasedAction,
                Motion = rig.Motion, Parts = rig.Parts ?? Array.Empty<WeaponMechanismRig.Part>(), MagazineAnchor = rig.MagazineAnchor,
                HintVisual = host.HintVisual, HintProximity = host.HintProximity
            };
        }

        private static void ReadVisuals(WeaponMechanismVisuals visuals, Source source)
        {
            source.Body = visuals.Body;
            source.Contact = visuals.ContactPart;
            source.Motion = visuals.Motion;
            source.MagazineAnchor = new SerializedObject(visuals).FindProperty("_magazineAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;
            source.Parts = (visuals.Bindings ?? Array.Empty<WeaponMechanismVisuals.Binding>()).Where(binding => binding != null)
                .Select(binding => new WeaponMechanismRig.Part
                {
                    Name = binding.Part, Target = binding.Target, RestPosition = binding.RestPosition,
                    RestRotation = binding.RestRotation, Rotate = binding.Rotate, InMagazine = binding.InMagazine
                }).ToArray();
        }

        private static void ReadFeedback(AutomaticWeaponSlideFeedback feedback, Source source)
        {
            var data = new SerializedObject(feedback);
            source.Handle = data.FindProperty("_slide").objectReferenceValue as UxrGrabbableObject ?? source.Handle;
            source.Gate = data.FindProperty("_slideThreshold").floatValue;
            source.Speed = data.FindProperty("_autoReturnSpeed").floatValue;
            // S4: без пружины отпущенной ручки (помпа FABARM) Action остаётся на месте.
            source.Released = data.FindProperty("_autoReturnOnRelease").boolValue ? WS.WeaponReleasedAction.Spring : WS.WeaponReleasedAction.Stay;
        }

        private static void ReadPumpNumbers(UxrShotgunPump pump, Source source)
        {
            var data = new SerializedObject(pump);
            source.Gate = data.FindProperty("_slideThreshold").floatValue;
            source.Released = WS.WeaponReleasedAction.Stay; // у помпы нет пружины
        }

        /// <summary>
        /// Затвор без клипов (волна F3, только <c>AutomaticWeaponSlideFeedback</c>): один жёсткий Action — сама ручка,
        /// корпус — её родитель, задняя поза — покой + полный ход из Translation Limits. Те же условия, что у скалярной ветки
        /// прежней фабрики привязок: ручка с запретом поворота, ManualReturn/ReturnToRest, других граббаблов, кроме магазина, нет.
        /// </summary>
        private static void ReadSlideGeometry(GameObject root, AutomaticWeaponSlideFeedback feedback, WeaponReadinessProfile profile, Source source)
        {
            UxrGrabbableObject handle = feedback.Slide;
            if (handle == null || handle.transform.parent == null || !handle.transform.IsChildOf(root.transform) || handle.gameObject == root)
                throw new InvalidOperationException("Нет ручки затвора внутри оружия.");
            if (profile.ChamberPolicy != WeaponChamberPolicy.ManualReturn || profile.EmptyPose != WeaponEmptyPose.ReturnToRest)
                throw new InvalidOperationException("Затвор без клипов допускает только ManualReturn/ReturnToRest (нет проверенной задней позы).");
            if (!WeaponMechanismRig.TryGetTravel(handle, out Vector3 direction, out float length))
                throw new InvalidOperationException("Ручка затвора без хода (Restrict Local Offset).");
            foreach (UxrGrabbableObject part in root.GetComponentsInChildren<UxrGrabbableObject>(true))
                if (part.gameObject != root && part != handle && part.GetComponentInParent<UxrFirearmMag>() == null)
                    throw new InvalidOperationException("Кроме затвора и магазина есть ещё граббабл: " + part.name);
            Transform body = handle.transform.parent;
            Vector3 rest = handle.transform.localPosition;
            Quaternion rotation = handle.transform.localRotation;
            source.Handle = handle;
            source.Body = body;
            source.Contact = handle.transform;
            source.Bindings = new[] { new ChamberActionBinding { Target = handle.transform, RestPosition = rest, RearPosition = rest + direction * length,
                RestRotation = rotation, RearRotation = rotation, AnimateRotation = false } };
            source.LocalRest = new[] { new WeaponMechanismRig.LocalPose(rest, rotation) };
            source.EmptyRearTime = -1f;
        }

        /// <summary>Помпа: один жёсткий Action (сама помпа), полный ход из Translation Limits.</summary>
        private static void ReadPumpGeometry(GameObject root, UxrShotgunPump pump, AutomaticWeaponSlideFeedback feedback,
            WeaponMechanismVisuals visuals, Source source)
        {
            var handle = new SerializedObject(pump).FindProperty("_pump").objectReferenceValue as UxrGrabbableObject;
            // Не «?.»: отсутствующий компонент в редакторе — поддельный null Unity.
            var follow = root.GetComponent<PumpGrabFollow>();
            if (handle == null || follow == null || follow.Pump != handle || (feedback != null && feedback.Slide != handle))
                throw new InvalidOperationException("Помпа, PumpGrabFollow и ручка затвора не совпадают.");
            Transform body = visuals != null ? visuals.Body : source.Body;
            if (visuals != null)
            {
                Transform[] targets = visuals.GetRequiredActionTargets();
                if (targets.Length != 1 || targets[0] != handle.transform || visuals.ContactPart == null || !visuals.ContactPart.IsChildOf(handle.transform))
                    throw new InvalidOperationException("Помпа должна быть единственной жёсткой деталью Action.");
            }
            if (body == null || !WeaponMechanismRig.TryGetTravel(handle, out Vector3 direction, out float length))
                throw new InvalidOperationException("Нет корпуса или хода помпы.");
            Matrix4x4 bodyFrame = MatrixToRoot(body, root.transform);
            Matrix4x4 restFrame = bodyFrame.inverse * MatrixToRoot(handle.transform, root.transform);
            Vector3 rest = restFrame.MultiplyPoint3x4(Vector3.zero);
            Quaternion rotation = restFrame.rotation;
            Vector3 fullTravel = bodyFrame.inverse.MultiplyVector(MatrixToRoot(handle.transform.parent, root.transform).MultiplyVector(direction * length));
            source.Handle = handle;
            source.Body = body;
            source.Bindings = new[] { new ChamberActionBinding { Target = handle.transform, RestPosition = rest, RearPosition = rest + fullTravel,
                RestRotation = rotation, RearRotation = rotation, AnimateRotation = false } };
            source.LocalRest = new[] { new WeaponMechanismRig.LocalPose(handle.transform.localPosition, handle.transform.localRotation) };
            source.EmptyRearTime = -1f;
        }

        /// <summary>Локальный покой: ручка — её авторская поза; детали — поза покоя клипа (WMV), иначе авторская.</summary>
        private static WeaponMechanismRig.LocalPose[] LocalRest(ChamberActionBinding[] bindings, WeaponMechanismVisuals visuals)
        {
            var result = new WeaponMechanismRig.LocalPose[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                Transform target = bindings[index].Target;
                WeaponMechanismVisuals.Binding part = visuals.Bindings?.FirstOrDefault(binding => binding != null && binding.Target == target);
                result[index] = part != null
                    ? new WeaponMechanismRig.LocalPose(part.RestPosition, part.RestRotation)
                    : new WeaponMechanismRig.LocalPose(target.localPosition, target.localRotation);
            }
            return result;
        }

        private static void WriteRig(SerializedObject settings, Source source)
        {
            SerializedProperty rig = settings.FindProperty("_rig");
            rig.FindPropertyRelative("_body").objectReferenceValue = source.Body;
            rig.FindPropertyRelative("_handle").objectReferenceValue = source.Handle;
            rig.FindPropertyRelative("_contactPart").objectReferenceValue = source.Contact;
            rig.FindPropertyRelative("_extractionGate").floatValue = source.Gate;
            rig.FindPropertyRelative("_springReturnSpeed").floatValue = source.Speed;
            rig.FindPropertyRelative("_autoReturnSpeed").floatValue = source.Speed;
            rig.FindPropertyRelative("_releasedAction").intValue = (int)source.Released;
            rig.FindPropertyRelative("_emptyRearTime").floatValue = source.EmptyRearTime;
            rig.FindPropertyRelative("_motion").objectReferenceValue = source.Motion;
            rig.FindPropertyRelative("_magazineAnchor").objectReferenceValue = source.MagazineAnchor;
            SerializedProperty bindings = rig.FindPropertyRelative("_actionBindings");
            bindings.arraySize = source.Bindings.Length;
            for (int index = 0; index < source.Bindings.Length; index++)
            {
                SerializedProperty item = bindings.GetArrayElementAtIndex(index);
                ChamberActionBinding binding = source.Bindings[index];
                item.FindPropertyRelative("Target").objectReferenceValue = binding.Target;
                item.FindPropertyRelative("RestPosition").vector3Value = binding.RestPosition;
                item.FindPropertyRelative("RearPosition").vector3Value = binding.RearPosition;
                item.FindPropertyRelative("RestRotation").quaternionValue = binding.RestRotation;
                item.FindPropertyRelative("RearRotation").quaternionValue = binding.RearRotation;
                item.FindPropertyRelative("AnimateRotation").boolValue = binding.AnimateRotation;
            }
            SerializedProperty rest = rig.FindPropertyRelative("_actionLocalRest");
            rest.arraySize = source.LocalRest.Length;
            for (int index = 0; index < source.LocalRest.Length; index++)
            {
                SerializedProperty item = rest.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("Position").vector3Value = source.LocalRest[index].Position;
                item.FindPropertyRelative("Rotation").quaternionValue = source.LocalRest[index].Rotation;
            }
            SerializedProperty parts = rig.FindPropertyRelative("_parts");
            parts.arraySize = source.Parts.Length;
            for (int index = 0; index < source.Parts.Length; index++)
            {
                SerializedProperty item = parts.GetArrayElementAtIndex(index);
                WeaponMechanismRig.Part part = source.Parts[index];
                item.FindPropertyRelative("Name").stringValue = part.Name;
                item.FindPropertyRelative("Target").objectReferenceValue = part.Target;
                item.FindPropertyRelative("RestPosition").vector3Value = part.RestPosition;
                item.FindPropertyRelative("RestRotation").quaternionValue = part.RestRotation;
                item.FindPropertyRelative("Rotate").boolValue = part.Rotate;
                item.FindPropertyRelative("InMagazine").boolValue = part.InMagazine;
            }
        }

        /// <summary>Звуки ствола по смыслу события (см. описание класса). Без прежних компонентов — звуки хоста не меняются.</summary>
        private static void WriteAudio(SerializedObject settings, UxrShotgunPump pump, AutomaticWeaponSlideFeedback feedback)
        {
            SerializedProperty audio = settings.FindProperty("_audio");
            if (pump != null)
            {
                var data = new SerializedObject(pump);
                Copy(data, "_audioSlide", audio, "_actionBack");
                Copy(data, "_audioSlideAlreadyLoaded", audio, "_chamberEjected");
                Copy(data, "_audioSlideBack", audio, "_actionForwardEmpty");
                Copy(data, "_audioSlideBackAlreadyLoaded", audio, "_actionForwardChambered");
            }
            else if (feedback != null)
            {
                var data = new SerializedObject(feedback);
                Copy(data, "_audioSlideBack", audio, "_actionBack");
                Copy(data, "_audioSlideForward", audio, "_actionForwardEmpty");
                Copy(data, "_audioSlideForwardWhenLoaded", audio, "_actionForwardChambered");
                // Отдельный звук извлечения живого патрона — только если он действительно другой (иначе двойной звук).
                bool distinct = data.FindProperty("_audioSlideBackWhenLoaded._clip").objectReferenceValue !=
                                data.FindProperty("_audioSlideBack._clip").objectReferenceValue;
                if (distinct) Copy(data, "_audioSlideBackWhenLoaded", audio, "_chamberEjected");
            }
        }

        /// <summary>
        /// Оверрайд, совпадающий с дефолтом категории, — копия дефолта, а не оверрайд: снимается. Так уходят прежние копии
        /// отказа S2 и вибраций пилотов drive (решение пользователя 2026-10-09), а повторный запуск не возвращает их.
        /// Звуки механизма, перенесённые из прежних компонентов, — данные ствола: их writer не трогает.
        /// </summary>
        private static void DropCopiesOfDefaults(SerializedObject settings, WeaponFeedbackDefaults defaults)
        {
            var common = new SerializedObject(defaults);
            SerializedProperty own = settings.FindProperty("_audio._refusal");
            if (SerializedProperty.DataEquals(own, common.FindProperty("_audio._refusal"))) own.boxedValue = new UxrAudioSample();
            foreach (string name in new[] { "_actionRear", "_notReady", "_faulted", "_rateOfFire" })
            {
                SerializedProperty clip = settings.FindProperty("_haptics." + name);
                if (SerializedProperty.DataEquals(clip, common.FindProperty("_haptics." + name))) clip.boxedValue = new UxrHapticClip();
            }
        }

        private static void Copy(SerializedObject source, string sourceName, SerializedProperty audio, string targetName)
        {
            SerializedProperty clip = source.FindProperty(sourceName + "._clip"), volume = source.FindProperty(sourceName + "._volume");
            if (clip == null || volume == null) throw new InvalidOperationException("Схема звука не совпала: " + sourceName);
            audio.FindPropertyRelative(targetName + "._clip").objectReferenceValue = clip.objectReferenceValue;
            audio.FindPropertyRelative(targetName + "._volume").floatValue = clip.objectReferenceValue != null ? volume.floatValue : 1f;
        }

        private static Matrix4x4 MatrixToRoot(Transform target, Transform root)
        {
            Matrix4x4 result = Matrix4x4.identity;
            for (Transform current = target; current != root; current = current.parent)
            {
                if (current == null) throw new InvalidOperationException("Action вне корня оружия.");
                result = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * result;
            }
            return result;
        }

        // ── Отчёт об отклике: событие → оверрайд / дефолт / нет звука вовсе ─────────────────

        private static readonly WS.WeaponCue[] ActionCues =
            { WS.WeaponCue.ActionBack, WS.WeaponCue.ActionForwardChambered, WS.WeaponCue.ActionForwardEmpty };
        private static readonly WS.WeaponHapticCue[] TriggerHaptics =
            { WS.WeaponHapticCue.NotReady, WS.WeaponHapticCue.Faulted, WS.WeaponHapticCue.RateOfFire };

        /// <summary>
        /// Откуда каждый сигнал, который машина может выдать этому стволу. Обязательные сигналы (ход Action, сухой щелчок,
        /// отказ, вибрации) без источника — ошибка в <paramref name="errors"/>. Извлечение патрона и задержка затвора —
        /// необязательные слои поверх оттяжки и позы HoldRear: молчание допустимо и печатается как «нет (слой)».
        /// Каждый разрешённый клип проверяется на уровень (<see cref="SfxClipLevel"/>): «почти тишина» — ошибка;
        /// оттяжка и возврат одного ствола побайтно одинаковы — предупреждение в <paramref name="warnings"/>.
        /// </summary>
        public static List<string> FeedbackReport(WeaponSystem host, List<string> errors, List<string> warnings = null)
        {
            var lines = new List<string>();
            WeaponFeedbackDefaults defaults = host.FeedbackDefaults;
            if (defaults == null) errors.Add(host.name + ": нет ссылки на дефолты категории");
            WeaponReadinessProfile profile = host.Profile;
            bool travel = profile != null && profile.PhysicalCapability == WeaponPhysicalCapability.ActionTravel;
            bool holdOpen = travel && profile.EmptyPose == WeaponEmptyPose.HoldOpen;
            var required = new List<WS.WeaponCue>();
            if (travel) required.AddRange(ActionCues);
            required.Add(WS.WeaponCue.DryFire); required.Add(WS.WeaponCue.Refusal);
            var optional = new List<WS.WeaponCue>();
            if (travel) optional.Add(WS.WeaponCue.ChamberEjected);
            if (holdOpen) optional.Add(WS.WeaponCue.SlideLockCatch);
            var actionClips = new Dictionary<WS.WeaponCue, AudioClip>();
            foreach (WS.WeaponCue cue in required.Concat(optional))
            {
                UxrAudioSample sample = WeaponAudioSet.Resolve(host.Audio, defaults != null ? defaults.Audio : null, cue, out WeaponFeedbackSource source);
                string what = source == WeaponFeedbackSource.Sdk ? "SDK спуска" : sample != null ? sample.Clip.name : null;
                bool isOptional = optional.Contains(cue);
                lines.Add($"звук {cue}: {Describe(source, what, isOptional)}");
                if (source == WeaponFeedbackSource.None && !isOptional) errors.Add($"{host.name}: нет звука вовсе — {cue}");
                if (source == WeaponFeedbackSource.Sdk || sample == null || sample.Clip == null) continue;
                if (ActionCues.Contains(cue)) actionClips[cue] = sample.Clip;
                if (SfxClipLevel.TryPeakDbfs(sample.Clip, out float peak, out string how) && peak < SfxClipLevel.NearSilenceDbfs)
                    errors.Add($"{host.name}: почти тишина — {cue} {sample.Clip.name}, пик {peak:0.#} dBFS ({how})");
            }
            if (actionClips.TryGetValue(WS.WeaponCue.ActionBack, out AudioClip back))
                foreach (WS.WeaponCue forward in new[] { WS.WeaponCue.ActionForwardChambered, WS.WeaponCue.ActionForwardEmpty })
                    if (actionClips.TryGetValue(forward, out AudioClip clip) && SfxClipLevel.SameBytes(back, clip))
                        warnings?.Add($"{host.name}: ActionBack и {forward} побайтно одинаковы — {back.name} / {clip.name}");
            var haptics = new List<WS.WeaponHapticCue>(TriggerHaptics);
            if (travel) haptics.Insert(0, WS.WeaponHapticCue.ActionRear);
            foreach (WS.WeaponHapticCue cue in haptics)
            {
                UxrHapticClip clip = WeaponHapticSet.Resolve(host.Haptics, defaults != null ? defaults.Haptics : null, cue, out WeaponFeedbackSource source);
                string what = clip == null ? null : clip.Clip != null ? clip.Clip.name : $"{clip.FallbackClipType}@{clip.FallbackAmplitude:0.##}";
                lines.Add($"вибрация {cue}: {Describe(source, what, false)}");
                if (source == WeaponFeedbackSource.None) errors.Add($"{host.name}: нет вибрации вовсе — {cue}");
            }
            return lines;
        }

        private static string Describe(WeaponFeedbackSource source, string what, bool optional)
        {
            switch (source)
            {
                case WeaponFeedbackSource.Override: return "оверрайд " + what;
                case WeaponFeedbackSource.Default: return "дефолт " + what;
                case WeaponFeedbackSource.Sdk: return "запасной звук " + what;
                default: return optional ? "нет (необязательный слой)" : "НЕТ ВОВСЕ";
            }
        }

        // ── Волны: preflight → apply → readback → повторный apply ──────────────────────────

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Weapon System/Preflight All Waves")]
        private static void PreflightMenu() => Log(Preflight("D", "F1", "F2", "F3", "F4", "F5"));

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Weapon System/Migrate Waves D-F4")]
        private static void MigrateMenu() => Log(Migrate("D", "F1", "F2", "F3", "F4"));

        /// <summary>Пилот этапа D (совместимость с прежним вызовом).</summary>
        public static Dictionary<string, object> PreflightPilots() => Preflight("D");
        public static Dictionary<string, object> MigratePilots() => Migrate("D");

        private static IEnumerable<Entry> Select(string[] waves) => Weapons.Where(entry => waves.Contains(entry.Wave));

        private static IEnumerable<Entry> Named(string[] names) =>
            names.Select(name => EntryFor(name) ?? throw new InvalidOperationException("Нет строки в таблице Weapons: " + name));

        /// <summary>Preflight выбранных стволов по имени корня (например, после правки <see cref="ClipOverrides"/>).</summary>
        public static Dictionary<string, object> PreflightWeapons(params string[] names) => Preflight(Named(names).ToArray());

        /// <summary>Перевести/переписать выбранные стволы по имени корня — тот же путь, что у волны.</summary>
        public static Dictionary<string, object> MigrateWeapons(params string[] names) => Migrate(Named(names).ToArray());

        /// <summary>
        /// Без записи: writer на загруженной копии каждого префаба волн, отчёт об отклике и проверка категории по
        /// <c>WeaponInfo</c>. Отсутствующие дефолты и профили — временные копии с начальными значениями.
        /// </summary>
        public static Dictionary<string, object> Preflight(params string[] waves) => Preflight(Select(waves).ToArray());

        private static Dictionary<string, object> Preflight(Entry[] entries)
        {
            EnsureIdle();
            var failures = new List<string>(); var warnings = new List<string>(); var rows = new List<object>(); var transient = new List<Object>();
            try
            {
                Dictionary<string, Object> data = EnsureData(false, new List<string>(), transient);
                foreach (Entry entry in entries)
                {
                    GameObject root = null;
                    try
                    {
                        CheckCategory(entry);
                        if (entry.Wave == "F5")
                            throw new InvalidOperationException("F5 не переводится: нужен порт учёта без патронника (MagazineOnly) и перенос legacy-помпы — развилка в Details.md.");
                        root = PrefabUtility.LoadPrefabContents(entry.Prefab);
                        var errors = new List<string>();
                        Configure(root, ProfileOf(entry, root, data), (WeaponFeedbackDefaults)data[entry.Category.ToString()]);
                        List<string> report = FeedbackReport(root.GetComponent<WeaponSystem>(), errors, warnings);
                        failures.AddRange(errors);
                        rows.Add(new { entry.Name, entry.Wave, category = entry.Category.ToString(), describe = Describe(entry.Prefab, root), feedback = report });
                    }
                    catch (Exception exception) { failures.Add(entry.Prefab + ": " + exception.Message); }
                    finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            catch (Exception exception) { failures.Add("данные: " + exception.Message); }
            finally { DestroyTransient(transient); }
            return new Dictionary<string, object> { ["passed"] = failures.Count == 0, ["failures"] = failures, ["warnings"] = warnings, ["rows"] = rows,
                ["scope"] = "Authoring preflight; runtime, Play и шлем не проверяются" };
        }

        /// <summary>
        /// Перевести волны: дефолты и профили (если нет), правка префабов (<c>LoadPrefabContents</c> → writer →
        /// <c>SaveAsPrefabAsset</c>; варианты остаются вариантами), readback — GUID, fileID оставшихся компонентов,
        /// <c>NetworkIdentity._assetId</c>, масштаб корня и точки хвата не изменились; удалены ровно прежние компоненты,
        /// добавлен только хост. Затем повторный apply: байты префаба не меняются. Сбой — побайтный откат и удаление
        /// созданных ассетов.
        /// </summary>
        public static Dictionary<string, object> Migrate(params string[] waves) => Migrate(Select(waves).ToArray());

        private static Dictionary<string, object> Migrate(Entry[] entries)
        {
            EnsureIdle();
            Dictionary<string, object> preflight = Preflight(entries);
            if (!(bool)preflight["passed"]) return preflight;
            var backups = new Dictionary<string, byte[]>();
            foreach (Entry entry in entries)
                foreach (string path in new[] { entry.Prefab, entry.Prefab + ".meta" })
                    backups[path] = File.ReadAllBytes(DiskPath(path));
            var created = new List<string>(); var failures = new List<string>(); var warnings = new List<string>(); var rows = new List<object>();
            try
            {
                Dictionary<string, Object> data = EnsureData(true, created, new List<Object>());
                foreach (Entry entry in entries)
                {
                    Snapshot before = Capture(entry.Prefab, AssetDatabase.LoadAssetAtPath<GameObject>(entry.Prefab));
                    Apply(entry, data);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Prefab);
                    Snapshot after = Capture(entry.Prefab, asset);
                    failures.AddRange(Compare(entry.Prefab, before, after));
                    failures.AddRange(Readback(entry, asset, data));
                    byte[] once = File.ReadAllBytes(DiskPath(entry.Prefab));
                    Apply(entry, data);
                    if (!once.SequenceEqual(File.ReadAllBytes(DiskPath(entry.Prefab))))
                        failures.Add(entry.Prefab + ": повторный apply изменил префаб");
                    var errors = new List<string>();
                    List<string> report = FeedbackReport(asset.GetComponent<WeaponSystem>(), errors, warnings);
                    failures.AddRange(errors);
                    rows.Add(new { entry.Name, entry.Wave, category = entry.Category.ToString(),
                        removed = before.Components.Keys.Except(after.Components.Keys).ToList(),
                        added = after.Components.Keys.Except(before.Components.Keys).ToList(), before.MissingScripts,
                        after.AssetId, after.Scale, describe = Describe(entry.Prefab, asset), feedback = report });
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            }
            catch (Exception exception)
            {
                foreach (KeyValuePair<string, byte[]> backup in backups) File.WriteAllBytes(DiskPath(backup.Key), backup.Value);
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                foreach (Entry entry in entries) AssetDatabase.ImportAsset(entry.Prefab, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                return new Dictionary<string, object> { ["passed"] = false, ["failures"] = new List<string> { exception.Message }, ["rows"] = rows };
            }
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object> { ["passed"] = true, ["failures"] = failures, ["warnings"] = warnings, ["rows"] = rows, ["created"] = created };
        }

        private static void Apply(Entry entry, Dictionary<string, Object> data)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(entry.Prefab);
            try
            {
                Configure(root, ProfileOf(entry, root, data), (WeaponFeedbackDefaults)data[entry.Category.ToString()]);
                if (PrefabUtility.SaveAsPrefabAsset(root, entry.Prefab) == null) throw new InvalidOperationException(entry.Prefab + ": сохранение не удалось");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            // SaveAsPrefabAsset пишет NetworkIdentity._assetId = 0 (контент префаба для Mirror — объект сцены): вернуть канон
            // одной строкой файла (NetworkAssetIdOnDiskTests). Readback затем сверяет значение на диске.
            VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { entry.Prefab }, false);
        }

        private static WeaponReadinessProfile ProfileOf(Entry entry, GameObject root, Dictionary<string, Object> data)
        {
            if (entry.Profile != null) return (WeaponReadinessProfile)data[entry.Profile];
            WeaponSystem host = root.GetComponent<WeaponSystem>();
            return host != null && host.Profile != null ? host.Profile : throw new InvalidOperationException("Нет хоста с профилем (пилот).");
        }

        /// <summary>«Пистолет» таблицы совпадает с <c>WeaponInfo.Category</c> (только чтение); дробовика в WeaponInfo нет.</summary>
        private static void CheckCategory(Entry entry)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo"))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || AssetDatabase.GetAssetPath(info.WeaponPrefab) != entry.Prefab) continue;
                if ((info.Category == WeaponCategory.Pistol) != (entry.Category == WeaponFeedbackCategory.Pistol))
                    throw new InvalidOperationException($"категория {entry.Category} не сходится с WeaponInfo {info.name}: {info.Category}");
            }
        }

        private static IEnumerable<string> Readback(Entry entry, GameObject asset, Dictionary<string, Object> data)
        {
            var host = asset.GetComponent<WeaponSystem>();
            if (host == null) { yield return entry.Prefab + ": нет хоста"; yield break; }
            if (entry.Profile != null && host.Profile != (Object)data[entry.Profile]) yield return entry.Prefab + ": профиль не волны";
            if (host.FeedbackDefaults != (Object)data[entry.Category.ToString()]) yield return entry.Prefab + ": ссылка на дефолты не своей категории";
            if (asset.GetComponentsInChildren<WeaponSystem>(true).Length != 1) yield return entry.Prefab + ": хост не один";
            if (asset.GetComponent<AutomaticWeaponSlideFeedback>() != null || asset.GetComponent<WeaponMechanismVisuals>() != null ||
                asset.GetComponent<WeaponChamberingReminder>() != null || asset.GetComponent<UxrShotgunPump>() != null)
                yield return entry.Prefab + ": остались прежние компоненты";
            if (!host.TryValidateConfiguration(out string error)) yield return entry.Prefab + ": " + error;
        }

        private sealed class Snapshot
        {
            public string Guid;
            public uint AssetId, CanonicalAssetId;
            public string Scale;
            public int MissingScripts;
            public readonly Dictionary<string, long> Components = new Dictionary<string, long>();
            public readonly Dictionary<string, int> GrabPoints = new Dictionary<string, int>();
        }

        private static readonly HashSet<string> Removable = new HashSet<string>
        { nameof(AutomaticWeaponSlideFeedback), nameof(WeaponMechanismVisuals), nameof(WeaponChamberingReminder), nameof(UxrShotgunPump) };

        private static Snapshot Capture(string path, GameObject asset)
        {
            var snapshot = new Snapshot
            {
                Guid = AssetDatabase.AssetPathToGUID(path),
                Scale = asset.transform.localScale.ToString("F6"),
                CanonicalAssetId = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)))
            };
            // У обычного префаба — строка компонента, у варианта — переопределение в m_Modifications.
            snapshot.AssetId = VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.ReadOnDisk(path) ?? 0;
            foreach (Transform transform in asset.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (component == null) { snapshot.MissingScripts++; continue; }
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string _, out long fileId);
                    snapshot.Components[component.GetType().Name + "@" + fileId] = fileId;
                    if (component is UxrGrabbableObject grabbable)
                        snapshot.GrabPoints[AnimationUtility.CalculateTransformPath(transform, asset.transform) + "@" + fileId] = grabbable.GrabPointCount;
                }
            }
            return snapshot;
        }

        private static IEnumerable<string> Compare(string path, Snapshot before, Snapshot after)
        {
            if (before.Guid != after.Guid) yield return path + ": GUID изменился";
            if (before.AssetId != after.AssetId || after.AssetId != after.CanonicalAssetId) yield return path + ": _assetId изменился или не канонический";
            if (before.Scale != after.Scale) yield return path + ": масштаб корня изменился";
            if (after.MissingScripts != 0) yield return path + ": остались отсутствующие скрипты";
            bool hadHost = before.Components.Keys.Any(key => key.StartsWith(nameof(WeaponSystem) + "@", StringComparison.Ordinal));
            foreach (string key in after.Components.Keys.Except(before.Components.Keys))
                if (hadHost || !key.StartsWith(nameof(WeaponSystem) + "@", StringComparison.Ordinal)) yield return path + ": новый компонент " + key;
            foreach (string key in before.Components.Keys.Except(after.Components.Keys))
                if (!Removable.Contains(key.Substring(0, key.IndexOf('@')))) yield return path + ": удалён не прежний компонент " + key;
            foreach (KeyValuePair<string, int> grab in before.GrabPoints)
                if (!after.GrabPoints.TryGetValue(grab.Key, out int count) || count != grab.Value) yield return path + ": хват изменился " + grab.Key;
        }

        private static object Describe(string path, GameObject root)
        {
            var host = root.GetComponent<WeaponSystem>();
            WeaponMechanismRig rig = host.Rig;
            return new
            {
                path,
                profile = host.Profile != null ? host.Profile.name : null,
                defaults = host.FeedbackDefaults != null ? host.FeedbackDefaults.name : null,
                handle = rig.Handle != null ? rig.Handle.name : null,
                body = rig.Body != null ? rig.Body.name : null,
                bindings = (rig.ActionBindings ?? Array.Empty<ChamberActionBinding>()).Select(binding =>
                    (binding.Target != null ? binding.Target.name : "null") + " rest=" + binding.RestPosition.ToString("F4") +
                    " rear=" + binding.RearPosition.ToString("F4") + " rot=" + binding.AnimateRotation).ToList(),
                rig.ExtractionGate, rig.SpringReturnSpeed, released = rig.ReleasedAction.ToString(), rig.EmptyRearTime,
                motion = rig.Motion != null ? rig.Motion.name : null, parts = (rig.Parts ?? Array.Empty<WeaponMechanismRig.Part>()).Length,
                hint = host.HintVisual != null ? host.HintVisual.name : null
            };
        }

        private static void EnsureIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Нужен простаивающий редактор (своя аренда).");
        }

        private static void CheckAbsent(string path)
        {
            if (File.Exists(DiskPath(path)) || File.Exists(DiskPath(path + ".meta")))
                throw new InvalidOperationException("Файл есть на диске, но не загружается как ассет: " + path);
        }

        private static void Log(Dictionary<string, object> report) =>
            VrBattlegrounds.Core.GameLog.WeaponSystem.Info(Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));

        private static string DiskPath(string path)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string target = Path.GetFullPath(Path.Combine(projectRoot, path));
            if (!target.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Путь вне проекта: " + path);
            return target;
        }
    }
}
