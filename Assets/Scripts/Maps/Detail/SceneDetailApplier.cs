using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Maps.Detail
{
    /// <summary>
    /// Единственный писатель детализации сцены в рантайме (задача scene-detail): применяет
    /// <see cref="SceneDetailProfile" /> к записям, которые заранее собрал редактор
    /// (<c>Tools/VR Battlegrounds/Detail/Собрать записи сцены</c>), и возвращает всё при выгрузке.
    ///
    /// <para>
    /// LOD — обрезкой списка уровней с сохранением порога отсечения последнего уровня (не ForceLOD:
    /// он снимает отсечение, и дальние объекты появлялись снова); рендереры пропущенных уровней
    /// выключаются. Мелочь — выключением рендереров. Текстуры — смещением групп mipmap limit
    /// <see cref="BandGroupPrefix" />N по полосам дистанции <see cref="BandEdges" />.
    /// Отладка правит живую копию правил (<see cref="Live" />), ассет профиля не меняется.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneDetailApplier : MonoBehaviour
    {
        [Serializable]
        public struct LodEntry
        {
            public LODGroup group;
            public float distance;
        }

        [Serializable]
        public struct RendererEntry
        {
            public Renderer renderer;
            public float distance;
            public float size;
        }

        /// <summary>Нижние границы полос текстур, м от края пола арены; полоса N — [BandEdges[N]; BandEdges[N+1]).</summary>
        public static readonly float[] BandEdges = { 0f, 5f, 10f, 15f, 20f, 25f, 30f, 40f, 50f };
        public const string BandGroupPrefix = "DetailBand";

        [SerializeField] private SceneDetailProfile _profile;
        [SerializeField] private LodEntry[] _lods = Array.Empty<LodEntry>();
        [SerializeField] private RendererEntry[] _renderers = Array.Empty<RendererEntry>();

        private LOD[][] _originalLods;
        private readonly Dictionary<Renderer, bool> _baseEnabled = new Dictionary<Renderer, bool>();
        private readonly Dictionary<string, TextureMipmapLimitSettings> _baseTextureSettings =
            new Dictionary<string, TextureMipmapLimitSettings>();
        private SceneDetailProfile _live;

        public static SceneDetailApplier Current { get; private set; }

        /// <summary>Живая копия профиля: правится отладкой, применяется <see cref="Apply" />.</summary>
        public SceneDetailProfile Live => _live;

        public SceneDetailProfile Profile => _profile;

        /// <summary>Локальная версия собственных Apply/Restore для read-only диагностики.</summary>
        public uint DiagnosticsRevision { get; private set; }

        /// <summary>
        /// Снимает данные без изменения сцены. Вызывать только на главном потоке Unity,
        /// на границе измерительной фазы, не каждый кадр. Успех не подтверждает GPU draw.
        /// </summary>
        public bool TryCaptureDiagnostics(out SceneDetailDiagnosticsSnapshot snapshot, out string unavailableReason)
        {
            snapshot = null;
            unavailableReason = null;
            if (Current != this || !isActiveAndEnabled)
            {
                unavailableReason = "Применитель не является активным Current.";
                return false;
            }
            if (_profile == null || _live == null || _lods == null || _renderers == null ||
                _originalLods == null || _originalLods.Length != _lods.Length)
            {
                unavailableReason = "Профиль, записи или исходный кэш применителя не инициализированы либо несовместимы.";
                return false;
            }

            var limitations = new List<string>
            {
                "original — кэш Awake до собственного Apply; соответствие edit-time authoring требует manifest.",
                "enabled и effective не доказывают видимость, выбранный камерой LOD или GPU draw."
            };
            var groups = new SceneDetailDiagnosticsSnapshot.Group[_lods.Length];
            bool complete = true;
            for (int i = 0; i < _lods.Length; i++)
            {
                LodEntry entry = _lods[i];
                LOD[] original = _originalLods[i];
                LOD[] effective = entry.group != null ? entry.group.GetLODs() : new LOD[0];
                SceneDetailProfile.PlaneRule rule = RuleAt(entry.distance);
                int[] mapping = CaptureDiagnosticsMapping(original, effective, rule.lodSkip, out string reason);
                if (entry.group == null) reason = "LODGroup отсутствует.";
                var group = new SceneDetailDiagnosticsSnapshot.Group
                {
                    group = entry.group,
                    arenaEdgeDistance = entry.distance,
                    band = entry.distance >= Mathf.Max(_live.farStart, _live.midStart) ? "far" :
                        entry.distance >= _live.midStart ? "mid" : "near",
                    rule = rule,
                    original = CaptureDiagnosticsLevels(original, "original группы " + i, limitations, ref complete),
                    effective = CaptureDiagnosticsLevels(effective, "effective группы " + i, limitations, ref complete),
                    effectiveToOriginal = mapping,
                    mappingVerified = string.IsNullOrEmpty(reason),
                    reason = reason
                };
                if (!group.mappingVerified)
                {
                    complete = false;
                    limitations.Add("Группа " + i + ": " + reason);
                    for (int level = 0; level < mapping.Length; level++) mapping[level] = -1;
                }
                groups[i] = group;
            }

            var renderers = new List<SceneDetailDiagnosticsSnapshot.RendererState>();
            var represented = new HashSet<Renderer>();
            // Повторные записи сохраняют каждую управляемую дистанцию/размер общего рендерера.
            foreach (RendererEntry entry in _renderers)
            {
                SceneDetailDiagnosticsSnapshot.RendererState state = CaptureDiagnosticsRenderer(entry.renderer);
                state.hasManagedDistance = true;
                state.arenaEdgeDistance = entry.distance;
                state.size = entry.size;
                renderers.Add(state);
                if (entry.renderer != null) represented.Add(entry.renderer);
            }
            foreach (KeyValuePair<Renderer, bool> pair in _baseEnabled)
            {
                if (pair.Key != null && !represented.Add(pair.Key)) continue;
                renderers.Add(new SceneDetailDiagnosticsSnapshot.RendererState
                {
                    renderer = pair.Key,
                    baselineKnown = true,
                    baselineEnabled = pair.Value,
                    currentEnabled = pair.Key != null && pair.Key.enabled
                });
            }
            foreach (SceneDetailDiagnosticsSnapshot.Group group in groups)
            {
                CaptureDiagnosticsLevelRenderers(group.original, represented, renderers);
                CaptureDiagnosticsLevelRenderers(group.effective, represented, renderers);
            }
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i].renderer != null && renderers[i].baselineKnown) continue;
                complete = false;
                limitations.Add("Рендерер " + i + ": отсутствует объект или исходное enabled.");
            }

            var textureNames = new List<string>();
            for (int band = 0; band < BandEdges.Length; band++) textureNames.Add(BandGroupPrefix + band);
            foreach (string name in _baseTextureSettings.Keys)
                if (!textureNames.Contains(name)) textureNames.Add(name);
            var textureGroups = new SceneDetailDiagnosticsSnapshot.TextureGroupState[textureNames.Count];
            for (int i = 0; i < textureNames.Count; i++)
            {
                string name = textureNames[i];
                bool exists = TextureMipmapLimitGroups.HasGroup(name);
                bool baselineKnown = _baseTextureSettings.TryGetValue(name, out TextureMipmapLimitSettings baseline);
                textureGroups[i] = new SceneDetailDiagnosticsSnapshot.TextureGroupState
                {
                    name = name,
                    exists = exists,
                    baselineKnown = baselineKnown,
                    baseline = baseline,
                    current = exists ? QualitySettings.GetTextureMipmapLimitSettings(name) : default
                };
                if (exists && baselineKnown) continue;
                complete = false;
                limitations.Add("Группа текстур " + name + ": отсутствует группа или исходные настройки.");
            }

            var rendererStates = new SceneDetailDiagnosticsSnapshot.RendererState[renderers.Count];
            renderers.CopyTo(rendererStates);
            snapshot = new SceneDetailDiagnosticsSnapshot
            {
                scenePath = gameObject.scene.path,
                sceneName = gameObject.scene.name,
                applier = this,
                profile = _profile,
                observationFrame = Time.frameCount,
                observationRealtime = Time.realtimeSinceStartupAsDouble,
                midStart = _live.midStart,
                farStart = _live.farStart,
                mid = _live.mid,
                far = _live.far,
                globalTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
                lodBias = QualitySettings.lodBias,
                groups = groups,
                renderers = rendererStates,
                textureGroups = textureGroups,
                limitations = limitations.ToArray(),
                complete = complete
            };
            return true;
        }

        private SceneDetailDiagnosticsSnapshot.RendererState CaptureDiagnosticsRenderer(Renderer renderer)
        {
            bool baseline = false;
            bool known = renderer != null && _baseEnabled.TryGetValue(renderer, out baseline);
            return new SceneDetailDiagnosticsSnapshot.RendererState
            {
                renderer = renderer,
                baselineKnown = known,
                baselineEnabled = baseline,
                currentEnabled = renderer != null && renderer.enabled
            };
        }

        private void CaptureDiagnosticsLevelRenderers(SceneDetailDiagnosticsSnapshot.Level[] levels,
            HashSet<Renderer> represented, List<SceneDetailDiagnosticsSnapshot.RendererState> renderers)
        {
            foreach (SceneDetailDiagnosticsSnapshot.Level level in levels)
                foreach (Renderer renderer in level.renderers)
                    if (renderer != null && represented.Add(renderer)) renderers.Add(CaptureDiagnosticsRenderer(renderer));
        }

        private static SceneDetailDiagnosticsSnapshot.Level[] CaptureDiagnosticsLevels(LOD[] source, string label,
            List<string> limitations, ref bool complete)
        {
            var levels = new SceneDetailDiagnosticsSnapshot.Level[source != null ? source.Length : 0];
            if (source == null)
            {
                complete = false;
                limitations.Add(label + ": отсутствует массив уровней.");
                return levels;
            }
            for (int i = 0; i < source.Length; i++)
            {
                Renderer[] sourceRenderers = source[i].renderers;
                var renderers = new Renderer[sourceRenderers != null ? sourceRenderers.Length : 0];
                if (sourceRenderers != null) Array.Copy(sourceRenderers, renderers, renderers.Length);
                if (sourceRenderers == null || Array.Exists(renderers, renderer => renderer == null))
                {
                    complete = false;
                    limitations.Add(label + ", уровень " + i + ": отсутствуют записи рендереров.");
                }
                levels[i] = new SceneDetailDiagnosticsSnapshot.Level
                {
                    screenRelativeTransitionHeight = source[i].screenRelativeTransitionHeight,
                    fadeTransitionWidth = source[i].fadeTransitionWidth,
                    renderers = renderers
                };
            }
            return levels;
        }

        private static int[] CaptureDiagnosticsMapping(LOD[] original, LOD[] effective, int lodSkip, out string reason)
        {
            var mapping = new int[effective.Length];
            for (int i = 0; i < mapping.Length; i++) mapping[i] = -1;
            reason = "Исходный кэш уровней отсутствует или пуст.";
            if (original == null || original.Length == 0) return mapping;
            int skip = Mathf.Clamp(lodSkip, 0, original.Length - 1);
            reason = "Текущий список LOD не соответствует исходному кэшу и правилу пропуска.";
            if (effective.Length != original.Length - skip) return mapping;
            for (int i = 0; i < effective.Length; i++)
            {
                LOD current = effective[i];
                LOD baseline = original[i + skip];
                if (current.screenRelativeTransitionHeight != baseline.screenRelativeTransitionHeight ||
                    current.fadeTransitionWidth != baseline.fadeTransitionWidth || current.renderers == null ||
                    baseline.renderers == null || current.renderers.Length != baseline.renderers.Length) return mapping;
                for (int renderer = 0; renderer < current.renderers.Length; renderer++)
                    if (current.renderers[renderer] == null || baseline.renderers[renderer] == null ||
                        current.renderers[renderer] != baseline.renderers[renderer]) return mapping;
            }
            for (int i = 0; i < mapping.Length; i++) mapping[i] = i + skip;
            reason = null;
            return mapping;
        }

        public void SetData(SceneDetailProfile profile, LodEntry[] lods, RendererEntry[] renderers)
        {
            _profile = profile;
            _lods = lods;
            _renderers = renderers;
        }

        private void Awake()
        {
            // Выделенный сервер без графики ничего не рисует — профиль не нужен.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            if (_profile == null)
            {
                GameLog.Error("[SceneDetail] Нет профиля детализации — сцена остаётся как есть.", this);
                enabled = false;
                return;
            }
            _live = Instantiate(_profile);
            _live.name = _profile.name + " (живой)";
            _originalLods = new LOD[_lods.Length][];
            for (int i = 0; i < _lods.Length; i++)
            {
                _originalLods[i] = _lods[i].group != null ? _lods[i].group.GetLODs() : Array.Empty<LOD>();
                foreach (LOD lod in _originalLods[i])
                    foreach (Renderer r in lod.renderers) Remember(r);
            }
            foreach (RendererEntry entry in _renderers) Remember(entry.renderer);
        }

        private void OnEnable()
        {
            if (_live == null) return;
            if (Current != null && Current != this)
            {
                GameLog.Error("[SceneDetail] Уже работает применитель другой сцены: " + Current.gameObject.scene.name, this);
                enabled = false;
                return;
            }
            // Снимок берётся только при получении владения, в том числе после повторного включения.
            _baseTextureSettings.Clear();
            for (int band = 0; band < BandEdges.Length; band++)
            {
                string group = BandGroupPrefix + band;
                if (TextureMipmapLimitGroups.HasGroup(group))
                    _baseTextureSettings[group] = QualitySettings.GetTextureMipmapLimitSettings(group);
            }
            Current = this;
            Apply();
        }

        private void OnDisable()
        {
            if (_live == null || Current != this) return;
            Restore();
            Current = null;
        }

        private void OnDestroy()
        {
            if (_live != null) Destroy(_live);
        }

        /// <summary>Применяет живые правила ко всем записям.</summary>
        public void Apply()
        {
            if (_live == null) return;
            var hidden = new HashSet<Renderer>();

            for (int i = 0; i < _lods.Length; i++)
            {
                LODGroup group = _lods[i].group;
                LOD[] original = _originalLods[i];
                if (group == null || original.Length == 0) continue;
                int skip = Mathf.Clamp(RuleAt(_lods[i].distance).lodSkip, 0, original.Length - 1);
                if (skip == 0) { group.SetLODs(original); continue; }

                var kept = new LOD[original.Length - skip];
                var keptRenderers = new HashSet<Renderer>();
                for (int k = 0; k < kept.Length; k++)
                {
                    LOD source = original[k + skip];
                    kept[k] = new LOD(source.screenRelativeTransitionHeight, source.renderers) { fadeTransitionWidth = source.fadeTransitionWidth };
                    foreach (Renderer r in source.renderers) if (r != null) keptRenderers.Add(r);
                }
                group.SetLODs(kept);
                for (int k = 0; k < skip; k++)
                    foreach (Renderer r in original[k].renderers)
                        if (r != null && !keptRenderers.Contains(r)) hidden.Add(r);
            }

            foreach (RendererEntry entry in _renderers)
                if (entry.renderer != null && entry.size < RuleAt(entry.distance).cullSize) hidden.Add(entry.renderer);
            // Каждый управляемый рендерер: исходное состояние, если его не скрыли пропуск LOD или отсечение.
            foreach (KeyValuePair<Renderer, bool> pair in _baseEnabled)
                if (pair.Key != null) pair.Key.enabled = pair.Value && !hidden.Contains(pair.Key);

            for (int band = 0; band < BandEdges.Length; band++)
                SetBandBias(band, RuleAt(BandEdges[band]).textureBias);
            DiagnosticsRevision++;
        }

        private void Restore()
        {
            for (int i = 0; i < _lods.Length; i++)
                if (_lods[i].group != null && _originalLods[i].Length > 0) _lods[i].group.SetLODs(_originalLods[i]);
            foreach (KeyValuePair<Renderer, bool> pair in _baseEnabled)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (KeyValuePair<string, TextureMipmapLimitSettings> pair in _baseTextureSettings)
                if (TextureMipmapLimitGroups.HasGroup(pair.Key))
                    QualitySettings.SetTextureMipmapLimitSettings(pair.Key, pair.Value);
            DiagnosticsRevision++;
        }

        private void Remember(Renderer renderer)
        {
            if (renderer != null && !_baseEnabled.ContainsKey(renderer)) _baseEnabled[renderer] = renderer.enabled;
        }

        private SceneDetailProfile.PlaneRule RuleAt(float distance)
        {
            float far = Mathf.Max(_live.farStart, _live.midStart);
            if (distance >= far) return _live.far.Clamped();
            if (distance >= _live.midStart) return _live.mid.Clamped();
            return default;
        }

        private static void SetBandBias(int band, int bias)
        {
            string group = BandGroupPrefix + band;
            if (!TextureMipmapLimitGroups.HasGroup(group)) return;
            QualitySettings.SetTextureMipmapLimitSettings(group, new TextureMipmapLimitSettings
            {
                limitBias = bias,
                limitBiasMode = TextureMipmapLimitBiasMode.OffsetGlobalLimit,
            });
        }
    }
}
