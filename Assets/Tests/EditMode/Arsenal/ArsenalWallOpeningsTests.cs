using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Проверяет реальные меши и коллайдеры перестраиваемых ниш на изолированных кубических стенах.
    /// Ловит оставшиеся старые отверстия после переноса маски, дубли сегментов и потерю исходных флагов.
    /// </summary>
    public sealed class ArsenalWallOpeningsTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Cut_LeavesApertureEmptyAndPreservesSolidWallInRenderingAndPhysics(bool alongZ)
        {
            var block = CreateBlock(Vector3.zero, alongZ);
            CreateOpening(block, alongZ);

            Apply();

            Assert.That(block.GetComponent<Renderer>().enabled, Is.False, "Исходный сплошной меш закрывает нишу.");
            Assert.That(block.GetComponent<BoxCollider>().enabled, Is.False, "Исходный коллайдер закрывает нишу.");
            var parts = Parts(block);
            Assert.That(parts.Length, Is.EqualTo(3), "Прямоугольная ниша оставляет две боковые части и перемычку.");
            foreach (Transform part in parts)
            {
                var renderer = part.GetComponent<Renderer>();
                var collider = part.GetComponent<BoxCollider>();
                Assert.That(renderer.enabled && collider.enabled, Is.True, part.name);
                Assert.That(Vector3.Distance(renderer.bounds.center, collider.bounds.center), Is.LessThan(0.001f), part.name);
                Assert.That(Vector3.Distance(renderer.bounds.size, collider.bounds.size), Is.LessThan(0.001f), part.name);
            }

            AssertCoverage(parts, block.transform.TransformPoint(new Vector3(0f, -1f / 6f, 0f)), false, "центр ниши");
            AssertCoverage(parts, block.transform.TransformPoint(alongZ
                ? new Vector3(0f, -1f / 6f, -1f / 3f) : new Vector3(-1f / 3f, -1f / 6f, 0f)), true, "левая часть");
            AssertCoverage(parts, block.transform.TransformPoint(alongZ
                ? new Vector3(0f, -1f / 6f, 1f / 3f) : new Vector3(1f / 3f, -1f / 6f, 0f)), true, "правая часть");
            AssertCoverage(parts, block.transform.TransformPoint(new Vector3(0f, 0.4f, 0f)), true, "перемычка");
        }

        [Test]
        public void MovingMask_RestoresPreviousWallAndCutsTheNewWall()
        {
            var first = CreateBlock(new Vector3(-4f, 0f, 0f));
            var second = CreateBlock(new Vector3(4f, 0f, 0f));
            var opening = CreateOpening(first);
            Apply();
            Assert.That(Parts(first).Length, Is.GreaterThan(0), "Исходная ниша должна существовать до переноса.");

            opening.transform.position += Vector3.right * 8f;
            Apply();

            AssertRestored(first);
            Assert.That(Parts(second).Length, Is.EqualTo(3), "Новая позиция маски не прорезала вторую стену.");
            Assert.That(second.GetComponent<BoxCollider>().enabled, Is.False);
        }

        [Test]
        public void RemovingMask_RestoresWallAndDeletesGeneratedParts()
        {
            var block = CreateBlock(Vector3.zero);
            var opening = CreateOpening(block);
            Apply();
            Assert.That(Parts(block).Length, Is.GreaterThan(0));

            UnityEngine.Object.DestroyImmediate(opening.gameObject);
            Apply();

            AssertRestored(block);
        }

        [Test]
        public void RepeatedApply_DoesNotDuplicatePartsOrChangeTheirGeometry()
        {
            var block = CreateBlock(Vector3.zero);
            CreateOpening(block);
            Apply();
            var first = Parts(block).Select(p => p.GetComponent<Renderer>().bounds).OrderBy(b => b.center.x).ToArray();

            Apply();
            Apply();

            Assert.That(block.transform.Cast<Transform>().Count(t => t.name == "OpeningBlocks"), Is.EqualTo(1));
            var repeated = Parts(block).Select(p => p.GetComponent<Renderer>().bounds).OrderBy(b => b.center.x).ToArray();
            Assert.That(repeated.Length, Is.EqualTo(first.Length));
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(Vector3.Distance(first[i].center, repeated[i].center), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(first[i].size, repeated[i].size), Is.LessThan(0.001f));
            }
        }

        [Test]
        public void RemovingMask_PreservesOriginallyDisabledRendererAndCollider()
        {
            var block = CreateBlock(Vector3.zero);
            block.GetComponent<Renderer>().enabled = false;
            var disabled = block.gameObject.AddComponent<BoxCollider>();
            disabled.enabled = false;
            var opening = CreateOpening(block);
            Apply();
            Assert.That(Parts(block).Length, Is.GreaterThan(0));

            UnityEngine.Object.DestroyImmediate(opening.gameObject);
            Apply();

            Assert.That(block.transform.Find("OpeningBlocks"), Is.Null);
            Assert.That(block.GetComponent<Renderer>().enabled, Is.False, "Нельзя включать ранее скрытый исходный меш.");
            Assert.That(block.GetComponents<BoxCollider>()[0].enabled, Is.True, "Исходный включённый коллайдер потерял состояние.");
            Assert.That(disabled.enabled, Is.False, "Нельзя включать ранее отключённый коллайдер.");
        }

        [Test]
        public void Rebuild_KeepsAuthoredWallStationAndOpeningTransformsUnchanged()
        {
            var block = CreateBlock(Vector3.zero);
            var station = new GameObject("FixtureInactiveStation");
            station.SetActive(false);
            SceneManager.MoveGameObjectToScene(station, _scene);
            station.AddComponent<Mirror.NetworkIdentity>();
            var anchor = station.AddComponent<ArsenalStationAnchor>();
            var standing = Child(station.transform, "StandingPoint", new Vector3(0f, 0f, -0.6f));
            var facing = Child(station.transform, "ArenaFacing", Vector3.zero);
            anchor.Configure(station.GetComponent<ArsenalWallController>(), null, standing, facing);

            var opening = CreateOpening(block);
            opening.transform.SetParent(station.transform, true);
            opening.GetComponent<BoxCollider>().size = new Vector3(1.2f, 2f, 2f);
            var unmarked = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(unmarked, _scene);
            unmarked.name = "FixtureUnmarkedGeometry";
            unmarked.transform.position = new Vector3(0.25f, 1.5f, 0f);
            var fixedTransforms = new[] { block.transform, station.transform, standing, facing, opening.transform, unmarked.transform };
            var positions = fixedTransforms.Select(t => t.position).ToArray();
            var rotations = fixedTransforms.Select(t => t.rotation).ToArray();
            Assert.That(block.GetComponent<Renderer>().bounds.min.z, Is.EqualTo(-0.15f).Within(0.001f),
                "Fixture задаёт авторскую позицию исходной стены.");

            Apply();

            Assert.That(block.GetComponent<Renderer>().bounds.min.z, Is.EqualTo(-0.15f).Within(0.001f),
                "Перестройка ниши не должна перемещать авторскую стену.");
            Assert.That(Parts(block).Length, Is.GreaterThan(0), "Должна сохраняться ниша.");
            foreach (var part in Parts(block))
            {
                Assert.That(part.GetComponent<Renderer>().bounds.min.z, Is.EqualTo(-0.15f).Within(0.001f), part.name + ": меш");
                Assert.That(part.GetComponent<BoxCollider>().bounds.min.z, Is.EqualTo(-0.15f).Within(0.001f), part.name + ": физика");
            }
            for (int i = 0; i < fixedTransforms.Length; i++)
            {
                Assert.That(Vector3.Distance(fixedTransforms[i].position, positions[i]), Is.LessThan(0.001f),
                    fixedTransforms[i].name + ": авторскую позицию нельзя менять при перестройке стены.");
                Assert.That(Quaternion.Angle(fixedTransforms[i].rotation, rotations[i]), Is.LessThan(0.001f),
                    fixedTransforms[i].name + ": авторский поворот нельзя менять при перестройке стены.");
            }
        }

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        private SpawnZoneBoundaryBlock CreateBlock(Vector3 offset, bool alongZ = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.name = "FixtureBoundaryBlock";
            go.transform.position = offset + Vector3.up * 1.5f;
            go.transform.localScale = alongZ ? new Vector3(0.3f, 3f, 6f) : new Vector3(6f, 3f, 0.3f);
            if (alongZ) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            return go.AddComponent<SpawnZoneBoundaryBlock>();
        }

        private SpawnZoneBoundaryOpening CreateOpening(SpawnZoneBoundaryBlock block, bool alongZ = false)
        {
            var go = new GameObject("FixtureOpening");
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.position = block.transform.position - Vector3.up * 0.5f;
            go.transform.rotation = block.transform.rotation;
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = alongZ ? new Vector3(0.8f, 2f, 1.2f) : new Vector3(1.2f, 2f, 0.8f);
            return go.AddComponent<SpawnZoneBoundaryOpening>();
        }

        private static Transform[] Parts(SpawnZoneBoundaryBlock block)
        {
            Transform group = block.transform.Find("OpeningBlocks");
            return group == null ? Array.Empty<Transform>() : group.Cast<Transform>().ToArray();
        }

        private static void AssertRestored(SpawnZoneBoundaryBlock block)
        {
            Assert.That(block.transform.Find("OpeningBlocks"), Is.Null, "Старая ниша осталась после изменения маски.");
            Assert.That(block.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(block.GetComponent<BoxCollider>().enabled, Is.True);
        }

        private static void AssertCoverage(Transform[] parts, Vector3 point, bool expected, string label)
        {
            Assert.That(parts.Any(p => p.GetComponent<Renderer>().bounds.Contains(point)), Is.EqualTo(expected), "Меш: " + label);
            Assert.That(parts.Any(p => p.GetComponent<BoxCollider>().bounds.Contains(point)), Is.EqualTo(expected), "Коллайдер: " + label);
        }

        private void Apply()
        {
            // Инструмент живёт в Assembly-CSharp-Editor, недоступной прямой ссылкой из тестового asmdef.
            Type tool = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VrBattlegrounds.EditorTools.ArsenalWallOpenings", false))
                .FirstOrDefault(t => t != null);
            Assert.That(tool, Is.Not.Null, "Не загружен редакторский инструмент разрезания стен.");
            MethodInfo apply = tool.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
            Assert.That(apply, Is.Not.Null);
            try { apply.Invoke(null, new object[] { _scene }); }
            catch (TargetInvocationException exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
                throw;
            }
            // В синхронном EditMode нет физического тика между построением сегментов и чтением bounds.
            // После SetParent/localPosition/localScale передаём новые позы в физический движок явно.
            Physics.SyncTransforms();
        }
    }
}
