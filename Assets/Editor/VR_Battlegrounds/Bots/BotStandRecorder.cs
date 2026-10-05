using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.DevTools.BotCombatStand;
using VrBattlegrounds.Editor.HandPoseReview;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Наблюдатель после SDK: действительные клипы, итоговые кисти и многоракурсные PNG.</summary>
    [InitializeOnLoad]
    public static class BotStandRecorder
    {
        private static BotStandCaseResult _case;
        private static BotStandSession _session;
        private static string _folder;
        private static float _next;
        private static BotStandVisualCapture _capture;
        private static bool _fitRequested;
        private static BlazeAISpace.CoverShooterBehaviour _observedBrain;
        private static UxrFirearmWeapon _observedWeapon;
        private static int _requests, _projectiles;
        private static string _readinessError;
        private static readonly List<object> Frames = new List<object>();
        private static readonly HashSet<string> Seen = new HashSet<string>();

        static BotStandRecorder()
        {
            BotCombatStand.CaseStarted += Begin; BotCombatStand.CaseFinished += Finish;
            UxrManager.AvatarsUpdated += Sample;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) Dispose(); };
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        private static void Begin(BotStandCaseResult result, BotStandSession session)
        {
            _case = result; _session = session; _next = Time.time + 1f; Frames.Clear(); Seen.Clear();
            _folder = Path.Combine(BotCombatStand.LastOutput, result.Id + "-" + result.Seed);
            Directory.CreateDirectory(_folder); _capture = result.Capture ? new BotStandVisualCapture() : null; _fitRequested=false; _requests=0; _projectiles=0; _readinessError=null;
            var weapons = BotCombatStand.Instance.Weapons.Where(w => w != null)
                .Select(w => new { name = w.name, category = w.Category.ToString(), path = AssetDatabase.GetAssetPath(w), guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(w)) }).ToArray();
            var player = session.Subject.ActiveAvatar;
            var source = player.SourcePrefab;
            var inputs = new { unity = Application.unityVersion, caseId = result.Id, seed = result.Seed, weapons,
                options=BotCombatStand.Instance.ActiveOptions, avatarPath=AssetDatabase.GetAssetPath(source), avatarIndex=session.Subject.AvatarIndex,
                sceneGuid=AssetDatabase.AssetPathToGUID(BotCombatStandBuilder.ScenePath), sourceHashes=SourceHashes() };
            File.WriteAllText(Path.Combine(_folder, "inputs.json"), JsonConvert.SerializeObject(inputs, Formatting.Indented));
        }

        private static void Sample()
        {
            if (_case == null || _session?.Subject?.ActiveAvatar == null || Time.time < _next) return;
            _next = Time.time + 0.5f;
            var player = _session.Subject.ActiveAvatar;
            var driver = player.GetComponent<BotCombatDriver>(); var body = player.GetComponent<BotBody>();
            var gun = player.GetComponent<BotGunner>(); var avatar = player.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if (driver == null || body == null || gun == null || avatar == null) return;
            try
            {
                Animator source = driver.PoseAnimator;
                var shooter=driver.Brain!=null ? driver.Brain.coverShooterBehaviour as BlazeAISpace.CoverShooterBehaviour : null;
                if(shooter!=_observedBrain) { if(_observedBrain!=null) _observedBrain.shootEvent.RemoveListener(Requested); _observedBrain=shooter; if(shooter!=null) shooter.shootEvent.AddListener(Requested); }
                if(gun.Firearm!=_observedWeapon) { if(_observedWeapon!=null) _observedWeapon.ProjectileShot-=Fired; _observedWeapon=gun.Firearm; if(_observedWeapon!=null) _observedWeapon.ProjectileShot+=Fired; }
                var weaponInfo=gun.Firearm!=null ? gun.Firearm.GetComponent<WeaponComponent>()?.WeaponData : null;
                string readinessReason="Отсутствует ReadinessProfile";
                bool profileValid=weaponInfo!=null && weaponInfo.ReadinessProfile!=null && weaponInfo.ReadinessProfile.TryValidate(out readinessReason);
                _readinessError=profileValid ? null : readinessReason;
                var clips = source != null ? source.GetCurrentAnimatorClipInfo(0).Select(c => new { name = c.clip.name, weight = c.weight,
                    path = AssetDatabase.GetAssetPath(c.clip), guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.clip)) }).ToArray() : null;
                string clip = clips != null && clips.Length > 0 ? clips[0].name : "none";
                var left = avatar.GetHandBone(UxrHandSide.Left); var right = avatar.GetHandBone(UxrHandSide.Right);
                var targetPlayer = _session.Target != null ? _session.Target.ActiveAvatar : null;
                var targetAvatar = targetPlayer != null ? targetPlayer.GetComponent<UltimateXR.Avatar.UxrAvatar>() : null;
                var camera = targetAvatar != null ? targetAvatar.CameraTransform : null;
                var projectile=gun.Firearm!=null ? gun.Firearm.GetComponent<UxrProjectileSource>() : null;
                var shot=projectile!=null && projectile.ShotTypes.Count>0 ? projectile.ShotTypes[0] : null;
                var solids=targetPlayer!=null ? targetPlayer.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger).ToArray() : new Collider[0];
                var bounds=new Bounds(camera!=null ? camera.position : Vector3.zero,Vector3.zero);
                if(solids.Length>0) { bounds=solids[0].bounds; foreach(var solid in solids.Skip(1)) bounds.Encapsulate(solid.bounds); }
                var muzzleHits=shot!=null && shot.ShotSource!=null && solids.Length>0 ? Physics.RaycastAll(shot.ShotSource.position,bounds.center-shot.ShotSource.position,40,shot.CollisionLayerMask,QueryTriggerInteraction.Ignore)
                    .OrderBy(h=>h.distance).Take(6).Select(h=>new { name=h.collider.name, distance=h.distance, actor=h.collider.GetComponentInParent<UxrActor>()?.name }).ToArray() : null;
                var hits = camera != null && driver.Brain != null
                    ? Physics.RaycastAll(driver.Brain.transform.position + Vector3.up * 1.55f,
                        camera.position - (driver.Brain.transform.position + Vector3.up * 1.55f), 40f,
                        LayerMask.GetMask("Default", "Ground", "Player"), QueryTriggerInteraction.Collide)
                        .Where(h => !driver.Brain.IsSelfCollider(h.collider)).OrderBy(h => h.distance)
                        .Take(6).Select(h => new { name = h.collider.name, distance = h.distance, layer = h.collider.gameObject.layer }).ToArray()
                    : null;
                Frames.Add(new { frame = Time.frameCount, time = Time.time, bodyId = player.GetInstanceID(),
                    state = driver.Brain != null ? driver.Brain.state.ToString() : "visual-idle", clips,
                    normalizedTime = source != null ? source.GetCurrentAnimatorStateInfo(0).normalizedTime : 0,
                    left = left != null ? new[] { left.position.x, left.position.y, left.position.z } : null,
                    right = right != null ? new[] { right.position.x, right.position.y, right.position.z } : null,
                    weapon = gun.Firearm != null ? gun.Firearm.name : null, category = gun.WeaponCategory.ToString(),
                    muzzleSeesTarget=gun.SeesTarget, muzzleRay=muzzleHits, ready=gun.Firearm!=null && gun.Firearm.IsReadyToFire(0),
                    readinessProfileValid=profileValid, readinessReason=profileValid ? null : readinessReason, blazeShootRequests=_requests, projectileEvents=_projectiles,
                    firstSight=driver.Brain!=null ? ((BlazeAISpace.CoverShooterBehaviour)driver.Brain.coverShooterBehaviour).firstSightDecision.ToString() : null,
                    shooterSettings=shooter!=null ? new { shooter.distanceFromEnemy,shooter.attackDistance,shooter.moveBackwardsDistance,shooter.moveForwardsToDistance,shooter.strafe,shooter.moveForwards,shooter.moveBackwards,shooter.changeCoverFrequency } : null,
                    subjectTeam = player.Session != null ? player.Session.TeamIndex : 0,
                    targetAlive = targetPlayer != null && targetPlayer.IsAlive,
                    targetTeam = _session.Target != null ? _session.Target.TeamIndex : 0,
                    targetRole = _session.Target != null ? _session.Target.Role.ToString() : null,
                    visionRay = hits,
                    targetCamera = camera != null ? new[] { camera.position.x, camera.position.y, camera.position.z } : null,
                    targetLayer = camera != null ? camera.gameObject.layer : -1,
                    targetTag = camera != null ? camera.gameObject.tag : null,
                    targetAngleFromBrainForward = camera != null && driver.Brain != null ? Vector3.Angle(driver.Brain.transform.forward,camera.position-driver.Brain.transform.position) : -1,
                    targetCameraCollider = camera != null && camera.GetComponent<Collider>() != null && camera.GetComponent<Collider>().enabled });
                string key = player.GetInstanceID() + "-" + clip;
                if (_capture != null && gun.Firearm != null && Seen.Add(key))
                    _case.Images.AddRange(_capture.Capture(player, _folder, "frame-" + Time.frameCount));
                if (!_fitRequested && gun.Firearm != null && BotCombatStand.Instance.ActiveOptions.HandFit)
                {
                    _fitRequested=true;
                    var result = _case;
                    if (HandPoseFitRuntimeCapture.Busy) { result.Metrics["handFit"]="InvalidFixture: анализатор занят другим заданием"; return; }
                    HandPoseFitRuntimeCapture.Request(avatar.GetGrabber(UxrHandSide.Right), new HandPoseFitRequest {
                        OutputDirectory=Path.Combine(_folder,"hand-fit"), SampleCount=1000, ImageSize=512, RenderImages=false,
                        IncludeOtherHand=true, CheckHandSelfIntersections=false, StateName=clip, SeriesName=result.Id },
                        fit => result.Metrics["handFit"] = new { status=fit.Status, report=fit.JsonPath, error=fit.Error,
                            frame=fit.CaptureFrame, captureSource=fit.CaptureSource, zones=fit.Zones, limitations=fit.Limitations, reliability=fit.Reliability }, gun.Firearm.gameObject);
                }
            }
            catch (Exception e) { _case.Status = "InvalidFixture"; _case.Error = "Запись/съёмка: " + e.Message; }
        }

        private static void Finish(BotStandCaseResult result)
        {
            if (_case != result) return;
            try
            {
            result.Metrics["subjectBlazeShootRequests"]=_requests; result.Metrics["subjectProjectileEvents"]=_projectiles;
            if(result.Status!="InvalidFixture" && _requests>0 && _projectiles==0 && _readinessError!=null) { result.Status="Failed"; result.Findings.Add("Запросы Blaze не могут стать выстрелами: " + _readinessError); }
            ValidateImages(result);
            File.WriteAllText(Path.Combine(_folder, "visual-frames.json"), JsonConvert.SerializeObject(Frames, Formatting.Indented));
            string html = "<!doctype html><meta charset='utf-8'><title>Стенд ботов</title><style>body{background:#202733;color:#edf2fa;font:16px sans-serif}img{width:31%;margin:1%}h1{font-size:24px}</style><h1>" +
                result.Id + " / seed " + result.Seed + "</h1><p>Статус: " + result.Status + ". Снимки фактического тела после SDK.</p>";
            foreach (string path in result.Images) html += "<a href='" + Path.GetFileName(path) + "'><img src='" + Path.GetFileName(path) + "'></a>";
            File.WriteAllText(Path.Combine(_folder, "report.html"), html, new System.Text.UTF8Encoding(false));
            }
            finally { Dispose(); }
        }

        public static void ValidateImages(BotStandCaseResult result)
        {
            if (result.Capture && result.Status != "Unsupported" && result.Status != "InvalidFixture" && (result.Images.Count < 5 || result.Images.Any(p=>!File.Exists(p))))
            { result.Status="InvalidFixture"; result.Error="Нет полного набора изображений."; }
        }

        private static Dictionary<string,string> SourceHashes()
        {
            var hashes=new Dictionary<string,string>();
            foreach(var path in new[] { "Assets/Scripts/Bots/BotBody.cs", "Assets/Scripts/Bots/BotCombatDriver.cs", "Assets/Scripts/Bots/BotGunner.cs", "Assets/Scripts/Bots/BotNavMesh.cs", "Assets/Resources/Bots/BotPistol.controller", "Assets/Resources/Bots/BotRifle.controller" })
                using(var stream=File.OpenRead(path)) using(var sha=System.Security.Cryptography.SHA256.Create())
                    hashes[path]=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            return hashes;
        }

        private static void Requested() => _requests++;
        private static void Fired(int trigger) => _projectiles++;
        private static void Dispose()
        {
            if(_observedBrain!=null) _observedBrain.shootEvent.RemoveListener(Requested);
            if(_observedWeapon!=null) _observedWeapon.ProjectileShot-=Fired;
            _observedBrain=null; _observedWeapon=null;
            _capture?.Dispose(); _capture = null; _case = null; _session = null;
        }
    }
}
