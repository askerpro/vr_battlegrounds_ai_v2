using System.Reflection;
using NUnit.Framework;
using UltimateXR.Core.Components;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Патч SDK 10: <c>NotifyOnValidate</c> у префаба-ассета с неверными флагами
    /// <c>__isInPrefab</c> / <c>__prefabGuid</c> исправляет флаги, а <c>_uxrUniqueId</c> не трогает.
    ///
    /// <para>
    /// <b>Зачем.</b> Без патча редактор выдаёт такому компоненту новый случайный id только в
    /// памяти — хост MPPM и клон, читающий файл, расходятся (MPPM-02). Неверные флаги в ассет
    /// приносят Apply to Prefab со сцены и <c>SaveAsPrefabAsset</c>.
    /// </para>
    ///
    /// <para>
    /// Вторая проверка страхует то, ради чего SDK вообще перевыдаёт id: экземпляр на сцене
    /// обязан получить свой id, иначе два экземпляра одного префаба в сцене делили бы один.
    /// </para>
    /// </summary>
    public class UxrUniqueIdStabilityTests
    {
        private const string ProbeFolder = "Assets/__UxrIdProbe";
        private const string ProbePath = ProbeFolder + "/UxrIdProbe.prefab";

        private GameObject _asset;
        private GameObject _instance;

        [SetUp]
        public void CreateAsset()
        {
            if (!AssetDatabase.IsValidFolder(ProbeFolder))
                AssetDatabase.CreateFolder("Assets", "__UxrIdProbe");

            var source = new GameObject("UxrIdProbe");
            source.AddComponent<UxrGrabbableObject>();
            _asset = PrefabUtility.SaveAsPrefabAsset(source, ProbePath);
            Object.DestroyImmediate(source);

            Assert.IsNotNull(_asset, "Не удалось создать временный префаб.");
        }

        [TearDown]
        public void DeleteAsset()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
            AssetDatabase.DeleteAsset(ProbeFolder);
        }

        [Test]
        public void Ассет_с_неверными_флагами_сохраняет_id()
        {
            var component = _asset.GetComponent<UxrGrabbableObject>();
            string idBefore = ReadId(component);
            Assert.IsNotEmpty(idBefore, "Контроль: у компонента ассета есть id.");

            // Флаги, какие приносит Apply to Prefab со сцены.
            var so = new SerializedObject(component);
            so.FindProperty("__isInPrefab").boolValue = false;
            so.FindProperty("__prefabGuid").stringValue = string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();

            InvokeOnValidate(component);

            so = new SerializedObject(component);
            Assert.AreEqual(idBefore, ReadId(component),
                "NotifyOnValidate перевыдал id префаба-ассета — хост и клиент MPPM разойдутся.");
            Assert.IsTrue(so.FindProperty("__isInPrefab").boolValue, "Флаг __isInPrefab не исправлен.");
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(ProbePath), so.FindProperty("__prefabGuid").stringValue,
                "Флаг __prefabGuid не исправлен.");
        }

        [Test]
        public void Экземпляр_на_сцене_получает_свой_id()
        {
            var assetComponent = _asset.GetComponent<UxrGrabbableObject>();
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(_asset);
            var instanceComponent = _instance.GetComponent<UxrGrabbableObject>();

            InvokeOnValidate(instanceComponent);

            Assert.AreNotEqual(ReadId(assetComponent), ReadId(instanceComponent),
                "Экземпляр на сцене делит id с ассетом — два экземпляра в сцене получат одинаковый id.");
        }

        private static string ReadId(Component component)
        {
            return new SerializedObject(component).FindProperty("_uxrUniqueId").stringValue;
        }

        private static void InvokeOnValidate(UxrComponent component)
        {
            MethodInfo onValidate = typeof(UxrComponent).GetMethod("OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(onValidate, "У UxrComponent нет OnValidate — тест устарел вместе с SDK.");
            onValidate.Invoke(component, null);
        }
    }
}
