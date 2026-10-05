using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>До выдачи предметов связывает статический ассортимент со сценой станции.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ArsenalWallController))]
    public sealed class ArsenalStationPresetBinding : MonoBehaviour
    {
        [SerializeField] private MapRegistry _mapRegistry;
        // Производная ссылка на source preset для prefab/preview; runtime выбирает только MapData.
        [SerializeField, HideInInspector] private ArsenalPreset _presentationPresetCache;
#if UNITY_EDITOR
        // Кэш завершённого редакторского применения Demo; не участвует в runtime или сети.
        [SerializeField, HideInInspector] private string _editorDemoSourceHash;
        public string EditorDemoSourceHash { get => _editorDemoSourceHash; set => _editorDemoSourceHash = value; }
#endif
        private ArsenalPreset _prepared;
        private readonly Dictionary<ArsenalSlotController, ArsenalPresentationSnapshot> _presentation = new Dictionary<ArsenalSlotController, ArsenalPresentationSnapshot>();
        private string _lastError;
        public bool IsPrepared => _prepared != null;
        public void ConfigureRegistry(MapRegistry registry) => _mapRegistry = registry;
        public ArsenalPreset PresentationPresetCache => _presentationPresetCache;
#if UNITY_EDITOR
        public void ConfigureEditorPresentationPreset(ArsenalPreset preset)
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
                throw new InvalidOperationException("Presentation cache задаётся только редакторским генератором.");
            _presentationPresetCache = preset;
        }
#endif

        public ArsenalPresentationSnapshot ResolvePresentation(ArsenalSlotController slot)
        {
            if (Application.isPlaying)
            {
                if (_prepared == null) throw new InvalidOperationException("Presentation context ещё не подготовлен.");
                if (_presentation.TryGetValue(slot, out var current)) return current;
                return ArsenalLegacyPresentationAdapter.Resolve(slot);
            }
            var map = _mapRegistry != null ? _mapRegistry.GetBySceneName(gameObject.scene.name) : null;
            bool isolated = string.IsNullOrEmpty(gameObject.scene.path);
#if UNITY_EDITOR
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            isolated |= UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene) ||
                (stage != null && stage.scene == gameObject.scene);
