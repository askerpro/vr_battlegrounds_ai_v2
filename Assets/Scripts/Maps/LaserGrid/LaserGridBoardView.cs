using TMPro;
using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Одно табло в лазерной сетке: рамка цвета лазеров, тёмная полупрозрачная подложка и один TMP-текст
    /// (заголовок, крупное и пояснение — размерами внутри одной строки, <see cref="LaserGridBoardText.ToRichText"/>).
    ///
    /// <para>
    /// Создаётся кодом (<see cref="LaserGridScreens"/>), без канваса: подложка — <c>SpriteRenderer</c> со
    /// встроенным материалом спрайтов (шейдер <c>Sprites/Default</c> в Always Included — в сборку попадёт
    /// без ссылок из ассетов), текст — <c>TextMeshPro</c> шрифтом TMP по умолчанию (Roboto Condensed,
    /// <c>Docs/ui-fonts.md</c>). Коллайдеров нет, объект не статичный — окклюдером не станет и пули
    /// не ловит. Текст присваивается только при изменении.
    /// </para>
    /// </summary>
    public sealed class LaserGridBoardView
    {
        /// <summary>Как лазеры сетки (<c>Shaders/SpawnZoneLaser</c>).</summary>
        public static readonly Color FrameColor = new Color(1f, 0.23f, 0.19f, 0.85f);
        public static readonly Color BackgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.62f);

        private const float FrameThickness = 0.025f;
        private const float TextPadding = 0.06f;

        private static Sprite s_white;

        public GameObject Root { get; }
        public TextMeshPro Text { get; }
        public LaserGridScreenPose Pose { get; }

        private string _shown;

        private LaserGridBoardView(GameObject root, TextMeshPro text, LaserGridScreenPose pose)
        {
            Root = root;
            Text = text;
            Pose = pose;
        }

        /// <summary>Текст, который сейчас на табло (null — ещё не задан).</summary>
        public string Shown => _shown;

        /// <summary>Пишет текст в TMP, только если он другой.</summary>
        public bool SetText(string richText)
        {
            if (richText == _shown) return false;
            _shown = richText;
            Text.text = richText;
            return true;
        }

        public static LaserGridBoardView Create(Transform parent, LaserGridScreenPose pose, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pose.Position, pose.Rotation);

            // Вперёд (+Z) смотрит наружу из зоны, зритель — внутри: всё, что ближе к нему, — по −Z.
            Quad(root.transform, "Frame", pose.Width, pose.Height, 0f, FrameColor, 0);
            Quad(root.transform, "Background", pose.Width - 2f * FrameThickness, pose.Height - 2f * FrameThickness,
                 -0.003f, BackgroundColor, 1);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.006f);

            var text = textGo.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = new Vector2(pose.Width - 2f * TextPadding, pose.Height - 2f * TextPadding);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.3f;
            // Крупная строка — четверть высоты табло (размер TMP в 3D: 10 = 1 м строки).
            text.fontSizeMax = pose.Height * 4.2f;
            text.lineSpacing = -10f;
            text.sortingOrder = 2;
            text.text = string.Empty;

            return new LaserGridBoardView(root, text, pose);
        }

        private static void Quad(Transform parent, string name, float width, float height, float z, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.transform.localScale = new Vector3(Mathf.Max(0.01f, width), Mathf.Max(0.01f, height), 1f);

            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = WhiteSprite();
            sprite.color = color;
            sprite.sortingOrder = order;
            sprite.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sprite.receiveShadows = false;
        }

        /// <summary>Белый спрайт 1×1 м — один на всю игру.</summary>
        private static Sprite WhiteSprite()
        {
            if (s_white != null) return s_white;
            Texture2D tex = Texture2D.whiteTexture;
            s_white = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            s_white.name = "LaserGridWhite";
            return s_white;
        }
    }
}
