using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Настоящие изображения формы без зависимости от очереди AssetPreview и позиции исходного префаба.</summary>
    [InitializeOnLoad]
    public static class BlockoutThumbnails
    {
        public const int Resolution = 256;
        public const string ThumbnailFolder = "Assets/Settings/LevelDesign/BlockoutBlocks/Thumbnails";
        public static event Action Changed;
        private sealed class Entry
        {
            public GameObject source;
            public Hash128 dependencyHash;
            public Texture2D texture;
            public bool complete;
        }
        private static readonly Dictionary<BlockoutBlockDefinition,Entry> Cache = new Dictionary<BlockoutBlockDefinition,Entry>();
        private static readonly Queue<BlockoutBlockDefinition> Pending = new Queue<BlockoutBlockDefinition>();
        private static readonly HashSet<BlockoutBlockDefinition> Queued = new HashSet<BlockoutBlockDefinition>();
        private static bool generating;
        private sealed class HeightRequest {public BlockoutBlockDefinition definition;public float height;public string key;}
        private static readonly Dictionary<string,Texture2D> HeightCache=new Dictionary<string,Texture2D>();
        private static readonly Queue<HeightRequest> HeightPending=new Queue<HeightRequest>();
        public static Texture2D Request(BlockoutBlockDefinition definition,float height)
        {
            if(definition==null)return null;
            string key=definition.shapeId+":"+height.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(HeightCache.TryGetValue(key,out var image))return image;
            HeightCache[key]=null;HeightPending.Enqueue(new HeightRequest {definition=definition,height=height,key=key});return null;
        }

        static BlockoutThumbnails()
        {
            EditorApplication.update += Update;
            EditorApplication.projectChanged += Invalidate;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }
        /// <summary>Сохранённая миниатюра или кеш; при отсутствии изображения ставит единственную заявку в очередь.</summary>
        public static Texture2D Request(BlockoutBlockDefinition definition)
        {
            if(definition==null||definition.geometryPrefab==null) return null;
            if(definition.thumbnail!=null) return definition.thumbnail;
            if(Cache.TryGetValue(definition,out var entry))
            {
                if(entry.source==definition.geometryPrefab&&entry.complete) return entry.texture;
                if(entry.source!=definition.geometryPrefab)
                {
                    if(entry.texture!=null) Object.DestroyImmediate(entry.texture);
                    Cache.Remove(definition);
                }
            }
            if(Queued.Add(definition)) Pending.Enqueue(definition);
            return null;
        }
        private static void Update()
        {
            if(!generating&&HeightPending.Count>0&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var request=HeightPending.Dequeue();
                if(request.definition!=null)HeightCache[request.key]=Render(request.definition,request.height);
                Changed?.Invoke();return;
            }
            if(generating||Pending.Count==0||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode) return;
            var definition=Pending.Dequeue();Queued.Remove(definition);
            if(definition==null||definition.geometryPrefab==null||definition.thumbnail!=null) return;
            var entry=new Entry {source=definition.geometryPrefab,dependencyHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(definition.geometryPrefab)),complete=true};
            try {entry.texture=Render(definition);}
            catch(Exception error) {GameLog.UI.Error("Миниатюра формы "+definition.title+": "+error.Message);}
            Cache[definition]=entry;
            Changed?.Invoke();
        }
        /// <summary>Изолированный синхронный рендер. Возвращённая текстура принадлежит вызывающему коду.</summary>
        public static Texture2D Render(BlockoutBlockDefinition definition)
            => Render(definition,BlockoutRegistryFactory.DefaultDimensions(definition).y);
        public static Texture2D Render(BlockoutBlockDefinition definition,float height)
        {
            if(definition==null||definition.geometryPrefab==null) throw new ArgumentException("Для миниатюры нужен geometryPrefab определения.");
            var preview=new PreviewRenderUtility();Texture2D image=null;bool opened=false,rendered=false;
            try
            {
                // InstantiatePrefabInScene создаёт объект сразу в изолированной preview scene, а не в карте.
                var clone=preview.InstantiatePrefabInScene(definition.geometryPrefab);
                BlockoutRegistryFactory.ApplyDisplayMaterial(clone,definition.defaultMaterial);
                var stepped=clone.GetComponent<BlockoutSteppedGeometry>();
                if(stepped!=null)stepped.ApplyHeight(height);
                else if(definition.heightEditable&&BlockoutHeightGeometryEditor.CanResize(clone,out _))
                    clone.AddComponent<BlockoutHeightGeometry>().Initialize(BlockoutRegistryFactory.GeometryBounds(definition.geometryPrefab).size.y,height);
                foreach(var transform in clone.GetComponentsInChildren<Transform>(true)) transform.gameObject.hideFlags=HideFlags.HideAndDontSave;
                foreach(var behaviour in clone.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled=false;
                clone.transform.position=Vector3.zero;
                var bounds=RendererBounds(clone);
                clone.transform.position-=bounds.center;
                bounds=RendererBounds(clone);
                var camera=preview.camera;
                camera.orthographic=true;camera.aspect=1;camera.allowHDR=false;camera.useOcclusionCulling=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.19f,.20f,.22f,1);
                camera.transform.rotation=Quaternion.Euler(25,-35,0);
                float horizontal=0,vertical=0;
                foreach(var corner in Corners(bounds))
                {
                    Vector3 delta=corner-bounds.center;
                    horizontal=Mathf.Max(horizontal,Mathf.Abs(Vector3.Dot(delta,camera.transform.right)));
                    vertical=Mathf.Max(vertical,Mathf.Abs(Vector3.Dot(delta,camera.transform.up)));
                }
                float radius=Mathf.Max(.05f,bounds.extents.magnitude),distance=radius*4+1;
                camera.orthographicSize=Mathf.Max(.05f,Mathf.Max(horizontal,vertical))*1.15f;
                camera.transform.position=bounds.center-camera.transform.forward*distance;
                camera.nearClipPlane=.01f;camera.farClipPlane=distance+radius*4+1;
                preview.ambientColor=new Color(.55f,.55f,.55f,1);
                preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-35,0);
                preview.lights[1].intensity=.8f;preview.lights[1].transform.rotation=Quaternion.Euler(340,145,0);
                preview.BeginStaticPreview(new Rect(0,0,Resolution,Resolution));opened=true;
                preview.Render(true,false);rendered=true;
            }
            finally
            {
                try
                {
                    if(opened) image=preview.EndStaticPreview();
                    if(!rendered&&image!=null) {Object.DestroyImmediate(image);image=null;}
                }
                finally {preview.Cleanup();}
            }
            image.name="Форма "+definition.shapeId;image.hideFlags=HideFlags.HideAndDontSave;return image;
        }
        private static Bounds RendererBounds(GameObject root)
        {
            bool found=false;var bounds=new Bounds();
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy) continue;
                // Свежая матрица клона исключает кеш bounds исходного prefab asset с далёкой позицией.
                foreach(var corner in Corners(renderer.localBounds))
                {
                    Vector3 point=renderer.transform.TransformPoint(corner);
                    if(!found) {bounds=new Bounds(point,Vector3.zero);found=true;} else bounds.Encapsulate(point);
                }
            }
            if(!found) throw new InvalidOperationException("У формы нет активных Renderer для геометрической миниатюры.");
            return bounds;
        }
        private static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            for(int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++)
                yield return new Vector3(x==0?bounds.min.x:bounds.max.x,y==0?bounds.min.y:bounds.max.y,z==0?bounds.min.z:bounds.max.z);
        }
        /// <summary>Явная генерация под замком Unity. Записывает только PNG миниатюр и ссылки выбранного реестра.</summary>
        public static void GeneratePersistent(BlockoutBlockRegistry registry)
        {
            if(registry==null) throw new ArgumentNullException(nameof(registry));
            GeneratePersistentDefinitions(registry.Definitions);
        }
        /// <summary>Перегенерация только указанных карточек, без пересборки реестра.</summary>
        public static void GeneratePersistentDefinitions(IEnumerable<BlockoutBlockDefinition> definitions)
        {
            if(definitions==null)throw new ArgumentNullException(nameof(definitions));
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Миниатюры генерируются вне Play Mode.");
            EnsureFolder(ThumbnailFolder);generating=true;
            try
            {
                foreach(var definition in definitions)
                {
                    if(definition==null||definition.geometryPrefab==null) continue;
                    string name=Regex.Replace(definition.shapeId??"","[^A-Za-z0-9_-]","_");
                    if(string.IsNullOrEmpty(name)) throw new InvalidOperationException("У определения отсутствует стабильный shapeId.");
                    string path=ThumbnailFolder+"/"+name+".png";Texture2D image=Render(definition);
                    try {File.WriteAllBytes(path,image.EncodeToPNG());} finally {Object.DestroyImmediate(image);}
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                    var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                    if(importer!=null)
                    {
                        importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=false;importer.maxTextureSize=Resolution;
                        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                    }
                    definition.thumbnail=AssetDatabase.LoadAssetAtPath<Texture2D>(path);EditorUtility.SetDirty(definition);AssetDatabase.SaveAssetIfDirty(definition);
                }
            }
            finally {generating=false;Invalidate();}
        }
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static void Invalidate() {Cleanup();Changed?.Invoke();}
        public static void InvalidateSource()=>Invalidate();
        public static void Cleanup()
        {
            foreach(var image in HeightCache.Values)if(image!=null)Object.DestroyImmediate(image);
            HeightCache.Clear();HeightPending.Clear();
            foreach(var entry in Cache.Values) if(entry.texture!=null) Object.DestroyImmediate(entry.texture);
            Cache.Clear();Pending.Clear();Queued.Clear();
        }
    }
}
