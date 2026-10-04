using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Ручная разметка физической арены и явное подтверждение защит на карте.</summary>
    [InitializeOnLoad]
    public static class PhysicalArenaPanel
    {
        public sealed class Proposal
        {
            public PhysicalArenaDefinition arena;
            public PhysicalObstacleMarker marker;
            public PhysicalObstacleProtection previous;
            public Bounds physical, required;
            public string fingerprint, reason;
            public GameObject replacement;
            public string definitionId;
            public CoverClass material=CoverClass.Hard;
            public BlockoutOpeningSettings openings=BlockoutOpeningSettings.Default;
        }
        private static readonly List<Proposal> proposals=new List<Proposal>();
        private static readonly Dictionary<string,GameObject> choices=new Dictionary<string,GameObject>();
        private static readonly Dictionary<string,string> definitionChoices=new Dictionary<string,string>();
        private static readonly Dictionary<string,CoverClass> materialChoices=new Dictionary<string,CoverClass>();
        private static BlockoutBlockRegistry registry;
        private static BlockoutBlockDefinition[] protectionDefinitions;
        private static string registrySignature;
        private static string status="";
        private static bool expanded=true;
        private sealed class SceneCache
        {
            public PhysicalArenaDefinition arena, builtOwner;
            public bool hasDefinitions, invalidReserves;
            public List<Proposal> built;
            public Dictionary<int,Proposal> byMarker;
            public readonly Dictionary<PlacementKey,string> geometryReasons=new Dictionary<PlacementKey,string>();
            public Reserve[] reserves=Array.Empty<Reserve>();
        }
        private readonly struct PlacementKey : IEquatable<PlacementKey>
        {
            private readonly int marker, previous;
            private readonly Bounds bounds;
            public PlacementKey(Proposal p,Bounds bounds){marker=p.marker.GetInstanceID();previous=p.previous!=null?p.previous.GetInstanceID():0;this.bounds=bounds;}
            public bool Equals(PlacementKey other)=>marker==other.marker&&previous==other.previous&&bounds==other.bounds;
            public override bool Equals(object obj)=>obj is PlacementKey other&&Equals(other);
            public override int GetHashCode()=>marker^previous^bounds.GetHashCode();
        }
        private readonly struct Reserve
        {
            public readonly Bounds bounds;
            public readonly string markerId;
            public readonly PhysicalObstacleMarker marker;
            public Reserve(Proposal p){bounds=p.required;markerId=p.marker.markerId;marker=p.marker;}
        }
        private readonly struct Drawing
        {
            public readonly Bounds bounds;
            public readonly Color color;
            public readonly string label;
            public Drawing(Bounds bounds,Color color,string label){this.bounds=bounds;this.color=color;this.label=label;}
        }
        private static readonly Dictionary<int,SceneCache> sceneCaches=new Dictionary<int,SceneCache>();
        private static Drawing[] drawing=Array.Empty<Drawing>();
        private static int drawingScene;
        private static bool drawingDirty=true;
        private const int MaxPreviewBoxes=256;
        private static float ProtectionHeight=>LevelDesignRules.CoverClasses.Max(c=>c.Max);
        static PhysicalArenaPanel()
        {
            BlockoutPainter.ArenaPanelRequested+=Draw;
            BlockoutPainter.ArenaFloorProvider=scene=>BlockoutGrid.TryFloor(scene,out var floor)?floor:(Bounds?)null;
            BlockoutPainter.ArenaIdProvider=scene=>Find(scene)!=null?Find(scene).arenaId:string.Empty;
            BlockoutWallEditing.PhysicalReserveConflict=Conflicts;
            BlockoutWallEditing.PhysicalReserveReason=ExplainConflict;
            BlockoutPainter.ProtectedGeometryProvider=go=>go!=null&&go.GetComponent<PhysicalObstacleProtection>()!=null;
            SceneView.duringSceneGui+=DrawPreview;
            EditorApplication.hierarchyChanged+=InvalidateAll;
            EditorApplication.projectChanged+=InvalidateAll;
            Undo.undoRedoPerformed+=InvalidateAll;
            ObjectChangeEvents.changesPublished+=ObjectsChanged;
            EditorSceneManager.sceneOpened+=(_,__)=>InvalidateAll();
            EditorSceneManager.sceneClosed+=_=>InvalidateAll();
            EditorSceneManager.activeSceneChangedInEditMode+=(_,__)=>InvalidateAll();
            EditorApplication.update+=RefreshDrawing;
        }
        private static void ObjectsChanged(ref ObjectChangeEventStream stream)=>InvalidateAll();
        private static void InvalidateAll(){sceneCaches.Clear();ResetRegistryCache();drawingDirty=true;}
        /// <summary>Вызывать после прямой записи полей из editor-кода; Inspector/Undo события обрабатываются автоматически.</summary>
        public static void Invalidate(Scene scene){sceneCaches.Remove(scene.handle);ResetRegistryCache();drawingDirty=true;}
        private static void ResetRegistryCache(){registry=null;protectionDefinitions=null;registrySignature=null;}
        private static SceneCache Cached(Scene scene)
        {
            if(!scene.IsValid()||!scene.isLoaded)return null;
            if(sceneCaches.TryGetValue(scene.handle,out var cached))return cached;
            var definitions=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PhysicalArenaDefinition>(true)).ToArray();
            var active=definitions.Where(a=>a.gameObject.activeInHierarchy).ToArray();
            cached=new SceneCache{arena=active.Length==1?active[0]:null,hasDefinitions=definitions.Length>0};
            sceneCaches.Add(scene.handle,cached);return cached;
        }
        public static PhysicalArenaDefinition Find(Scene scene)
        {
            return Cached(scene)?.arena;
        }
        public static bool HasDefinitions(Scene scene)=>Cached(scene)?.hasDefinitions??false;
        private static bool Writable(Scene scene)=>scene.IsValid()&&scene.isLoaded&&!EditorApplication.isPlayingOrWillChangePlaymode
            &&PrefabStageUtility.GetCurrentPrefabStage()==null;
        public static void Draw()
        {
            expanded=EditorGUILayout.Foldout(expanded,"Физическая арена",true);
            if(!expanded)return;
            var scene=SceneManager.GetActiveScene(); var arena=Find(scene);
            EditorGUILayout.HelpBox("На месте физического столба создаётся неперешагиваемое игровое препятствие Tall высотой 2.5 м. Материал Hard/Soft выбирается отдельно. Основание охватывает резерв по X/Z; высота исходного столба не меняется.",MessageType.Info);
            if(arena==null)
            {
                EditorGUILayout.HelpBox("Нужен ровно один PhysicalArenaDefinition. Выберите корень физической арены; ничего не перемещается автоматически.",MessageType.Warning);
                using(new EditorGUI.DisabledScope(HasDefinitions(scene)||!Writable(scene)||Selection.activeGameObject==null||Selection.activeGameObject.scene!=scene))
                    if(GUILayout.Button("Настроить выбранный корень как арену"))
                    {
                        var root=Selection.activeGameObject;
                        var definition=Undo.AddComponent<PhysicalArenaDefinition>(root);
                        definition.floorShape=root.GetComponentsInChildren<PhysicalArenaShape>(true).OrderByDescending(s=>s.size.x*s.size.z).FirstOrDefault();
                        EditorUtility.SetDirty(definition);Invalidate(scene); status="Источник создан. Укажите floorShape и gridOrigin явно; затем подтвердите защиту. В разметку не добавлять Collider.";
                    }
                return;
            }
            DrawSerialized(arena);
            if(!arena.Valid(out var inputReason))EditorGUILayout.HelpBox(inputReason,MessageType.Error);
            using(new EditorGUI.DisabledScope(!Writable(scene)))
            {
                if(GUILayout.Button("Добавить препятствие по координатам"))
                {
                    var go=new GameObject("Физическое препятствие"); Undo.RegisterCreatedObjectUndo(go,"Добавить маркер");
                    Undo.SetTransformParent(go.transform,arena.transform,"Маркер в арене");
                    var marker=Undo.AddComponent<PhysicalObstacleMarker>(go); marker.center=new Vector3(0,1.25f,0); Selection.activeGameObject=go;Invalidate(scene);
                }
                using(new EditorGUI.DisabledScope(Selection.activeGameObject==null||!Selection.activeGameObject.transform.IsChildOf(arena.transform)||Selection.activeGameObject==arena.gameObject))
                    if(GUILayout.Button("Отметить выбранные формы разметки"))
                    {
                        var go=Selection.activeGameObject;
                        var shapes=go.GetComponentsInChildren<PhysicalArenaShape>(true).Where(s=>s!=arena.floorShape).ToArray();
                        var sources=go.GetComponentsInChildren<Collider>(true).Where(c=>!BlockoutSupportSurfaces.IsSupportSurface(scene,c)&&!c.isTrigger).ToArray();
                        if(shapes.Length==0 && (go.GetComponentInParent<PhysicalArenaLayout>(true)!=null || sources.Length==0))
                        {status="Добавьте PhysicalArenaShape с размерами препятствия; Collider разметке не нужен.";return;}
                        var marker=go.GetComponent<PhysicalObstacleMarker>()??Undo.AddComponent<PhysicalObstacleMarker>(go);
                        Undo.RecordObject(marker,"Источник физического препятствия"); marker.useColliders=shapes.Length==0;
                        marker.sourceShapes=shapes;marker.sourceColliders=shapes.Length>0?Array.Empty<Collider>():sources;
                        EditorUtility.SetDirty(marker);Invalidate(scene);
                    }
                foreach(var marker in arena.GetComponentsInChildren<PhysicalObstacleMarker>(true))
                {
                    EditorGUILayout.LabelField(marker.name,EditorStyles.boldLabel); DrawSerialized(marker);
                    var accepted=Latest(arena,marker);
                    if(accepted!=null&&accepted.previous!=null&&accepted.previous.confirmedFingerprint!=accepted.fingerprint)
                        EditorGUILayout.HelpBox("Подтверждение защиты устарело; необходимо новое предложение.",MessageType.Warning);
                    var selectedGuard=Selection.activeGameObject;
                    using(new EditorGUI.DisabledScope(selectedGuard==null||selectedGuard.scene!=scene||selectedGuard.transform.IsChildOf(arena.transform)||selectedGuard.GetComponentsInChildren<Collider>(true).Length==0))
                        if(GUILayout.Button("Связать выбранную прежнюю защиту с этим маркером"))
                        {
                            var guard=selectedGuard.GetComponent<PhysicalObstacleProtection>()??Undo.AddComponent<PhysicalObstacleProtection>(selectedGuard);
                            Undo.RecordObject(guard,"Связать прежнюю защиту");guard.arenaId=arena.arenaId;guard.markerId=marker.markerId;
                            guard.protectedVolume=ActualBounds(selectedGuard);guard.confirmedFingerprint="";EditorUtility.SetDirty(guard);Invalidate(scene);
                            status="Прежний объём сохранён. Связь не подтверждает безопасность — предложите защиту заново.";
                        }
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        if(GUILayout.Button("Выделить"))Selection.activeGameObject=marker.gameObject;
                        if(GUILayout.Button("Новый ID")){Undo.RecordObject(marker,"Обновить ID маркера");marker.markerId=Guid.NewGuid().ToString("N");EditorUtility.SetDirty(marker);Invalidate(scene);}
                        if(GUILayout.Button("Убрать маркер")){Undo.DestroyObjectImmediate(marker);Invalidate(scene);}
                    }
                }
                if(GUILayout.Button("Предложить защиту")){Invalidate(scene);proposals.Clear();proposals.AddRange(Build(arena));drawingDirty=true;SceneView.RepaintAll();}
                if(proposals.Count>0&&GUILayout.Button("Подтвердить все подходящие"))foreach(var p in proposals.ToArray())Apply(p,out status);
                foreach(var p in proposals.Where(p=>p.marker!=null&&p.arena==arena))
                {
                    EditorGUILayout.LabelField(p.marker.name+" → "+p.required.size.ToString("F2")+" м");
                    EditorGUILayout.LabelField("Физический источник",p.physical.size.ToString("F2")+" м");
                    var key=Key(p.arena,p.marker);
                    var definitions=ProtectionDefinitions();
                    var selected=ResolveDefinition(p,out _);
                    int selectedIndex=string.IsNullOrEmpty(p.definitionId)&&p.replacement==null?0:selected!=null?Array.FindIndex(definitions,d=>d==selected)+1:-1;
                    var labels=new[]{"Автоматическая Tall"}.Concat(definitions.Select(d=>d.title+" · Tall")).ToArray();
                    int chosenIndex=EditorGUILayout.Popup("Форма защиты из реестра",selectedIndex,labels);
                    if(chosenIndex!=selectedIndex)
                    {
                        p.definitionId=chosenIndex==0?"":definitions[chosenIndex-1].shapeId;
                        definitionChoices[key]=p.definitionId;p.replacement=null;choices.Remove(key);drawingDirty=true;
                    }
                    int materialIndex=p.material==CoverClass.Soft?1:0;
                    int chosenMaterial=EditorGUILayout.Popup("Материал препятствия",materialIndex,new[]{"Hard","Soft"});
                    if(chosenMaterial!=materialIndex)
                    {
                        p.material=chosenMaterial==1?CoverClass.Soft:CoverClass.Hard;
                        materialChoices[key]=p.material;drawingDirty=true;
                    }
                    if(p.openings.enabled)EditorGUILayout.LabelField("Щели прежнего препятствия",p.openings.width.ToString("F2")+" м · сохранены");
                    if(registry==null)EditorGUILayout.HelpBox("Создайте/обновите общий реестр во вкладке «Блоки». Без реестра защита не применяется.",MessageType.Warning);
                    string reason=p.reason;
                    if(reason.Length==0&&!Current(p))reason="Устарело: изменены маркер / grid / policy / прежняя защита. Предложите заново.";
                    if(reason.Length==0)
                    {
                        if(!ReplacementBounds(p,out var candidate,out var replaceReason))reason=replaceReason;
                        else reason=GeometryReason(p,candidate,Build(p.arena));
                    }
                    if(reason.Length>0)EditorGUILayout.HelpBox(reason,MessageType.Warning);
                    else EditorGUILayout.HelpBox("Добавить/обновить одно неперешагиваемое игровое препятствие Tall по marker ID. Основание может закрыть больше пространства, чем физический объект.",MessageType.Info);
                    using(new EditorGUI.DisabledScope(reason.Length>0))if(GUILayout.Button("Подтвердить это препятствие"))Apply(p,out status);
                }
            }
            if(status.Length>0)EditorGUILayout.HelpBox(status,MessageType.Info);
        }
        private static void DrawSerialized(UnityEngine.Object target)
        {
            var so=new SerializedObject(target);so.Update();var iterator=so.GetIterator();bool enter=true;
            while(iterator.NextVisible(enter)){enter=false;if(iterator.name!="m_Script")EditorGUILayout.PropertyField(iterator,true);}
            if(so.ApplyModifiedProperties()&&target is Component component)Invalidate(component.gameObject.scene);
        }
        private static string Key(PhysicalArenaDefinition arena,PhysicalObstacleMarker marker)=>arena.GetInstanceID()+":"+marker.markerId;
        public static List<Proposal> Build(PhysicalArenaDefinition arena)
        {
            if(arena==null)return new List<Proposal>();
            var cache=Cached(arena.gameObject.scene);
            if(cache==null)return new List<Proposal>();
            EnsureBuilt(cache,arena);
            // Изменяемые предложения UI отделены от кэшированного источника резервов.
            return cache.built.Select(p=>new Proposal{arena=p.arena,marker=p.marker,previous=p.previous,physical=p.physical,required=p.required,
                fingerprint=p.fingerprint,reason=p.reason,definitionId=definitionChoices.TryGetValue(Key(arena,p.marker),out var selected)?selected:p.definitionId,
                material=materialChoices.TryGetValue(Key(arena,p.marker),out var material)?material:p.material,openings=p.openings,
                replacement=definitionChoices.ContainsKey(Key(arena,p.marker))?null:choices.TryGetValue(Key(arena,p.marker),out var choice)?choice:p.replacement}).ToList();
        }
        private static void EnsureBuilt(SceneCache cache,PhysicalArenaDefinition arena)
        {
            if(cache.built!=null&&cache.builtOwner==arena)return;
            cache.builtOwner=arena;cache.built=BuildUncached(arena);cache.geometryReasons.Clear();
            cache.byMarker=cache.built.ToDictionary(p=>p.marker.GetInstanceID());
            cache.invalidReserves=cache.built.Any(p=>p.reason.Length>0);
            cache.reserves=cache.built.Where(p=>p.reason.Length==0).Select(p=>new Reserve(p)).ToArray();
        }
        private static List<Proposal> BuildUncached(PhysicalArenaDefinition arena)
        {
            var result=new List<Proposal>();
            if(arena==null)return result;
            Physics.SyncTransforms();
            var markers=arena.GetComponentsInChildren<PhysicalObstacleMarker>(true);
            var guards=arena.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PhysicalObstacleProtection>(true)).ToArray();
            foreach(var marker in markers)
            {
                var p=new Proposal{arena=arena,marker=marker,reason=""};result.Add(p);
                if(!arena.Valid(out p.reason))continue;
                if(markers.Count(m=>m.markerId==marker.markerId)!=1){p.reason="ID маркера повторяется; назначьте новый ID копии.";continue;}
                if(!marker.TryBounds(arena,out var physical,out p.reason))continue;
                p.physical=physical;
                var matching=guards.Where(g=>g.arenaId==arena.arenaId&&g.markerId==marker.markerId).ToArray();
                if(matching.Length>1){p.reason="Несколько защит с одним ID; исправьте связь вручную.";continue;}
                p.previous=matching.FirstOrDefault();
                p.material=PreviousMaterial(p.previous);
                var previousOpenings=p.previous!=null?p.previous.GetComponent<BlockoutBlockInstance>()?.openings??p.previous.GetComponent<BlockoutCellWall>()?.openings??BlockoutOpeningSettings.Default:BlockoutOpeningSettings.Default;
                // Узкие щели не дают пройти игроку и не обязаны закрывать обзор или останавливать пулю.
                if(MovementSafeOpenings(previousOpenings))p.openings=previousOpenings;
                var required=physical;required.Expand(arena.safetyMargin*2);
                if(p.previous!=null){required.Encapsulate(p.previous.protectedVolume);required.Encapsulate(ActualBounds(p.previous.gameObject));}
                // Tall — стандартное игровое препятствие на месте столба, а не проверка его высоты.
                // Размеры и положение основания прежнего guard сохраняем независимо от исходной высоты.
                if (!BlockoutGrid.TryFloor(arena.gameObject.scene, out var mapFloor)) { p.reason="Нет игрового пола карты."; continue; }
                var min=required.min;var max=required.max;min.y=mapFloor.max.y;max.y=min.y+ProtectionHeight;required.SetMinMax(min,max);
                p.required=Outward(required,arena.WorldOrigin,arena.gridStep);
                if(p.required.size.x/arena.gridStep*p.required.size.z/arena.gridStep>10000){p.reason="Оболочка превышает бюджет 10000 клеток.";continue;}
                p.fingerprint=Fingerprint(p,physical);
                p.replacement=choices.TryGetValue(Key(arena,marker),out var choice)?choice:p.previous!=null?p.previous.chosenReplacement:null;
                p.definitionId=p.previous!=null?p.previous.chosenDefinitionId:"";
                if(!WithinFloor(p.required,arena.FloorBounds))p.reason="Требуемая защита выходит за физическую площадку.";
            }
            return result;
        }
        public static Bounds Outward(Bounds volume,Vector2 origin,float step)
        {
            var min=volume.min;var max=volume.max;
            min.x=origin.x+Mathf.Floor((min.x-origin.x+.00001f)/step)*step;min.z=origin.y+Mathf.Floor((min.z-origin.y+.00001f)/step)*step;
            max.x=origin.x+Mathf.Ceil((max.x-origin.x-.00001f)/step)*step;max.z=origin.y+Mathf.Ceil((max.z-origin.y-.00001f)/step)*step;
            var result=new Bounds();result.SetMinMax(min,max);return result;
        }
        private static Bounds ActualBounds(GameObject go)
        {
            bool first=true;Bounds result=default;
            foreach(var collider in go.GetComponentsInChildren<Collider>(true))
            {if(!BlockoutSupportSurfaces.IsActiveSolid(collider))continue;if(first){result=collider.bounds;first=false;}else result.Encapsulate(collider.bounds);}
            foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))
            {if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;if(first){result=renderer.bounds;first=false;}else result.Encapsulate(renderer.bounds);}
            return first?new Bounds(go.transform.position,Vector3.zero):result;
        }
        private static CoverClass PreviousMaterial(PhysicalObstacleProtection previous)
        {
            if(previous==null)return CoverClass.Hard;
            var instance=previous.GetComponent<BlockoutBlockInstance>();
            var material=instance!=null?instance.material:previous.GetComponent<CoverSurface>()?.Class??CoverClass.Hard;
            return material==CoverClass.Soft?CoverClass.Soft:CoverClass.Hard;
        }
        private static bool MovementSafeOpenings(BlockoutOpeningSettings openings)=>!openings.enabled
            ||PhysicalArenaDefinition.Finite(openings.width)&&openings.width>0&&openings.width<LevelDesignRules.SqueezeWidth-.0001f;
        private static string Fingerprint(Proposal p,Bounds physical)
        {
            // InstanceID меняется при открытии сцены; для источников используем устойчивый GlobalObjectId.
            var marker=p.marker;var arena=p.arena;
            string sources=string.Join(";",(marker.sourceColliders??Array.Empty<Collider>()).Select(c=>c==null?"missing":GlobalObjectId.GetGlobalObjectIdSlow(c)+":"+c.GetType().FullName));
            string guardGeometry="";
            if(p.previous!=null)
            {
                var go=p.previous.gameObject;var wall=go.GetComponent<BlockoutCellWall>();
                guardGeometry=go.transform.position.ToString("R")+go.transform.rotation.ToString("R")+go.transform.lossyScale.ToString("R")+ActualBounds(go).ToString("R")
                    +(wall!=null?JsonUtility.ToJson(wall):string.Join(";",go.GetComponentsInChildren<Collider>(true).Select(c=>c.GetType().FullName+":"+c.bounds.ToString("R")+":"+c.enabled+":"+c.isTrigger)))
                    +"|"+(go.GetComponent<CoverSurface>()?.Class??CoverClass.Hard)+"|"+(go.GetComponent<BlockoutBlockInstance>()?.material??CoverClass.Hard)
                    +"|"+JsonUtility.ToJson(go.GetComponent<BlockoutBlockInstance>()?.openings??BlockoutOpeningSettings.Default)
                    +"|"+JsonUtility.ToJson(go.GetComponent<BlockoutBlockInstance>());
            }
            return Hash128.Compute(arena.arenaId+"|"+marker.markerId+"|"+arena.WorldOrigin.ToString("R")+"|"+arena.gridStep.ToString("R",System.Globalization.CultureInfo.InvariantCulture)
                +"|"+arena.safetyMargin.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+arena.FloorBounds.ToString("R")+"|"+marker.useColliders
                +"|"+marker.center.ToString("R")+"|"+marker.size.ToString("R")+"|"+marker.yaw.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+sources+"|"+physical.ToString("R")+"|"+p.required.ToString("R")+"|"+guardGeometry+"|"+RegistrySignature()).ToString();
        }
        private static bool Current(Proposal p)
        {
            if(p.arena==null||p.marker==null)return false;
            var current=Latest(p.arena,p.marker);
            return current!=null&&current.reason.Length==0&&current.fingerprint==p.fingerprint;
        }
        private static Proposal Latest(PhysicalArenaDefinition arena,PhysicalObstacleMarker marker)
        {
            var cache=Cached(arena.gameObject.scene);if(cache==null)return null;
            EnsureBuilt(cache,arena);return cache.byMarker.TryGetValue(marker.GetInstanceID(),out var latest)?latest:null;
        }
        public static bool Overlap(Bounds a,Bounds b)=>Mathf.Min(a.max.x,b.max.x)-Mathf.Max(a.min.x,b.min.x)>.001f
            &&Mathf.Min(a.max.y,b.max.y)-Mathf.Max(a.min.y,b.min.y)>.001f&&Mathf.Min(a.max.z,b.max.z)-Mathf.Max(a.min.z,b.min.z)>.001f;
        private static bool WithinFloor(Bounds a,Bounds floor)=>a.min.x>=floor.min.x-.0001f&&a.max.x<=floor.max.x+.0001f&&a.min.z>=floor.min.z-.0001f&&a.max.z<=floor.max.z+.0001f;
        public static bool Conflicts(Scene scene,Bounds volume,GameObject ignore)
            => ExplainConflict(scene,volume,ignore)!=null;
        public static BlockoutWallEditing.ConflictInfo ExplainConflict(Scene scene,Bounds volume,GameObject ignore)
        {
            var cache=Cached(scene);if(cache==null)return null;
            var arena=cache.arena;if(arena==null)return cache.hasDefinitions?new BlockoutWallEditing.ConflictInfo {message="Физическая арена не настроена: невозможно проверить резерв препятствия."}:null;
            var protection=ignore!=null?ignore.GetComponent<PhysicalObstacleProtection>():null;
            EnsureBuilt(cache,arena);
            if(cache.invalidReserves)
            {
                var invalid=cache.built.FirstOrDefault(p=>!string.IsNullOrEmpty(p.reason));
                return new BlockoutWallEditing.ConflictInfo {message="Невозможно проверить резерв физического препятствия: "+(invalid?.reason??"не настроен источник"),target=invalid?.marker};
            }
            foreach(var reserve in cache.reserves)
            {
                if(protection!=null&&protection.arenaId==arena.arenaId&&protection.markerId==reserve.markerId)continue;
                if(Overlap(volume,reserve.bounds))return new BlockoutWallEditing.ConflictInfo
                {message=$"Занимает зарезервированную область препятствия «{reserve.marker?.name}» (маркер {reserve.markerId}).",target=reserve.marker};
            }
            return null;
        }
        private static BlockoutBlockDefinition[] ProtectionDefinitions()
        {
            if(protectionDefinitions!=null)return protectionDefinitions;
            registry=BlockoutRegistryFactory.Current;
            if(registry==null){registrySignature="registry:missing";return protectionDefinitions=Array.Empty<BlockoutBlockDefinition>();}
            var allowed=new List<BlockoutBlockDefinition>();var signature=new List<string>{"registry-movement-v4",ProtectionHeight.ToString("R",System.Globalization.CultureInfo.InvariantCulture)};
            foreach(var definition in registry.Definitions)
            {
                if(definition==null||definition.geometryPrefab==null){signature.Add("missing-definition");continue;}
                Vector3 size;
                try{size=BlockoutRegistryFactory.DefaultDimensions(definition);}catch{signature.Add(definition.shapeId+":invalid-dimensions");continue;}
                // Начальная высота кисти не ограничивает защиту: паспорт может разрешать её изменение.
                bool tall=definition.heightEditable?definition.minDimensions.y<=ProtectionHeight+.0001f&&definition.maxDimensions.y>=ProtectionHeight-.0001f
                    :Mathf.Abs(size.y-ProtectionHeight)<=.0001f;
                bool eligible=definition.gameplayGeometry&&(definition.Allows(CoverClass.Hard)||definition.Allows(CoverClass.Soft))&&tall
                    &&definition.geometryPrefab.GetComponentInChildren<VaultableObstacle>(true)==null&&(definition.supportsCellWall||RootBox(definition));
                if(eligible)allowed.Add(definition);
                signature.Add(definition.shapeId+":"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition.geometryPrefab))+":"
                    +size.ToString("R")+":"+definition.minDimensions.ToString("R")+":"+definition.maxDimensions.ToString("R")+":"
                    +definition.editableDimensions+":"+definition.heightEditable+":"+definition.supportsCellWall+":"+definition.gameplayGeometry+":"
                    +definition.Allows(CoverClass.Hard)+":"+definition.Allows(CoverClass.Soft)+":"+eligible);
            }
            registrySignature=Hash128.Compute(string.Join("|",signature)).ToString();return protectionDefinitions=allowed.ToArray();
        }
        private static string RegistrySignature(){ProtectionDefinitions();return registrySignature;}
        private static bool RootBox(BlockoutBlockDefinition definition)
        {
            var root=definition.geometryPrefab;var colliders=root.GetComponentsInChildren<Collider>(true);
            if(colliders.Length!=1||!(colliders[0] is BoxCollider box)||!box.enabled||box.isTrigger||box.transform!=root.transform
                ||Quaternion.Angle(root.transform.rotation,Quaternion.identity)>.001f||(root.transform.localScale-Vector3.one).sqrMagnitude>.000001f)return false;
            var physical=new Bounds(box.center,box.size);var visual=BlockoutRegistryFactory.GeometryBounds(root);
            // Сравниваем исходную коллизию с исходным мешем, а не с высотой нового экземпляра.
            return (physical.min-visual.min).sqrMagnitude<.000001f&&(physical.max-visual.max).sqrMagnitude<.000001f;
        }
        private static BlockoutBlockDefinition ResolveDefinition(Proposal p,out string reason)
        {
            reason="";var definitions=ProtectionDefinitions();
            if(registry==null){reason="Общий реестр блоков отсутствует.";return null;}
            BlockoutBlockDefinition definition=null;
            bool explicitLegacy=p.replacement!=null&&(string.IsNullOrEmpty(p.definitionId)||p.previous==null||p.replacement!=p.previous.chosenReplacement);
            if(explicitLegacy)
            {
                if(!BlockoutRegistryFactory.TryDefinition(p.replacement,out definition)){reason="Прежний выбор не найден в общем реестре; выберите форму защиты заново.";return null;}
            }
            else if(!string.IsNullOrEmpty(p.definitionId))definition=registry.Definitions.FirstOrDefault(d=>d!=null&&d.shapeId==p.definitionId);
            else
            {
                // Выбор зависит от полного резерва и возможностей паспорта; имя и исходная высота не являются контрактом.
                float smallest=float.PositiveInfinity;
                foreach(var candidate in definitions)
                    if(TryReplacementGeometry(p,candidate,out _,out var volume,out _))
                    {
                        float occupied=volume.size.x*volume.size.y*volume.size.z;
                        if(occupied<smallest){smallest=occupied;definition=candidate;}
                    }
                if(definition==null){reason="В реестре нет подходящей неперешагиваемой формы с проверяемым основанием по X/Z и выбранным материалом.";return null;}
            }
            if(definition==null||!definitions.Contains(definition)){reason="Для этой формы проверка непроходимого основания не поддерживается. Выберите прямоугольную форму с игровой коллизией из активного реестра.";return null;}
            return definition;
        }
        private static bool TryReplacementGeometry(Proposal p,BlockoutBlockDefinition definition,out Vector3 dimensions,out Bounds volume,out string reason)
        {
            dimensions=BlockoutRegistryFactory.DefaultDimensions(definition);volume=p.required;
            if(definition.supportsCellWall&&definition.editableDimensions)
            {
                dimensions.x=Mathf.Max(p.required.size.x,definition.minDimensions.x);
                dimensions.z=Mathf.Max(p.required.size.z,definition.minDimensions.z);
            }
            if(definition.heightEditable)dimensions.y=ProtectionHeight;
            if(!BlockoutRegistryFactory.ValidatePlacement(definition,dimensions,p.material,p.openings,0,out reason,p.arena.gridStep))return false;
            if(!MovementSafeOpenings(p.openings)){reason="Проходимость широкого проёма не подтверждена: щель должна быть уже "+LevelDesignRules.SqueezeWidth.ToString("F2")+" м.";return false;}
            if(Mathf.Abs(dimensions.x/p.arena.gridStep-Mathf.Round(dimensions.x/p.arena.gridStep))>.001f||Mathf.Abs(dimensions.z/p.arena.gridStep-Mathf.Round(dimensions.z/p.arena.gridStep))>.001f)
            {reason="Размеры формы защиты некратны сетке арены.";return false;}
            volume=Envelope(BlockoutRegistryFactory.PreviewVolumes(definition,p.required.min,0,dimensions,p.openings,p.arena.gridStep));
            if(!ContainsFootprint(volume,p.required)){reason="Основание формы из реестра не охватывает резерв по X/Z.";return false;}
            reason="";return true;
        }
        private static bool TryReplacement(Proposal p,out BlockoutBlockDefinition definition,out Vector3 dimensions,out Bounds volume,out string reason)
        {
            dimensions=default;volume=p.required;definition=ResolveDefinition(p,out reason);
            return definition!=null&&TryReplacementGeometry(p,definition,out dimensions,out volume,out reason);
        }
        private static Bounds Envelope(IEnumerable<Bounds> volumes)
        {
            bool first=true;Bounds result=default;
            foreach(var volume in volumes){if(first){result=volume;first=false;}else result.Encapsulate(volume);}
            return result;
        }
        private static bool ContainsFootprint(Bounds outer,Bounds inner)=>outer.min.x<=inner.min.x+.0001f&&outer.min.z<=inner.min.z+.0001f
            &&outer.max.x>=inner.max.x-.0001f&&outer.max.z>=inner.max.z-.0001f;
        private static bool ContainsLocalFootprint(Bounds local,Matrix4x4 worldToLocal,Bounds required,float referenceY)
        {
            // AABB повёрнутого box может охватить резерв, хотя углы его основания находятся вне коллизии.
            // Y берём от самого игрового препятствия: высота исходного столба в этой проверке не участвует.
            for(int x=0;x<2;x++)for(int z=0;z<2;z++)
            {
                var point=worldToLocal.MultiplyPoint3x4(new Vector3(x==0?required.min.x:required.max.x,referenceY,z==0?required.min.z:required.max.z));
                if(point.x<local.min.x-.0001f||point.x>local.max.x+.0001f||point.z<local.min.z-.0001f||point.z>local.max.z+.0001f)return false;
            }
            return true;
        }
        private static bool Protects(GameObject go,Bounds required)
        {
            var instance=go.GetComponent<BlockoutBlockInstance>();if(instance==null||instance.definition==null||instance.VaultSuitable
                ||go.GetComponentInChildren<VaultableObstacle>(true)!=null||!PhysicalArenaDefinition.Finite(instance.dimensions.y)
                ||Mathf.Abs(instance.dimensions.y-ProtectionHeight)>.0001f||!MovementSafeOpenings(instance.openings))return false;
            var sections=go.GetComponent<BlockoutSectionGeometry>();bool sectional=sections!=null&&sections.Initialized;
            if(sectional)
            {
                if(!instance.HasSections||sections.parts==null||sections.parts.Length!=3)return false;
                for(int i=0;i<3;i++)
                {
                    var part=sections.parts[i];if(part==null||part.owner!=sections||part.sectionIndex!=i||!MovementSafeOpenings(instance.sections[i].openings))return false;
                    var childCollider=part.GetComponent<MeshCollider>();var childFilter=part.GetComponent<MeshFilter>();var surface=part.GetComponent<CoverSurface>();
                    if(!BlockoutSupportSurfaces.IsActiveSolid(childCollider)||childCollider.sharedMesh==null||childFilter==null||childFilter.sharedMesh!=childCollider.sharedMesh
                        ||surface==null||(surface.Class!=CoverClass.Hard&&surface.Class!=CoverClass.Soft))return false;
                }
            }
            if(go.TryGetComponent<BlockoutCellWall>(out var wall))
            {
                var collider=go.GetComponent<MeshCollider>();
                var filter=go.GetComponent<MeshFilter>();
                if(!MovementSafeOpenings(wall.openings)||(!sectional&&(collider==null||!collider.enabled||collider.isTrigger||collider.sharedMesh==null||filter==null||filter.sharedMesh!=collider.sharedMesh))
                    ||Quaternion.Angle(go.transform.rotation,Quaternion.identity)>.001f||(go.transform.lossyScale-Vector3.one).sqrMagnitude>.000001f
                    ||!ContainsFootprint(sectional?ActualBounds(go):collider.bounds,required))return false;
                if(wall.geometryMode==BlockoutGeometryMode.ThinStraight)
                {
                    // Узкие сквозные щели могут пропускать лучи, но не проход игрока. Основание
                    // прямой формы проверяется отдельно от баллистической сплошности её частей.
                    var local=new Bounds(new Vector3(wall.length/2,0,wall.thickness/2),new Vector3(wall.length,0,wall.thickness));
                    var matrix=go.transform.localToWorldMatrix*Matrix4x4.Translate(sectional?-sections.AnchorOffset:Vector3.zero)*Matrix4x4.Rotate(Quaternion.Euler(0,wall.yaw,0));
                    return wall.SolidParts().Any()&&ContainsLocalFootprint(local,matrix.inverse,required,go.transform.position.y);
                }
                if(wall.geometryMode!=BlockoutGeometryMode.LegacyCellUnion)return false;
                var cells=new HashSet<Vector2Int>(wall.cells);var origin=go.transform.position-(sectional?sections.AnchorOffset:Vector3.zero);float step=wall.cellSize;
                if(step<=0||!PhysicalArenaDefinition.Finite(step))return false;
                int x0=Mathf.FloorToInt((required.min.x-origin.x+.00001f)/step),x1=Mathf.CeilToInt((required.max.x-origin.x-.00001f)/step);
                int z0=Mathf.FloorToInt((required.min.z-origin.z+.00001f)/step),z1=Mathf.CeilToInt((required.max.z-origin.z-.00001f)/step);
                for(int x=x0;x<x1;x++)for(int z=z0;z<z1;z++)if(!cells.Contains(new Vector2Int(x,z)))return false;
                return true;
            }
            var box=go.GetComponent<BoxCollider>();return box!=null&&(sectional||box.enabled)&&!box.isTrigger
                &&(!sectional||ContainsFootprint(ActualBounds(go),required))
                &&ContainsLocalFootprint(new Bounds(box.center-(sectional?sections.AnchorOffset:Vector3.zero),box.size),box.transform.worldToLocalMatrix,required,box.transform.TransformPoint(box.center).y);
        }
        public static bool ReplacementBounds(Proposal p,out Bounds volume,out string reason)
        {
            return TryReplacement(p,out _,out _,out volume,out reason);
        }
        public static bool Apply(Proposal p,out string reason)
        {
            reason="";
            // Последняя проверка безопасности читает живой источник даже до публикации Unity change events.
            if(p!=null&&p.arena!=null)Invalidate(p.arena.gameObject.scene);
            if(p==null||p.arena==null||!Writable(p.arena.gameObject.scene)||Find(p.arena.gameObject.scene)!=p.arena||!Current(p)){reason="Устаревшее предложение, неоднозначная арена или недоступный редактор; предложите заново.";return false;}
            if(!TryReplacement(p,out var definition,out var dimensions,out var volume,out reason))return false;
            var scene=p.arena.gameObject.scene;
            reason=GeometryReason(p,volume,Build(p.arena));if(reason.Length>0)return false;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Подтвердить физическую защиту");
            var reserves=Build(p.arena);string refusal="";GameObject go;
            try
            {
                go=BlockoutRegistryFactory.Create(definition,scene,p.required.min,0,dimensions,p.material,p.openings,
                    placementConflict:(created,actual)=>
                    {
                        var envelope=Envelope(actual);
                        if(!Protects(created,p.required)){refusal="Игровая коллизия препятствия Tall не блокирует проход в резерве по X/Z либо разрешает перешагивание.";return true;}
                        refusal=GeometryReasonUncached(p,envelope,reserves,created);return refusal.Length>0;
                    },cellStep:p.arena.gridStep);
            }
            catch(Exception exception){reason=(refusal.Length>0?refusal:exception.Message)+" Прежняя защита сохранена.";return false;}
            var guard=Undo.AddComponent<PhysicalObstacleProtection>(go);Undo.RecordObject(guard,"Подтвердить паспорт защиты");
            guard.arenaId=p.arena.arenaId;guard.markerId=p.marker.markerId;guard.protectedVolume=ActualBounds(go);
            guard.chosenReplacement=p.replacement;guard.chosenDefinitionId=definition.shapeId;guard.confirmedFingerprint=p.fingerprint;
            if(p.previous!=null)Undo.DestroyObjectImmediate(p.previous.gameObject);
            Undo.CollapseUndoOperations(group);EditorSceneManager.MarkSceneDirty(scene);
            p.previous=guard; // Следующее предложение сверяет уже подтверждённый объём.
            Invalidate(scene);
            var updated=Build(p.arena).First(x=>x.marker==p.marker);guard.confirmedFingerprint=updated.fingerprint;p.fingerprint=updated.fingerprint;p.required=updated.required;
            reason="Защита подтверждена под Undo. Сцена не сохранена.";SceneView.RepaintAll();return true;
        }
        public static string GeometryReason(Proposal p,Bounds volume,IEnumerable<Proposal> reserves)
        {
            var cache=Cached(p.arena.gameObject.scene);if(cache==null)return "Недоступная сцена.";
            var key=new PlacementKey(p,volume);
            if(cache.geometryReasons.TryGetValue(key,out var reason))return reason;
            reason=GeometryReasonUncached(p,volume,reserves);
            if(cache.geometryReasons.Count>=1024)cache.geometryReasons.Clear();
            cache.geometryReasons.Add(key,reason);return reason;
        }
        private static string GeometryReasonUncached(Proposal p,Bounds volume,IEnumerable<Proposal> reserves,GameObject generated=null)
        {
            if(!WithinFloor(volume,p.arena.FloorBounds))return "Замена выходит за физическую площадку.";
            foreach(var other in reserves)
            {
                if(other.marker==p.marker)continue;
                if(other.reason.Length>0)return "Неверный резерв "+other.marker.name+": "+other.reason;
                if(Overlap(volume,other.required))return "Конфликт с резервом "+other.marker.name+"; прежняя защита сохранена.";
            }
            var physical=p.marker.useColliders?p.marker.sourceColliders:Array.Empty<Collider>();
            foreach(var c in p.arena.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>(false)))
            {
                if(!BlockoutSupportSurfaces.IsActiveSolid(c)||BlockoutSupportSurfaces.IsSupportSurface(p.arena.gameObject.scene,c)||physical.Contains(c)||(p.previous!=null&&c.transform.IsChildOf(p.previous.transform))||(generated!=null&&c.transform.IsChildOf(generated.transform)))continue;
                if(c.GetComponentInParent<PhysicalObstacleMarker>()==p.marker)continue;
                var wall=c.GetComponentInParent<BlockoutCellWall>();
                if(wall!=null)
                {
                    if(IntersectsWallParts(volume,wall))return "Пересечение стены; прежняя защита сохранена.";
                }
                else if(Overlap(volume,c.bounds))return "Пересечение с "+c.name+" (консервативный AABB); прежняя защита сохранена.";
            }
            return "";
        }
        private static bool IntersectsWallParts(Bounds volume,BlockoutCellWall wall)
            =>wall.SolidParts().Any(part=>volume.Intersects(part.BroadphaseBounds)
                &&BlockoutWallEditing.IntersectsPart(volume,part));
        private static void RefreshDrawing()
        {
            if(!drawingDirty||!expanded||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            var scene=SceneManager.GetActiveScene();drawingScene=scene.handle;
            if(proposals.Count==0){drawing=Array.Empty<Drawing>();drawingDirty=false;return;}
            var snapshots=new List<Drawing>();var arena=Find(scene);var current=arena!=null?Build(arena):new List<Proposal>();
            var byMarker=current.ToDictionary(p=>p.marker.GetInstanceID());
            foreach(var p in proposals.Where(p=>p.marker!=null&&p.arena!=null&&p.arena.gameObject.scene==scene).Take(MaxPreviewBoxes))
            {
                bool replacementValid=ReplacementBounds(p,out var volume,out _);
                bool fresh=byMarker.TryGetValue(p.marker.GetInstanceID(),out var latest)&&latest.reason.Length==0&&latest.fingerprint==p.fingerprint;
                var color=p.reason.Length==0&&fresh&&replacementValid&&GeometryReason(p,volume,current).Length==0?new Color(1,.6f,.1f,.9f):Color.red;
                snapshots.Add(new Drawing(volume,color,p.marker.name+" · консервативный защитный box"));
            }
            drawing=snapshots.ToArray();drawingDirty=false;SceneView.RepaintAll();
        }
        private static void DrawPreview(SceneView view)
        {
            if(!expanded||Event.current==null||Event.current.type!=EventType.Repaint||drawingScene!=SceneManager.GetActiveScene().handle)return;
            foreach(var snapshot in drawing)
            {
                Handles.color=drawingDirty?Color.red:snapshot.color;
                Handles.DrawWireCube(snapshot.bounds.center,snapshot.bounds.size);
                Handles.Label(snapshot.bounds.max,snapshot.label);
            }
        }
    }
}
