using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Поддерживаемый маршрут каталога, ассортиментов и отдельной демонстрационной станции.</summary>
    public static class ArsenalPresetAssetBuilder
    {
        public const string CommonPath = "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab";
        public const string DemoPath = "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab";
        public const string GameplayPath = "Assets/Data/Weapons/CurrentGameplayArsenal.asset";
        public const string FullPath = "Assets/Data/Weapons/FullDemoArsenal.asset";
        private const string RegistryPath = "Assets/Data/Maps/MapRegistry.asset";

        /// <summary>Узкая миграция двух presentation zones существующего FullDemo; прочие данные не записывает.</summary>
        public static string ApplyDemoPresentationPolicy()
        {
            CheckEditor();
            var preset = Required<ArsenalPreset>(FullPath);
            if (EditorUtility.IsDirty(preset)) throw new InvalidOperationException("Сначала сохранить FullDemoArsenal.");
            ApplyCanonicalDemoZones(preset);
            return "FullDemo: MP5K и MKR9 размещены на Shelf; другие пресеты, классы и WeaponInfo не изменены.";
        }

        public static string ConfigureAssets()
        {
            CheckEditor();
            var registry = Required<MapRegistry>(RegistryPath);
            var common = PrefabUtility.LoadPrefabContents(CommonPath);
            try
            {
                var slots = common.GetComponent<ArsenalWallController>().Slots;
                if (slots.Count != 10) throw new InvalidOperationException("Исходный общий арсенал должен содержать 10 слотов.");
                foreach (var slot in slots) slot.ConfigurePresentationZone(ZoneOf(slot));
                var gameplayEntries = slots.Select(s => new ArsenalPreset.Entry { Weapon = s.WeaponData, Zone = s.PresentationZone }).ToArray();
                var gameplay = EnsurePreset(GameplayPath, "CurrentGameplay", gameplayEntries);
                EnsureWeapon("AK105", "AK-105", "Scar", "AK105/AK105", "AK105/AK105_mag", WeaponCategory.Rifle, 30, 660, true);
                EnsureWeapon("R08", "R08", "Revolver", "R08/R08", "R08/R08_mag", WeaponCategory.Pistol, 8, 420, false);
                EnsureWeapon("SDKGun", "SDK Gun", "Gun", "Gun/Gun", "Gun/MagGun", WeaponCategory.Pistol, 40, 600, false);
                var weapons = AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)
                    .Select(AssetDatabase.LoadAssetAtPath<WeaponInfo>).ToArray();
                if (weapons.Length != 20 || weapons.Any(w => w.WeaponPrefab == null || w.MagazinePrefab == null) || weapons.Select(w => w.WeaponId).Distinct().Count() != 20 || weapons.Select(w => w.WeaponPrefab).Distinct().Count() != 20)
                    throw new InvalidOperationException("Каталог должен содержать ровно 20 уникальных готовых стволов.");
                RegisterNetworkPrefabs(weapons);
                var fullEntries = gameplayEntries.Concat(weapons.Where(w => !gameplayEntries.Any(e => e.Weapon == w))
                    .Select(w => new ArsenalPreset.Entry { Weapon = w, Zone = CanonicalDemoZone(w) })).ToArray();
                var full = EnsurePreset(FullPath, "FullDemo", fullEntries);
                ApplyCanonicalDemoZones(full);
                var catalogue = new SerializedObject(Required<WeaponRegistry>("Assets/Data/Weapons/Resources/WeaponRegistry.asset"));
                SetReferences(catalogue.FindProperty("_weapons"), weapons);
                if (catalogue.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssetIfDirty(catalogue.targetObject);
                foreach (var map in registry.maps)
                {
                    if (map == null) continue;
                    // Существующая настройка Inspector сохраняется при повторном запуске.
                    if (map.arsenalPreset == null) { map.arsenalPreset = map == registry.lobby ? full : gameplay; EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map); }
                }
                var binding = common.GetComponent<ArsenalStationPresetBinding>() ?? common.AddComponent<ArsenalStationPresetBinding>();
                binding.ConfigureRegistry(registry);
                ArsenalMagazineOfferInstaller.Configure(common);
                PrefabUtility.SaveAsPrefabAsset(common, CommonPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(common); }
            return "Каталог 20; начальные ассортимент карты и лобби созданы. Общая геометрия не менялась.";
        }

        public static string CreateDemoPrefab()
        {
            CheckEditor();
            var preset = Required<ArsenalPreset>(FullPath);
            if (EditorUtility.IsDirty(preset) || (preset.PresentationStyle != null && EditorUtility.IsDirty(preset.PresentationStyle)))
                throw new InvalidOperationException("Сначала явно сохраните canonical preset/style inputs.");
            string invalid = ArsenalEditorStatus.ValidatePreset(preset);
            if (invalid != null) throw new InvalidOperationException(invalid);
            // Существующая Demo изменяется на месте: повтор сохраняет localFileID anchors/components,
            // на которые ссылаются prefab overrides четырёх материализованных станций.
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath) != null;
            const string idPrefs = "UltimateXR.Editor.AutomaticUniqueIdGeneration";
            bool hadIdPrefs = EditorPrefs.HasKey(idPrefs), previousIdPrefs = EditorPrefs.GetBool(idPrefs, true);
            GameObject root = null;
            var previews = new List<GameObject>();
            try
            {
                // Preview SDK видит nested slot GUID, а сохранённый asset — outer Demo GUID.
                // Генератор задаёт final provenance сам; автоматическое OnValidate восстановится точно в finally.
                EditorPrefs.SetBool(idPrefs, false);
                root = PrefabUtility.LoadPrefabContents(existing ? DemoPath : CommonPath);
                root.name = "LobbyDemoArsenalStation";
                // Derived source context готов до первого ConfigureWeapon/Card/installer.
                var binding = root.GetComponent<ArsenalStationPresetBinding>();
                binding.ConfigureRegistry(Required<MapRegistry>(RegistryPath));
                binding.ConfigureEditorPresentationPreset(preset);
                var source = root.GetComponent<ArsenalWallController>().Slots.ToArray();
                // Захват до перемещения/удаления: смена физической зоны не создаёт новый slot или SDK anchor.
                if (source.Any(s => s == null || s.WeaponData == null) || source.Select(s => s.WeaponData).Distinct().Count() != source.Length)
                    throw new InvalidOperationException("Исходная Demo содержит пустые или повторные логические слоты.");
                var templates = source.GroupBy(ZoneOf).ToDictionary(g => g.Key, g => g.OrderBy(s => s.transform.localPosition.x).First());
                float shelfPanelWidth = PanelWidth(templates[ArsenalPresentationZone.Pegboard]);
                var previousIds = new Dictionary<MonoBehaviour, string>();
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) continue;
                    var id = new SerializedObject(component).FindProperty("_uxrUniqueId");
                    if (id != null) previousIds.Add(component, id.stringValue);
                }
                var all = new List<ArsenalSlotController>();
                foreach (ArsenalPresentationZone zone in Enum.GetValues(typeof(ArsenalPresentationZone)))
                {
                    var entries = preset.Entries.Where(e => e.Zone == zone).ToArray();
                    if (entries.Length == 0 || !templates.TryGetValue(zone, out var template)) throw new InvalidOperationException("Нет исходной зоны " + zone);
                    var parent = template.transform.parent;
                    for (int i = 0; i < entries.Length; i++)
                    {
                        // Логическая идентичность слота — оружие; presentation zone и порядок меняют только его позу.
                        var slot = source.FirstOrDefault(s => s.WeaponData == entries[i].Weapon);
                        if (slot == null)
                        {
                            slot = UnityEngine.Object.Instantiate(template, parent);
                            slot.name = (zone == ArsenalPresentationZone.Pegboard ? "PegboardSlot_" : "ShelfSlot_") + entries[i].Weapon.WeaponId;
                        }
                        else if (ZoneOf(slot) != zone)
                        {
                            CopyZoneFrame(slot, template);
                            slot.transform.SetParent(parent, false);
                            slot.transform.localPosition = template.transform.localPosition;
                            slot.transform.localRotation = template.transform.localRotation;
                            slot.transform.localScale = template.transform.localScale;
                            slot.name = (zone == ArsenalPresentationZone.Pegboard ? "PegboardSlot_" : "ShelfSlot_") + entries[i].Weapon.WeaponId;
                        }
                        slot.ConfigurePresentationZone(zone);
                        slot.ConfigureWeapon(entries[i].Weapon);
                        var position = slot.transform.localPosition;
                        float pitch = zone == ArsenalPresentationZone.Pegboard ? .425f : shelfPanelWidth + .03f;
                        position.x = .085f + (i - (entries.Length - 1) * .5f) * pitch;
                        slot.transform.localPosition = position;
                        if (zone == ArsenalPresentationZone.Shelf) ConfigureShelfPanel(slot, shelfPanelWidth);
                        var presentation = ArsenalPresentationApplicator.Resolve(slot);
                        ArsenalSupportModuleBuilder.MaterializePresentation(slot, presentation);
                        all.Add(slot);
                    }
                }
                if (source.Any(s => !all.Contains(s)))
                    throw new InvalidOperationException("Этот обновляющий генератор не удаляет retained slots. Для изменения вместимости нужен отдельный план.");
                var wall = new SerializedObject(root.GetComponent<ArsenalWallController>());
                SetReferences(wall.FindProperty("_allSlots"), all.Cast<UnityEngine.Object>().ToArray());
                wall.ApplyModifiedPropertiesWithoutUndo();
                binding.Prepare(preset);
                StretchHousing(root, preset);
                ArsenalMagazineOfferInstaller.Configure(root);
                // Новые якоря получают свои ID в сохранённом префабе.
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) continue;
                    var serialized = new SerializedObject(component);
                    var id = serialized.FindProperty("_uxrUniqueId");
                    if (id != null)
                    {
                        string desiredId = previousIds.TryGetValue(component, out string previousId) ? previousId : Guid.NewGuid().ToString();
                        ((UltimateXR.Core.Components.UxrComponent)component).SetEditorUniqueId(new Guid(desiredId), true,
                            existing ? AssetDatabase.AssetPathToGUID(DemoPath) : AssetDatabase.AssetPathToGUID(CommonPath));
                    }
                }
                foreach (var slot in all)
                {
                    if (slot.ItemAnchor == null) throw new InvalidOperationException("Нет якоря " + slot.name);
                    var item = ArsenalPresentationApplicator.CreateDisplaySample(slot, false);
                    ArsenalPresentationApplicator.ApplyWeapon(slot, item.transform);
                    previews.Add(item);
                    // Карточка — постоянная часть слота, удаляются только временные образцы оружия.
                    if (slot.GetComponentInChildren<ArsenalPriceTag>(true) == null)
                        throw new InvalidOperationException("Нет исходной карточки " + slot.name);
                    var card = ArsenalPriceTag.Create(slot); card.Show(slot.WeaponData, true);
                }
                var equipment = root.GetComponent<ArsenalEquipmentPoses>();
                ArsenalMagazineOfferInstaller.AddRestingMagazineSamples(root, previews);
                equipment.Apply(1f);
                var raised = Measure(root.transform);
                root.GetComponent<ArsenalStationAnchor>().ConfigureRaisedBounds(raised);
                equipment.Apply(0f);
                equipment.ConfigureFoldedBounds(Measure(root.transform));
                equipment.Apply(1f);
                ConfigureOpening(root, raised);
                foreach (var item in previews) UnityEngine.Object.DestroyImmediate(item);
                previews.Clear();
                ValidateEditorIdentities(root, true, AssetDatabase.AssetPathToGUID(existing ? DemoPath : CommonPath));
                PrefabUtility.SaveAsPrefabAsset(root, DemoPath);
                if (!existing)
                {
                    var saved = Required<GameObject>(DemoPath);
                    foreach (var component in saved.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true))
                        component.SetEditorUniqueId(component.UniqueId, true, AssetDatabase.AssetPathToGUID(DemoPath));
                    ValidateEditorIdentities(saved, true, AssetDatabase.AssetPathToGUID(DemoPath));
                    PrefabUtility.SavePrefabAsset(saved);
                }
            }
            finally
            {
                foreach (var item in previews) if (item != null) UnityEngine.Object.DestroyImmediate(item);
                try { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
                finally { if (hadIdPrefs) EditorPrefs.SetBool(idPrefs, previousIdPrefs); else EditorPrefs.DeleteKey(idPrefs); }
            }
            return "Создан отдельный LobbyDemoArsenalStation; 20 слотов, исходный Common не менялся.";
        }

        public static string MigrateLobby()
        {
            CheckEditor();
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/Lobby.unity");
            bool loaded = scene.IsValid() && scene.isLoaded;
            if (loaded && scene.isDirty) throw new InvalidOperationException("Сначала сохранить текущие изменения Lobby.");
            if (!loaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", OpenSceneMode.Additive);
            try
            {
                var walls = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalWallController>(true)).ToArray();
                var oldWalls = walls.Where(w => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(w.gameObject) != DemoPath).ToArray();
                if (walls.Length == 0) throw new InvalidOperationException("В Lobby нет станций для обновления.");
                if (oldWalls.Length == 0)
                {
                    return RefreshDemoLobby(scene, walls);
                }
                if (oldWalls.Length != walls.Length) throw new InvalidOperationException("В Lobby смешаны старые и демонстрационные станции.");
                string backup = System.IO.Path.GetFullPath("tmp/arsenal-presets/Lobby-before-demo.unity");
                if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new InvalidOperationException("Не сохранена резервная копия Lobby.");
                var replacements = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
                var demo = Required<GameObject>(DemoPath);
                foreach (var old in oldWalls)
                {
                    var fresh = (GameObject)PrefabUtility.InstantiatePrefab(demo, scene);
                    fresh.name = old.name;
                    fresh.transform.SetParent(old.transform.parent, false);
                    fresh.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                    fresh.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                    fresh.transform.localScale = old.transform.localScale;
                    foreach (var oldTransform in old.GetComponentsInChildren<Transform>(true))
                    {
                        var path = AnimationUtility.CalculateTransformPath(oldTransform, old.transform);
                        var target = string.IsNullOrEmpty(path) ? fresh.transform : fresh.transform.Find(path);
                        if (target == null) continue;
                        replacements[oldTransform] = target;
                        replacements[oldTransform.gameObject] = target.gameObject;
                        foreach (var component in oldTransform.GetComponents<Component>())
                        {
                            if (component == null || component is Transform) continue;
                            var peers = oldTransform.GetComponents(component.GetType());
                            var targets = target.GetComponents(component.GetType());
                            int index = Array.IndexOf(peers, component);
                            if (index >= 0 && index < targets.Length) replacements[component] = targets[index];
                        }
                    }
                    var oldAnchor = old.GetComponent<ArsenalStationAnchor>();
                    var anchor = fresh.GetComponent<ArsenalStationAnchor>();
                    if (oldAnchor.StandingPoint != null) anchor.StandingPoint.SetPositionAndRotation(oldAnchor.StandingPoint.position, oldAnchor.StandingPoint.rotation);
                    anchor.Configure(fresh.GetComponent<ArsenalWallController>(), oldAnchor.Zone, anchor.StandingPoint, anchor.ArenaFacing);
                    CopyBoundary(old.GetComponent<ArsenalDeploymentAnimator>(), fresh.GetComponent<ArsenalDeploymentAnimator>());
                    PrefabUtility.RecordPrefabInstancePropertyModifications(fresh.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.StandingPoint);
                }
                foreach (var component in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)))
                {
                    // Иерархия уже изменена через Transform API. m_Children/m_Father не являются внешними ссылками.
                    if (component == null || component is Transform || oldWalls.Any(w => component.transform.IsChildOf(w.transform))) continue;
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator(); bool changed = false;
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var value = property.objectReferenceValue;
                        if (value == null) continue;
                        if (replacements.TryGetValue(value, out var replacement)) { property.objectReferenceValue = replacement; changed = true; }
                        else
                        {
                            var target = value is Component c ? c.transform : value is GameObject go ? go.transform : null;
                            if (target != null && oldWalls.Any(w => target.IsChildOf(w.transform)))
                                throw new InvalidOperationException("Не сопоставлена внешняя ссылка: " + component.name + "." + property.propertyPath);
                        }
                    }
                    if (changed) { serialized.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
                }
                foreach (var old in oldWalls) UnityEngine.Object.DestroyImmediate(old.gameObject);
                ArsenalMapMigration.UpdateLayout(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                return "Lobby: заменены 4 станции с сохранением внешних ссылок и обновлением проёмов.";
            }
            finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }
        }

        private static string RefreshDemoLobby(Scene scene, ArsenalWallController[] walls)
        {
            var desired = Required<GameObject>(DemoPath);
            var full = Required<ArsenalPreset>(FullPath);
            var sourceSlots = desired.GetComponent<ArsenalWallController>().Slots;
            if (full.Entries.Count == 0 || sourceSlots.Count != full.Entries.Count ||
                full.Entries.Any(e => sourceSlots.Count(s => s.WeaponData == e.Weapon && s.PresentationZone == e.Zone) != 1))
                throw new InvalidOperationException("Demo не соответствует сохранённому FullDemoArsenal: сначала пересоберите демонстрационную станцию.");
            var sourceBinding = desired.GetComponent<ArsenalStationPresetBinding>();
            if (full.PresentationStyle != null && sourceBinding.PresentationPresetCache != full)
                throw new InvalidOperationException("Demo имеет stale presentation context; пересоберите её.");
            foreach (var slot in sourceSlots)
                ArsenalSupportModuleBuilder.ValidateProjection(slot, ArsenalPresentationApplicator.Resolve(slot));
            string sourceState = DemoOwnedState(desired);
            string revision = AssetDatabase.GetAssetDependencyHash(DemoPath).ToString();
            string Context(ArsenalWallController wall)
            {
                var anchor = wall.GetComponent<ArsenalStationAnchor>();
                var deployment = wall.GetComponent<ArsenalDeploymentAnimator>();
                var boundary = new SerializedObject(deployment).FindProperty("_boundary").objectReferenceValue;
                return revision + "|" + wall.transform.position.ToString("R") + "|" + wall.transform.rotation.ToString("R") + "|" + wall.transform.lossyScale.ToString("R") +
                    "|" + GlobalObjectId.GetGlobalObjectIdSlow(anchor.Zone) + "|" + GlobalObjectId.GetGlobalObjectIdSlow(boundary);
            }
            if (walls.All(w => w.GetComponent<ArsenalStationPresetBinding>().EditorDemoSourceHash == Context(w) &&
                DemoOwnedState(w.gameObject) == sourceState && EditorIdentitiesMatch(w.gameObject, false, AssetDatabase.AssetPathToGUID(DemoPath))))
                return "Lobby: 4 станции соответствуют FullDemo/Demo; изменений 0.";
            string backup = System.IO.Path.GetFullPath("tmp/arsenal-presets/Lobby-before-demo-refresh.unity");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new InvalidOperationException("Не сохранена резервная копия Lobby.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var wall in walls)
            {
                // Только поля состава, карточек и склада канонической Demo. Корень, владелец, zone/boundary и позы не сбрасываются.
                RevertOwned(wall, "_allSlots");
                RevertOwned(wall.GetComponent<ArsenalStationPresetBinding>(), "_presentationPresetCache");
                foreach (var slot in wall.Slots)
                {
                    if (slot == null) throw new InvalidOperationException("Пустая ссылка слота после обновления Demo.");
                    RevertOwned(slot, "_weaponInfo", "_presentationZone", "_itemAnchor", "_priceTag", "_magAnchor", "_customCardPresentation", "_cardLocalPosition", "_cardLocalEulerAngles", "_cardSize", "_cardFontSize");
                    foreach (var offer in slot.GetComponentsInChildren<ArsenalMagazineOffer>(true))
                        RevertOwned(offer, "_slot", "_anchor", "_surface", "_surfaceNormalLocal");
                    foreach (var text in slot.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                        RevertOwned(text, "m_text", "m_fontSize", "m_fontSizeMax", "m_fontSizeMin", "m_overflowMode", "m_TextWrappingMode", "m_enableAutoSizing");
                    foreach (var frame in OwnedPresentationFrames(slot))
                        RevertOwned(frame, "m_LocalPosition", "m_LocalRotation", "m_LocalScale");
                    if (ArsenalPresentationApplicator.Resolve(slot).IsStyled)
                    {
                        RevertOwned(slot.ItemAnchor, "_activateOnCompatibleNear");
                        RevertOwned((slot as FirearmSlotController)?.MagAnchor, "_activateOnCompatibleNear");
                    }
                }
                foreach (var component in wall.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) continue;
                    var serialized = new SerializedObject(component);
                    var id = serialized.FindProperty("_uxrUniqueId");
                    if (id == null) continue;
                    string desiredId = id.stringValue;
                    if (!id.prefabOverride || string.IsNullOrEmpty(desiredId) || !ids.Add(desiredId))
                    {
                        desiredId = Guid.NewGuid().ToString(); ids.Add(desiredId);
                    }
                    var identity = (UltimateXR.Core.Components.UxrComponent)component;
                    identity.SetEditorUniqueId(new Guid(desiredId), false, AssetDatabase.AssetPathToGUID(DemoPath));
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
                if (DemoOwnedState(wall.gameObject) != sourceState)
                    throw new InvalidOperationException("Станция отличается от канонической Demo после обновления: " + wall.name);
                ValidateEditorIdentities(wall.gameObject, false, AssetDatabase.AssetPathToGUID(DemoPath));
            }
            ArsenalMapMigration.UpdateLayout(scene);
            foreach (var wall in walls)
            {
                var binding = wall.GetComponent<ArsenalStationPresetBinding>();
                binding.EditorDemoSourceHash = Context(wall);
                EditorUtility.SetDirty(binding); PrefabUtility.RecordPrefabInstancePropertyModifications(binding);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Не сохранено обновление Lobby.");
            return "Lobby: применён сохранённый FullDemo/Demo к 4 станциям; состав, карточки, склад и проёмы обновлены. Повтор без изменений безопасен.";
        }

        private static void ValidateEditorIdentities(GameObject root, bool isPrefab, string guid)
        {
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Нет GUID источника editor identity.");
            var ids = new HashSet<Guid>();
            foreach (var component in root.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true))
            {
                var serialized = new SerializedObject(component);
                if (component.UniqueId == Guid.Empty || !ids.Add(component.UniqueId) ||
                    serialized.FindProperty("_uxrUniqueId").stringValue != component.UniqueId.ToString() ||
                    serialized.FindProperty("__isInPrefab").boolValue != isPrefab ||
                    serialized.FindProperty("__prefabGuid").stringValue != guid)
                    throw new InvalidOperationException("Неверная editor identity/provenance: " + component.name);
            }
        }

        private static bool EditorIdentitiesMatch(GameObject root, bool isPrefab, string guid)
        {
            try { ValidateEditorIdentities(root, isPrefab, guid); return true; }
            catch (InvalidOperationException) { return false; }
        }

        private static void RevertOwned(Component component, params string[] names)
        {
            var serialized = new SerializedObject(component);
            foreach (string name in names)
            {
                var property = serialized.FindProperty(name);
                if (property != null && property.prefabOverride)
                    PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
            }
        }

        private static string DemoOwnedState(GameObject root)
        {
            string Path(Component component) => component == null ? "null" : AnimationUtility.CalculateTransformPath(component.transform, root.transform);
            var rows = new List<string>();
            var binding = root.GetComponent<ArsenalStationPresetBinding>();
            rows.Add("context|" + AssetDatabase.GetAssetPath(binding.PresentationPresetCache));
            var style = binding.PresentationPresetCache != null ? binding.PresentationPresetCache.PresentationStyle : null;
            rows.Add("style|" + AssetDatabase.GetAssetPath(style) + "|" + (style != null ? EditorJsonUtility.ToJson(style) : "legacy"));
            foreach (var slot in root.GetComponent<ArsenalWallController>().Slots)
            {
                if (slot == null) { rows.Add("null slot"); continue; }
                rows.Add(Path(slot) + "|" + AssetDatabase.GetAssetPath(slot.WeaponData) + "|" + slot.PresentationZone + "|" + slot.transform.localPosition.ToString("F5") + "|" +
                    slot.CardLocalPosition.ToString("F5") + "|" + slot.CardLocalRotation.ToString("F5") + "|" + slot.CardSize.ToString("F5") + "|" + slot.CardFontSize.ToString("F5"));
                foreach (var offer in slot.GetComponentsInChildren<ArsenalMagazineOffer>(true))
                    rows.Add(Path(offer) + "|" + Path(offer.Slot) + "|" + Path(offer.Anchor) + "|" + Path(offer.Surface) + "|" + root.transform.InverseTransformDirection(offer.SurfaceNormal).ToString("F5"));
                foreach (var text in slot.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                    rows.Add(Path(text) + "|" + text.text + "|" + text.fontSize.ToString("F5") + "|" + text.enableAutoSizing + "|" + text.overflowMode + "|" + text.textWrappingMode);
                foreach (var frame in OwnedPresentationFrames(slot))
                    rows.Add(Path(frame) + "|" + frame.localPosition.ToString("R") + "|" + frame.localRotation.ToString("R") + "|" + frame.localScale.ToString("R"));
                foreach (var anchor in new[] { slot.ItemAnchor, (slot as FirearmSlotController)?.MagAnchor })
                    if (anchor != null) rows.Add(Path(anchor) + "|near|" + (anchor.ActivateOnCompatibleNear != null ? Path(anchor.ActivateOnCompatibleNear.transform) : "null"));
                foreach (var containerName in new[] { ArsenalSupportModuleBuilder.ContainerName, ArsenalSupportModuleBuilder.HintsName })
                {
                    var supports = slot.transform.Find(containerName);
                    if (supports == null) continue;
                    foreach (var filter in supports.GetComponentsInChildren<MeshFilter>(true))
                    {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(filter.sharedMesh, out string meshGuid, out long meshId);
                        rows.Add(Path(filter) + "|mesh|" + meshGuid + ":" + meshId);
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (renderer != null) rows.Add(Path(renderer) + "|materials|" + string.Join(",", renderer.sharedMaterials.Select(AssetDatabase.GetAssetPath)));
                    }
                }
            }
            return string.Join("\n", rows);
        }

        private static IEnumerable<Transform> OwnedPresentationFrames(ArsenalSlotController slot)
        {
            var seen = new HashSet<Transform>();
            void Add(Transform root)
            {
                if (root != null) foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    if ((child.gameObject.hideFlags & HideFlags.DontSave) == 0) seen.Add(child);
            }
            Add(slot.ItemAnchor != null ? slot.ItemAnchor.transform : null);
            Add((slot as FirearmSlotController)?.MagAnchor?.transform);
            Add(slot.GetComponentInChildren<ArsenalPriceTag>(true)?.transform);
            Add(slot.transform.Find(ArsenalSupportModuleBuilder.ContainerName));
            Add(slot.transform.Find(ArsenalSupportModuleBuilder.HintsName));
            return seen.OrderBy(t => AnimationUtility.CalculateTransformPath(t, slot.transform), StringComparer.Ordinal);
        }

        private static void CopyBoundary(ArsenalDeploymentAnimator source, ArsenalDeploymentAnimator target)
        {
            var from = new SerializedObject(source); var to = new SerializedObject(target);
            to.FindProperty("_boundary").objectReferenceValue = from.FindProperty("_boundary").objectReferenceValue;
            to.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static ArsenalPresentationZone ZoneOf(ArsenalSlotController slot)
        {
            for (var parent = slot.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.name == "ShelfSlotsContainer") return ArsenalPresentationZone.Shelf;
                if (parent.name == "RiflesSlotsContainer") return ArsenalPresentationZone.Pegboard;
            }
            throw new InvalidOperationException("Не определена физическая зона " + slot.name);
        }

        private static void CopyZoneFrame(ArsenalSlotController slot, ArsenalSlotController template)
        {
            foreach (var frame in slot.GetComponentsInChildren<Transform>(true))
            {
                if (frame == slot.transform) continue;
                var supports = slot.transform.Find(ArsenalSupportModuleBuilder.ContainerName);
                if (supports != null && (frame == supports || frame.IsChildOf(supports))) continue;
                var hints = slot.transform.Find(ArsenalSupportModuleBuilder.HintsName);
                if (hints != null && (frame == hints || frame.IsChildOf(hints))) continue;
                string path = AnimationUtility.CalculateTransformPath(frame, slot.transform);
                var target = template.transform.Find(path);
                if (target == null) throw new InvalidOperationException("Несовместимый шаблон зоны: " + path);
                frame.localPosition = target.localPosition;
                frame.localRotation = target.localRotation;
                frame.localScale = target.localScale;
            }
        }

        private static float HousingWidth(ArsenalPreset preset)
        {
            int pegboard = preset.Entries.Count(e => e.Zone == ArsenalPresentationZone.Pegboard);
            int shelf = preset.Entries.Count(e => e.Zone == ArsenalPresentationZone.Shelf);
            return Mathf.Max(5.25f, (pegboard - 1) * .425f + .65f, (shelf - 1) * .65f + .70f);
        }

        private static float PanelWidth(ArsenalSlotController slot)
        {
            var panel = slot.transform.Find("PegboardSection");
            var mesh = panel != null ? panel.GetComponent<MeshFilter>() : null;
            if (mesh == null || mesh.sharedMesh == null || mesh.sharedMesh.bounds.size.x <= 0f)
                throw new InvalidOperationException("Нет исходной Peg panel: " + slot.name);
            return mesh.sharedMesh.bounds.size.x * Mathf.Abs(panel.localScale.x);
        }

        private static void ConfigureShelfPanel(ArsenalSlotController slot, float width)
        {
            var panel = slot.transform.Find("PegboardSection");
            var mesh = panel != null ? panel.GetComponent<MeshFilter>() : null;
            var collider = panel != null ? panel.GetComponent<BoxCollider>() : null;
            if (mesh == null || mesh.sharedMesh == null || collider == null || mesh.sharedMesh.bounds.size.x <= 0f)
                throw new InvalidOperationException("Нет исходной Shelf panel/collider: " + slot.name);
            // Ширина меняется только у поверхности. Хваты, предметы и карточки сохраняют масштаб.
            var scale = panel.localScale;
            scale.x = width / mesh.sharedMesh.bounds.size.x;
            panel.localScale = scale;
            var position = panel.localPosition;
            position.x = -mesh.sharedMesh.bounds.center.x * scale.x;
            panel.localPosition = position;
            collider.center = mesh.sharedMesh.bounds.center;
            collider.size = mesh.sharedMesh.bounds.size;
        }

        private static void StretchHousing(GameObject root, ArsenalPreset preset)
        {
            // Слоты не тесним: ширина покрывает фактические центры и крайние образцы оружия/магазинов.
            float width = HousingWidth(preset);
            var wide = new HashSet<string> { "SlimTopTrim", "RearPlate", "BottomCrossmember", "EquipmentTray", "TrayFrontLip", "SehlfMesh", "Niche_Top", "Niche_Bottom", "Niche_Back" };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (wide.Contains(t.name))
                {
                    var mesh = t.GetComponent<MeshFilter>();
                    if (mesh != null && mesh.sharedMesh != null) { var scale = t.localScale; scale.x = width / mesh.sharedMesh.bounds.size.x; t.localScale = scale; }
                }
                if (new[] { "LeftUpright", "RightUpright", "FloorFoot", "OwnerLight", "Niche_Left", "Niche_Right" }.Contains(t.name))
                { var p = t.localPosition; p.x = .085f + (p.x < .085f ? -1f : 1f) * (width * .5f + .06f); t.localPosition = p; }
                if (t.name == "DogTagPanel" || t.name == "WalletDisplayHousing" || t.name == "WalletSupportBracket")
                {
                    var commonTransform = Required<GameObject>(CommonPath).transform.Find(AnimationUtility.CalculateTransformPath(t, root.transform));
                    if (commonTransform == null) throw new InvalidOperationException("Нет исходной детали корпуса " + t.name);
                    var p = t.localPosition; p.x = commonTransform.localPosition.x + (width - 2.55f) * .5f; t.localPosition = p;
                }
            }
        }

        private static Bounds Measure(Transform root)
        {
            bool first = true; Bounds bounds = default;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = renderer.bounds.center + Vector3.Scale(renderer.bounds.extents, new Vector3((i&1)==0?-1:1, (i&2)==0?-1:1, (i&4)==0?-1:1));
                    var point = root.InverseTransformPoint(corner);
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("Не измерена геометрия станции.");
            return bounds;
        }

        private static void ConfigureOpening(GameObject root, Bounds bounds)
        {
            var opening = root.GetComponentInChildren<SpawnZoneBoundaryOpening>(true);
            if (opening == null) throw new InvalidOperationException("Нет BoundaryOpening.");
            var box = opening.GetComponent<BoxCollider>();
            var local = new Bounds(opening.transform.InverseTransformPoint(root.transform.TransformPoint(bounds.center)), Vector3.zero);
            for (int i = 0; i < 8; i++) local.Encapsulate(opening.transform.InverseTransformPoint(root.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3((i&1)==0?-1:1, (i&2)==0?-1:1, (i&4)==0?-1:1)))));
            box.center = local.center; box.size = local.size; box.isTrigger = true;
        }

        private static ArsenalPreset EnsurePreset(string path, string id, ArsenalPreset.Entry[] entries)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ArsenalPreset>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<ArsenalPreset>(); AssetDatabase.CreateAsset(asset, path);
            var serialized = new SerializedObject(asset); serialized.FindProperty("_presetId").stringValue = id;
            var list = serialized.FindProperty("_entries"); list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++) { list.GetArrayElementAtIndex(i).FindPropertyRelative("Weapon").objectReferenceValue = entries[i].Weapon; list.GetArrayElementAtIndex(i).FindPropertyRelative("Zone").enumValueIndex = (int)entries[i].Zone; }
            serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(asset); return asset;
        }

        /// <summary>Только исходный FullDemo: игровые классы и пользовательские пресеты не изменяются.</summary>
        private static ArsenalPresentationZone CanonicalDemoZone(WeaponInfo weapon) =>
            weapon.WeaponId == "MP5K" || weapon.WeaponId == "MKR9" || weapon.Category == WeaponCategory.Pistol
                ? ArsenalPresentationZone.Shelf : ArsenalPresentationZone.Pegboard;

        private static void ApplyCanonicalDemoZones(ArsenalPreset preset)
        {
            var so = new SerializedObject(preset);
            var entries = so.FindProperty("_entries");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var weapon = entry.FindPropertyRelative("Weapon").objectReferenceValue as WeaponInfo;
                if (weapon != null && (weapon.WeaponId == "MP5K" || weapon.WeaponId == "MKR9"))
                    entry.FindPropertyRelative("Zone").enumValueIndex = (int)CanonicalDemoZone(weapon);
            }
            if (so.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssetIfDirty(preset);
        }

        private static string UniqueIdKey(GameObject root, MonoBehaviour component) =>
            AnimationUtility.CalculateTransformPath(component.transform, root.transform) + "|" + component.GetType().FullName + "|" +
            Array.IndexOf(component.GetComponents(component.GetType()), component);

        private static Dictionary<string, string> ReadUniqueIds(GameObject root)
        {
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root == null) return ids;
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                var property = new SerializedObject(component).FindProperty("_uxrUniqueId");
                if (property != null && !string.IsNullOrEmpty(property.stringValue)) ids.Add(UniqueIdKey(root, component), property.stringValue);
            }
            return ids;
        }

        private static void EnsureWeapon(string id, string label, string donor, string prefab, string magazine, WeaponCategory category, int capacity, int rpm, bool fullAuto)
        {
            string path = "Assets/Data/Weapons/" + id + "_Weapon.asset";
            if (AssetDatabase.LoadAssetAtPath<WeaponInfo>(path) != null) return;
            if (!AssetDatabase.CopyAsset("Assets/Data/Weapons/" + donor + "_Weapon.asset", path)) throw new InvalidOperationException("Не создано " + path);
            var metadata = Required<WeaponInfo>(path); metadata.name = id + "_Weapon";
            var asset = new SerializedObject(metadata);
            asset.FindProperty("_weaponId").stringValue = id; asset.FindProperty("_displayName").stringValue = label;
            asset.FindProperty("_weaponPrefab").objectReferenceValue = Required<GameObject>("Assets/Prefabs/Weapons/" + prefab + ".prefab");
            asset.FindProperty("_magazinePrefab").objectReferenceValue = Required<GameObject>("Assets/Prefabs/Weapons/" + magazine + ".prefab");
            asset.FindProperty("_category").enumValueIndex = (int)category;
            // Новые демонстрационные записи получают урон из текущих префабов; готовые ассеты выше не перезаписываются.
            asset.FindProperty("_damage").floatValue = id switch
            {
                "AK105" => 33f,
                "R08" => 38f,
                "SDKGun" => 5f,
                _ => throw new InvalidOperationException("Не задан исходный урон нового оружия: " + id)
            };
            asset.FindProperty("_description").stringValue = id == "AK105"
                ? "Автоматический карабин AK-105. Универсальное основное оружие для коротких очередей и огня на средней дистанции."
                : id == "R08"
                    ? "Самозарядный пистолет R08. Компактное запасное оружие с мощным одиночным выстрелом."
                    : id == "SDKGun"
                        ? "Футуристический самозарядный пистолет. Вместительный магазин и слабый одиночный снаряд для частых повторных выстрелов."
                        : throw new InvalidOperationException("Не задано описание нового оружия: " + id);
            asset.FindProperty("_magazineSize").intValue = capacity;
            asset.FindProperty("_fireRate").intValue = rpm;
            asset.FindProperty("_fullAuto").boolValue = fullAuto;
            asset.FindProperty("_maxMagazineCount").intValue = 3;
            asset.FindProperty("_pellets").intValue = 1;
            asset.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(metadata);
        }

        public static string RegisterNetworkPrefabs(WeaponInfo[] weapons)
        {
            const string path = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
            CheckEditor();
            if (weapons == null || weapons.Length == 0 || weapons.Any(w => w == null || w.WeaponPrefab == null || w.MagazinePrefab == null))
                throw new InvalidOperationException("Сетевой каталог требует WeaponInfo, оружие и магазин каждой записи.");
            var prefabs = weapons.SelectMany(w => new[] { w.WeaponPrefab, w.MagazinePrefab }).Distinct().ToArray();
            var ids = new HashSet<uint>();
            var guids = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            void Validate(GameObject prefab)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                if (string.IsNullOrEmpty(guid) || guids.TryGetValue(guid, out var previous) && previous != prefab)
                    throw new InvalidOperationException("Пустой или повторный GUID сетевого префаба: " + prefab.name);
                guids[guid] = prefab;
                var identity = prefab.GetComponent<NetworkIdentity>();
                if (identity == null || identity.assetId == 0 || !ids.Add(identity.assetId))
                    throw new InvalidOperationException("Отсутствующий NetworkIdentity, пустой или повторный Mirror assetId: " + prefab.name);
            }
            foreach (var prefab in prefabs)
                Validate(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var manager = root.GetComponentsInChildren<NetworkManager>(true).Single();
                ids.Clear();
                guids.Clear();
                if (manager.spawnPrefabs.Any(p => p == null)) throw new InvalidOperationException("Пустая ссылка в каноническом spawnPrefabs.");
                foreach (var prefab in manager.spawnPrefabs.Concat(prefabs).Where(p => p != null).Distinct())
                    Validate(prefab);
                int added = 0;
                foreach (var prefab in prefabs)
                    if (!manager.spawnPrefabs.Contains(prefab)) { manager.spawnPrefabs.Add(prefab); added++; }
                if (added > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"Сетевая регистрация: проверено {prefabs.Length} префабов выбранного каталога и {ids.Count} уникальных сетевых префабов менеджера; добавлено {added}; {path}.";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetReferences(SerializedProperty property, UnityEngine.Object[] values)
        { property.arraySize = values.Length; for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
        private static T Required<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Нет " + path);
        private static void CheckEditor() { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Редактор в Play Mode."); }
    }
}
