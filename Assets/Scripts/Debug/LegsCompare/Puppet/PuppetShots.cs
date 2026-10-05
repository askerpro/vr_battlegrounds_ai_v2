using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Снимки стенда <see cref="AvatarPuppetStand"/>: лист «строка — аватар, столбцы — спереди, сбоку, сзади-сбоку»,
    /// камера смотрит на таз каждого аватара. Своя камера, рисует в текстуру — Game view не нужен.
    /// </summary>
    public sealed class PuppetShots
    {
        private const int Width = 360, Height = 480;
        private static readonly Vector3[] Views = { new Vector3(0f, 0.35f, 3.2f), new Vector3(3.2f, 0.35f, 0f), new Vector3(2.3f, 0.9f, -2.3f) };

        private readonly Camera _camera;
        private readonly RenderTexture _target;
        private Texture2D _sheet;

        public PuppetShots()
        {
            _camera = new GameObject("PuppetStand_ShotCamera").AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 40f;
            _camera.nearClipPlane = 0.05f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.55f, 0.6f, 0.68f);
            _target = new RenderTexture(Width, Height, 24);
        }

        /// <summary>Лист по аватарам: <paramref name="hips"/> — таз каждого аватара, куда смотрит камера.</summary>
        public void Capture(string path, IReadOnlyList<Transform> hips, Quaternion standRotation)
        {
            if (hips.Count == 0) return;
            if (_sheet == null || _sheet.height != Height * hips.Count)
                _sheet = new Texture2D(Width * Views.Length, Height * hips.Count, TextureFormat.RGB24, false);

            for (int row = 0; row < hips.Count; row++)
            {
                Vector3 focus = new Vector3(hips[row].position.x, 0.8f, hips[row].position.z);
                for (int col = 0; col < Views.Length; col++)
                {
                    _camera.transform.position = focus + standRotation * Views[col];
                    _camera.transform.LookAt(focus + Vector3.down * 0.05f);
                    _camera.targetTexture = _target;
                    _camera.Render();
                    RenderTexture.active = _target;
                    _sheet.ReadPixels(new Rect(0, 0, Width, Height), col * Width, (hips.Count - 1 - row) * Height);
                }
            }

            RenderTexture.active = null;
            _camera.targetTexture = null;
            _sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, _sheet.EncodeToPNG());
        }

        public void Dispose()
        {
            if (_camera != null) Object.Destroy(_camera.gameObject);
            if (_target != null) _target.Release();
            if (_sheet != null) Object.Destroy(_sheet);
        }
    }
}
