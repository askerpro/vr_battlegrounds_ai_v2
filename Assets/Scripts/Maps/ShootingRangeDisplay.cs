using TMPro;
using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>Общий экран рядом со стрелком: последнее полученное попадание из локального SDK replay.</summary>
    [DefaultExecutionOrder(10010)]
    public sealed class ShootingRangeDisplay : MonoBehaviour
    {
        [SerializeField] private ShootingRangePhotoCapture _capture;
        [SerializeField] private Renderer _screen;
        [SerializeField] private TMP_Text _caption;
        private int _revision=-1;
        private MaterialPropertyBlock _properties;
        public ShootingRangePhotoCapture Capture=>_capture;
        public Renderer Screen=>_screen;

        private void LateUpdate()
        {
            if(_capture==null||_screen==null||_capture.Revision==_revision)return;
            _revision=_capture.Revision;
            _properties=_properties??new MaterialPropertyBlock();_screen.GetPropertyBlock(_properties);
            Texture image=_capture.Photo!=null?(Texture)_capture.Photo:Texture2D.blackTexture;
            _properties.SetTexture("_BaseMap",image);_properties.SetTexture("_MainTex",image);
            _screen.SetPropertyBlock(_properties);
            if(_caption!=null)_caption.text=_capture.Photo!=null
                ? _capture.TargetName+" · "+_capture.Distance.ToString("F1")+" м\nX "+_capture.OffsetMillimetres.x.ToString("+0.0;-0.0;0")+" мм · Y "+_capture.OffsetMillimetres.y.ToString("+0.0;-0.0;0")+" мм"
                : "Стрельбище\nПопадите в щит — здесь появится точка попадания";
        }
    }
}
