using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay.Tests
{
    /// <summary>
    /// Откат пересборки ствола (<see cref="WeaponRebuildTransaction" />): возвращает байты файлов, удаляет созданное
    /// сборкой, отбрасывает её несохранённые правки в памяти; после успешной сборки отбрасывает несохранённое вне её
    /// области, появившееся за сборку. Несохранённое до начала не трогается. Работа — во временной папке проекта.
    /// </summary>
    public class WeaponRebuildTransactionTests
    {
        private const string Root = "Assets/__WeaponRebuildTransactionTests";
        private const string Inside = Root + "/Inside";
        private const string Outside = Root + "/Outside";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "__WeaponRebuildTransactionTests");
            AssetDatabase.CreateFolder(Root, "Inside");
            AssetDatabase.CreateFolder(Root, "Outside");
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Root);

        private static Mesh CreateMesh(string path, float size)
        {
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.vertices = new[] { Vector3.zero, Vector3.right * size, Vector3.up * size };
            mesh.triangles = new[] { 0, 1, 2 };
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static byte[] Bytes(string path) => File.ReadAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));

        [Test]
        public void Откат_возвращает_файлы_удаляет_созданное_и_отбрасывает_память()
        {
            string kept = Inside + "/Kept.asset", edited = Inside + "/Edited.asset";
            CreateMesh(kept, 1f);
            CreateMesh(edited, 1f);
            byte[] before = Bytes(edited);

            WeaponRebuildTransaction tx = WeaponRebuildTransaction.Begin(new string[0], new[] { Inside });
            // Сборка: переписала файл, создала новый, оставила несохранённую правку в памяти.
            var onDisk = AssetDatabase.LoadAssetAtPath<Mesh>(edited);
            onDisk.vertices = new[] { Vector3.zero, Vector3.right * 5f, Vector3.up * 5f };
            AssetDatabase.SaveAssetIfDirty(onDisk);
            CreateMesh(Inside + "/Created.asset", 2f);
            var inMemory = AssetDatabase.LoadAssetAtPath<Mesh>(kept);
            inMemory.vertices = new[] { Vector3.zero, Vector3.right * 9f, Vector3.up * 9f };
            EditorUtility.SetDirty(inMemory);

            string report = tx.Rollback();

            CollectionAssert.AreEqual(before, Bytes(edited), "Байты изменённого файла не вернулись. " + report);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Mesh>(Inside + "/Created.asset"), "Созданный сборкой файл не удалён. " + report);
            Assert.IsFalse(OwnAssetSave.AllDirty().Contains(kept), "Несохранённая правка сборки осталась в памяти. " + report);
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(kept).bounds.size.x, Is.EqualTo(1f).Within(1e-4f),
                "Меш в памяти не перечитан с диска. " + report);
        }

        [Test]
        public void Несохранённое_до_начала_откат_не_трогает()
        {
            string foreign = Outside + "/Foreign.asset";
            var mesh = CreateMesh(foreign, 1f);
            mesh.vertices = new[] { Vector3.zero, Vector3.right * 3f, Vector3.up * 3f };
            EditorUtility.SetDirty(mesh);

            WeaponRebuildTransaction tx = WeaponRebuildTransaction.Begin(new string[0], new[] { Inside });
            tx.Rollback();

            Assert.IsTrue(OwnAssetSave.AllDirty().Contains(foreign), "Откат отбросил чужую правку, сделанную до сборки.");
        }

        [Test]
        public void После_сборки_чужое_несохранённое_вне_области_отбрасывается()
        {
            string foreign = Outside + "/Foreign.asset", own = Inside + "/Own.asset";
            CreateMesh(foreign, 1f);
            CreateMesh(own, 1f);

            WeaponRebuildTransaction tx = WeaponRebuildTransaction.Begin(new string[0], new[] { Inside });
            // За сборку: Unity обновил ассет вне области (как материал пака), а свой ассет сборка сохранила.
            var outside = AssetDatabase.LoadAssetAtPath<Mesh>(foreign);
            outside.vertices = new[] { Vector3.zero, Vector3.right * 4f, Vector3.up * 4f };
            EditorUtility.SetDirty(outside);

            string report = tx.ReleaseForeign();

            StringAssert.Contains(foreign, report);
            Assert.IsFalse(OwnAssetSave.AllDirty().Contains(foreign), "Несохранённое вне области сборки осталось в памяти.");
        }
    }
}
