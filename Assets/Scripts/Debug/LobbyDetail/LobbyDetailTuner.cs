using System;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Подбор детализации Lobby в шлеме (решение пользователя 2026-10-13): границы среднего и дальнего
    /// планов и сила ухудшения каждого меняются на планшете (раздел «Отладка»), картинка — сразу.
    /// Дистанция — по горизонтали от края пола арены до центра объекта (для текстуры — до ближайшего
    /// использования); её и состав считает редакторская подготовка
    /// (<c>Tools/VR Battlegrounds/Lobby Decoration/Quest: подготовить подбор детализации</c>).
    ///
    /// <para>
    /// Уровень плана L: LOD принудительно не выше L-го (0 — как задано LODGroup), объекты мельче
    /// <see cref="SmallCullSteps"/>[L] не рисуются, текстуры теряют L верхних mip-уровней (группы
    /// mipmap limit <see cref="BandGroupPrefix"/>N по полосам <see cref="BandEdges"/>). Только локальная
    /// картинка: сеть и игровое состояние не затрагиваются. Выбранный уровень затем переносится в сцену
    /// и импорт текстур, компонент удаляется.
    /// </para>
    /// </summary>
    public sealed class LobbyDetailTuner : MonoBehaviour
    {
        [Serializable]
        public struct LodEntry
        {
            public LODGroup group;
            public float distance;
        }

        [Serializable]
        public struct SmallEntry
        {
            public Renderer renderer;
            public float distance;
            public float size;
        }

        public enum Parameter { MidStart, FarStart, MidLevel, FarLevel }

        /// <summary>Нижние границы полос текстур, м от края пола арены; полоса N — [BandEdges[N]; BandEdges[N+1]).</summary>
        public static readonly float[] BandEdges = { 0f, 5f, 10f, 15f, 20f, 25f, 30f, 40f, 50f };
        public const string BandGroupPrefix = "LobbyBand";

        public static readonly float[] MidStartSteps = { 5f, 10f, 15f, 20f, 25f, 30f };
        public static readonly float[] FarStartSteps = { 10f, 15f, 20f, 25f, 30f, 40f, 50f };

        /// <summary>Порог «мелочи» по уровню, м (не больше 0,6 м — решение пользователя).</summary>
        public static readonly float[] SmallCullSteps = { 0f, 0f, .2f, .35f, .5f, .6f };
        public static int MaxLevel => SmallCullSteps.Length - 1;

        [SerializeField] private LodEntry[] _lods = Array.Empty<LodEntry>();
        [SerializeField] private SmallEntry[] _small = Array.Empty<SmallEntry>();
        [SerializeField] private int _midStart = 1;
        [SerializeField] private int _farStart = 3;
        [SerializeField] private int _midLevel;
        [SerializeField] private int _farLevel;

        public static LobbyDetailTuner Current { get; private set; }

        public float MidStart => MidStartSteps[_midStart];
        public float FarStart => FarStartSteps[_farStart];
        public int MidLevel => _midLevel;
        public int FarLevel => _farLevel;

        public void SetData(LodEntry[] lods, SmallEntry[] small)
        {
            _lods = lods;
            _small = small;
        }

        public void Step(Parameter parameter, int delta)
        {
            if (Current != this) return;
            switch (parameter)
            {
                case Parameter.MidStart: _midStart = Mathf.Clamp(_midStart + delta, 0, MidStartSteps.Length - 1); break;
                case Parameter.FarStart: _farStart = Mathf.Clamp(_farStart + delta, 0, FarStartSteps.Length - 1); break;
                case Parameter.MidLevel: _midLevel = Mathf.Clamp(_midLevel + delta, 0, MaxLevel); break;
                case Parameter.FarLevel: _farLevel = Mathf.Clamp(_farLevel + delta, 0, MaxLevel); break;
            }
            Apply();
            GameLog.Perf.Info("[LobbyDetailTuner] " + Describe());
        }

        public string Describe() =>
            $"средний план с {MidStart:0} м, уровень {_midLevel}; дальний с {Mathf.Max(FarStart, MidStart):0} м, уровень {_farLevel}";

        /// <summary>Что делает уровень: для подписи на планшете.</summary>
        public static string DescribeLevel(int level) =>
            level == 0 ? "без изменений"
                       : $"LOD не выше {level}, текстуры /{1 << level}, без объектов мельче {SmallCullSteps[level] * 100f:0} см";

        // Только в режиме отладки Development-сборки (условие performance, событие 5934): вне него компонент
        // ничего не применяет и QualitySettings не трогает; выключение режима возвращает всё как было.
        private void OnEnable()
        {
            if (!Debug.isDebugBuild) { enabled = false; return; }
            DebugMode.Changed += OnDebugModeChanged;
            OnDebugModeChanged(DebugMode.Enabled);
        }

        private void OnDisable()
        {
            DebugMode.Changed -= OnDebugModeChanged;
            Deactivate();
        }

        private void OnDebugModeChanged(bool on)
        {
            if (on) { Current = this; Apply(); }
            else Deactivate();
        }

        private void Deactivate()
        {
            if (Current != this) return;
            Current = null;
            _midLevel = _farLevel = 0;
            Apply();
        }

        private int LevelAt(float distance)
        {
            if (distance >= Mathf.Max(FarStart, MidStart)) return _farLevel;
            return distance >= MidStart ? _midLevel : 0;
        }

        private void Apply()
        {
            foreach (LodEntry entry in _lods)
            {
                if (entry.group == null) continue;
                int level = LevelAt(entry.distance);
                entry.group.ForceLOD(level == 0 ? -1 : Mathf.Min(level, entry.group.lodCount - 1));
            }

            foreach (SmallEntry entry in _small)
            {
                if (entry.renderer == null) continue;
                entry.renderer.enabled = entry.size >= SmallCullSteps[LevelAt(entry.distance)];
            }

            for (int band = 0; band < BandEdges.Length; band++)
            {
                string group = BandGroupPrefix + band;
                if (!TextureMipmapLimitGroups.HasGroup(group)) continue;
                QualitySettings.SetTextureMipmapLimitSettings(group, new TextureMipmapLimitSettings
                {
                    limitBias = LevelAt(BandEdges[band]),
                    limitBiasMode = TextureMipmapLimitBiasMode.OffsetGlobalLimit,
                });
            }
        }
    }
}
