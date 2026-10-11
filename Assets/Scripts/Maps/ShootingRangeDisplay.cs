using TMPro;
using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Экран рядом со стрелком: последнее попадание из локального SDK replay.
    /// С заданной мишенью — экран дорожки (только её попадания), без неё — общий экран (последнее попадание в любой щит).
    /// </summary>
    [DefaultExecutionOrder(10010)]
    public sealed class ShootingRangeDisplay : MonoBehaviour
    {
        [SerializeField] private ShootingRangePhotoCapture _capture;
        [SerializeField] private Renderer _screen;
        [SerializeField] private TMP_Text _caption;
        [Tooltip("Мишень дорожки; пусто — общий экран всех мишеней.")]
        [SerializeField] private ShootingTarget _target;
        [Tooltip("Подпись экрана дорожки до первого попадания.")]
        [SerializeField] private string _laneTitle;
        private int _revision=-1;
        private MaterialPropertyBlock _properties;
        public ShootingRangePhotoCapture Capture=>_capture;
        public Renderer Screen=>_screen;
        public ShootingTarget Target=>_target;

        private void LateUpdate()
        {
            if(_capture==null||_screen==null)return;
            Texture2D photo;string weaponLine;Vector2 offset;float distance;int revision;string title;
            if(_target!=null)
            {
                if(!_capture.TryGetShot(_target,out var shot)){photo=null;offset=default;distance=0;revision=0;}
                else {photo=shot.Photo;offset=shot.OffsetMillimetres;distance=shot.Distance;revision=shot.Revision;}
                title=string.IsNullOrEmpty(_laneTitle)?_target.name:_laneTitle;
            }
            else {photo=_capture.Photo;offset=_capture.OffsetMillimetres;distance=_capture.Distance;revision=_capture.Revision;title=_capture.TargetName;}
            if(revision==_revision)return;
            _revision=revision;
            _properties=_properties??new MaterialPropertyBlock();_screen.GetPropertyBlock(_properties);
            Texture image=photo!=null?(Texture)photo:Texture2D.blackTexture;
            _properties.SetTexture("_BaseMap",image);_properties.SetTexture("_MainTex",image);
            _screen.SetPropertyBlock(_properties);
            weaponLine=distance.ToString("F1")+" м\nX "+offset.x.ToString("+0.0;-0.0;0")+" мм · Y "+offset.y.ToString("+0.0;-0.0;0")+" мм";
            if(_caption!=null)_caption.text=photo!=null
                ? title+" · "+weaponLine
                : _target!=null ? title+"\nПопадите в щит" : "Стрельбище\nПопадите в щит — здесь появится точка попадания";
        }
    }
}
