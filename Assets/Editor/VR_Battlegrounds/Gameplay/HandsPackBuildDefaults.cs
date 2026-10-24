using System;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Общие значения сборщиков стволов из паков (<see cref="HandsPackWeaponBuilder" />, <see cref="KinemationWeaponBuilder" />)
    /// вместо доноров (решение пользователя 2026-10-23): всё, что не выводится из пака и не входит в баланс. Значения сняты
    /// с префабов, которые сборщик раньше копировал у доноров, и одинаковы у всех 20 префабов (исследование
    /// <c>tasks/weapon-batch-legacy-recipe/research/E-donor-fields.md</c>); различие — только по виду механики
    /// (<see cref="HandsPackWeaponRecipe.ActionKind" />: затвор, помпа, ничего).
    ///
    /// <para>
    /// Здесь только то, что отличается от инициализаторов полей SDK/Unity: новый компонент и новый элемент списка
    /// (<c>_triggers</c>, <c>_shotTypes</c>, <c>_additionalGrabPoints</c>) получают дефолты SDK конструктором
    /// (<see cref="SerializedProperty.boxedValue" />), а не нулями наращенного <c>arraySize</c>, затем этот набор.
    /// Баланс (урон, темп, отдача, разлёт, скорость и дальность снаряда, режим огня) пишет <see cref="WeaponBalanceApplier" />,
    /// механику и звуки механизма — хост <see cref="WeaponSystemAuthoring" />; сборщик отдаёт ему только входной формат.
    /// </para>
    /// </summary>
    public static class HandsPackBuildDefaults
    {
        // Звуковых путей здесь нет (решение пользователя 2026-10-23: звуковые дефолты — данные в ассетах, подстановка в момент
        // проигрывания). Запасная пара гнезда магазина и звуки касания гильз — WeaponFeedbackDefaults категории ствола,
        // падение оружия и магазина — ImpactSoundDefaults, «нет патронов» — DryFire категории (в спуск SDK его копирует
        // NewTrigger). Свои клипы ствола (нарезки пака) сборщик пишет в объект — это оверрайд.

        /// <summary>
        /// Громкость «нет патронов» в спуске SDK (щелчок упёртого ствола <c>BarrelObstruction</c>): прежнее значение всех стволов.
        /// Клип — DryFire категории ствола (<see cref="NewTrigger" />).
        /// </summary>
        public const float NoAmmoVolume = 0.3f;

        /// <summary>
        /// Ассет дефолтов категории ствола по таблице хоста (<c>WeaponSystemAuthoring.Weapons</c>): из него сборщик берёт
        /// «нет патронов» для спуска SDK. Нет строки или ассета — отказ до записи (новому стволу нужна строка таблицы).
        /// </summary>
        public static WeaponFeedbackDefaults CategoryDefaults(string weaponName)
        {
            WeaponSystemAuthoring.Entry entry = WeaponSystemAuthoring.EntryFor(weaponName) ?? throw new InvalidOperationException(
                $"{weaponName}: нет строки в таблице WeaponSystemAuthoring.Weapons — неизвестна категория звуковых дефолтов.");
            return WeaponSystemAuthoring.LoadDefaults(entry.Category) ?? throw new InvalidOperationException(
                $"{weaponName}: нет ассета дефолтов категории {entry.Category} ({WeaponSystemAuthoring.DefaultsPathFor(entry.Category)}).");
        }

        /// <summary>Путь ассета дефолтов категории ствола; null — ствола нет в таблице хоста (входы preflight).</summary>
        public static string CategoryDefaultsPath(string weaponName)
        {
            WeaponSystemAuthoring.Entry entry = WeaponSystemAuthoring.EntryFor(weaponName);
            return entry != null ? WeaponSystemAuthoring.DefaultsPathFor(entry.Category) : null;
        }

        /// <summary>Радиус приёма магазина гнездом (SDK 0,1): окно приёма патрона пишет <c>ManualLoadingAuthoring</c> само.</summary>
        public const float AnchorMaxPlaceDistance = 0.15f;

        // Снаряд: стволы проекта бьют по Default + Ground + Hitbox (SDK — Everything), декали — на Default + Ground.
        public const float ProjectileLength = 0.03f;
        public const int CollisionLayers = 4105;
        public const int DecalLayers = 9;
        public const float DecalFadeout = 3f;

        /// <summary>Затвор: доля хода, после которой оттяжка засчитана (SDK и AWSF — 0,7), и скорость пружинного возврата.</summary>
        public const float SlideGate = 0.855f;
        public const float SlideReturnSpeed = 1.5f;

        /// <summary>Помпа: порог хода — дефолт SDK; громкость звуков помпы — как у принятого FABARM.</summary>
        public const float PumpGate = 0.7f;
        public const float PumpVolume = 0.3f;

        /// <summary>
        /// Rigidbody корня: кинематический до хвата и Continuous Dynamic после отпускания (<c>WeaponDropPhysicsTests</c>).
        /// Масса, затухание, гравитация, интерполяция и ограничения — дефолты Unity. Поля пишутся сериализованно: свойство
        /// <see cref="Rigidbody.collisionDetectionMode" /> у кинематического тела Unity сводит к Speculative.
        /// </summary>
        public static void ApplyRigidbody(Rigidbody rb)
        {
            var so = new SerializedObject(rb);
            so.FindProperty("m_IsKinematic").boolValue = true;
            so.FindProperty("m_CollisionDetection").intValue = (int)CollisionDetectionMode.ContinuousDynamic;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Хват корня: лёгкое сопротивление переносу и повороту, свой стартовый якорь, выравнивание по контроллеру у каждой
        /// точки. <paramref name="additionalPoints" /> — число дополнительных точек (вторая рука), новых — с дефолтами SDK.
        /// </summary>
        public static void ApplyRootGrab(SerializedObject grabbable, int additionalPoints)
        {
            grabbable.FindProperty("_translationResistance").floatValue = 0.03f;
            grabbable.FindProperty("_rotationResistance").floatValue = 0.05f;
            grabbable.FindProperty("_autoCreateStartAnchor").boolValue = true;
            ApplyRootPoint(grabbable.FindProperty("_grabPoint"));
            SerializedProperty additional = grabbable.FindProperty("_additionalGrabPoints");
            additional.arraySize = additionalPoints;
            for (int i = 0; i < additional.arraySize; i++)
            {
                SerializedProperty point = additional.GetArrayElementAtIndex(i);
                point.boxedValue = new UxrGrabPointInfo();
                ApplyRootPoint(point);
            }
        }

        private static void ApplyRootPoint(SerializedProperty point)
        {
            point.FindPropertyRelative("_alignToController").boolValue = true;
            // Кнопки при _useDefaultGrabButtons не читаются; 0 — как у всех принятых префабов (без шума в YAML).
            point.FindPropertyRelative("_inputButtons").intValue = 0;
        }

        /// <summary>
        /// Граббабл затвора/помпы (рукоятка M16, затвор Gun_real, помпа Shotgun_real): превью только левой руки, без
        /// перепривязки к руке и без привязки к якорю при отпускании. Ход, ограничения поворота и наведение ствола ставит
        /// сборщик по виду механики.
        /// </summary>
        public static void ApplyActionGrab(SerializedObject grabbable)
        {
            grabbable.FindProperty("_previewGrabPosesMode").intValue = 1;
            grabbable.FindProperty("_useParenting").boolValue = false;
            grabbable.FindProperty("_dropSnapMode").intValue = 0;
            grabbable.FindProperty("_grabPoint._inputButtons").intValue = 0;
        }

        /// <summary>Гнездо магазина: радиус приёма и без обмена магазина на лету.</summary>
        public static void ApplyAnchor(SerializedObject anchor)
        {
            anchor.FindProperty("_maxPlaceDistance").floatValue = AnchorMaxPlaceDistance;
            anchor.FindProperty("_allowSwap").boolValue = false;
        }

        /// <summary>
        /// Звук гнезда: вставка — только от руки (программная раскладка молчит), снятие — и от кнопки выброса без руки.
        /// Звук снятия — свой у ствола (<paramref name="takeOut" />, нарезка пака), пусто — поле пустое: в момент снятия
        /// <c>AnchorSound</c> берёт запасной звук категории ствола, затем дефолт роли гнезда.
        /// </summary>
        public static void ApplyAnchorSound(SerializedObject sound, AudioSource source, string takeOut)
        {
            sound.FindProperty("_source").objectReferenceValue = source;
            sound.FindProperty("_onlyByHand").boolValue = true;
            sound.FindProperty("_takeOutOnlyByHand").boolValue = false;
            sound.FindProperty("_takeOutClip").objectReferenceValue = string.IsNullOrEmpty(takeOut) ? null :
                AssetDatabase.LoadAssetAtPath<AudioClip>(takeOut) ?? throw new InvalidOperationException("Нет звука снятия магазина: " + takeOut);
        }

        /// <summary>
        /// Выстрелы с дефолтами SDK (<see cref="UxrShotDescriptor" />) и общим набором снаряда. Эффекты, точки дула,
        /// поля дроби и рецепта ставит сборщик; урон, скорость и дальность — баланс (до миграции снаряда в баланс —
        /// дефолт SDK 30 м/с / 300 м).
        /// </summary>
        public static void NewShots(SerializedProperty shotTypes, int count)
        {
            shotTypes.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty shot = shotTypes.GetArrayElementAtIndex(i);
                shot.boxedValue = new UxrShotDescriptor();
                shot.FindPropertyRelative("_projectileLength").floatValue = ProjectileLength;
                shot.FindPropertyRelative("_collisionLayerMask").intValue = CollisionLayers;
                shot.FindPropertyRelative("_createDecalLayerMask").intValue = DecalLayers;
                shot.FindPropertyRelative("_decalFadeoutDuration").floatValue = DecalFadeout;
            }
        }

        /// <summary>
        /// Единственный спуск с дефолтами SDK (<c>UxrFirearmTrigger</c> — internal-класс SDK, экземпляр через отражение) и
        /// общим набором: звук «нет патронов», досыл патрона перед выстрелом. Без затвора (<paramref name="hasAction" /> false,
        /// револьвер) досылать нечем — выстрел прямо из барабана.
        ///
        /// <para>
        /// «Нет патронов»: щелчок отказа играет исполнитель хоста из DryFire категории в момент отказа, а поле спуска SDK
        /// звучит только у <c>BarrelObstruction</c> (упёртый ствол) и как запасной звук SDK. Подставить его в момент проигрывания
        /// нельзя (поле SDK), поэтому клип копируется из DryFire ассета категории <paramref name="category" /> при сборке —
        /// правка ассета доходит до этого щелчка с пересборкой. DryFire категории пуст — отказ до записи.
        /// </para>
        /// </summary>
        public static SerializedProperty NewTrigger(SerializedProperty triggers, bool hasAction, WeaponFeedbackDefaults category)
        {
            AudioClip noAmmo = category != null && category.Audio.DryFire != null ? category.Audio.DryFire.Clip : null;
            if (noAmmo == null)
                throw new InvalidOperationException("Нет звука «нет патронов»: DryFire пуст в ассете категории " +
                                                    (category != null ? category.name : "(нет ассета)") + ".");
            triggers.arraySize = 1;
            SerializedProperty trigger = triggers.GetArrayElementAtIndex(0);
            trigger.boxedValue = Activator.CreateInstance(TriggerType, true);
            trigger.FindPropertyRelative("_useHasReloadedForSemiAndFullAuto").boolValue = hasAction;
            trigger.FindPropertyRelative("_shotAudio._volume").floatValue = 1f;
            // Вибрация выстрела — ShotBig, как у всех стволов до отказа от доноров (дефолт SDK — Shot, слабее; ревью
            // weapon-system 2026-10-24). Своего клипа нет — выбор по типу.
            trigger.FindPropertyRelative("_shotHapticClip._fallbackClipType").intValue = (int)UltimateXR.Haptics.UxrHapticClipType.ShotBig;
            trigger.FindPropertyRelative("_shotAudioNoAmmo._clip").objectReferenceValue = noAmmo;
            trigger.FindPropertyRelative("_shotAudioNoAmmo._volume").floatValue = NoAmmoVolume;
            return trigger;
        }

        private static readonly Type TriggerType = typeof(UxrFirearmWeapon).Assembly.GetType("UltimateXR.Mechanics.Weapons.UxrFirearmTrigger") ??
            throw new InvalidOperationException("В сборке UltimateXR нет UxrFirearmTrigger");

        /// <summary>Затвор (вход хоста — <see cref="AutomaticWeaponSlideFeedback" />): порог оттяжки и пружинный возврат.</summary>
        public static void ApplySlide(SerializedObject feedback)
        {
            feedback.FindProperty("_slideThreshold").floatValue = SlideGate;
            feedback.FindProperty("_autoReturnOnRelease").boolValue = true;
            feedback.FindProperty("_autoReturnSpeed").floatValue = SlideReturnSpeed;
        }

        /// <summary>Помпа (вход хоста — <see cref="UxrShotgunPump" />): порог хода; пружины у помпы нет.</summary>
        public static void ApplyPump(SerializedObject pump) => pump.FindProperty("_slideThreshold").floatValue = PumpGate;

        /// <summary>Звук в поле <see cref="UltimateXR.Audio.UxrAudioSample" />: пусто — поле не трогается (хост берёт дефолт категории).</summary>
        public static void SetAudio(SerializedObject target, string field, string clipPath, float volume = 1f)
        {
            if (string.IsNullOrEmpty(clipPath)) return;
            target.FindProperty(field + "._clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath) ??
                throw new InvalidOperationException("Нет звука " + field + ": " + clipPath);
            target.FindProperty(field + "._volume").floatValue = volume;
        }
    }
}
