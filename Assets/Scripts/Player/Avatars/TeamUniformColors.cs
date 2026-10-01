using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Красит форму аватара в цвета команды его игрока: одежду — в <see cref="TeamData.mainColor"/>,
    /// экипировку — в <see cref="TeamData.additionalColor"/>, каску и очки — в <see cref="TeamData.helmetColor"/>, с силой
    /// <c>*UniformStrength</c> команды. Перекраску считает шейдер
    /// <c>VR Battlegrounds/Team Uniform Lit</c> по маске модели; компонент только передаёт цвета через
    /// <c>MaterialPropertyBlock</c>.
    ///
    /// <para>
    /// Цвета перечитываются каждый кадр и применяются только при изменении — правка ассета команды видна сразу,
    /// в том числе в Play Mode. Пока команда неизвестна — работают значения материала (вариант аватара по умолчанию).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class TeamUniformColors : MonoBehaviour
    {
        public const string ShaderName = "VR Battlegrounds/Team Uniform Lit";

        private static readonly int MainColor = Shader.PropertyToID("_TeamMainColor");
        private static readonly int AdditionalColor = Shader.PropertyToID("_TeamAdditionalColor");
        private static readonly int HelmetColor = Shader.PropertyToID("_TeamHelmetColor");

        private PlayerController _player;
        private Renderer[] _uniform;
        private MaterialPropertyBlock _block;
        private bool _applied;
        private Color _main;
        private Color _additional;
        private Color _helmet;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _uniform = UniformRenderers(gameObject);
        }

        private void LateUpdate()
        {
            Apply(_player.Team);
        }

        /// <summary>Применяет цвета команды; <c>null</c> — снимает переопределение (значения материала).</summary>
        public void Apply(TeamData team)
        {
            if (team == null)
            {
                if (!_applied) return;
                foreach (Renderer r in _uniform) if (r != null) r.SetPropertyBlock(null);
                _applied = false;
                return;
            }

            Color main = WithStrength(team.mainColor, team.mainUniformStrength);
            Color additional = WithStrength(team.additionalColor, team.additionalUniformStrength);
            Color helmet = WithStrength(team.helmetColor, team.helmetUniformStrength);
            if (_applied && main == _main && additional == _additional && helmet == _helmet) return;

            _block ??= new MaterialPropertyBlock();
            ApplyTo(_uniform, main, additional, helmet, _block);

            _main = main;
            _additional = additional;
            _helmet = helmet;
            _applied = true;
        }

        /// <summary>Пишет цвета каналов в блоки свойств рендереров, сохраняя прочие переопределения блока.</summary>
        public static void ApplyTo(Renderer[] renderers, Color main, Color additional, Color helmet, MaterialPropertyBlock block)
        {
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(MainColor, main);
                block.SetColor(AdditionalColor, additional);
                block.SetColor(HelmetColor, helmet);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Альфа цвета в шейдере — сила перекраски канала.</summary>
        public static Color WithStrength(Color color, float strength) => new Color(color.r, color.g, color.b, Mathf.Clamp01(strength));

        /// <summary>Рендереры аватара на шейдере формы (тело и голова всех LOD).</summary>
        public static Renderer[] UniformRenderers(GameObject avatar)
        {
            var list = new List<Renderer>();
            foreach (Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name == ShaderName) list.Add(r);
            }

            return list.ToArray();
        }
    }
}
