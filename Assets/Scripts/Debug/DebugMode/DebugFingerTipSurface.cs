using System.Collections.Generic;
using UltimateXR.UI.UnityInputModule;
using UltimateXR.UI.UnityInputModule.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Прозрачная подложка под UI для диагностики пальца. Пустой обработчик позволяет
    /// SDK считать фон интерактивным. Создаётся и удаляется вместе с DebugFingerTipRays.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugFingerTipSurface : MonoBehaviour, IPointerDownHandler
    {
        private readonly List<Graphic> _mutedGraphics = new List<Graphic>();
        private UxrCanvas _canvas;

        internal static DebugFingerTipSurface Create(UxrCanvas canvas)
        {
            var go = new GameObject("DebugFingerTipSurface", typeof(RectTransform));
            go.SetActive(false);
            go.layer = canvas.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = go.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            image.canvasRenderer.cullTransparentMesh = false;

            // SDK подавляет свой стандартный виброотклик при наличии UxrControlInput.
            // Компонент выключен: его обработчики, drag и глобальные события не работают.
            // Активен только наш пустой IPointerDownHandler.
            go.AddComponent<UxrControlInput>().enabled = false;
            var surface = go.AddComponent<DebugFingerTipSurface>();
            surface._canvas = canvas;
            go.SetActive(true);
            surface.Refresh();
            return surface;
        }

        public void OnPointerDown(PointerEventData eventData) { }

        internal void Refresh()
        {
            // Неподвижные изображения фона могут перекрывать подложку в raycast.
            // Убираем только цели без обработчиков SDK/клика; кнопки и ScrollRect
            // сохраняют свой порядок и обработчики. Новые элементы после перестройки
            // меню проверяются здесь же; исходные raycastTarget восстанавливаются.
            // GraphicRegistry возвращает IndexedSet без GetEnumerator — как raycaster
            // SDK, читаем его по индексу. raycastTarget меняет отдельный список целей.
            IList<Graphic> graphics = GraphicRegistry.GetGraphicsForCanvas(_canvas.UnityCanvas);
            for (int i = 0; i < graphics.Count; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.raycastTarget || graphic.gameObject == gameObject
                    || UxrPointerInputModule.IsInteractive(graphic.gameObject)
                    || ExecuteEvents.GetEventHandler<IPointerClickHandler>(graphic.gameObject) != null) continue;
                graphic.raycastTarget = false;
                _mutedGraphics.Add(graphic);
            }
        }

        internal void Dispose()
        {
            // Явное освобождение не зависит от того, вызывался ли Awake/OnDisable
            // (например, при проверке в изолированной preview-сцене редактора).
            RestoreGraphics();
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private void OnDisable() => RestoreGraphics();
        private void OnDestroy() => RestoreGraphics();

        private void RestoreGraphics()
        {
            foreach (Graphic graphic in _mutedGraphics)
                if (graphic != null) graphic.raycastTarget = true;
            _mutedGraphics.Clear();
        }
    }
}
