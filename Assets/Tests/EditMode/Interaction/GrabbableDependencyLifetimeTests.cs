using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>Все кэши SDK исключают уничтоженные детали перед любым потребителем.</summary>
    public class GrabbableDependencyLifetimeTests
    {
        [TestCase("AllParents")]
        [TestCase("ParentLookAts")]
        [TestCase("AllChildren")]
        [TestCase("AllChildrenLookAts")]
        [TestCase("DirectChildrenLookAts")]
        public void Уничтоженная_деталь_не_выдаётся_из_кэша(string propertyName)
        {
            var root = new GameObject("DependencyRoot");
            var alive = new GameObject("AlivePart");
            var dead = new GameObject("DestroyedPart");
            try
            {
                var grabbable = root.AddComponent<UxrGrabbableObject>();
                var livePart = alive.AddComponent<UxrGrabbableObject>();
                var deadPart = dead.AddComponent<UxrGrabbableObject>();
                var property = typeof(UxrGrabbableObject).GetProperty(propertyName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(property);
                property.SetValue(grabbable, new List<UxrGrabbableObject> { deadPart, livePart });
                Object.DestroyImmediate(dead);
                Assert.IsTrue(deadPart == null, "Контроль: компонент действительно уничтожен Unity.");

                var dependencies = (List<UxrGrabbableObject>)property.GetValue(grabbable);
                CollectionAssert.AreEqual(new[] { livePart }, dependencies,
                    "Телепорт, ограничения и запросы хвата не должны видеть мёртвую деталь.");
                CollectionAssert.AreEqual(new[] { livePart }, (List<UxrGrabbableObject>)property.GetValue(grabbable),
                    "Очистка должна сохраняться при повторном чтении.");
            }
            finally
            {
                if (dead != null) Object.DestroyImmediate(dead);
                Object.DestroyImmediate(alive);
                Object.DestroyImmediate(root);
            }
        }
    }
}