#endif
            var preset = isolated ? _presentationPresetCache : map != null ? map.arsenalPreset : null;
            if (!isolated && _presentationPresetCache != null && preset != _presentationPresetCache)
                throw new InvalidOperationException("Stale presentation cache: MapData и generated station отличаются.");
            if (preset == null || preset.PresentationStyle == null) return ArsenalLegacyPresentationAdapter.Resolve(slot);
            if (_presentationPresetCache != preset) throw new InvalidOperationException("Styled station требует актуальный derived preset cache.");
            int matches = 0;
            foreach (var entry in preset.Entries)
                if (entry.Weapon == slot.WeaponData && entry.Zone == slot.PresentationZone) matches++;
            if (matches != 1) throw new InvalidOperationException("Styled slot не соответствует source preset entry.");
            return ArsenalPresentationResolver.Resolve(slot.WeaponData, slot.PresentationZone, preset.PresentationStyle);
        }

        private void Awake()
        {
            if (Application.isPlaying) TryPrepareFromScene();
        }

        public bool TryPrepareFromScene()
        {
            if (IsPrepared) return true;
            var map = _mapRegistry != null ? _mapRegistry.GetBySceneName(gameObject.scene.name) : null;
            try
            {
                if (map == null) throw new InvalidOperationException("Нет MapData для сцены " + gameObject.scene.name);
                Prepare(map.arsenalPreset);
                return true;
            }
            catch (InvalidOperationException exception)
            {
                if (_lastError != exception.Message)
                {
                    _lastError = exception.Message;
                    GameLog.Arsenal.Error("[ArsenalPreset] " + exception.Message, this);
                }
                return false;
            }
        }

        /// <summary>Проверяет весь ассортимент до изменения слотов. Повтор той же настройки безопасен.</summary>
        public void Prepare(ArsenalPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.PresetId))
                throw new InvalidOperationException("У карты не задан арсенал с постоянным ID.");
            if (_prepared != null)
            {
                if (_prepared != preset) throw new InvalidOperationException("Нельзя менять арсенал загруженной станции.");
                return;
            }
            var slots = GetComponent<ArsenalWallController>().Slots;
            var capacity = new Dictionary<ArsenalPresentationZone, int>();
            var needed = new Dictionary<ArsenalPresentationZone, int>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var prefabs = new HashSet<GameObject>();
            var uniqueSlots = new HashSet<ArsenalSlotController>();
            foreach (var slot in slots)
            {
                if (slot == null || !uniqueSlots.Add(slot)) throw new InvalidOperationException("Пустая или повторная ссылка в массиве слотов.");
                capacity.TryGetValue(slot.PresentationZone, out int count);
                capacity[slot.PresentationZone] = count + 1;
            }
            foreach (var entry in preset.Entries)
            {
                var weapon = entry.Weapon;
                if (!Enum.IsDefined(typeof(ArsenalPresentationZone), entry.Zone)) throw new InvalidOperationException("Неизвестная зона в " + preset.name);
                if (weapon == null || weapon.WeaponPrefab == null || string.IsNullOrWhiteSpace(weapon.WeaponId) || !ids.Add(weapon.WeaponId))
                    throw new InvalidOperationException("Пустое оружие, префаб или повтор ID в " + preset.name);
                if (!prefabs.Add(weapon.WeaponPrefab) || weapon.MagazinePrefab == null)
                    throw new InvalidOperationException("Повтор оружейного префаба или отсутствующий магазин в " + preset.name);
                needed.TryGetValue(entry.Zone, out int count);
                needed[entry.Zone] = count + 1;
                if (preset.PresentationStyle != null) ArsenalPresentationResolver.Resolve(weapon, entry.Zone, preset.PresentationStyle);
            }
            foreach (var pair in needed)
                if (!capacity.TryGetValue(pair.Key, out int count) || count < pair.Value)
                    throw new InvalidOperationException($"{preset.name}: {pair.Key} требует {pair.Value} слотов, есть {count}.");
            var used = new HashSet<ArsenalSlotController>();
            var preparedPresentation = new Dictionary<ArsenalSlotController, ArsenalPresentationSnapshot>();
            var assignments = new List<KeyValuePair<ArsenalSlotController, ArsenalPreset.Entry>>();
            foreach (var entry in preset.Entries)
                foreach (var slot in slots)
                    if (slot.PresentationZone == entry.Zone && used.Add(slot))
                    {
                        if (preset.PresentationStyle != null)
                        {
                            ArsenalPresentationResolver.ValidateFrame(slot.transform, slot.ItemAnchor.transform, slot.ItemAnchor.AlignTransform);
                            var firearm = slot as FirearmSlotController;
                            if (firearm == null || firearm.MagAnchor == null) throw new InvalidOperationException("Нет magazine frame styled slot.");
                            ArsenalPresentationResolver.ValidateFrame(slot.transform, firearm.MagAnchor.transform, firearm.MagAnchor.AlignTransform);
                            var resolved = ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, preset.PresentationStyle);
                            if (Application.isPlaying)
                                ArsenalPresentationResolver.ValidateMaterialized(slot, resolved);
                            preparedPresentation.Add(slot, resolved);
                        }
                        assignments.Add(new KeyValuePair<ArsenalSlotController, ArsenalPreset.Entry>(slot, entry));
                        break;
                    }
            // Все inputs/frames проверены; контекст опубликован ДО Configure/Show/Assign.
            _presentation.Clear();
            foreach (var pair in preparedPresentation) _presentation.Add(pair.Key, pair.Value);
            _prepared = preset;
            foreach (var assignment in assignments)
            {
                assignment.Key.ConfigureWeapon(assignment.Value.Weapon);
                assignment.Key.gameObject.SetActive(true);
            }
            foreach (var slot in slots)
                if (!used.Contains(slot))
                {
                    slot.ConfigureWeapon(null);
                    slot.gameObject.SetActive(false);
                }
            _lastError = null;
        }
    }
}
