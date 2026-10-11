// Живой диагностический просмотр foundation, без подключения старого bot runtime.
if (!UnityEditor.EditorApplication.isPlaying || !Mirror.NetworkServer.active ||
    !VrBattlegrounds.Maps.Runtime.MapRunAdmission.CanActivateActiveMap)
    throw new System.InvalidOperationException("Для просмотра нужен готовый сервер с активной картой.");
var library = UnityEngine.Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
var actors = new System.Collections.Generic.List<VrBattlegrounds.Player.PlayerSession>();
var items = new System.Collections.Generic.List<UnityEngine.GameObject>();
var roots = new System.Collections.Generic.List<UnityEngine.Vector3>();
var poses = new System.Collections.Generic.List<VrBattlegrounds.Bots.BotClipPose>();
var phases = new System.Collections.Generic.List<int>();
var bodies = new System.Collections.Generic.List<VrBattlegrounds.Player.PlayerController>();
var sources = new System.Collections.Generic.List<UnityEngine.GameObject>();
var models = new[] { "Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab", "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab" };
UnityEngine.Vector3 origin = UnityEngine.Vector3.zero;
UnityEngine.GameObject cameraObject = null;
try {
    foreach (var model in models) foreach (var pose in new[] {
        VrBattlegrounds.Bots.BotClipPose.Unarmed,
        VrBattlegrounds.Bots.BotClipPose.Pistol,
        VrBattlegrounds.Bots.BotClipPose.Rifle }) {
        var session = VrBattlegrounds.Managers.PlayersManager.Instance.CreateBotSession(
            "Клипы " + System.IO.Path.GetFileNameWithoutExtension(model) + " " + pose,
            "human-preview-" + System.Guid.NewGuid().ToString("N"), true);
        actors.Add(session);
        var team = VrBattlegrounds.Managers.MapReferee.Instance.ActiveGameMode.Teams.First(t => t != null);
        VrBattlegrounds.GameModes.SessionTeamAssigner.Apply(session, team, 0, "foundation preview");
        VrBattlegrounds.Player.Avatars.AvatarManager.Instance.SpawnAvatar(null, null, session);
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(model);
        typeof(VrBattlegrounds.Player.Avatars.AvatarManager).GetMethod("ReplaceBody",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(
                VrBattlegrounds.Player.Avatars.AvatarManager.Instance,
                new object[] { null, session, library.GetClipBody(prefab), true, "foundation preview" });
        var player = session.ActiveAvatar;
        var clips = player.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
        string reason;
        if (!clips.Bind(library, pose, out reason)) throw new System.InvalidOperationException(reason);
        clips.Animator.Update(0f);
        if (actors.Count == 1) origin = player.transform.position;
        var root = origin + new UnityEngine.Vector3(((actors.Count - 1) % 3 - 1) * 2.5f, 0f, (actors.Count - 1) / 3 * 3f);
        player.transform.SetPositionAndRotation(root, UnityEngine.Quaternion.identity);
        roots.Add(root); poses.Add(pose); phases.Add(-1);
        bodies.Add(player); sources.Add(player.SourcePrefab);
        if (pose == VrBattlegrounds.Bots.BotClipPose.Unarmed) continue;
        var id = pose == VrBattlegrounds.Bots.BotClipPose.Rifle ? "AK105" : "PPK";
        var source = UnityEngine.Resources.Load<UnityEngine.GameObject>("Bots/EquipmentProbe/" + id);
        var item = VrBattlegrounds.Maps.Runtime.MapRunAdmission.CreateActiveMapItem(source);
        items.Add(item); item.SetActive(true);
        VrBattlegrounds.Network.NetworkUxrIdentity.SpawnServerObject(item);
        var avatar = player.GetComponent<UltimateXR.Avatar.UxrAvatar>();
        var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>("Assets/Data/Weapons/" + id + "_Weapon.asset");
        UnityEngine.Pose socket;
        if (!library.TryGetSocket(player.SourcePrefab, definition, out socket))
            throw new System.InvalidOperationException("Нет authored NPC socket: " + id);
        if (!item.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>().TryAttach(avatar, socket.position, socket.rotation, out reason))
            throw new System.InvalidOperationException(reason);
    }
    cameraObject = new UnityEngine.GameObject("Камера просмотра foundation");
    var camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.depth = 100; camera.fieldOfView = 50;
    camera.transform.position = origin + new UnityEngine.Vector3(0f, 3.2f, 10f);
    camera.transform.LookAt(origin + new UnityEngine.Vector3(0f, 1f, 1.5f));
    float started = UnityEngine.Time.time;
    float previousTime = started;
    UnityEditor.EditorApplication.CallbackFunction tick = null;
    tick = () => {
        try {
        if (!UnityEditor.EditorApplication.isPlaying) { UnityEditor.EditorApplication.update -= tick; return; }
        if (!Mirror.NetworkServer.active || !VrBattlegrounds.Maps.Runtime.MapRunAdmission.CanActivateActiveMap ||
            !VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority) return;
        float elapsed = UnityEngine.Time.time - started;
        float deltaTime = UnityEngine.Time.time - previousTime;
        if (deltaTime <= 0f) return;
        previousTime = UnityEngine.Time.time;
        int phase = ((int)(elapsed / 5f)) % 5;
        var states = new[] { "Idle", "Move", "Back", "Left", "Right" };
        for (int i = 0; i < actors.Count; i++) {
            var player = actors[i].ActiveAvatar;
            if (player == null || player != bodies[i] || player.SourcePrefab != sources[i] || !player.IsAlive)
                throw new System.InvalidOperationException("Тело preview заменено или больше не живо.");
            if (poses[i] == VrBattlegrounds.Bots.BotClipPose.Unarmed) continue;
            var clips = player.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
            if (phases[i] != phase) { clips.Animator.CrossFadeInFixedTime(states[phase], .1f); phases[i] = phase; }
            float t = elapsed % 5f;
            // Forward/Back и Left/Right проходят один отрезок в противоположных направлениях.
            // Направление внутри одной фазы не меняется; смена клипа совпадает с разворотом движения.
            float progress = UnityEngine.Mathf.SmoothStep(0f, 1f, t / 5f);
            var displacement = phase == 1 ? UnityEngine.Vector3.forward * progress :
                phase == 2 ? UnityEngine.Vector3.forward * (1f - progress) :
                phase == 3 ? UnityEngine.Vector3.left * progress :
                phase == 4 ? UnityEngine.Vector3.left * (1f - progress) : UnityEngine.Vector3.zero;
            var previousPosition = player.transform.position;
            player.transform.SetPositionAndRotation(roots[i] + displacement, UnityEngine.Quaternion.identity);
            float speed = (player.transform.position - previousPosition).magnitude / deltaTime;
            string motionError;
            if (!clips.TrySetMotionSpeed(states[phase], speed, out motionError))
                throw new System.InvalidOperationException(motionError);
        }
        } catch (System.Exception error) {
            UnityEditor.EditorApplication.update -= tick;
            VrBattlegrounds.Core.GameLog.Debug.Error("[NPC preview] Движение остановлено: " + error.Message);
        }
    };
    UnityEditor.EditorApplication.update += tick;
    return new { ready = true, actors = actors.Count, weapons = items.Count,
        preview = "Два ряда: MEF и Cyborg; столбцы Unarmed, PPK, AK105. Каждые5с Idle/Move/Back/Left/Right.",
        scope = "Foundation: клипы и rigid equipment. Стрельба и боевой runtime не подключены." };
} catch {
    foreach (var item in items) if (item != null) Mirror.NetworkServer.Destroy(item);
    foreach (var actor in actors) {
        if (actor.ActiveAvatar != null) {
            VrBattlegrounds.Player.Avatars.AvatarTeardown.ConfiscateBeforeDestroy(actor.ActiveAvatar, "failed foundation preview");
            Mirror.NetworkServer.Destroy(actor.ActiveAvatar.gameObject);
        }
        VrBattlegrounds.Managers.PlayersManager.Instance.UnregisterBot(actor);
    }
    if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
    throw;
}
