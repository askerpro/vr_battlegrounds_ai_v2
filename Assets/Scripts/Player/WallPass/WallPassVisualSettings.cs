using UnityEngine;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>Поставляемый визуальный профиль T-40. Не содержит параметров штрафа.</summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Wall Pass Visual Profile")]
    public sealed class WallPassVisualSettings : ScriptableObject
    {
        [Range(0f, 2f)] public float Intensity = 1f;
        [Range(0f, 1f)] public float ParticleDensity = 0.6f;
        [Range(0f, 2f)] public float ParticleBrightness = 0.7f;
        [Range(0.05f, 2f)] public float ParticleSpeed = 0.7f;
        [Range(0.02f, 0.4f)] public float ParticleTailLength = 0.12f;
        [Range(0.08f, 0.5f)] public float PeripheralWidth = 0.28f;
        [Range(1f, 3f)] public float EffectRadius = 1.5f;
        [Range(0f, 1f)] public float RimStrength = 0.5f;
        [Range(0f, 1f)] public float WorldStrength = 0.7f;
        [Range(0f, 1f)] public float BodyStrength = 0.8f;
        [Range(0f, 1f)] public float InitialStrength = 0.35f;
        [Range(0f, 1f)] public float MaximumStrength = 1f;
        public AnimationCurve Escalation = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public float Evaluate(float risk)
        {
            float curve = Escalation != null ? Escalation.Evaluate(Safe01(risk)) : Safe01(risk);
            return Mathf.Clamp(Safe(Intensity, 1f), 0f, 2f) *
                   Mathf.Lerp(Safe01(InitialStrength), Mathf.Max(Safe01(InitialStrength), Safe01(MaximumStrength)), Safe01(curve));
        }

        public static float Safe(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        public static float Safe01(float value) => Mathf.Clamp01(Safe(value, 0f));
    }

    /// <summary>Один владелец локального профиля и косметического предпросмотра.</summary>
    public static class WallPassVisualRuntime
    {
        private static WallPassVisualSettings _settings;
        public static WallPassVisualSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    WallPassVisualSettings source = Resources.Load<WallPassVisualSettings>("WallPassVisualProfile");
                    _settings = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<WallPassVisualSettings>();
                    _settings.hideFlags = HideFlags.HideAndDontSave;
                }
                return _settings;
            }
        }

#if UNITY_EDITOR
        public static bool PreviewEnabled { get; set; }
        public static float PreviewRisk { get; set; }
        public static float PreviewYaw { get; set; }
#else
        public static bool PreviewEnabled => false;
        public static float PreviewRisk => 0f;
        public static float PreviewYaw => 0f;
#endif
    }
}
