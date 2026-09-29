using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Красит тело аватара в цвет команды его игрока (<see cref="TeamData.color"/>) —
    /// <c>MaterialPropertyBlock</c>, свойство <c>_BaseColor</c>, альфа задаётся здесь. Команда
    /// берётся из сессии и перекрашивается, если стала известна позже спавна (порядок доставки
    /// сессии и аватара не гарантирован). Сейчас стоит на призраке — аватаре выбывшего (T-35):
    /// один префаб на все команды.
    ///
    /// <para>
    /// Тело — все <c>SkinnedMeshRenderer</c>, кроме моделей контроллеров интеграции рук.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class TeamColorTint : MonoBehaviour
    {
        [Tooltip("Прозрачность тела (альфа цвета команды). Материал должен быть прозрачным.")]
        [SerializeField, Range(0f, 1f)] private float _alpha = 0.35f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private PlayerController _player;
        private Renderer[] _body;
        private MaterialPropertyBlock _block;
        private int _tintedTeam = int.MinValue;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _body = BodyRenderers();
        }

        private void LateUpdate()
        {
            int team = _player.TeamIndex;
            if (team == _tintedTeam) return;

            Tint(_player.Team);
            _tintedTeam = team;
        }

        private void Tint(TeamData team)
        {
            Color color = team != null ? team.color : Color.white;
            color.a = _alpha;

            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColor, color);
            foreach (Renderer r in _body)
            {
                if (r != null) r.SetPropertyBlock(_block);
            }
        }

        private Renderer[] BodyRenderers()
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.GetComponentInParent<UxrHandIntegration>(true) == null) list.Add(r);
            }
            return list.ToArray();
        }
    }
}
