using UnityEngine;

namespace VrBattlegrounds.Core
{
    public enum GameRole
    {
        Player,    // Participates in combat, spawns physical avatar, has team and avatar.
        Spectator  // Observes combat, no physical avatar (or invisible), can be admin.
    }
}
