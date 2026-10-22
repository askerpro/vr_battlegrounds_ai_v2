using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Последний контакт SDK для экранов стрельбища; наблюдение не вызывает сетевые действия.
    /// Хранит последний снимок по каждой мишени (экран дорожки) и общий последний (общий экран).
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class ShootingRangePhotoCapture : MonoBehaviour
    {
        /// <summary>Последнее попадание в одну мишень.</summary>
        public sealed class Shot
        {
            public Texture2D Photo;
            public string WeaponName;
            public Vector2 OffsetMillimetres;
            public float Distance;
            public int Revision;
        }

        public static ShootingRangePhotoCapture Active { get; private set; }
        public Texture2D Photo { get; private set; }
        public string TargetName { get; private set; }
        public string WeaponName { get; private set; }
        public Vector2 OffsetMillimetres { get; private set; }
        public Vector2 MarkUv { get; private set; }
        public float Distance { get; private set; }
        public int Revision { get; private set; }
        private const int Resolution = 512;
        private static readonly Vector3 Studio = new Vector3(0,-512,0);
        private readonly List<MeshRenderer> _copies = new List<MeshRenderer>();
        private GameObject _studio;
        private Camera _camera;
        private RenderTexture _render;
        private bool _pending;
        private Vector3 _point;
        private ShootingTarget _pendingTarget;
        private readonly Dictionary<ShootingTarget, Shot> _shots = new Dictionary<ShootingTarget, Shot>();

        /// <summary>Последнее попадание в мишень; false, если в неё ещё не попадали.</summary>
        public bool TryGetShot(ShootingTarget target, out Shot shot) => _shots.TryGetValue(target, out shot);

        /// <summary>
        /// Снимок нужен только экрану, который кто-то видит. Снаряд симулирует каждая машина
        /// (см. <see cref="ShootingTarget"/>), поэтому контакт приходит и на выделенном сервере;
        /// без графического устройства (-nographics) URP не создаёт RenderTexture — захват там выключен.
        /// </summary>
        public static bool CanRender => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        private void OnEnable()
        {
            if (Active != null && Active != this) { enabled=false; GameLog.WeaponSystem.Error("[RangePhoto] Второй capture в загруженной сцене.",this);return; }
            Active=this;
            if(!CanRender){GameLog.WeaponSystem.Info("[RangePhoto] Нет графического устройства — фото попаданий не снимаются.",this);return;}
            ShootingTarget.ProjectileContact += HandleContact;
        }

        private void OnDisable()
        {
            ShootingTarget.ProjectileContact -= HandleContact;
            if(Active==this) Active=null;
            _pending=false;
            foreach(var copy in _copies)if(copy!=null)copy.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            // Общий Photo — один из снимков мишеней; освобождаются только снимки мишеней.
            foreach(var shot in _shots.Values) if(shot.Photo!=null) Destroy(shot.Photo);
            _shots.Clear();
            if(_studio!=null) Destroy(_studio);
            if(_render!=null) {_render.Release();Destroy(_render);}
        }

        private void HandleContact(ShootingTarget target,UxrProjectileSource source,RaycastHit hit)
        {
            if(target==null||source==null||target.gameObject.scene!=gameObject.scene)return;
            string id=source.name.EndsWith("_instance",StringComparison.Ordinal)?source.name.Substring(0,source.name.Length-9):source.name;
            var info=WeaponRegistry.Instance!=null?WeaponRegistry.Instance.GetById(id):null;
            Transform muzzle=source.ShotTypes.Count>0?source.ShotTypes[0].ShotSource:null;
            QueuePhoto(target,hit.point,info!=null?info.DisplayName:id,muzzle!=null?Vector3.Distance(muzzle.position,hit.point):0);
        }

        /// <summary>Замораживает реальную геометрию и точку контакта. Сам метод не вызывает игровые/сетевые действия.</summary>
        public bool QueuePhoto(ShootingTarget target,Vector3 point,string weapon,float distance)
        {
            if(!CanRender||target==null||target.Pivot==null||!Finite(point.x)||!Finite(point.y)||!Finite(point.z))return false;
            try
            {
                EnsureStudio();
                var originals=target.Pivot.GetComponentsInChildren<MeshRenderer>(false);
                while(_copies.Count<originals.Length)
                {
                    var go=new GameObject("FrozenTargetPart");go.transform.SetParent(_studio.transform,false);
                    go.AddComponent<MeshFilter>();_copies.Add(go.AddComponent<MeshRenderer>());
                }
                bool first=true;Bounds bounds=default;
                for(int i=0;i<_copies.Count;i++)
                {
                    var copy=_copies[i];bool visible=i<originals.Length&&originals[i].enabled;
                    copy.gameObject.SetActive(visible);if(!visible)continue;
                    var original=originals[i];var filter=original.GetComponent<MeshFilter>();
                    if(filter==null||filter.sharedMesh==null){copy.gameObject.SetActive(false);continue;}
                    copy.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;copy.sharedMaterials=original.sharedMaterials;
                    copy.transform.SetPositionAndRotation(Studio+original.transform.position-target.Pivot.position,original.transform.rotation);
                    copy.transform.localScale=original.transform.lossyScale;copy.shadowCastingMode=ShadowCastingMode.Off;copy.receiveShadows=false;
                    if(first){bounds=copy.bounds;first=false;}else bounds.Encapsulate(copy.bounds);
                }
                if(first)return false;
                Vector3 centre=target.Pivot.Find("RingOuter")!=null?target.Pivot.Find("RingOuter").position:target.Pivot.position;
                Vector3 delta=point-centre;
                _point=Studio+point-target.Pivot.position;
                Vector3 normal=target.Pivot.forward;
                _camera.transform.SetPositionAndRotation(bounds.center+normal*.3f,Quaternion.LookRotation(-normal,target.Pivot.up));
                OffsetMillimetres=new Vector2(Vector3.Dot(delta,_camera.transform.right),Vector3.Dot(delta,_camera.transform.up))*1000;
                _camera.orthographicSize=Mathf.Max(bounds.size.magnitude*.6f,.03f);
                TargetName=target.name;WeaponName=weapon;Distance=distance;_pendingTarget=target;_pending=true;return true;
            }
            catch(Exception exception){GameLog.WeaponSystem.Error("[RangePhoto] "+exception,this);return false;}
        }

        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        private void EnsureStudio()
        {
            if(_studio!=null)return;
            _studio=new GameObject("RangePhotoStudio") {hideFlags=HideFlags.DontSave};
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_studio,gameObject.scene);
            _camera=new GameObject("RangePhotoCamera").AddComponent<Camera>();_camera.transform.SetParent(_studio.transform,false);
            _camera.enabled=false;_camera.orthographic=true;_camera.aspect=1;_camera.nearClipPlane=.01f;_camera.farClipPlane=.6f;
            _camera.clearFlags=CameraClearFlags.SolidColor;_camera.backgroundColor=MenuTheme.Instance.Surface;
            _camera.GetUniversalAdditionalCameraData().allowXRRendering=false;
            _camera.GetUniversalAdditionalCameraData().renderShadows=false;
            _render=new RenderTexture(Resolution,Resolution,24) {name="RangePhotoRT"};_render.Create();
        }

        private void LateUpdate(){if(_pending)RenderPending();}

        /// <summary>Одна отрисовка последнего контакта за кадр; предыдущая CPU-текстура освобождается.</summary>
        public bool RenderPending()
        {
            if(!_pending||_camera==null)return false;_pending=false;
            RenderTexture previous=RenderTexture.active;Texture2D texture=null;
            try
            {
#if UNITY_EDITOR
                _camera.overrideSceneCullingMask=UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(gameObject.scene);
#endif
                if(GraphicsSettings.currentRenderPipeline!=null)
                    RenderPipeline.SubmitRenderRequest(_camera,new UniversalRenderPipeline.SingleCameraRequest {destination=_render});
                else {_camera.targetTexture=_render;_camera.Render();_camera.targetTexture=null;}
                RenderTexture.active=_render;
                texture=new Texture2D(Resolution,Resolution,TextureFormat.RGB24,false) {name="Последнее попадание"};
                texture.ReadPixels(new Rect(0,0,Resolution,Resolution),0,0,false);
                Vector3 uv=_camera.WorldToViewportPoint(_point);MarkUv=new Vector2(uv.x,uv.y);
                int cx=Mathf.RoundToInt(uv.x*(Resolution-1)),cy=Mathf.RoundToInt(uv.y*(Resolution-1));
                Color mark=MenuTheme.Instance.Accent;
                for(int y=-8;y<=8;y++)for(int x=-8;x<=8;x++)
                {
                    int radius=x*x+y*y;
                    if((radius<=4||(radius>=30&&radius<=55))&&cx+x>=0&&cy+y>=0&&cx+x<Resolution&&cy+y<Resolution)
                        texture.SetPixel(cx+x,cy+y,mark);
                }
                texture.Apply(false,false);
                Revision++;
                if(_pendingTarget!=null)
                {
                    if(!_shots.TryGetValue(_pendingTarget,out var shot)){shot=new Shot();_shots.Add(_pendingTarget,shot);}
                    if(shot.Photo!=null)Destroy(shot.Photo);
                    shot.Photo=texture;shot.WeaponName=WeaponName;shot.OffsetMillimetres=OffsetMillimetres;shot.Distance=Distance;shot.Revision=Revision;
                }
                else if(Photo!=null&&!_shots.Values.Any(s=>s.Photo==Photo))Destroy(Photo);
                Photo=texture;texture=null;return true;
            }
            catch(Exception exception){if(texture!=null)Destroy(texture);GameLog.WeaponSystem.Error("[RangePhoto] Render: "+exception,this);return false;}
            finally{RenderTexture.active=previous;foreach(var copy in _copies)copy.gameObject.SetActive(false);}
        }

        public void Clear()
        {
            if(Photo!=null&&!_shots.Values.Any(s=>s.Photo==Photo))Destroy(Photo);
            foreach(var shot in _shots.Values) if(shot.Photo!=null) Destroy(shot.Photo);
            _shots.Clear();Photo=null;_pending=false;_pendingTarget=null;Revision++;
        }
    }
}
