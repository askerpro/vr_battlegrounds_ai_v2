using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Какой труп оставляет этот аватар (T-35) и где его модель, с которой снимается поза. Ставит
    /// и заполняет сборщик трупов (<c>Tools/VR Battlegrounds/Avatars/Build Corpses</c>) — на каждый
    /// аватар, который можно выбрать в команде. У призрака и киборга (запасной, в команде не бывает)
    /// трупа нет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CorpseSource : MonoBehaviour
    {
        [SerializeField] private Corpse _corpse;
        [SerializeField] private Transform _modelRoot;

        public Corpse CorpsePrefab => _corpse;

        public Transform ModelRoot => _modelRoot;

        /// <summary>Уронить труп в позе модели этого аватара. Локально, на этой машине.</summary>
        public Corpse Spawn(DeathImpact impact)
        {
            if (_corpse == null || _modelRoot == null) return null;

            Corpse corpse = Instantiate(_corpse, _modelRoot.position, _modelRoot.rotation);
            corpse.name = $"{_corpse.name} ({name})";
            corpse.Launch(_modelRoot, impact);
            return corpse;
        }
    }
}
