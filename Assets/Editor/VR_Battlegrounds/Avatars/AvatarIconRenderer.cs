using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    ///     Иконка аватара для меню выбора (<see cref="AvatarData.icon" />): снимок груди и головы префаба
    ///     в превью-сцене. Рецепт — <c>.claude/skills/setup-avatar/game-variant.md</c>, раздел «Регистрация»:
    ///     два направленных света, камера fov≈22 на грудь-голову, 512×512, фон как у прежних иконок.
    ///     Модели кистей UltimateXR прячутся — в игре их показывают позы, а в bind-позе они торчат.
    /// </summary>
    internal static class AvatarIconRenderer
    {
        private const string IconFolder = "Assets/Art/Textures/AvatarsIcons/";
        private const int Size = 512;
        private const float FieldOfView = 22f;
        private const float FrameHeight = 0.85f;   // метров кадра по вертикали: грудь и голова
        private static readonly Color Background = new Color(0.13f, 0.16f, 0.20f);

        [MenuItem("Tools/VR Battlegrounds/Avatars/Render Icon For Selected AvatarData")]
        private static void RenderSelected()
        {
            foreach (AvatarData data in Selection.objects.OfType<AvatarData>())
            {
                Render(data);
            }
        }

        [MenuItem("Tools/VR Battlegrounds/Avatars/Render Icon For Selected AvatarData", true)]
        private static bool CanRenderSelected() => Selection.objects.OfType<AvatarData>().Any();

        /// <summary>Снимает иконку префаба <paramref name="data" />, сохраняет PNG-спрайт и назначает его.</summary>
        public static string Render(AvatarData data)
        {
            if (data == null || data.prefab == null) return null;

            string path = IconFolder + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(data)) + ".png";
            byte[] png = Capture(data.prefab);
            File.WriteAllBytes(path, png);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            data.icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            GameLog.Debug.Info($"[AvatarIconRenderer] {data.name}: иконка {path}.");
            return path;
        }

        /// <summary>
        ///     Съёмка через <see cref="PreviewRenderUtility" />: камера собственной превью-сцены с URP
        ///     рендерит только так (прямой <c>Camera.Render</c> в <c>NewPreviewScene</c> даёт чёрный кадр).
        /// </summary>
        private static byte[] Capture(GameObject prefab)
        {
            var preview = new PreviewRenderUtility();
            Texture2D readback = null;

            try
            {
                GameObject avatar = preview.InstantiatePrefabInScene(prefab);
                avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                foreach (UxrHandIntegration hands in avatar.GetComponentsInChildren<UxrHandIntegration>(true))
                {
                    hands.gameObject.SetActive(false);
                }

                foreach (LODGroup group in avatar.GetComponentsInChildren<LODGroup>(true))
                {
                    group.ForceLOD(0);
                }

                Bounds body = BodyBounds(avatar);
                Vector3 focus = new Vector3(body.center.x, body.max.y - FrameHeight * 0.5f, body.center.z);
                float distance = FrameHeight * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);

                Camera camera = preview.camera;
                camera.fieldOfView = FieldOfView;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;

                // Аватар смотрит вдоль +Z корня — камера спереди, лицом к нему.
                camera.transform.position = focus + avatar.transform.forward * distance;
                camera.transform.LookAt(focus);

                preview.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[1].transform.rotation = Quaternion.Euler(20f, -140f, 0f);
                preview.lights[1].intensity = 0.5f;
                preview.ambientColor = new Color(0.35f, 0.35f, 0.38f);

                preview.BeginStaticPreview(new Rect(0, 0, Size, Size));
                preview.Render(true);
                Texture2D rendered = preview.EndStaticPreview();

                // EndStaticPreview отдаёт текстуру размера с учётом масштаба экрана — приводим к Size.
                readback = Resize(rendered, Size);
                if (readback != rendered) Object.DestroyImmediate(rendered);
                return readback.EncodeToPNG();
            }
            finally
            {
                preview.Cleanup();
                if (readback != null) Object.DestroyImmediate(readback);
            }
        }

        private static Texture2D Resize(Texture2D source, int size)
        {
            if (source.width == size && source.height == size) return source;

            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(size, size, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        /// <summary>Габарит тела — без кистей UltimateXR и без отключённых объектов.</summary>
        private static Bounds BodyBounds(GameObject avatar)
        {
            Renderer[] renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                                         .Where(r => r.GetComponentInParent<UxrHandIntegration>(true) == null)
                                         .Cast<Renderer>().ToArray();

            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.up, Vector3.one * 2f);
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }
    }
}
