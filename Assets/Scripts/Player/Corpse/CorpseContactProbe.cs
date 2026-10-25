using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Первые контакты падающего трупа — для лога, когда тело легло далеко от места смерти
    /// (<see cref="Corpse"/>). Ставит труп на свои кости в момент падения; замёрзший труп коллайдеры
    /// выключает, и сообщения прекращаются сами. Своих данных не держит — передаёт контакт трупу.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CorpseContactProbe : MonoBehaviour
    {
        private Corpse _corpse;

        public void Bind(Corpse corpse) => _corpse = corpse;

        private void OnCollisionEnter(Collision collision)
        {
            if (_corpse != null && collision != null) _corpse.NoteContact(collision.collider);
        }
    }
}
