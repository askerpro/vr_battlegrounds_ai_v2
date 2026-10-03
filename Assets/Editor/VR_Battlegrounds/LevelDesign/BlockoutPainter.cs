using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Core;
using Object=UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Строительная палитра и смысловые кисти непосредственно в Scene View.</summary>
    [InitializeOnLoad]
    public sealed class BlockoutPainter : EditorWindow
    {
        private static readonly HashSet<BlockoutPainter> openPainters=new HashSet<BlockoutPainter>();
        private static BlockoutPainter handlesPainter;
        private static bool ownsNativeTools,previousToolsHidden;
        private static void RestoreNativeTools()
        {if(ownsNativeTools){Tools.hidden=previousToolsHidden;ownsNativeTools=false;}}
        private sealed class RotationShortcutContext : IShortcutContext
        {
            public bool active => focusedWindow is SceneView && !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorGUIUtility.editingTextField && RotationPainter!=null;
        }
        private static BlockoutPainter RotationPainter => openPainters.OrderByDescending(w=>w==handlesPainter).FirstOrDefault(w=>w!=null&&(w.active||w.mode==Mode.Selected)
            && (w.mode==Mode.Build && w.Prefab!=null || w.mode==Mode.Selected && Selection.activeGameObject!=null
                && BlockoutRegistryFactory.TryDefinition(Selection.activeGameObject,out _) && !IsProtection(Selection.activeGameObject)));
        static BlockoutPainter() => EditorApplication.delayCall += RegisterRotationContext;
        private static void RegisterRotationContext() => ShortcutManager.RegisterContext(new RotationShortcutContext());
        [Shortcut("VR Battlegrounds/Blockout/Rotate selected shape",typeof(RotationShortcutContext),"",KeyCode.R,ShortcutModifiers.None,100)]
        private static void RotateKeyboardShortcut(ShortcutArguments arguments)
        {
            var painter=RotationPainter;
            if(painter!=null && painter.ApplyRotation(1)) painter.RepaintViews();
        }
        private const string Alphabet = "Assets/Prefabs/LevelDesign/LD_Alphabet/";
        private enum Mode { Build, Delete, Markup, Selected, Arena, Passive }
        public static event Action ArenaPanelRequested;
        public static Func<Scene,Bounds?> ArenaFloorProvider;
        public static Func<Scene,string> ArenaIdProvider;
        private static string ArenaId(Scene scene) => ArenaIdProvider?.Invoke(scene) ?? "";
        public static Func<GameObject,bool> ProtectedGeometryProvider;
        private static bool IsProtection(GameObject go) => BlockoutBlockRoles.IsProtection(go) || go!=null&&ProtectedGeometryProvider?.Invoke(go)==true;
        [SerializeField] private Mode mode;
        [SerializeField] private int mainTab;
        [SerializeField] private bool active;
        [SerializeField] private bool groupByThickness=true;
        [SerializeField] private int selected, quarterTurns;
        [SerializeField] private float height;
        [SerializeField] private BlockoutMarkup markup;
        [SerializeField] private BlockoutMarkup.Layer layer;
        [SerializeField] private string activeId = "position-1";
        [SerializeField] private int brushCells = 5;
        [SerializeField] private bool erase;
        [SerializeField] private float placementYaw;
        [SerializeField] private float placementHeight=1.6f;
        [SerializeField] private bool detailMode;
        [SerializeField] private bool editCells, removeCells;
        [SerializeField] private bool suggestPosition;
        [SerializeField] private float mainThreatYaw;
        [SerializeField] private string coverId = "cover-1";
        [SerializeField] private int candidateIndex;
        private BlockoutBlockDefinition[] palette = Array.Empty<BlockoutBlockDefinition>();
        [SerializeField] private VrBattlegrounds.Maps.CoverClass placementMaterial;
        [SerializeField] private BlockoutOpeningSettings placementOpenings=BlockoutOpeningSettings.Default;
        private Vector2 scroll,windowScroll;
        private readonly HashSet<Vector2Int> painted = new HashSet<Vector2Int>();
        private readonly HashSet<GameObject> gestureBlocks = new HashSet<GameObject>();
        private Vector2Int previous;
        private bool drawing;
        private int undoGroup = -1, control;
        private Scene gestureScene;
        private string status;
        private Object statusTarget;
        private int handleUndoGroup=-1;

        [MenuItem("Tools/VR Battlegrounds/Level Design/Редактор блокаута",false,100)]
        public static void Open() => GetWindow<BlockoutPainter>("Редактор блокаута");
        private void OnEnable()
        {
            minSize=new Vector2(330,300);
            titleContent=new GUIContent("Редактор блокаута");
            active = false;
            openPainters.Add(this);
            handlesPainter=this;
            palette = BlockoutRegistryFactory.Current?.Definitions.Where(d=>d.gameplayGeometry).ToArray() ?? Array.Empty<BlockoutBlockDefinition>();
            SceneView.duringSceneGui += SceneGUI;
            Undo.undoRedoPerformed += RepaintViews;
            Selection.selectionChanged += SelectionChanged;
            BlockoutThumbnails.Changed += Repaint;
            BlockoutEditDiagnostics.Changed += Repaint;
            BlockoutWorkspacePanel.RegistryRebuildRequested = () => { BlockoutCanonicalRegistry.Apply(); ReloadPalette(); };
            BlockoutWorkspacePanel.LegacyNormalizeRequested = () => BlockoutGrid.Snap(SceneManager.GetActiveScene(),true);
            ReloadPalette();
        }
        private void OnDisable()
        {
            FinishGesture();
            openPainters.Remove(this);
            if(handlesPainter==this)handlesPainter=openPainters.FirstOrDefault();
            if(openPainters.Count==0)RestoreNativeTools();
            SceneView.duringSceneGui -= SceneGUI;
            Undo.undoRedoPerformed -= RepaintViews;
            Selection.selectionChanged -= SelectionChanged;
            BlockoutThumbnails.Changed -= Repaint;
            BlockoutEditDiagnostics.Changed -= Repaint;
        }
        private void SelectionChanged()
        {
            status=null;statusTarget=null;
            var go=Selection.activeGameObject;
            var sectionRoot=BlockoutSectionFactory.Root(go);
            if(sectionRoot!=go){Selection.activeGameObject=sectionRoot;return;}
            if(go==null||!go.scene.IsValid()){RestoreNativeTools();if(mainTab==0)mode=Mode.Build;Repaint();return;}
            if(IsProtection(go)){RestoreNativeTools();FinishGesture();active=false;mainTab=2;mode=Mode.Arena;Repaint();return;}
            if(!BlockoutRegistryFactory.TryDefinition(go,out _)&&go.GetComponent<BlockoutCellWall>()==null){RestoreNativeTools();if(mainTab==0)mode=Mode.Build;Repaint();return;}
            FinishGesture();active=false;editCells=false;mainTab=0;mode=Mode.Selected;Repaint();
        }
        private void RepaintViews() { Repaint(); SceneView.RepaintAll(); }
        private bool TryEscape(Event e)
        {
            if(e==null||e.type!=EventType.KeyDown||e.keyCode!=KeyCode.Escape||e.alt||e.control||e.command||EditorGUIUtility.editingTextField)return false;
            var go=BlockoutSectionFactory.Root(Selection.activeGameObject);
            bool editable=mainTab==0&&go!=null&&!IsProtection(go)
                &&(BlockoutRegistryFactory.TryDefinition(go,out _)||go.GetComponent<BlockoutCellWall>()!=null);
            bool arenaSelection=IsArenaSelection(go);
            editable=editable||arenaSelection;
            if(!editable&&!active)return false;
            FinishGesture();active=false;editCells=false;RestoreNativeTools();
            if(editable){Selection.activeGameObject=null;mainTab=0;mode=Mode.Build;ResetPlacementDefaults();}
            e.Use();RepaintViews();return true;
        }
        private static bool IsArenaSelection(GameObject go)
        {
            if(go==null||!go.scene.IsValid())return false;
            if(IsProtection(go)||go.GetComponentInParent<PhysicalObstacleMarker>()!=null
                ||go.GetComponentInParent<PhysicalObstacleProtection>()!=null||go.GetComponent<PhysicalArenaDefinition>()!=null)return true;
            var arena=go.GetComponentInParent<PhysicalArenaDefinition>();
            if(arena==null)return false;
            if(arena.floor!=null&&go.transform.IsChildOf(arena.floor.transform))return true;
            return arena.GetComponentsInChildren<PhysicalObstacleMarker>(true).Any(m=>m.sourceColliders!=null
                &&m.sourceColliders.Any(c=>c!=null&&(go.transform.IsChildOf(c.transform)||c.transform.IsChildOf(go.transform))));
        }
        private void ReloadPalette()
        {
            palette=BlockoutRegistryFactory.Current?.Definitions.Where(d=>d!=null&&d.gameplayGeometry).ToArray()??Array.Empty<BlockoutBlockDefinition>();
            foreach(var definition in palette) BlockoutDefinitionText.FillMissing(definition);
            ResetPlacementDefaults();
        }
        private void ResetPlacementDefaults()
        {
            if(Definition==null)return;
            placementMaterial=Definition.defaultMaterial;placementOpenings=BlockoutOpeningSettings.Default;
            detailMode=false;
            placementYaw=0;quarterTurns=0;height=0;placementHeight=BlockoutRegistryFactory.DefaultDimensions(Definition).y;
        }
        private void ResetBuildMaterial()
        {if(Definition!=null)placementMaterial=detailMode?VrBattlegrounds.Maps.CoverClass.Hard:Definition.defaultMaterial;placementOpenings=BlockoutOpeningSettings.Default;}
        private void DrawSections()
        {
            string[] names={"Блоки","Позиции","Арена","Общие настройки","Проверки","Обслуживание"};
            int columns=position.width<700?3:6;
            for(int row=0;row<names.Length;row+=columns)
                using(new EditorGUILayout.HorizontalScope())
                    for(int i=row;i<Mathf.Min(row+columns,names.Length);i++)
                        if(GUILayout.Toggle(mainTab==i,names[i],"Button"))mainTab=i;
        }
        private BlockoutBlockDefinition Definition => palette.Length==0?null:palette[Mathf.Clamp(selected,0,palette.Length-1)];
        private GameObject Prefab => Definition?.geometryPrefab;
        private Vector3 PlacementDimensions {get {var size=BlockoutRegistryFactory.DefaultDimensions(Definition);size.y=placementHeight;if(detailMode){size.x=.3f;size.z=.3f;}return size;}}
        private void OnGUI()
        {
            if(BlockoutRegistryFactory.Current!=null&&palette.Length!=BlockoutRegistryFactory.Current.Definitions.Count(d=>d!=null&&d.gameplayGeometry))ReloadPalette();
            if(focusedWindow==this)handlesPainter=this;
            if(focusedWindow==this&&TryEscape(Event.current))return;
            if(Event.current.type==EventType.KeyDown) TryRotationInput(Event.current);
            if(palette.Length==0 && BlockoutRegistryFactory.Current!=null)
                palette=BlockoutRegistryFactory.Current.Definitions.Where(d=>d.gameplayGeometry).ToArray();
            windowScroll=EditorGUILayout.BeginScrollView(windowScroll);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Сверху")) SetView(Quaternion.Euler(90, 0, 0));
                if (GUILayout.Button("Изометрия")) SetView(Quaternion.Euler(45, 45, 0));
            }
            DrawSections();
            if(mainTab==0)
            {
                var chosen=BlockoutSectionFactory.Root(Selection.activeGameObject);
                mode=chosen!=null&&!IsProtection(chosen)&&(BlockoutRegistryFactory.TryDefinition(chosen,out _)||chosen.GetComponent<BlockoutCellWall>()!=null)?Mode.Selected:Mode.Build;
                EditorGUILayout.LabelField(mode==Mode.Selected?"Редактировать":"Строить",EditorStyles.boldLabel);
                if(mode==Mode.Selected&&GUILayout.Button("Выбрать форму для строительства"))
                {Selection.activeGameObject=null;mode=Mode.Build;ResetPlacementDefaults();}
            }
            else mode=mainTab==1 ? Mode.Markup : mainTab==2?Mode.Arena:Mode.Passive;
            if(handlesPainter==this&&mode!=Mode.Selected)RestoreNativeTools();
            if(mode==Mode.Build||mode==Mode.Markup||mode==Mode.Selected&&editCells)
            {
                bool wasActive=active;
                active=GUILayout.Toggle(active,active?"Кисть включена · Esc для выхода":"Включить кисть Scene View","Button");
                if(active&&!wasActive) foreach(var painter in openPainters)if(painter!=this)painter.active=false;
            }
            if(mode==Mode.Arena)
            {
                if(ArenaPanelRequested!=null) ArenaPanelRequested.Invoke();
                else EditorGUILayout.HelpBox("Панель физической арены ещё не подключена.",MessageType.Info);
            }
            if (mode == Mode.Build)
            {
                ResetBuildMaterial();
                if(palette.Length==0) EditorGUILayout.HelpBox("Реестр блоков отсутствует. Создайте его через миграцию LD Alphabet.",MessageType.Warning);
                float tileWidth=Mathf.Max(86,(position.width-60)/3);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Max(90,position.height-270)));
                for(int i=0;i<palette.Length;i++)
                {
                var definition=palette[i];EditorGUILayout.LabelField(definition.displayName,EditorStyles.boldLabel);
                    using(new EditorGUILayout.HorizontalScope())
                        for(int column=0;column<3;column++)
                        {
                            float presetHeight=new[]{1.2f,1.6f,2.5f}[column];
                            var size=BlockoutRegistryFactory.DefaultDimensions(definition);size.y=presetHeight;
                            var preview=BlockoutThumbnails.Request(definition,presetHeight);
                            var stepDescription=SteppedDimensions(definition,presetHeight);
                            var tileText=new[]{"Низкая","Средняя","Высокая"}[column]+"\n"+(stepDescription??SizeText(size));
                            float tileHeight=Mathf.Max(154,110+EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(tileText),tileWidth-10));
                            var tile=GUILayoutUtility.GetRect(tileWidth,tileHeight,GUILayout.Width(tileWidth),GUILayout.Height(tileHeight));
                            if(GUI.Toggle(tile,!detailMode&&selected==i&&Mathf.Abs(placementHeight-presetHeight)<.001f,GUIContent.none,"Button") && (detailMode||selected!=i||Mathf.Abs(placementHeight-presetHeight)>.001f))
                            {Selection.activeGameObject=null;selected=i;ResetPlacementDefaults();placementHeight=presetHeight;}
                            var thumbnail=preview;
                            if(thumbnail!=null) GUI.DrawTexture(new Rect(tile.x+4,tile.y+4,tile.width-8,94),thumbnail,ScaleMode.ScaleToFit);
                            GUI.Label(new Rect(tile.x+5,tile.y+102,tile.width-10,tileHeight-106),tileText,EditorStyles.wordWrappedLabel);
                        }
                }
                DrawDetailChoices();
                EditorGUILayout.EndScrollView();
                if(Definition!=null) EditorGUILayout.LabelField(Definition.description,EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Колесо / R: поворот целой формы ±15°. Касание разрешено; пересечение запрещено.",EditorStyles.wordWrappedLabel);
            }
            if(mode==Mode.Selected) DrawSelected();
            if(mainTab==3)BlockoutWorkspacePanel.DrawSettings();
            if(mainTab==4)BlockoutWorkspacePanel.DrawChecks();
            if(mainTab==5)BlockoutWorkspacePanel.DrawMaintenance();
            if (mode==Mode.Markup && markup != null && markup.positions.Count > 0)
            {
                candidateIndex = EditorGUILayout.Popup("Позиция возле укрытия", Mathf.Clamp(candidateIndex, 0, markup.positions.Count - 1), markup.positions.Select(p => p.id).ToArray());
                var position = markup.positions[candidateIndex];
                EditorGUILayout.LabelField(position.confirmed ? "Подтверждена человеком (без оценки защиты)" : "Кандидат: требуется ручное решение");
                EditorGUI.BeginChangeCheck();
                Vector2Int center = EditorGUILayout.Vector2IntField("Центр, клетка", position.centerCell);
                float threat = EditorGUILayout.FloatField("Угроза, yaw°", position.mainThreatYaw);
                var chosenSupports=new List<string>(position.supportingCoverIds);
                EditorGUILayout.LabelField("Опорные сборки укрытий");
                for(int i=0;i<markup.covers.Count;i++)
                {
                    var cover=markup.covers[i]; bool had=chosenSupports.Contains(cover.id);
                    bool has=EditorGUILayout.Toggle($"Укрытие {i+1} · объектов {cover.blockGlobalObjectIds.Count}",had);
                    if(has&&!had) chosenSupports.Add(cover.id); else if(!has&&had) chosenSupports.Remove(cover.id);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RegisterCompleteObjectUndo(markup, "Переместить позицию");
                    var area = markup.cells.Where(c => c.positionIds.Contains(position.id)).Select(c => new Vector2Int(c.x, c.z)).ToArray();
                    Vector2Int offset = center - position.centerCell;
                    PaintPosition(position, true); position.centerCell = center; position.mainThreatYaw = threat;
                    position.supportingCoverIds = chosenSupports;
                    foreach (var coordinate in area) markup.Paint(coordinate + offset, BlockoutMarkup.Layer.Position, position.id, false);
                    markup.needsReevaluation = true; EditorUtility.SetDirty(markup);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Подтвердить")) { Undo.RecordObject(markup, "Подтвердить позицию"); position.confirmed = true; EditorUtility.SetDirty(markup); }
                    if (GUILayout.Button("Удалить позицию")) { Undo.RegisterCompleteObjectUndo(markup, "Удалить позицию"); PaintPosition(position, true); markup.positions.Remove(position); EditorUtility.SetDirty(markup); }
                }
            }
            if (mode == Mode.Markup)
            {
                markup = (BlockoutMarkup)EditorGUILayout.ObjectField("Артефакт", markup, typeof(BlockoutMarkup), false);
                if (GUILayout.Button("Создать разметку этой карты…")) CreateMarkup();
                if(GUILayout.Button("Объединить выделенные объекты в укрытие")) GroupSelectedCover();
                layer = (BlockoutMarkup.Layer)EditorGUILayout.EnumPopup("Слой", layer);
                activeId = EditorGUILayout.TextField("Активный ID", activeId);
                brushCells = EditorGUILayout.IntSlider("Кисть, клеток на сторону", brushCells, 1, 15);
                erase = GUILayout.Toolbar(erase?1:0,new[]{"Рисовать","Стирать активный ID"})==1;
                if (markup != null)
                {
                    EditorGUILayout.LabelField($"Клеток {markup.cells.Count}; противоречий {markup.cells.Count(c => c.Conflicting)}");
                    var route = markup.routes.FirstOrDefault(r => r.id == activeId);
                    if (layer == BlockoutMarkup.Layer.Route && route != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        string from = EditorGUILayout.TextField("Сценарий: из позиции", route.scenarioFromPositionId);
                        string to = EditorGUILayout.TextField("Сценарий: в позицию", route.scenarioToPositionId);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(markup, "Сценарий маршрута");
                            route.scenarioFromPositionId = from; route.scenarioToPositionId = to;
                            EditorUtility.SetDirty(markup);
                        }
                        EditorGUILayout.LabelField("Физический маршрут двунаправленный");
                    }
                    if (GUILayout.Button("Сохранить артефакт и экспортировать JSON…")) Export();
                }
            }
            if(mainTab<3)EditorGUILayout.HelpBox("ЛКМ кистью: размещение. Выделение блока открывает редактирование. Delete удаляет выделенный объект штатно. R/колесо: поворот; Esc: выход. Alt, ПКМ и СКМ: навигация. Ctrl/Cmd+Z: Undo.", MessageType.Info);
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Warning);
            if(!string.IsNullOrEmpty(status)&&statusTarget!=null)
                using(new EditorGUI.DisabledScope(true))EditorGUILayout.ObjectField("Конфликт с",statusTarget,typeof(Object),true);
            if (EditorGUI.EndChangeCheck()) { FinishGesture(); RepaintViews(); }
            EditorGUILayout.EndScrollView();
        }
        private static void DrawProperties(BlockoutBlockDefinition definition,ref VrBattlegrounds.Maps.CoverClass material,ref BlockoutOpeningSettings openings)
        {
            int index=Array.IndexOf(definition.allowedMaterials,material);
            index=EditorGUILayout.Popup("Материал",Mathf.Max(0,index),definition.allowedMaterials.Select(c=>c.ToString()).ToArray());
            material=definition.allowedMaterials[index];
            using(new EditorGUI.DisabledScope(!definition.supportsOpenings))
                openings.enabled=EditorGUILayout.Toggle("Сквозные щели",openings.enabled);
            if(!definition.supportsOpenings) EditorGUILayout.LabelField(definition.openingDisabledReason,EditorStyles.wordWrappedMiniLabel);
            if(openings.enabled && definition.supportsOpenings)
            {
                openings.spacing=EditorGUILayout.FloatField("Шаг щелей, м",openings.spacing);
                openings.width=EditorGUILayout.FloatField("Ширина щели, м",openings.width);
                openings.sillHeight=EditorGUILayout.FloatField("Основание, м",openings.sillHeight);
                openings.lintelHeight=EditorGUILayout.FloatField("Перемычка, м",openings.lintelHeight);
            }
        }
        private static void SetView(Quaternion rotation)
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.LookAt(view.pivot, rotation, view.size, true);
        }
        private bool Context(out Scene scene, out Vector2 origin, out Bounds floor)
        {
            scene = SceneManager.GetActiveScene(); floor = default;
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null
                || !BlockoutGrid.TryOrigin(scene, out origin, out _)) { origin = default; return false; }
            Bounds? arenaFloor=ArenaFloorProvider?.Invoke(scene);
            if(arenaFloor.HasValue) {floor=arenaFloor.Value;return true;}
            return BlockoutGrid.TryFloor(scene,out floor);
        }
        private bool Matches(Scene scene, Vector2 origin) => markup != null && markup.sceneGuid == AssetDatabase.AssetPathToGUID(scene.path)
            && markup.arenaId == ArenaId(scene) && (markup.origin - origin).sqrMagnitude < .000001f
            && Mathf.Abs(markup.step - BlockoutGrid.Cell) < .00001f && Mathf.Abs(markup.module - BlockoutGrid.Module) < .00001f;
        private void SceneGUI(SceneView view)
        {
            if(handlesPainter==this&&focusedWindow==view&&TryEscape(Event.current))return;
            if(mode==Mode.Arena||mode==Mode.Passive) return;
            if(mode==Mode.Selected&&!editCells)
            {
                if(handlesPainter!=this)return;
                TryRotationInput(Event.current);DrawSelectedHandles();return;
            }
            if (!active) return;
            var e = Event.current;
            if (!Context(out Scene scene, out Vector2 origin, out Bounds floor)) { FinishGesture(); status = "Настройте PhysicalArenaDefinition, пол и origin во вкладке «Арена»; Play Mode / prefab stage недоступны."; return; }
            if (drawing && scene != gestureScene) FinishGesture();
            if (mode == Mode.Markup && !Matches(scene, origin)) { status = "Разметка не соответствует карте/арене/сетке. Создайте или выберите подходящий артефакт."; return; }
            if(TryRotationInput(e)) return;
            if(mode==Mode.Selected && (!editCells || IsProtection(Selection.activeGameObject))) return;
            if(e.type==EventType.Repaint) DrawMarkup(scene, origin, floor.max.y + .025f);
            if (e.alt || e.button > 0 || e.control || e.command) { if (e.type == EventType.MouseUp) FinishGesture(); return; }
            control = GUIUtility.GetControlID("VrBattlegrounds.BlockoutPainter".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!new Plane(Vector3.up, new Vector3(0, floor.max.y, 0)).Raycast(ray, out float distance)) return;
            Vector3 hit = ray.GetPoint(distance);
            var cell = new Vector2Int(Mathf.FloorToInt((hit.x - origin.x) / BlockoutGrid.Cell), Mathf.FloorToInt((hit.z - origin.y) / BlockoutGrid.Cell));
            if (e.type == EventType.Repaint) DrawGhost(cell, scene, origin, floor);
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if(mode==Mode.Build)
                {
                    var picked=HandleUtility.PickGameObject(e.mousePosition,false);
                    for(var p=picked!=null?picked.transform:null;p!=null;p=p.parent)
                        if(BlockoutRegistryFactory.TryDefinition(p.gameObject,out _)) {Selection.activeGameObject=p.gameObject;e.Use();return;}
                }
                FinishGesture(); Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(mode == Mode.Build ? "Нарисовать блокаут" : "Нарисовать смысловой слой");
                drawing = true; gestureScene = scene; previous = cell; GUIUtility.hotControl = control;
                if (mode == Mode.Markup) Undo.RegisterCompleteObjectUndo(markup, "Смысловая кисть");
                Apply(cell, scene, origin, floor); e.Use();
            }
            else if (e.type == EventType.MouseDrag && drawing && e.button == 0)
            {
                foreach (var point in Stroke(previous, cell, mode != Mode.Build || PlanSize().x <= BlockoutGrid.Cell * 1.01f || PlanSize().y <= BlockoutGrid.Cell * 1.01f))
                    Apply(point, scene, origin, floor);
                previous = cell; e.Use();
            }
            else if (e.type == EventType.MouseUp && drawing && e.button == 0) { FinishGesture(); e.Use(); }
            if (e.type == EventType.MouseMove || drawing) view.Repaint();
        }
        // Широкие блоки перекрываются при диагональном шаге; узкие и клеточные кисти идут ортогонально.
        public static IEnumerable<Vector2Int> Stroke(Vector2Int from, Vector2Int to, bool orthogonal)
        {
            Vector2Int p = from;
            yield return p;
            int dx = Mathf.Abs(to.x - from.x), dz = Mathf.Abs(to.y - from.y), steps = Mathf.Max(dx, dz);
            for (int i = 1; i <= steps; i++)
            {
                var next = new Vector2Int(from.x + Mathf.RoundToInt((to.x - from.x) * (float)i / steps),
                    from.y + Mathf.RoundToInt((to.y - from.y) * (float)i / steps));
                if (orthogonal && next.x != p.x && next.y != p.y) yield return new Vector2Int(next.x, p.y);
                yield return next; p = next;
            }
        }
        private Vector2 PlanSize()
        {
            if (Prefab == null) return Vector2.one * BlockoutGrid.Cell;
            var size=PlacementDimensions;float c=Mathf.Abs(Mathf.Cos(placementYaw*Mathf.Deg2Rad)),s=Mathf.Abs(Mathf.Sin(placementYaw*Mathf.Deg2Rad));
            return new Vector2(c*size.x+s*size.z,s*size.x+c*size.z);
        }
        public static Bounds LocalBounds(GameObject prefab)
        {
            bool found = false; Bounds result = default;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 v = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    v = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(v));
                    if (!found) { result = new Bounds(v, Vector3.zero); found = true; } else result.Encapsulate(v);
                }
            }
            return result;
        }
        private Bounds Candidate(Vector2Int cell, Vector2 origin, Bounds floor)
        {
            var parts=BlockoutRegistryFactory.PreviewParts(Definition,new Vector3(origin.x+cell.x*BlockoutGrid.Cell,floor.max.y+height,origin.y+cell.y*BlockoutGrid.Cell),placementYaw,PlacementDimensions,placementOpenings).ToArray();
            var bounds=parts[0].BroadphaseBounds;foreach(var part in parts.Skip(1))bounds.Encapsulate(part.BroadphaseBounds);return bounds;
        }
        public static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(false).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
        private bool Conflict(Bounds candidate, Scene scene, Bounds floor)
        {
            return BlockoutPlacementPreview.Conflict(Definition,PlacementDimensions,placementYaw,scene,floor,candidate);
        }
        private Vector2 PlanSizeUnrotated() => BlockoutGridSettings.Dimensions(Prefab.name);
        private void MarkGeometryChanged(Scene editedScene=default)
        {
            if(markup!=null) { Undo.RecordObject(markup,"Изменить геометрию укрытия"); markup.needsReevaluation=true; EditorUtility.SetDirty(markup); }
            var scene=editedScene.IsValid()?editedScene:SceneManager.GetActiveScene(); if(scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
        private void DrawSelected()
        {
            var go=BlockoutSectionFactory.Root(Selection.activeGameObject);
            if(go==null) return;
            if(BlockoutRegistryFactory.TryDefinition(go,out _))
            {
                var warning=BlockoutEditDiagnostics.Get(go);
                if(!string.IsNullOrEmpty(warning))EditorGUILayout.HelpBox(warning,MessageType.Warning);
                var conflictTarget=BlockoutEditDiagnostics.ConflictTarget(go);
                if(!string.IsNullOrEmpty(warning)&&conflictTarget!=null)
                    using(new EditorGUI.DisabledScope(true))EditorGUILayout.ObjectField("Конфликт с",conflictTarget,typeof(Object),true);
            }
            EditorGUILayout.LabelField("Esc — вернуться к палитре",EditorStyles.miniLabel);
            DrawVaultability(go);
            if(!IsProtection(go)&&BlockoutRegistryFactory.TryDefinition(go,out _))
            {
                var selectedWall=go.GetComponent<BlockoutCellWall>();
                var position=go.transform.position;
                EditorGUI.BeginChangeCheck();position=EditorGUILayout.Vector3Field("Нижний центр, м",position);
                if(EditorGUI.EndChangeCheck())TransformSelected(go,position,selectedWall!=null?selectedWall.RotationYaw:go.transform.eulerAngles.y);
                if(selectedWall==null)
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("−15°"))TransformSelected(go,position,go.transform.eulerAngles.y-15);
                    if(GUILayout.Button("+15°"))TransformSelected(go,position,go.transform.eulerAngles.y+15);
                }
                EditorGUILayout.LabelField("Scene: перемещение по сетке; поворот целой формы 15°. Y сохраняется.",EditorStyles.wordWrappedLabel);
            }
            if(!IsProtection(go) && BlockoutRegistryFactory.TryDefinition(go,out var definition))
            {
                var instance=go.GetComponent<BlockoutBlockInstance>();
                var dimensions=instance!=null ? instance.dimensions : BlockoutRegistryFactory.DefaultDimensions(definition);
                if(instance==null)dimensions.y=WorldBounds(go).size.y;
                var material=instance!=null ? instance.material : (VrBattlegrounds.Maps.CoverClassRules.TryExpectedClass(go,out var expected)?expected:definition.defaultMaterial);
                var openings=instance!=null ? instance.openings : BlockoutOpeningSettings.Default;
                if(go.TryGetComponent<BlockoutCellWall>(out var existingWall)) {dimensions=new Vector3(existingWall.length,existingWall.wallHeight,existingWall.thickness);openings=existingWall.openings;}
                var sections=BlockoutSectionFactory.Recipe(go,dimensions.y,material,openings);
                EditorGUILayout.LabelField("Толщина формы",DepthLabel(DepthGroup(dimensions.z)));
                EditorGUILayout.LabelField("Форма",definition.displayName);
                EditorGUILayout.LabelField(definition.description,EditorStyles.wordWrappedLabel);
                var steppedDescription=SteppedDimensions(definition,dimensions.y,go.GetComponent<BlockoutSteppedGeometry>());
                if(steppedDescription!=null)EditorGUILayout.LabelField(steppedDescription,EditorStyles.wordWrappedLabel);
                EditorGUI.BeginChangeCheck();
                if(definition.heightEditable)
                {
                    float[] heights={1.2f,1.6f,2.5f};
                    int preset=Array.FindIndex(heights,h=>Mathf.Abs(h-dimensions.y)<.0001f);
                    int choice=EditorGUILayout.Popup("Высота",preset<0?3:preset,new[]{"Низкая · 1.2 м","Средняя · 1.6 м","Высокая · 2.5 м","Своя высота"});
                    if(choice<3&&choice!=preset)dimensions.y=heights[choice];
                    dimensions.y=EditorGUILayout.FloatField("Высота, м",dimensions.y);
                }
                else EditorGUILayout.LabelField("Высота фиксирована",$"{dimensions.y:0.##} м · форма сохраняет эталонную геометрию");
                BlockoutSectionPropertiesPanel.Draw(definition,dimensions.y,sections);
                if(EditorGUI.EndChangeCheck())
                {
                    if(BlockoutSectionFactory.Apply(go,dimensions,sections,out var reason)) MarkGeometryChanged(go.scene);
                    else status=reason;
                }
            }
            if(!go.TryGetComponent<BlockoutCellWall>(out var wall))
            {
                if(Context(out var selectedScene,out var selectedOrigin,out var selectedFloor))
                {
                    if(BlockoutRegistryFactory.TryDefinition(go,out var wallDefinition)&&wallDefinition.supportsCellWall)
                    {
                        if(GUILayout.Button("Сделать выбранную стену клеточной (Undo)"))
                        {
                            var b=WorldBounds(go); var rounded=BlockoutWallEditing.Outward(b,selectedOrigin,BlockoutGrid.Cell);
                            if(!BlockoutWallEditing.Conflict(selectedScene,selectedFloor,new[]{rounded},go)) { BlockoutWallEditing.Convert(go,rounded,0,true); MarkGeometryChanged(); }
                            else status="Клеточная форма пересечёт другой объект; прежняя стена сохранена.";
                        }
                    }
                    else EditorGUILayout.HelpBox("Обычный блок редактируется штатными инструментами Unity.",MessageType.Info);
                }
                return;
            }
            if(IsProtection(go)) {EditorGUILayout.HelpBox("Защитная оболочка редактируется через панель арены; обычная клеточная кисть не может уменьшить её защиту.",MessageType.Info);return;}
            EditorGUI.BeginChangeCheck();
            float length=EditorGUILayout.FloatField("Длина, м",wall.length);
            float thickness=EditorGUILayout.FloatField("Толщина, м",wall.thickness);
            float h=wall.wallHeight;
            float yaw=EditorGUILayout.FloatField("Угол, °",wall.RotationYaw);
            if(EditorGUI.EndChangeCheck()) TryEditWall(wall,length,thickness,h,yaw);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("−15°")) TransformSelected(wall.gameObject,wall.transform.position,wall.RotationYaw-15);
                if(GUILayout.Button("+15°")) TransformSelected(wall.gameObject,wall.transform.position,wall.RotationYaw+15);
            }
            if(wall.geometryMode==BlockoutGeometryMode.LegacyCellUnion)
            {
                editCells=EditorGUILayout.Toggle(new GUIContent("Редактировать клетки композиции",
                    "Только для сохранённой клеточной формы: кисть добавляет клетки к выбранному объекту."),editCells);
                if(editCells)
                    removeCells=EditorGUILayout.Toggle(new GUIContent("Удалять клетки кистью",
                        "Клик кисти удаляет клетку из выбранной композиции вместо добавления."),removeCells);
                EditorGUILayout.LabelField("Сохранённая клеточная форма. Кисть меняет её состав; поворот меняет позу целого объекта.",EditorStyles.wordWrappedLabel);
            }
            else
            {
                editCells=false;removeCells=false;
                EditorGUILayout.LabelField("Поворот меняет только позу целой формы. Размеры и щели меняют меш.",EditorStyles.wordWrappedLabel);
            }
        }
        private void RotatePlacement(float direction)
        {
            float step=15;
            placementYaw=Mathf.Repeat(placementYaw+direction*step,360);
            quarterTurns=Mathf.RoundToInt(placementYaw/90)%4;
        }
        private static int DepthGroup(float depth)=>depth<=.3001f?0:depth<=.6001f?1:2;
        private static string DepthLabel(int group)=>group==0?"Тонкие · до 0.3 м":group==1?"Средние · до 0.6 м":"Толстые · более 0.6 м";
        private static string SizeText(Vector3 size)=>$"{size.x:0.##} × {size.y:0.##} × {size.z:0.##} м";
        private static string SteppedDimensions(BlockoutBlockDefinition definition,float height,BlockoutSteppedGeometry actual=null)
        {
            var profile=actual!=null?actual:definition?.geometryPrefab?.GetComponent<BlockoutSteppedGeometry>();
            if(profile==null)return null;
            var parts=profile.LocalPartsAtHeight(height).ToArray();
            return "Низ: "+SizeText(parts[0].size)+"\nВерх: "+(parts.Length>1?SizeText(parts[1].size):"отсутствует");
        }
        private void DrawDetailChoices()
        {
            var thin=palette.FirstOrDefault(d=>d.supportsCellWall&&d.Allows(VrBattlegrounds.Maps.CoverClass.Hard)
                &&Mathf.Abs(BlockoutRegistryFactory.DefaultDimensions(d).z-.3f)<.0001f&&d.minDimensions.x<=.3001f);
            EditorGUILayout.LabelField("Детали · 0.3 × 0.3 м",EditorStyles.boldLabel);
            using(new EditorGUI.DisabledScope(thin==null))
            using(new EditorGUILayout.HorizontalScope())
                for(int column=0;column<3;column++)
                {
                    float h=new[]{1.2f,1.6f,2.5f}[column];
                    if(GUILayout.Toggle(detailMode&&Mathf.Abs(placementHeight-h)<.001f,new[]{"Низкая","Средняя","Высокая"}[column]+$"\n{h:0.##} м","Button"))
                        if(!detailMode||Mathf.Abs(placementHeight-h)>.001f)SelectDetail(h);
                }
            if(thin==null)EditorGUILayout.HelpBox("В активном реестре нет формы, допускающей деталь 0.3 × 0.3 м.",MessageType.Info);
        }
        /// <summary>Деталь — параметр существующей тонкой стены, без отдельного определения или смысловой позиции.</summary>
        public bool SelectDetail(float detailHeight=1.6f)
        {
            var thin=palette.FirstOrDefault(d=>d.supportsCellWall&&d.Allows(VrBattlegrounds.Maps.CoverClass.Hard)
                &&Mathf.Abs(BlockoutRegistryFactory.DefaultDimensions(d).z-.3f)<.0001f&&d.minDimensions.x<=.3001f);
            if(thin==null)return false;
            if(!BlockoutRegistryFactory.Validate(thin,new Vector3(.3f,detailHeight,.3f),VrBattlegrounds.Maps.CoverClass.Hard,BlockoutOpeningSettings.Default,out var reason)){status=reason;return false;}
            FinishGesture();RestoreNativeTools();Selection.activeGameObject=null;
            selected=Array.IndexOf(palette,thin);ResetPlacementDefaults();detailMode=true;placementHeight=detailHeight;
            placementMaterial=VrBattlegrounds.Maps.CoverClass.Hard;mainTab=0;mode=Mode.Build;status=null;RepaintViews();return true;
        }
        private bool TryRotationInput(Event e)
        {
            if((!active&&mode!=Mode.Selected) || mode==Mode.Arena || e.alt || e.control || e.command || e.shift || EditorGUIUtility.editingTextField
                || !(e.type==EventType.ScrollWheel || e.type==EventType.KeyDown && e.keyCode==KeyCode.R)) return false;
            float direction=e.type==EventType.ScrollWheel?Mathf.Sign(e.delta.y):1;
            if(!ApplyRotation(direction))return false;
            e.Use();RepaintViews();return true;
        }
        private bool ApplyRotation(float direction)
        {
            if(mode==Mode.Build) RotatePlacement(direction);
            else if(mode==Mode.Selected && Selection.activeGameObject!=null && !IsProtection(Selection.activeGameObject))
            {var go=BlockoutSectionFactory.Root(Selection.activeGameObject);var wall=go.GetComponent<BlockoutCellWall>();TransformSelected(go,go.transform.position,(wall!=null?wall.RotationYaw:go.transform.eulerAngles.y)+direction*15);}
            else return false;
            return true;
        }
        private void TransformSelected(GameObject go,Vector3 position,float yaw)
        {
            go=BlockoutSectionFactory.Root(go);
            if(BlockoutRegistryFactory.ApplyTransform(go,position,Mathf.Repeat(yaw,360),out var reason))
            {status=null;statusTarget=null;MarkGeometryChanged(go.scene);}
            else status=reason;
        }
        private void DrawSelectedHandles()
        {
            var go=BlockoutSectionFactory.Root(Selection.activeGameObject);
            if(go==null||IsProtection(go)||EditorApplication.isPlayingOrWillChangePlaymode||!BlockoutRegistryFactory.TryDefinition(go,out _))return;
            if(!ownsNativeTools){previousToolsHidden=Tools.hidden;ownsNativeTools=true;}Tools.hidden=true;
            if(Event.current.alt||Event.current.control||Event.current.command)return;
            var wall=go.GetComponent<BlockoutCellWall>();
            var position=go.transform.position;
            float yaw=wall!=null?wall.RotationYaw:go.transform.eulerAngles.y;
            EditorGUI.BeginChangeCheck();
            var moved=Handles.PositionHandle(position,Quaternion.identity);
            var rotation=Handles.Disc(Quaternion.Euler(0,yaw,0),position,Vector3.up,HandleUtility.GetHandleSize(position),false,15);
            if(EditorGUI.EndChangeCheck())
            {
                if(handleUndoGroup<0){Undo.IncrementCurrentGroup();handleUndoGroup=Undo.GetCurrentGroup();}
                float step=15;
                TransformSelected(go,moved,Mathf.Round(rotation.eulerAngles.y/step)*step);Repaint();
            }
            if(handleUndoGroup>=0&&Event.current.rawType==EventType.MouseUp)
            {Undo.CollapseUndoOperations(handleUndoGroup);handleUndoGroup=-1;}
        }
        private void TryEditWall(BlockoutCellWall wall,float length,float thickness,float h,float yaw)
        {
            if(Mathf.Abs(length-wall.length)<.0001f&&Mathf.Abs(thickness-wall.thickness)<.0001f&&Mathf.Abs(h-wall.wallHeight)<.0001f)
            {TransformSelected(wall.gameObject,wall.transform.position,yaw);return;}
            if(!BlockoutRegistryFactory.TryDefinition(wall.gameObject,out var definition)) {status="Для редактирования нужна форма в реестре.";return;}
            var instance=wall.GetComponent<BlockoutBlockInstance>();
            var material=instance!=null?instance.material:definition.defaultMaterial;
            if(BlockoutRegistryFactory.ApplyProperties(wall.gameObject,new Vector3(length,h,thickness),material,wall.openings,out var reason,Mathf.Repeat(yaw,360))) MarkGeometryChanged(wall.gameObject.scene);
            else status=reason;
        }
        [Serializable] private sealed class GuardBackup { public string objectId; public Bounds bounds; public Vector3 position,scale; public Quaternion rotation; }
        private static void DrawVaultability(GameObject go)
        {
            var b=LocalBounds(go); float h=b.size.y*Mathf.Abs(go.transform.lossyScale.y);
            float depth=Mathf.Min(b.size.x*Mathf.Abs(go.transform.lossyScale.x),b.size.z*Mathf.Abs(go.transform.lossyScale.z));
            bool knownShape=go.GetComponent<BoxCollider>()!=null;
            if(go.TryGetComponent<BlockoutCellWall>(out var wall))
            {
                h=wall.wallHeight;
                var normal=new Vector2(Mathf.Sin(wall.yaw*Mathf.Deg2Rad),Mathf.Cos(wall.yaw*Mathf.Deg2Rad));
                float low=float.PositiveInfinity,high=float.NegativeInfinity;
                foreach(var c in wall.cells) foreach(var d in new[]{Vector2Int.zero,Vector2Int.right,Vector2Int.up,Vector2Int.one})
                {float p=Vector2.Dot(new Vector2(c.x+d.x,c.y+d.y)*wall.cellSize,normal);low=Mathf.Min(low,p);high=Mathf.Max(high,p);}
                depth=wall.geometryMode==BlockoutGeometryMode.ThinStraight?wall.thickness:high-low;
                knownShape=wall.geometryMode==BlockoutGeometryMode.ThinStraight||new HashSet<Vector2Int>(wall.cells).SetEquals(BlockoutCellWall.Raster(wall.length,wall.thickness,wall.yaw,wall.cellSize));
            }
            string reason=h>1.0001f ? $"высота {h:0.##} м > 1 м" : depth>.3001f ? $"толщина {depth:0.##} м > 0.3 м" : !knownShape ? "пригодность формы не подтверждена" : "габариты допускают; нужна проверка комфорта человеком";
            EditorGUILayout.LabelField("Перешагивание: "+reason,EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Механика: "+(go.GetComponentInChildren<VrBattlegrounds.Maps.VaultableObstacle>()!=null ? "маркер VaultableObstacle включён" : "маркер VaultableObstacle отсутствует"),EditorStyles.wordWrappedLabel);
        }
        private void GroupSelectedCover()
        {
            if(!Context(out var scene,out var origin,out _) || !Matches(scene,origin)) return;
            var selectedRoots=Selection.gameObjects.Select(BlockoutSectionFactory.Root).Distinct().ToArray();
            var blocks=BlockoutGrid.Blocks(scene).Where(b=>selectedRoots.Contains(b)).ToArray();
            if(blocks.Length==0) return;
            Undo.RegisterCompleteObjectUndo(markup,"Объединить выделенные укрытия");
            var ids=blocks.Select(b=>GlobalObjectId.GetGlobalObjectIdSlow(b).ToString()).ToList();
            var cover=new BlockoutMarkup.Cover { id="cover-"+Guid.NewGuid().ToString("N"),blockGlobalObjectIds=ids,footprintSnapshot=WorldBounds(blocks[0]) };
            foreach(var b in blocks.Skip(1)) cover.footprintSnapshot.Encapsulate(WorldBounds(b));
            // Старые memberships остаются: объект может поддерживать несколько сборок и позиций.
            markup.covers.Add(cover); markup.needsReevaluation=true; EditorUtility.SetDirty(markup);
        }
        public static GameObject Place(GameObject prefab, Scene scene, Bounds target, int turns)
        {
            var definition=BlockoutRegistryFactory.Current?.Definitions.FirstOrDefault(d=>d.geometryPrefab==prefab||d.materialVariants.Any(v=>v.sourcePrefab==prefab));
            if(definition==null)throw new InvalidOperationException("Форма отсутствует в реестре блоков.");
            var material=VrBattlegrounds.Maps.CoverClassRules.TryExpectedClass(prefab,out var expected)?expected:definition.defaultMaterial;
            return BlockoutRegistryFactory.Create(definition,scene,target.min,turns*90,BlockoutRegistryFactory.DefaultDimensions(definition),material,BlockoutOpeningSettings.Default);
        }
        private void Apply(Vector2Int cell, Scene scene, Vector2 origin, Bounds floor)
        {
            statusTarget=null;
            if (!painted.Add(cell)) return;
            if(mode==Mode.Build && !BlockoutRegistryFactory.ValidatePlacement(Definition,Definition!=null?PlacementDimensions:Vector3.zero,placementMaterial,placementOpenings,placementYaw,out var placementReason))
            {status=placementReason;return;}
            if (mode == Mode.Build && Prefab != null)
            {
                var container=FindBrushContainer(scene,out var containerReason);
                if(containerReason!=null){status=containerReason;return;}
                Bounds target = Candidate(cell, origin, floor);
                bool wall=BlockoutWallEditing.IsWall(Prefab);
                Vector3 wallOrigin=new Vector3(origin.x+cell.x*BlockoutGrid.Cell,floor.max.y+height,origin.y+cell.y*BlockoutGrid.Cell);
                BlockoutWallEditing.ConflictInfo conflict;
                bool blocked=wall ? BlockoutWallEditing.Conflict(scene,floor,BlockoutRegistryFactory.PreviewParts(Definition,wallOrigin,placementYaw,PlacementDimensions,placementOpenings),out conflict)
                    :BlockoutPlacementPreview.Conflict(Definition,PlacementDimensions,placementYaw,scene,floor,target,out conflict);
                if(blocked)
                {
                    status=conflict.message+" Размещение пропущено.";statusTarget=conflict.target;
                    GameLog.Debug.Warning("Блокаут: "+status,statusTarget);return;
                }
                if(!BlockoutRegistryFactory.Validate(Definition,PlacementDimensions,placementMaterial,placementOpenings,out var reason)) {status=reason;return;}
                GameObject placed;
                try { placed=BlockoutRegistryFactory.Create(Definition,scene,wallOrigin,placementYaw,PlacementDimensions,placementMaterial,placementOpenings); }
                catch(ArgumentException error) {status=error.Message;return;}
                catch(InvalidOperationException error) {status=error.Message;return;}
                if(container==null)
                {
                    var parent=new GameObject("Блокаут");SceneManager.MoveGameObjectToScene(parent,scene);
                    Undo.RegisterCreatedObjectUndo(parent,"Создать контейнер блокаута");container=parent.AddComponent<BlockoutSceneContainer>();
                }
                Undo.SetTransformParent(placed.transform,container.transform,"Поместить блок в контейнер");
                target=WorldBounds(placed);
                coverId="cover-"+Guid.NewGuid().ToString("N"); suggestPosition=false;
                gestureBlocks.Add(placed);
                if (!detailMode && Matches(scene, origin) && !string.IsNullOrWhiteSpace(coverId))
                {
                    Undo.RegisterCompleteObjectUndo(markup, "Связать укрытие");
                    markup.needsReevaluation = true;
                    var cover = markup.covers.FirstOrDefault(c => c.id == coverId);
                    if (cover == null) { cover = new BlockoutMarkup.Cover { id = coverId, footprintSnapshot = target }; markup.covers.Add(cover); }
                    else cover.footprintSnapshot.Encapsulate(target);
                    cover.blockGlobalObjectIds.Add(GlobalObjectId.GetGlobalObjectIdSlow(placed).ToString());
                    if (suggestPosition && !markup.positions.Any(p => p.supportingCoverIds.Contains(coverId)))
                    {
                        // Игрок находится за укрытием относительно указанного направления угрозы.
                        Vector3 away = -(Quaternion.Euler(0, mainThreatYaw, 0) * Vector3.forward);
                        float clearance = Mathf.Abs(away.x) * target.extents.x + Mathf.Abs(away.z) * target.extents.z + BlockoutGrid.PositionSize / 2 + BlockoutGrid.Cell;
                        Vector3 point = target.center + away * clearance;
                        var position = new BlockoutMarkup.Position { id = "candidate-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            centerCell = new Vector2Int(Mathf.FloorToInt((point.x - origin.x) / BlockoutGrid.Cell), Mathf.FloorToInt((point.z - origin.y) / BlockoutGrid.Cell)),
                            sizeCells = Mathf.Max(1, Mathf.RoundToInt(BlockoutGrid.PositionSize / BlockoutGrid.Cell)), mainThreatYaw = mainThreatYaw };
                        position.supportingCoverIds.Add(coverId); markup.positions.Add(position); PaintPosition(position, false);
                    }
                    EditorUtility.SetDirty(markup);
                }
                if(detailMode)MarkGeometryChanged(scene);else EditorSceneManager.MarkSceneDirty(scene);
            }
            else if(mode==Mode.Selected && BlockoutSectionFactory.Root(Selection.activeGameObject)!=null && BlockoutSectionFactory.Root(Selection.activeGameObject).TryGetComponent<BlockoutCellWall>(out var edited))
            {
                var localPoint=edited.transform.InverseTransformPoint(new Vector3(origin.x+cell.x*BlockoutGrid.Cell,edited.transform.position.y,origin.y+cell.y*BlockoutGrid.Cell));
                localPoint+=edited.GetComponent<BlockoutSectionGeometry>()?.AnchorOffset??Vector3.zero;
                var local=new Vector2Int(Mathf.RoundToInt(localPoint.x/edited.cellSize),Mathf.RoundToInt(localPoint.z/edited.cellSize));
                var next=new HashSet<Vector2Int>(edited.cells);
                if(removeCells) next.Remove(local); else next.Add(local);
                if(BlockoutRegistryFactory.ApplyCells(edited.gameObject,next,out var reason)) MarkGeometryChanged();else status=reason;
            }
            else if (mode == Mode.Markup && Matches(scene, origin))
            {
                if (string.IsNullOrWhiteSpace(activeId)) { status = "Укажите непустой ID."; return; }
                int half = brushCells / 2;
                if (!erase && layer == BlockoutMarkup.Layer.Position && !markup.positions.Any(p => p.id == activeId.Trim()))
                    markup.positions.Add(new BlockoutMarkup.Position { id = activeId.Trim(), centerCell = cell, sizeCells = brushCells });
                for (int x = 0; x < brushCells; x++) for (int z = 0; z < brushCells; z++)
                {
                    var c = cell + new Vector2Int(x - half, z - half);
                    Vector3 center = new Vector3(origin.x + (c.x + .5f) * BlockoutGrid.Cell, floor.center.y, origin.y + (c.y + .5f) * BlockoutGrid.Cell);
                    if (floor.Contains(center)) markup.Paint(c, layer, activeId.Trim(), erase);
                }
                EditorUtility.SetDirty(markup);
                markup.needsReevaluation = true;
            }
            status = null;
        }
        private static BlockoutSceneContainer FindBrushContainer(Scene scene,out string reason)
        {
            reason=null;
            var containers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BlockoutSceneContainer>(true)).ToArray();
            if(containers.Length>1){reason="В сцене несколько контейнеров блокаута. Оставьте один перед новым размещением.";return null;}
            var container=containers.FirstOrDefault();
            if(container!=null&&(container.transform.parent!=null||container.transform.position.sqrMagnitude>.00000001f
                ||Quaternion.Angle(container.transform.rotation,Quaternion.identity)>.001f||(container.transform.localScale-Vector3.one).sqrMagnitude>.00000001f))
            {reason="Контейнер блокаута должен быть корневым, с позицией 0, поворотом 0 и масштабом 1. Существующие объекты не перемещены.";return null;}
            return container;
        }
        private void PaintPosition(BlockoutMarkup.Position position, bool remove)
        {
            if (remove)
            {
                foreach (var cell in markup.cells.Where(c => c.positionIds.Contains(position.id)).ToArray())
                    markup.Paint(new Vector2Int(cell.x, cell.z), BlockoutMarkup.Layer.Position, position.id, true);
                return;
            }
            int half = position.sizeCells / 2;
            for (int x = 0; x < position.sizeCells; x++) for (int z = 0; z < position.sizeCells; z++)
                markup.Paint(position.centerCell + new Vector2Int(x - half, z - half), BlockoutMarkup.Layer.Position, position.id, remove);
        }
        private void FinishGesture()
        {
            if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);
            if (drawing) GUIUtility.hotControl = 0;
            undoGroup = -1; drawing = false; painted.Clear(); gestureBlocks.Clear();
        }
        private void DrawGhost(Vector2Int cell, Scene scene, Vector2 origin, Bounds floor)
        {
            if(mode==Mode.Build && Definition!=null && !BlockoutRegistryFactory.ValidatePlacement(Definition,PlacementDimensions,placementMaterial,placementOpenings,placementYaw,out var placementReason))
            {status=placementReason;var rejected=Candidate(cell,origin,floor);DrawCell(rejected.min.x,rejected.min.z,rejected.size.x,rejected.size.z,floor.max.y+.03f,Color.red);return;}
            if(mode==Mode.Selected && BlockoutSectionFactory.Root(Selection.activeGameObject)!=null && BlockoutSectionFactory.Root(Selection.activeGameObject).TryGetComponent<BlockoutCellWall>(out var edit))
            {
                var point=edit.transform.InverseTransformPoint(new Vector3(origin.x+cell.x*BlockoutGrid.Cell,edit.transform.position.y,origin.y+cell.y*BlockoutGrid.Cell));
                point+=edit.GetComponent<BlockoutSectionGeometry>()?.AnchorOffset??Vector3.zero;
                var local=new Vector2Int(Mathf.RoundToInt(point.x/edit.cellSize),Mathf.RoundToInt(point.z/edit.cellSize));
                var next=new HashSet<Vector2Int>(edit.cells); if(removeCells) next.Remove(local); else next.Add(local);
                bool valid=BlockoutRegistryFactory.ValidateCells(edit.gameObject,next,out _);
                var b=BlockoutWallEditing.Volumes(new[]{local},edit.transform.position-edit.transform.TransformVector(edit.GetComponent<BlockoutSectionGeometry>()?.AnchorOffset??Vector3.zero),edit.cellSize,edit.wallHeight).First();
                Handles.color=valid ? Color.green : Color.red; DrawCell(b.min.x,b.min.z,b.size.x,b.size.z,b.min.y,Handles.color);Handles.DrawWireCube(b.center,b.size);return;
            }
            if (mode == Mode.Build && Prefab != null)
            {
                if(BlockoutWallEditing.IsWall(Prefab))
                {
                    var volumes=BlockoutRegistryFactory.PreviewParts(Definition,new Vector3(origin.x+cell.x*BlockoutGrid.Cell,floor.max.y+height,origin.y+cell.y*BlockoutGrid.Cell),placementYaw,PlacementDimensions,placementOpenings).ToArray();
                    bool valid=!BlockoutWallEditing.Conflict(scene,floor,volumes);
                    Handles.color=valid?Color.green:Color.red;
                    foreach(var part in volumes)
                        using(new Handles.DrawingScope(Handles.color,Matrix4x4.TRS(part.center,part.rotation,Vector3.one)))
                            Handles.DrawWireCube(Vector3.zero,part.size);
                    return;
                }
                Bounds target = Candidate(cell, origin, floor);
                Color color = Conflict(target, scene, floor) ? Color.red : Color.green;
                DrawCell(target.min.x, target.min.z, target.size.x, target.size.z, floor.max.y + .03f, color);
                BlockoutPlacementPreview.Draw(Definition,PlacementDimensions,placementYaw,target,color);
            }
            else
            {
                int n = mode == Mode.Markup ? brushCells : 1, half = mode == Mode.Markup ? n / 2 : 0;
                DrawCell(origin.x + (cell.x - half) * BlockoutGrid.Cell, origin.y + (cell.y - half) * BlockoutGrid.Cell,
                    n * BlockoutGrid.Cell, n * BlockoutGrid.Cell, floor.max.y + .04f, erase || mode == Mode.Delete ? Color.red : Color.cyan);
            }
        }
        private static void DrawCell(float x, float z, float width, float depth, float y, Color color)
        {
            var old = Handles.zTest; Handles.zTest = CompareFunction.Always;
            Handles.DrawSolidRectangleWithOutline(new[] { new Vector3(x, y, z), new Vector3(x + width, y, z),
                new Vector3(x + width, y, z + depth), new Vector3(x, y, z + depth) }, new Color(color.r, color.g, color.b, .15f), color);
            Handles.zTest = old;
        }
        private void DrawMarkup(Scene scene, Vector2 origin, float y)
        {
            if (!Matches(scene, origin)) return;
            foreach (var c in markup.cells)
            {
                Color color = c.Conflicting ? Color.red : c.coverIds.Count > 0 ? Color.magenta : c.positionIds.Count > 0 ? Color.yellow : Color.cyan;
                DrawCell(origin.x + c.x * BlockoutGrid.Cell, origin.y + c.z * BlockoutGrid.Cell, BlockoutGrid.Cell, BlockoutGrid.Cell, y, color);
                if (c.routeIds.Count > 1 || c.Conflicting)
                    Handles.Label(new Vector3(origin.x + (c.x + .5f) * BlockoutGrid.Cell, y, origin.y + (c.z + .5f) * BlockoutGrid.Cell),
                        c.Conflicting ? "!" : c.routeIds.Count.ToString());
            }
            foreach (var position in markup.positions)
            {
                Vector3 center = new Vector3(origin.x + (position.centerCell.x + .5f) * BlockoutGrid.Cell, y, origin.y + (position.centerCell.y + .5f) * BlockoutGrid.Cell);
                Handles.Label(center, position.id + (position.confirmed ? " ✓" : " ?"));
                Handles.ArrowHandleCap(0, center, Quaternion.Euler(0, position.mainThreatYaw, 0), BlockoutGrid.PositionSize, EventType.Repaint);
            }
        }
        private void CreateMarkup()
        {
            if (!Context(out Scene scene, out Vector2 origin, out _)) { status = "Откройте карту с физической ареной."; return; }
            string path = EditorUtility.SaveFilePanelInProject("Разметка карты", scene.name + "-markup", "asset", "Editor-only артефакт ручной гипотезы", "Assets/Editor/VR_Battlegrounds/LevelDesign");
            if (string.IsNullOrEmpty(path)) return;
            var data = CreateInstance<BlockoutMarkup>();
            data.sceneGuid = AssetDatabase.AssetPathToGUID(scene.path); data.scenePath = scene.path;
            data.arenaId=ArenaId(scene);
            data.origin = origin; data.step = BlockoutGrid.Cell; data.module = BlockoutGrid.Module;
            AssetDatabase.CreateAsset(data, path); AssetDatabase.SaveAssets(); markup = data;
        }
        private void Export()
        {
            if (markup == null) return;
            string path = EditorUtility.SaveFilePanel("JSON разметки", "Tools/LevelDesign", markup.name, "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonUtility.ToJson(markup, true)); AssetDatabase.SaveAssetIfDirty(markup);
        }
    }
}
