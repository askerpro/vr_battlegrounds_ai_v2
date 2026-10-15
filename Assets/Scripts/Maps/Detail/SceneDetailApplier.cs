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
