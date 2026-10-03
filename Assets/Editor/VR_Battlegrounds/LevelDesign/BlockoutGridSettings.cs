using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Общий паспорт размеров; шаг перемещения независим от модуля геометрии.</summary>
    public static class BlockoutGridSettings
    {
        public const string Path = "Tools/LevelDesign/blockout-grid.json";
        [Serializable] public class Block
        {
            public string name;
            public float width, depth;
            public Block(string name, float width, float depth)
            { this.name = name; this.width = width; this.depth = depth; }
        }
        [Serializable] public class Data
        {
            public float step = .3f, module = .3f;
            public float stepTopWidth = .9f, stepTopDepth = .9f;
            public Block[] blocks = Array.Empty<Block>();
        }
        // Размеры визуала до миграций. Повторное округление всегда можно начать с этой базы.
        private static readonly Block[] Original =
        {
            new Block("LD_Beam_Low",3,1.6f), new Block("LD_Block_Low",2,.6f),
            new Block("LD_Can_Mid",1.5f,1.5f), new Block("LD_Tree_Tall",2.4f,2.4f),
            new Block("LD_Crate",.9f,.9f), new Block("LD_Crate_Mid",1.2f,1.2f),
            new Block("LD_Crate_Soft",1.2f,1.2f), new Block("LD_Dorito_Mid",1.5f,1.5f),
            new Block("LD_Door_Lintel",1.4f,.15f), new Block("LD_PillarBox",1.1f,1.1f),
            new Block("LD_Snake_Segment",3,.6f), new Block("LD_Floor_Tile",10,10),
            new Block("LD_Fence_Mid_Hard",9.065f,.349f), new Block("LD_Fence_Mid_Soft",3.538f,.336f),
            new Block("LD_Fence_Vault",2,.1f), new Block("LD_Net_Tall_Visual",2,.05f),
            new Block("LD_PalletFence_Set_Soft",2,.1f), new Block("LD_PalletFence_Single_Soft",2.01f,.1f),
            new Block("LD_Wall_Mid",2,.15f), new Block("LD_Wall_Tall",2,.15f),
            new Block("LD_Wall_Mid_Soft",2,.1f), new Block("LD_Wall_Tall_Soft",2,.1f),
            new Block("LD_Window_Sill",1.2f,.15f), new Block("LD_Window_Lintel",1.2f,.15f),
            new Block(HalfCylinderSourceBuilder.SourceKey,1.2f,.6f)
        };
        private static Data current;
        public static Data Current => current ??= Load();
        public static float Quantize(float value, float module) => Mathf.Max(module, Mathf.Round(value / module) * module);
        public static Block[] FromOriginal(float module) => Original.Select(b => b.name==HalfCylinderSourceBuilder.SourceKey ?
            new Block(b.name,Quantize(b.width,module*2),Quantize(b.width,module*2)/2) :
            new Block(b.name, Quantize(b.width,module), Quantize(b.depth,module))).ToArray();
        public static Vector2 Dimensions(string name)
        {
            var b = Current.blocks.FirstOrDefault(item => item.name == name);
            if (b == null) throw new InvalidOperationException("Нет размеров блока: " + name);
            return new Vector2(b.width, b.depth);
        }
        private static Data Load()
        {
            string json = File.Exists(Path) ? File.ReadAllText(Path) : null;
            var data = json != null ? JsonUtility.FromJson<Data>(json) : new Data();
            if (json == null || !json.Contains("\"stepTopWidth\"")) data.stepTopWidth = .9f;
            if (json == null || !json.Contains("\"stepTopDepth\"")) data.stepTopDepth = .9f;
            if (data.blocks == null || data.blocks.Length == 0) data.blocks = FromOriginal(data.module);
            AddMissingActiveRows(data);
            Validate(data);
            return data;
        }
        public static void Validate(Data data)
        {
            if (!ValidNumber(data.step) || !ValidNumber(data.module))
                throw new ArgumentException("Шаг и модуль должны быть положительными конечными числами.");
            if (data.blocks == null || data.blocks.Select(b=>b.name).Distinct().Count()!=data.blocks.Length)
                throw new ArgumentException("Размеры блоков отсутствуют или имена повторяются.");
            foreach (var b in data.blocks)
                if (!ValidNumber(b.width) || !ValidNumber(b.depth)
                    || Mathf.Abs(b.width/data.module-Mathf.Round(b.width/data.module))>.001f
                    || Mathf.Abs(b.depth/data.module-Mathf.Round(b.depth/data.module))>.001f)
                    throw new ArgumentException("Габариты должны быть кратны модулю: " + b.name);
            foreach (var b in Original)
                if (!data.blocks.Any(item=>item.name==b.name)) throw new ArgumentException("Пропущен блок: "+b.name);
            var half=data.blocks.First(b=>b.name==HalfCylinderSourceBuilder.SourceKey);
            if(Mathf.Abs(half.depth-half.width/2)>.0001f)
                throw new ArgumentException("Глубина полуцилиндра равна половине диаметра; диаметр кратен двум модулям (0,6 м при модуле 0,3).");
            var stepBlock=data.blocks.First(b=>b.name=="LD_Dorito_Mid");
            if(!ValidNumber(data.stepTopWidth)||!ValidNumber(data.stepTopDepth)||data.stepTopWidth>stepBlock.width+.0001f||data.stepTopDepth>stepBlock.depth+.0001f)
                throw new ArgumentException("Верх ступенчатой формы должен иметь положительные конечные размеры и помещаться над низом; кратность модулю не требуется.");
        }
        private static bool ValidNumber(float value) => value >= .01f && !float.IsNaN(value) && !float.IsInfinity(value);
        public static void AddMissingActiveRows(Data data)
        {
            if(!data.blocks.Any(b=>b.name==HalfCylinderSourceBuilder.SourceKey))
            {
                float diameter=Quantize(1.2f,data.module*2);
                data.blocks=data.blocks.Append(new Block(HalfCylinderSourceBuilder.SourceKey,diameter,diameter/2)).ToArray();
            }
        }
        public static void Save(Data data)
        {
            Validate(data);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            if (File.Exists(Path))
            {
                Directory.CreateDirectory("Temp/LevelDesign/GridSettings");
                File.Copy(Path,"Temp/LevelDesign/GridSettings/"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
            }
            File.WriteAllText(Path,JsonUtility.ToJson(data,true));
            current = JsonUtility.FromJson<Data>(JsonUtility.ToJson(data));
        }
    }

    /// <summary>Предпросмотр и редактирование размеров до применения к общим префабам.</summary>
    public static class BlockoutGridSettingsPanel
    {
        private static BlockoutGridSettings.Data draft;
        private static string status;
        public static void Reload() => draft = JsonUtility.FromJson<BlockoutGridSettings.Data>(JsonUtility.ToJson(BlockoutGridSettings.Current));
        public static void Draw()
        {
            if (draft == null) Reload();
            EditorGUILayout.LabelField("Общий паспорт размеров",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Карточки задают основание общих префабов. Высоты экземпляров выбираются в редакторе блокаута: 1,2 / 1,6 / 2,5 м. Шаг перемещения независим от модуля геометрии.", EditorStyles.wordWrappedLabel);
            draft.step = EditorGUILayout.FloatField("Шаг перемещения, м",draft.step);
            draft.module = EditorGUILayout.FloatField("Модуль блока, м",draft.module);
            var registry=BlockoutRegistryFactory.Current;
            bool activeRegistryReady=registry!=null&&registry.Definitions.Count>0&&registry.Definitions.All(BlockoutCanonicalRegistry.IsActiveDefinition);
            if (GUILayout.Button("Вернуть базовые размеры активных форм в черновик") && draft.module>=.01f && activeRegistryReady)
            {
                var baseline=BlockoutGridSettings.FromOriginal(draft.module);
                foreach(var definition in registry.Definitions.Where(d=>d!=null&&d.gameplayGeometry))
                {
                    var original=baseline.FirstOrDefault(b=>b.name==definition.dimensionsSourceKey);
                    var block=draft.blocks.FirstOrDefault(b=>b.name==definition.dimensionsSourceKey);
                    if(original!=null&&block!=null) {block.width=original.width;block.depth=original.depth;}
                }
            }
            EditorGUILayout.LabelField("Размеры активных форм",EditorStyles.boldLabel);
            if(activeRegistryReady) foreach(var definition in registry.Definitions.Where(d=>d!=null&&d.gameplayGeometry))
                DrawCard(definition);
            else EditorGUILayout.HelpBox("Для карточек нужен действующий реестр активных форм.",MessageType.Warning);
            string error=null;
            try { BlockoutGridSettings.Validate(draft); } catch(ArgumentException e) { error=e.Message; }
            if (error!=null) EditorGUILayout.HelpBox(error,MessageType.Error);
            if(!activeRegistryReady) error="Нужен действующий реестр активных форм.";
            EditorGUILayout.HelpBox("Размеры общих источников и игровых блоков активной сцены задаются принудительно. Пересечения и выход за пол показываются предупреждениями. Высота, материал, щели, поворот и ID сохраняются; пользовательская длина стен меняется пропорционально шаблону. Физические защиты и маркеры исключены. Undo отменяет сцену; ассеты восстанавливаются из Temp. Сцена не сохраняется, occlusion не запекается.",MessageType.Info);
            using(new EditorGUI.DisabledScope(error!=null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                if(GUILayout.Button("Применить размеры активных форм"))
                {
                    try {string backup=BlockoutAlphabetGridMigration.Apply(draft);status="Размеры опубликованы. "+BlockoutPublishedSceneSync.LastReport?.Summary+" Резервная копия: "+backup;}
                    catch(Exception exception) {status="Применение остановлено: "+exception.Message+" "+BlockoutPublishedSceneSync.LastReport?.Summary;}
                }
            if(!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status,MessageType.Info);
            var report=BlockoutPublishedSceneSync.LastReport;
            if(report!=null) foreach(var warning in report.warnings)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.ObjectField("Проблемный объект",warning.target,typeof(GameObject),true);
                EditorGUILayout.LabelField(warning.reason,EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
                if(GUILayout.Button("Выбрать")) Selection.activeGameObject=warning.target;
                if(GUILayout.Button("Показать")) {Selection.activeGameObject=warning.target;EditorGUIUtility.PingObject(warning.target);SceneView.lastActiveSceneView?.FrameSelected();}
                EditorGUILayout.EndHorizontal();EditorGUILayout.EndVertical();
            }
        }
        private static void DrawCard(BlockoutBlockDefinition definition)
        {
            var primary=draft.blocks.FirstOrDefault(b=>b.name==definition.dimensionsSourceKey);
            if(primary==null) {EditorGUILayout.HelpBox("Нет размеров формы: "+definition.displayName,MessageType.Error);return;}
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.BeginVertical(GUILayout.Width(100));
            var preview=BlockoutThumbnails.Request(definition,1.6f);
            Rect rect=GUILayoutUtility.GetRect(96,96,GUILayout.Width(96),GUILayout.Height(96));
            if(preview!=null) GUI.DrawTexture(rect,preview,ScaleMode.ScaleToFit);
            else GUI.Label(rect,"Миниатюра готовится",EditorStyles.centeredGreyMiniLabel);
            GUILayout.Label("Исходная форма",EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(definition.displayName,EditorStyles.boldLabel);
            float previousLabelWidth=EditorGUIUtility.labelWidth;EditorGUIUtility.labelWidth=145;
            if(!definition.editableDimensions)
            {
                bool half=BlockoutCanonicalRegistry.IsHalfCylinder(definition);
                string label=definition.title=="Cylinder"||half?"Диаметр основания, м":"Сторона основания, м";
                EditorGUI.BeginChangeCheck();float side=EditorGUILayout.FloatField(label,primary.width);
                if(EditorGUI.EndChangeCheck()) {primary.width=side;primary.depth=half?side/2:side;}
                if(half) {EditorGUILayout.LabelField("Глубина (Z), м",primary.depth.ToString("0.##"));EditorGUILayout.LabelField("Диаметр кратен двум модулям; глубина = D/2.",EditorStyles.wordWrappedMiniLabel);}
            }
            else
            {
                primary.width=EditorGUILayout.FloatField("Длина (X), м",primary.width);
                primary.depth=EditorGUILayout.FloatField("Толщина (Z), м",primary.depth);
            }
            var sourceMesh=definition.geometryPrefab!=null?definition.geometryPrefab.GetComponent<MeshFilter>()?.sharedMesh:null;
            var step=definition.geometryPrefab!=null?definition.geometryPrefab.GetComponent<BlockoutSteppedGeometry>():null;
            if(step!=null)
            {
                float lower=Mathf.Min(step.totalHeight,step.lowerHeightLimit),upper=Mathf.Max(0,step.totalHeight-lower);
                EditorGUILayout.LabelField("Низ (X × Y × Z), м",$"{primary.width:0.##} × {lower:0.##} × {primary.depth:0.##}");
                draft.stepTopWidth=EditorGUILayout.FloatField("Верх: длина (X), м",draft.stepTopWidth);
                EditorGUILayout.LabelField("Верх: высота (Y), м",upper.ToString("0.##"));
                draft.stepTopDepth=EditorGUILayout.FloatField("Верх: глубина (Z), м",draft.stepTopDepth);
                EditorGUILayout.LabelField("Верх центрирован, не требует кратности модулю. При высоте 1,2 м отсутствует.",EditorStyles.wordWrappedMiniLabel);
            }
            else if(sourceMesh!=null) EditorGUILayout.LabelField("Высота источника (Y), м",sourceMesh.bounds.size.y.ToString("0.##"));
            EditorGUILayout.LabelField("Игровая высота, м","1,2 / 1,6 / 2,5");
            EditorGUILayout.LabelField("Поля справа — черновик; миниатюра обновится после применения.",EditorStyles.wordWrappedMiniLabel);
            EditorGUIUtility.labelWidth=previousLabelWidth;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }
    }

    // Совместимость для старых программных вызовов; самостоятельного пункта меню нет.
    public class BlockoutGridSettingsWindow : EditorWindow
    {
        public static void Open() => GetWindow<BlockoutGridSettingsWindow>("Сетка и блоки");
        private void OnGUI() => BlockoutGridSettingsPanel.Draw();
    }
}
