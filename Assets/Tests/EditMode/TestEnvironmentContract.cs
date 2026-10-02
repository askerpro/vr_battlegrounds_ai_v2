using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests
{
    /// <summary>Предусловия интеграционного стенда: ошибка окружения отделена от ошибки проверяемой механики.</summary>
    internal static class TestEnvironmentContract
    {
        internal static void ResetDestroyedSingleton<T>() where T : Component
        {
            // В EditMode OnDestroy SDK не всегда выполняется. Уничтоженная Unity-ссылка
            // проходит CLR-проверку «is null» в SDK и мешает Instance восстановить стенд.
            var type = typeof(T);
            System.Reflection.FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField("s_instance", System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }
            Assert.That(field, Is.Not.Null, "[TestEnvironment] Изменился контракт singleton SDK.");
            var cached = field.GetValue(null) as Object;
            if (!ReferenceEquals(cached, null) && cached == null)
                field.SetValue(null, null);
        }

        internal static T ExactlyOneInScene<T>() where T : Component
        {
            var objects = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(objects.Length, Is.EqualTo(1),
                $"[TestEnvironment] {typeof(T).Name}: требуется 1, найдено {objects.Length}: " +
                string.Join(", ", objects.Select(o => o.gameObject.scene.name + "/" + o.name)));
            return objects[0];
        }

        internal static void IsActive(Component component)
        {
            Assert.That(component != null && component.gameObject.activeInHierarchy,
                Is.True, "[TestEnvironment] Обязательный объект отсутствует, уничтожен или выключен.");
        }
    }
}
