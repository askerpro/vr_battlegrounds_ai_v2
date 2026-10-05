using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.Core;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Чистые ракурсы реального тела и кистей; не меняет позу или материалы актора.</summary>
    public sealed class BotStandVisualCapture : IDisposable
    {
        private readonly Camera _camera;
        public BotStandVisualCapture()
        {
            var go = new GameObject("BotStandCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            _camera = go.AddComponent<Camera>();
            _camera.enabled = false; _camera.useOcclusionCulling = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.52f, 0.56f, 0.62f);
            _camera.nearClipPlane = 0.01f; _camera.farClipPlane = 80;
            _camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
        }

        public string[] Capture(PlayerController player, string folder, string label)
        {
            if (player == null) throw new ArgumentException("Нет тела для снимка.");
            var body = player.GetComponent<BotBody>(); var avatar = player.GetComponent<UxrAvatar>();
            if (body == null || avatar == null) throw new ArgumentException("Нет BotBody/UxrAvatar.");
            Vector3 focus = body.Feet + Vector3.up;
            Quaternion yaw = body.BodyRotation;
            Directory.CreateDirectory(folder);
            var files = new System.Collections.Generic.List<string>();
            files.Add(Shot(folder, label + "-front.png", focus, focus + yaw * new Vector3(0, 0.15f, 3.5f), 40));
            files.Add(Shot(folder, label + "-side.png", focus, focus + yaw * new Vector3(3.5f, 0.15f, 0), 40));
            files.Add(Shot(folder, label + "-angle.png", focus, focus + yaw * new Vector3(2.4f, 0.8f, 2.4f), 40));
            foreach (var side in new[] { UxrHandSide.Right, UxrHandSide.Left })
            {
                Transform hand = avatar.GetHandBone(side);
                if (hand == null) throw new ArgumentException("Нет кости " + side);
                files.Add(Shot(folder, label + "-" + side + ".png", hand.position,
                    hand.position + yaw * new Vector3(side == UxrHandSide.Right ? 0.32f : -0.32f, 0.15f, 0.38f), 48));
            }
            return files.ToArray();
        }

        private string Shot(string folder, string name, Vector3 focus, Vector3 position, float fov)
        {
            Vector3 delta=position-focus;
            var blockers=Physics.RaycastAll(focus,delta.normalized,delta.magnitude,LayerMask.GetMask("Default","Ground"),QueryTriggerInteraction.Ignore)
                .Where(h=>h.collider.attachedRigidbody==null && h.collider.GetComponentInParent<UxrAvatar>()==null).OrderBy(h=>h.distance).ToArray();
            if(blockers.Length>0)
            {
                position=focus+delta.normalized*Mathf.Max(.08f,blockers[0].distance-.15f);
                fov=Mathf.Min(120,2*Mathf.Atan(Mathf.Tan(fov*Mathf.Deg2Rad*.5f)*delta.magnitude/Vector3.Distance(focus,position))*Mathf.Rad2Deg);
            }
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
            _camera.fieldOfView = fov;
            RenderTexture previous = RenderTexture.active;
            var target = new RenderTexture(640, 640, 24);
            var image = new Texture2D(640, 640, TextureFormat.RGB24, false);
            try
            {
                _camera.targetTexture = target; _camera.Render();
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); image.Apply();
                var pixels=image.GetPixels32(); var first=pixels[0];
                if(!pixels.Any(p=>System.Math.Abs(p.r-first.r)>2 || System.Math.Abs(p.g-first.g)>2 || System.Math.Abs(p.b-first.b)>2))
                    throw new InvalidOperationException("Одноцветный ракурс " + name + ": изображение не подтверждает визуал.");
                string path = Path.Combine(folder, name); File.WriteAllBytes(path, image.EncodeToPNG()); return path;
            }
            finally
            {
                _camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }

        public void Dispose() { if (_camera != null) UnityEngine.Object.DestroyImmediate(_camera.gameObject); }
    }
}
