var root="tasks/bots-fix/reports/equipment";
System.IO.Directory.CreateDirectory(root);
var library=UnityEngine.Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
var results=new System.Collections.Generic.List<object>();
var actors=new System.Collections.Generic.List<VrBattlegrounds.Player.PlayerSession>();
var objects=new System.Collections.Generic.List<UnityEngine.GameObject>();
var models=new[]{"Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab","Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab"};
System.Action<string,bool> check=(name,passed)=>results.Add(new{name=name,passed=passed});
try {
    // Настоящий enabled VR hand fixture отдельно от NPC: clip variants намеренно отключают grabbers.
    var vrSession=VrBattlegrounds.Managers.PlayersManager.Instance.CreateBotSession("VR equipment fixture","equipment-vr-"+System.Guid.NewGuid().ToString("N"),false);
    actors.Add(vrSession);
    var vrTeam=VrBattlegrounds.Managers.MapReferee.Instance.ActiveGameMode.Teams.First(t=>t!=null);
    VrBattlegrounds.GameModes.SessionTeamAssigner.Apply(vrSession,vrTeam,0,"equipment VR fixture");
    VrBattlegrounds.Player.Avatars.AvatarManager.Instance.SpawnAvatar(null,null,vrSession);
    typeof(VrBattlegrounds.Player.Avatars.AvatarManager).GetMethod("ReplaceBody",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(VrBattlegrounds.Player.Avatars.AvatarManager.Instance,new object[]{null,vrSession,UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(models[0]),true,"equipment VR fixture"});
    var vrHand=vrSession.ActiveAvatar.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true).FirstOrDefault(h=>h.isActiveAndEnabled);
    if(vrHand==null) throw new System.InvalidOperationException("VR hand fixture не зарегистрирован/enabled.");
    foreach(var model in models) {
        var session=VrBattlegrounds.Managers.PlayersManager.Instance.CreateBotSession("Equipment probe","equipment-probe-"+System.Guid.NewGuid().ToString("N"),true);
        actors.Add(session);
        var team=VrBattlegrounds.Managers.MapReferee.Instance.ActiveGameMode.Teams.First(t=>t!=null);
        VrBattlegrounds.GameModes.SessionTeamAssigner.Apply(session,team,0,"equipment probe");
        VrBattlegrounds.Player.Avatars.AvatarManager.Instance.SpawnAvatar(null,null,session);
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(model);
        typeof(VrBattlegrounds.Player.Avatars.AvatarManager).GetMethod("ReplaceBody",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(VrBattlegrounds.Player.Avatars.AvatarManager.Instance,new object[]{null,session,library.GetClipBody(prefab),true,"equipment probe"});
        var player=session.ActiveAvatar;
        var clips=player.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
        var initialAnimator=player.GetComponentsInChildren<UnityEngine.Animator>(true).First(a=>a.avatar!=null&&a.avatar.isHuman);
        for(int frame=0;frame<60;frame++) initialAnimator.Update(1f/60f);
        float defaultLeft=initialAnimator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperArm).position.y-initialAnimator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftHand).position.y;
        float defaultRight=initialAnimator.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperArm).position.y-initialAnimator.GetBoneTransform(UnityEngine.HumanBodyBones.RightHand).position.y;
        int initialControllers=player.GetComponentsInChildren<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>(true).Count(c=>c.isActiveAndEnabled);
        results.Add(new{name=model+"/unarmed-before-bind",passed=defaultLeft>.15f&&defaultRight>.15f&&initialControllers==0,
            leftDrop=defaultLeft,rightDrop=defaultRight,enabledSdkControllers=initialControllers});
        string reason;
        if(!clips.Bind(library,false,out reason)) throw new System.InvalidOperationException(reason);
        clips.SetPose(VrBattlegrounds.Bots.BotClipPose.Unarmed);
        for(int frame=0;frame<60;frame++) clips.Animator.Update(1f/60f);
        var lHand=clips.Animator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftHand);
        var rHand=clips.Animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightHand);
        var lShoulder=clips.Animator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperArm);
        var rShoulder=clips.Animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperArm);
        results.Add(new{name=model+"/unarmed-idle",passed=lHand.position.y<lShoulder.position.y-.15f && rHand.position.y<rShoulder.position.y-.15f,
            leftDrop=lShoulder.position.y-lHand.position.y,rightDrop=rShoulder.position.y-rHand.position.y});
        var renderers=player.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
        var layers=renderers.Select(r=>r.gameObject.layer).ToArray();
        var cameraObject=new UnityEngine.GameObject("Unarmed fixture capture");
        var camera=cameraObject.AddComponent<UnityEngine.Camera>();
        var target=new UnityEngine.RenderTexture(640,800,24);
        var pixels=new UnityEngine.Texture2D(640,800,UnityEngine.TextureFormat.RGB24,false);
        var previous=UnityEngine.RenderTexture.active;
        try {
            foreach(var renderer in renderers)renderer.gameObject.layer=31;
            camera.cullingMask=1<<31; camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;
            camera.backgroundColor=new UnityEngine.Color(.13f,.15f,.18f); camera.fieldOfView=35;
            camera.transform.position=player.transform.position+new UnityEngine.Vector3(0,1.1f,3.5f);
            camera.transform.LookAt(player.transform.position+UnityEngine.Vector3.up*.9f);
            camera.targetTexture=target; camera.Render(); UnityEngine.RenderTexture.active=target;
            pixels.ReadPixels(new UnityEngine.Rect(0,0,640,800),0,0); pixels.Apply();
            System.IO.File.WriteAllBytes(root+"/unarmed-"+System.IO.Path.GetFileNameWithoutExtension(model)+".png",pixels.EncodeToPNG());
        }finally {
            UnityEngine.RenderTexture.active=previous;
            for(int i=0;i<renderers.Length;i++)renderers[i].gameObject.layer=layers[i];
            UnityEngine.Object.Destroy(cameraObject); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
        }
        clips.SetCategory(false);
        foreach(var id in new[]{"PPK","AK105"}) {
            var source=UnityEngine.Resources.Load<UnityEngine.GameObject>("Bots/EquipmentProbe/"+id);
            if(id=="PPK") {
                var raw=UnityEngine.Object.Instantiate(source);
                var rawGrip=raw.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>();
                var rawAnchor=rawGrip.CurrentAnchor;
                try {
                    VrBattlegrounds.Network.NetworkUxrIdentity.SpawnServerObject(raw);
                    string rawReason;
                    bool rawAccepted=raw.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>().TryAttach(player.GetComponent<UltimateXR.Avatar.UxrAvatar>(),UnityEngine.Vector3.zero,UnityEngine.Quaternion.identity,out rawReason);
                    results.Add(new{name=model+"/plain-spawn-anchor-rejected",passed=rawAnchor!=null&&!rawAccepted,anchor=rawAnchor!=null?rawAnchor.name:null,reason=rawReason});
                }finally {
                    Mirror.NetworkServer.Destroy(raw);
                    if(rawAnchor!=null)UnityEngine.Object.Destroy(rawAnchor.gameObject);
                }
            }
            var item=VrBattlegrounds.Maps.Runtime.MapRunAdmission.CreateActiveMapItem(source);
            if(item==null)throw new System.InvalidOperationException("Fixture: карта отказала в создании оружия.");
            objects.Add(item);
            item.SetActive(true);
            VrBattlegrounds.Network.NetworkUxrIdentity.SpawnServerObject(item);
            var binding=item.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>();
            var avatar=player.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            var grip=item.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>();
            var hand=vrHand;
            var manager=UltimateXR.Manipulation.UxrGrabManager.Instance;
            var prefix=model+"/"+id+"/";
            var transports=item.GetComponents<Mirror.NetworkTransformBase>();
            var directions=transports.Select(t=>t.syncDirection).ToArray();
            bool attachedInitially=binding.TryAttach(avatar,UnityEngine.Vector3.zero,UnityEngine.Quaternion.identity,out reason);
            bool attachReady=attachedInitially&&binding.IsActive&&binding.Owner==avatar&&binding.OwnerAlive&&binding.Epoch==1&&binding.HasValidAttachment;
            results.Add(new{name=prefix+"attach",passed=attachReady,accepted=attachedInitially,reason,insideStateSync=UltimateXR.Core.UxrManager.Instance.IsInsideStateSync,
                server=binding.isServer,netId=binding.netId,active=item.activeInHierarchy,ownerNetId=player.netId,canonical=session.ActiveAvatarNetId,alive=player.IsAlive,bot=session.IsBotControlled,
                admission=VrBattlegrounds.Maps.Runtime.MapRunAdmission.CanActivateActiveMap,valid=binding.HasValidAttachment});
            if(!attachReady) continue;
            check(prefix+"availability",item.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabbableObject>(true).All(g=>!g.IsGrabbable));
            var childGrip=item.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabbableObject>(true).FirstOrDefault(g=>g!=grip);
            if(childGrip!=null) {
                var innerType=typeof(VrBattlegrounds.Player.PlayerController).Assembly.GetType("VrBattlegrounds.EditorTools.EquipmentAvailabilityProbe");
                if(innerType==null)innerType=System.AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VrBattlegrounds.EditorTools.EquipmentAvailabilityProbe")).FirstOrDefault(t=>t!=null);
                if(innerType!=null) {var inner=childGrip.gameObject.AddComponent(innerType);check(prefix+"nested-provider-and",!childGrip.IsGrabbable);UnityEngine.Object.DestroyImmediate(inner);}
            }
            manager.GrabObject(hand,grip,0,false);
            check(prefix+"direct-grab-denied",!manager.IsBeingGrabbed(grip));
            var grabArgs=new UltimateXR.Core.StateSync.UxrMethodInvokedSyncEventArgs("GrabObject",new object[]{hand,grip,0,null,false});
            var replay=UltimateXR.Core.UxrManager.Instance.ExecuteStateSyncEvent(grabArgs.SerializeEventBinary(manager));
            check(prefix+"replayed-grab-denied",!replay.IsError&&!manager.IsBeingGrabbed(grip));
            if(manager.IsBeingGrabbed(grip)) {
                var release=new UltimateXR.Core.StateSync.UxrMethodInvokedSyncEventArgs("ReleaseObject",new object[]{hand,grip,grip.transform.position,grip.transform.rotation,UnityEngine.Vector3.zero,UnityEngine.Vector3.zero,false});
                UltimateXR.Core.UxrManager.Instance.ExecuteStateSyncEvent(release.SerializeEventBinary(manager));
            }
            float maxPos=0,maxRot=0;
            for(int n=0;n<240;++n) {
                clips.SetCategory(n>=120);
                player.transform.SetPositionAndRotation(new UnityEngine.Vector3(UnityEngine.Mathf.Sin(n*.08f)*2,0,-4+UnityEngine.Mathf.Cos(n*.07f)),UnityEngine.Quaternion.Euler(0,n*11f,0));
                clips.Animator.Play("Move",0,(n%120)/120f); clips.Animator.Update(1f/60f);
                binding.SendMessage("LateUpdate");
                maxPos=UnityEngine.Mathf.Max(maxPos,UnityEngine.Vector3.Distance(item.transform.position,clips.RightHand.position));
                maxRot=UnityEngine.Mathf.Max(maxRot,UnityEngine.Quaternion.Angle(item.transform.rotation,clips.RightHand.rotation));
            }
            results.Add(new{name=prefix+"rigid-follow",passed=maxPos<.001f&&maxRot<.1f,maxPositionMm=maxPos*1000,maxAngle=maxRot});
            var writer=new Mirror.NetworkWriter(); binding.OnSerialize(writer,true);
            var copy=VrBattlegrounds.Network.NetworkUxrIdentity.CreateInstance(source); objects.Add(copy); copy.SetActive(true);
            VrBattlegrounds.Network.NetworkUxrIdentity.Align(copy,900000u+(uint)objects.Count);
            var observer=copy.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>();
            var copyGrip=copy.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>();
            // До получения claim копия имела предсказанный локальный SDK-хват.
            manager.GrabObject(hand,copyGrip,0,false);
            int outgoing=0;
            System.Action<UltimateXR.Core.StateSync.IUxrStateSync,UltimateXR.Core.StateSync.UxrSyncEventArgs> observed=(c,e)=>++outgoing;
            UltimateXR.Core.UxrManager.ComponentStateChanged+=observed;
            bool nestedSeen=false,nestedDeferred=false;
            System.EventHandler<UltimateXR.Core.StateSync.UxrSyncEventArgs> nested=(sender,args)=> {
                nestedSeen=UltimateXR.Core.UxrManager.Instance.IsInsideStateSync;
                observer.OnDeserialize(new Mirror.NetworkReader(writer.ToArray()),true);
                nestedDeferred=!observer.HasValidAttachment&&copyGrip.enabled&&manager.IsBeingGrabbed(copyGrip);
            };
            copyGrip.StateChanged+=nested;
            try {
                var outer=new UltimateXR.Core.StateSync.UxrPropertyChangedSyncEventArgs("IsGrabbable",false);
                var outerResult=UltimateXR.Core.UxrManager.Instance.ExecuteStateSyncEvent(outer.SerializeEventBinary(copyGrip));
                check(prefix+"nested-receive-deferred",!outerResult.IsError&&nestedSeen&&nestedDeferred&&!UltimateXR.Core.UxrManager.Instance.IsInsideStateSync);
                observer.SendMessage("LateUpdate");
            }finally{copyGrip.StateChanged-=nested;UltimateXR.Core.UxrManager.ComponentStateChanged-=observed;}
            check(prefix+"prediction-rollback",observer.IsActive&&observer.HasValidAttachment&&!manager.IsBeingGrabbed(copy.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>())&&outgoing==0);
            check(prefix+"client-write-denied",!observer.TryDrop(UnityEngine.Vector3.zero,UnityEngine.Vector3.zero,out reason)&&observer.Epoch==binding.Epoch);
            var snapshot=typeof(VrBattlegrounds.Weapons.WeaponEquipmentBinding).GetField("_snapshot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var unresolved=(VrBattlegrounds.Weapons.WeaponEquipmentBinding.EquipmentSnapshot)snapshot.GetValue(observer);
            unresolved.ownerNetId=uint.MaxValue; unresolved.epoch++;
            snapshot.SetValue(observer,unresolved); observer.SendMessage("LateUpdate");
            check(prefix+"unresolved-owner-claim",observer.IsActive&&observer.Owner==null&&!observer.OwnerAlive&&!observer.HasValidAttachment&&!copy.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>().IsGrabbable);
            check(prefix+"transfer",binding.TryTransfer(avatar,UnityEngine.Vector3.zero,UnityEngine.Quaternion.identity,out reason)&&binding.Epoch==2&&binding.HasValidAttachment);
            check(prefix+"drop",binding.TryDrop(UnityEngine.Vector3.right,UnityEngine.Vector3.zero,out reason)&&!binding.IsActive&&binding.Epoch==3&&grip.IsGrabbable&&!item.GetComponent<UnityEngine.Rigidbody>().isKinematic);
            check(prefix+"transport-authority-preserved",transports.Length>0&&transports.Select(t=>t.syncDirection).SequenceEqual(directions));
            manager.GrabObject(hand,grip,0,false);
            check(prefix+"held-attach-denied",!binding.TryAttach(avatar,UnityEngine.Vector3.zero,UnityEngine.Quaternion.identity,out reason)&&binding.Epoch==3);
            manager.ReleaseGrabs(grip,false);
            if(id=="AK105") {
                bool attached=binding.TryAttach(avatar,UnityEngine.Vector3.zero,UnityEngine.Quaternion.identity,out reason);
                Mirror.NetworkServer.Destroy(player.gameObject);
                binding.SendMessage("LateUpdate");
                check(prefix+"owner-destroy-revokes",attached&&!binding.IsActive&&binding.Epoch==5);
            }
        }
    }
}catch(System.Exception error){results.Add(new{name="fixture",passed=false,error=error.ToString()});}
finally {
    foreach(var item in objects)if(item!=null){
        foreach(var grip in item.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabbableObject>(true))UltimateXR.Manipulation.UxrGrabManager.Instance.ReleaseGrabs(grip,false);
        if(item.GetComponent<Mirror.NetworkIdentity>().netId!=0)Mirror.NetworkServer.Destroy(item);else UnityEngine.Object.Destroy(item);
    }
    foreach(var actor in actors)if(actor!=null){
        if(actor.ActiveAvatar!=null){VrBattlegrounds.Player.Avatars.AvatarTeardown.ConfiscateBeforeDestroy(actor.ActiveAvatar,"equipment probe");Mirror.NetworkServer.Destroy(actor.ActiveAvatar.gameObject);}
        VrBattlegrounds.Managers.PlayersManager.Instance.UnregisterBot(actor);
    }
}
System.IO.File.WriteAllText(root+"/binding.json",Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
return new{report=root+"/binding.json",checks=results.Count};
