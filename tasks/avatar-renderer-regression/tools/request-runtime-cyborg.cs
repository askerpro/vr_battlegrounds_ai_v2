// Тот же клиентский запрос, что при выборе команды/скина в планшете; права проверяет сервер.
var source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
    "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab");
var session = VrBattlegrounds.Player.PlayerSession.LocalSession;
if (session == null || !session.isLocalPlayer) return new { requested = false, reason = "no-local-session" };
var team = VrBattlegrounds.TeamRegistry.Instance.teams.FirstOrDefault(t => t != null && t.avatars.Any(a => a != null && a.prefab == source));
if (team == null) return new { requested = false, reason = "cyborg-not-in-team-registry" };
var index = team.avatars.FindIndex(a => a != null && a.prefab == source);
session.CmdRequestTeamChange(team.teamIndex, index);
return new { requested = true, team = team.teamIndex, avatarIndex = index, sessionNetId = session.netId };
