using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UltimateXR.Audio;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;
using WS = VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Единственный writer хоста оружия <see cref="WeaponSystem"/> (этап D WeaponSystem): профиль, механизм
    /// (<see cref="WeaponMechanismRig"/>), звуки (<see cref="WeaponAudioSet"/>) и подсказка. Переносит данные из прежних
    /// компонентов — <c>AutomaticWeaponSlideFeedback</c> (ручка, порог, пружина), <c>WeaponMechanismVisuals</c> (корпус,
    /// клипы, детали, фабрика Action-привязок), <c>UxrShotgunPump</c> (помпа и её звуки), <c>WeaponChamberingReminder</c>
    /// (подсветка) — и удаляет их вместе с исчезнувшими <c>WeaponTriggerAttemptRouter</c>/<c>WeaponAttemptFeedback</c>
    /// (скрипты удалены, на префабе — отсутствующие скрипты). Повторный запуск на уже переведённом префабе берёт данные
    /// из самого хоста и ничего не меняет.
    ///
    /// <para>
    /// Вызывают сборщики (рецепт с профилем готовности), <see cref="ManualLoadingAuthoring"/> и адресная миграция
    /// пилота <see cref="MigratePilots"/>. Звуки переносятся по смыслу события: у помпы <c>_audioSlide</c> — оттяжка,
    /// <c>_audioSlideBack</c> — возврат; у затвора сборщиков <c>_audioSlideBack</c> — оттяжка, <c>_audioSlideForward</c> —
    /// возврат (рецепт <c>SlideBackAudio</c>/<c>SlideForwardAudio</c>). Прежний <c>AutomaticWeaponSlideFeedback</c> играл
    /// эти поля наоборот (одно имя поля — два смысла); хост больше не зависит от имени поля.
    /// </para>
    /// </summary>
    public static class WeaponSystemAuthoring
    {
        /// <summary>Съёмный магазин + патронник, ручное досылание, затвор остаётся сзади после последнего патрона.</summary>
        public const string DetachableHoldOpenProfile = "Assets/Data/Weapons/Profiles/DetachableHoldOpenReadiness.asset";

        /// <summary>Звук отказа до конца паузы между выстрелами (решение S2).</summary>
        public const string RefusalClip = "Assets/Audio/SFX/Weapons/UI_Error_Subtle_Deep_stereo.wav";

        /// <summary>Пилот этапа D: стволы, переведённые на машину первыми.</summary>
        public static readonly string[] PilotPrefabs =
        {
            "Assets/Prefabs/Weapons/Herrington/Herrington.prefab",
            "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab"
        };

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

        // ── Writer ──────────────────────────────────────────────────────────────────────────

        /// <summary>Записать хост по профилю и прежним компонентам (или по самому хосту) и удалить прежние компоненты.</summary>
        public static void Configure(GameObject root, WeaponReadinessProfile profile)
        {
            if (root == null || profile == null || !profile.TryValidate(out _) || profile.AmmoCapability == WeaponAmmoCapability.LegacyAmmo)
                throw new InvalidOperationException("Нужен профиль готовности на учёте (не LegacyAmmo).");
            if (root.GetComponent<UxrFirearmWeapon>() == null) throw new InvalidOperationException("Нет UxrFirearmWeapon.");
            var pump = root.GetComponent<UxrShotgunPump>();
            if (pump != null && pump.enabled)
                throw new InvalidOperationException("Включённый UxrShotgunPump — legacy-помпа (волна F5), переводится отдельно.");
            var feedback = root.GetComponent<AutomaticWeaponSlideFeedback>();
            var visuals = root.GetComponent<WeaponMechanismVisuals>();
            var reminder = root.GetComponent<WeaponChamberingReminder>();
            var host = root.GetComponent<WeaponSystem>();
            bool travel = profile.PhysicalCapability == WeaponPhysicalCapability.ActionTravel;

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
            WriteRig(settings, source);
            WriteAudio(settings, pump, feedback);
            WriteHaptics(settings.FindProperty("_haptics"));
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

        /// <summary>Звуки по смыслу события (см. описание класса). Без прежних компонентов — звуки хоста не меняются.</summary>
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
            SerializedProperty refusal = audio.FindPropertyRelative("_refusal._clip");
            if (refusal.objectReferenceValue == null)
            {
                refusal.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(RefusalClip) ??
                                               throw new InvalidOperationException("Нет звука отказа " + RefusalClip);
                audio.FindPropertyRelative("_refusal._volume").floatValue = 1f;
            }
        }

        /// <summary>
        /// Вибрации по умолчанию (контракт haptics-api, ревизия 2): отказы — сила ≈0,4, коротко; ход механизма — ≈0,25.
        /// До SDK-патча 66 форм «двойной/тройной импульс» нет — стандартные <see cref="UltimateXR.Haptics.UxrHapticClipType"/>.
        /// Записывается только пустой клип: настроенный вручную или формой патча 66 не перетирается.
        /// </summary>
        private static void WriteHaptics(SerializedProperty haptics)
        {
            Default(haptics, "_notReady", UltimateXR.Haptics.UxrHapticClipType.RumbleFreqNormal, 0.4f, 0.08f);
            Default(haptics, "_faulted", UltimateXR.Haptics.UxrHapticClipType.RumbleFreqNormal, 0.4f, 0.15f);
            Default(haptics, "_rateOfFire", UltimateXR.Haptics.UxrHapticClipType.RumbleFreqLow, 0.4f, 0.06f);
            Default(haptics, "_actionRear", UltimateXR.Haptics.UxrHapticClipType.Click, 0.25f, -1f);
        }

        private static void Default(SerializedProperty haptics, string name, UltimateXR.Haptics.UxrHapticClipType type, float amplitude, float seconds)
        {
            SerializedProperty clip = haptics.FindPropertyRelative(name);
            if (clip.FindPropertyRelative("_clip").objectReferenceValue != null ||
                clip.FindPropertyRelative("_fallbackClipType").intValue != (int)UltimateXR.Haptics.UxrHapticClipType.None) return;
            clip.FindPropertyRelative("_fallbackClipType").intValue = (int)type;
            clip.FindPropertyRelative("_fallbackAmplitude").floatValue = amplitude;
            clip.FindPropertyRelative("_fallbackDurationSeconds").floatValue = seconds;
            clip.FindPropertyRelative("_hapticMode").intValue = (int)UltimateXR.Haptics.UxrHapticMode.Mix;
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

        // ── Пилот этапа D: Herrington и FABARM ──────────────────────────────────────────────

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Weapon System/Preflight Pilot")]
        private static void PreflightMenu() => Log(PreflightPilots());

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Weapon System/Migrate Pilot")]
        private static void MigrateMenu() => Log(MigratePilots());

        /// <summary>Без записи: writer на загруженной копии каждого пилотного префаба.</summary>
        public static Dictionary<string, object> PreflightPilots()
        {
            EnsureIdle();
            var failures = new List<string>(); var rows = new List<object>();
            foreach (string path in PilotPrefabs)
            {
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    var host = root.GetComponent<WeaponSystem>();
                    if (host == null) throw new InvalidOperationException("Нет хоста (WeaponReadinessController).");
                    Configure(root, host.Profile);
                    rows.Add(Describe(path, root));
                }
                catch (Exception exception) { failures.Add(path + ": " + exception.Message); }
                finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
            }
            return new Dictionary<string, object> { ["passed"] = failures.Count == 0, ["failures"] = failures, ["rows"] = rows };
        }

        /// <summary>
        /// Перевести пилот: правка ассета префаба (SerializedObject + <c>PrefabUtility.SavePrefabAsset</c>), затем readback —
        /// GUID, fileID оставшихся компонентов, <c>NetworkIdentity._assetId</c>, масштаб корня и точки хвата не изменились;
        /// удалены ровно прежние компоненты. Сбой — побайтный откат префабов.
        /// </summary>
        public static Dictionary<string, object> MigratePilots()
        {
            EnsureIdle();
            Dictionary<string, object> preflight = PreflightPilots();
            if (!(bool)preflight["passed"]) return preflight;
            var backups = PilotPrefabs.ToDictionary(path => path, path => File.ReadAllBytes(DiskPath(path)));
            var failures = new List<string>(); var rows = new List<object>();
            try
            {
                foreach (string path in PilotPrefabs)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Snapshot before = Capture(path, asset);
                    Configure(asset, asset.GetComponent<WeaponSystem>().Profile);
                    PrefabUtility.SavePrefabAsset(asset);
                    asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Snapshot after = Capture(path, asset);
                    failures.AddRange(Compare(path, before, after));
                    rows.Add(new { path, removed = before.Components.Keys.Except(after.Components.Keys).ToList(), before.MissingScripts,
                        after.AssetId, after.Scale, describe = Describe(path, asset) });
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            }
            catch (Exception exception)
            {
                foreach (KeyValuePair<string, byte[]> backup in backups) File.WriteAllBytes(DiskPath(backup.Key), backup.Value);
                foreach (string path in PilotPrefabs) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                return new Dictionary<string, object> { ["passed"] = false, ["failures"] = new List<string> { exception.Message }, ["rows"] = rows };
            }
            return new Dictionary<string, object> { ["passed"] = true, ["failures"] = failures, ["rows"] = rows };
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
            Match match = Regex.Match(File.ReadAllText(DiskPath(path)), @"^  _assetId: (\d+)", RegexOptions.Multiline);
            snapshot.AssetId = match.Success ? uint.Parse(match.Groups[1].Value) : 0;
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
            foreach (string key in after.Components.Keys.Except(before.Components.Keys)) yield return path + ": новый компонент " + key;
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
                handle = rig.Handle != null ? rig.Handle.name : null,
                bindings = (rig.ActionBindings ?? Array.Empty<ChamberActionBinding>()).Select(binding =>
                    (binding.Target != null ? binding.Target.name : "null") + " rest=" + binding.RestPosition.ToString("F4") +
                    " rear=" + binding.RearPosition.ToString("F4") + " rot=" + binding.AnimateRotation).ToList(),
                rig.ExtractionGate, rig.SpringReturnSpeed, released = rig.ReleasedAction.ToString(), rig.EmptyRearTime,
                motion = rig.Motion != null ? rig.Motion.name : null, parts = (rig.Parts ?? Array.Empty<WeaponMechanismRig.Part>()).Length,
                audio = new
                {
                    back = Name(host.Audio.ActionBack), forwardChambered = Name(host.Audio.ActionForwardChambered),
                    forwardEmpty = Name(host.Audio.ActionForwardEmpty), ejected = Name(host.Audio.ChamberEjected), refusal = Name(host.Audio.Refusal)
                },
                haptics = new
                {
                    notReady = host.Haptics.NotReady.FallbackClipType + "@" + host.Haptics.NotReady.FallbackAmplitude,
                    faulted = host.Haptics.Faulted.FallbackClipType + "@" + host.Haptics.Faulted.FallbackAmplitude,
                    rateOfFire = host.Haptics.RateOfFire.FallbackClipType + "@" + host.Haptics.RateOfFire.FallbackAmplitude,
                    actionRear = host.Haptics.ActionRear.FallbackClipType + "@" + host.Haptics.ActionRear.FallbackAmplitude
                },
                hint = host.HintVisual != null ? host.HintVisual.name : null
            };
        }

        private static string Name(UxrAudioSample sample) => sample?.Clip != null ? sample.Clip.name : null;

        private static void EnsureIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Нужен простаивающий редактор (своя аренда).");
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
