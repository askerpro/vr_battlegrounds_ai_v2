using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Записи хвата семейства кистей Cyborg живут только под базой.
    ///
    /// <para>
    /// <b>Правило.</b> Записи хвата (<c>_avatarGripPoseEntries</c> точек хвата <see cref="UxrGrabbableObject"/>)
    /// семейства Cyborg хранятся под GUID базы <c>CyborgHandsBase</c>. Потомки (сейчас PlayerControllersCyborgAvatar,
    /// MEF_cyborg_hands_Player, MEF_cyborg_hands_Player_Black) своих записей не имеют и наследуют записи базы:
    /// <see cref="UxrGrabPointInfo.GetGripPoseInfo(UxrAvatar, bool)"/> идёт по цепочке <c>ParentAvatarPrefab</c>.
    /// Запись потомка молча перекрывает хват базы только для него, и правка хвата в базе до него не доходит.
    /// </para>
    ///
    /// <para>
    /// <b>Источники.</b> Потомки вычисляются, а не перечисляются: префаб с <see cref="UxrAvatar"/> в корне, у которого
    /// цепочка <see cref="UxrAvatar.GetParentPrefabChain"/> содержит базу. Префабы — все <c>Assets</c>, включая
    /// ThirdParty и Art. Значения берутся у загруженного префаба через <see cref="SerializedObject"/>, то есть с
    /// учётом override'ов варианта. Загружаются только префабы, в тексте которых встречается GUID потомка:
    /// в действующие данные варианта GUID попадает либо из собственного текста (override), либо из текста
    /// источника, а источник проверяется сам. Сцены Build Settings проверяются поиском по тексту (поле и
    /// override <c>_avatarPrefabGuid</c>); унаследованные сценами записи проверены в префабах-источниках.
    /// </para>
    ///
    /// <para>
    /// Тот же признак у поля редактора <c>_selectedAvatarForGrips</c>: выбранный потомок заставляет кнопку
    /// регистрации аватара в инспекторе создавать записи потомку.
    /// </para>
    /// </summary>
    public class CyborgGripEntriesOwnershipTests
    {
        private const string BasePath = "Assets/Prefabs/Player/Avatars/CyborgHands/Base/CyborgHandsBase.prefab";
        private const string BaseGuid = "71b74b0d08444f49bbb7aa2828d0890a";

        /// <summary>Потомки на 2026-10-22 — только проверка, что поиск их видит; список потомков не ограничивают.</summary>
        private static readonly string[] ExpectedDescendants =
        {
            "8e0e06323e4754f479a9f19788554652", // PlayerControllersCyborgAvatar
            "2aa0ab6714e2413f9d829637b62df0c0", // MEF_cyborg_hands_Player
            "60604d8891226f64592f880c9b2da883", // MEF_cyborg_hands_Player_Black
        };

        private static readonly Regex AvatarGuidInText =
            new Regex(@"_avatarPrefabGuid(?::[ \t]*|[ \t]*\r?\n[ \t]+value:[ \t]*)([0-9a-f]{32})", RegexOptions.Compiled);

        private Dictionary<string, string> _prefabTexts;
        private Dictionary<string, string> _descendants; // GUID → путь префаба-потомка

        [OneTimeSetUp]
        public void Собрать_префабы_и_потомков()
        {
            _prefabTexts = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                                        .Select(AssetDatabase.GUIDToAssetPath)
                                        .Where(p => p.EndsWith(".prefab") && File.Exists(p))
                                        .Distinct()
                                        .ToDictionary(p => p, File.ReadAllText);
            _descendants = FindDescendants(_prefabTexts);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Тесты
        // ══════════════════════════════════════════════════════════════════

        [Test]
        public void Потомки_базы_найдены()
        {
            Assert.That(AssetDatabase.AssetPathToGUID(BasePath), Is.EqualTo(BaseGuid), "GUID CyborgHandsBase изменился");
            Assert.That(_descendants.Keys, Has.No.Member(BaseGuid), "База не может быть своим потомком");
            foreach (string guid in ExpectedDescendants)
            {
                Assert.That(_descendants.Keys, Has.Member(guid),
                            $"Потомок {AssetDatabase.GUIDToAssetPath(guid)} ({guid}) не найден по цепочке ParentAvatarPrefab — " +
                            "поиск потомков сломан, остальные тесты ничего не проверяют");
            }
        }

        [Test]
        public void В_префабах_нет_записей_хвата_под_потомками_базы()
        {
            var problems = new SortedSet<string>();
            foreach (string path in CandidatePrefabs())
            {
                problems.UnionWith(DescendantEntries(path));
            }

            Assert.That(problems, Is.Empty,
                        $"Записи хвата под потомками CyborgHandsBase ({problems.Count}):\n  " + string.Join("\n  ", problems) +
                        "\nПотомки наследуют записи базы: перенесите запись на CyborgHandsBase (тот же элемент, GUID базы) " +
                        "или удалите её, если у базы та же поза и выравнивание.");
        }

        [Test]
        public void В_сценах_Build_Settings_нет_записей_хвата_под_потомками_базы()
        {
            var problems = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)))
            {
                string text = File.ReadAllText(scene.path);
                foreach (Match match in AvatarGuidInText.Matches(text))
                {
                    string guid = match.Groups[1].Value;
                    if (!_descendants.TryGetValue(guid, out string avatarPath)) continue;
                    int line = 1 + text.Take(match.Index).Count(c => c == '\n');
                    problems.Add($"{scene.path}:{line}: запись хвата под {Path.GetFileNameWithoutExtension(avatarPath)} ({guid})");
                }
            }

            Assert.That(problems, Is.Empty,
                        $"Записи хвата под потомками CyborgHandsBase в сценах ({problems.Count}):\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void Выбранный_для_хватов_аватар_не_потомок_базы()
        {
            var problems = new SortedSet<string>();
            foreach (string path in CandidatePrefabs())
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (UxrGrabbableObject grabbable in prefab.GetComponentsInChildren<UxrGrabbableObject>(true))
                {
                    Object selected = new SerializedObject(grabbable).FindProperty("_selectedAvatarForGrips")?.objectReferenceValue;
                    if (selected == null) continue;
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected));
                    if (_descendants.TryGetValue(guid, out string avatarPath))
                        problems.Add($"{path} :: {HierarchyPath(grabbable.transform, prefab.transform)}: выбран {Path.GetFileNameWithoutExtension(avatarPath)}");
                }
            }

            Assert.That(problems, Is.Empty,
                        $"_selectedAvatarForGrips указывает на потомка CyborgHandsBase ({problems.Count}):\n  " + string.Join("\n  ", problems) +
                        "\nВыберите для хватов корневой префаб CyborgHandsBase.");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Поиск
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Потомки базы по цепочке <c>ParentAvatarPrefab</c>. Префаб потомка ссылается на GUID родителя (источник варианта,
        /// <c>_parentPrefab</c>), поэтому загружаются только префабы, в тексте которых есть GUID уже известного звена.
        /// </summary>
        private static Dictionary<string, string> FindDescendants(Dictionary<string, string> texts)
        {
            var found = new Dictionary<string, string>();
            var known = new HashSet<string> { BaseGuid };
            var visited = new HashSet<string> { BasePath };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (KeyValuePair<string, string> pair in texts)
                {
                    if (visited.Contains(pair.Key) || !known.Any(pair.Value.Contains)) continue;
                    visited.Add(pair.Key);

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Key);
                    UxrAvatar avatar = prefab != null ? prefab.GetComponent<UxrAvatar>() : null;
                    if (avatar == null || !avatar.GetParentPrefabChain().Any(a => AssetDatabase.GetAssetPath(a) == BasePath)) continue;

                    foreach (string guid in new[] { AssetDatabase.AssetPathToGUID(pair.Key), avatar.PrefabGuid })
                    {
                        if (string.IsNullOrEmpty(guid) || guid == BaseGuid) continue;
                        found[guid] = pair.Key;
                        known.Add(guid);
                    }

                    grew = true;
                }
            }

            return found;
        }

        /// <summary>Префабы, в тексте которых встречается GUID потомка (кандидаты на записи потомка).</summary>
        private IEnumerable<string> CandidatePrefabs() =>
            _prefabTexts.Where(p => _descendants.Keys.Any(p.Value.Contains)).Select(p => p.Key).OrderBy(p => p);

        /// <summary>Действующие (с override'ами варианта) записи хвата под потомками в префабе.</summary>
        private List<string> DescendantEntries(string path)
        {
            var problems = new List<string>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return problems;

            foreach (UxrGrabbableObject grabbable in prefab.GetComponentsInChildren<UxrGrabbableObject>(true))
            {
                var so = new SerializedObject(grabbable);
                var points = new List<SerializedProperty> { so.FindProperty("_grabPoint") };
                SerializedProperty additional = so.FindProperty("_additionalGrabPoints");
                if (additional != null && additional.isArray)
                {
                    for (int i = 0; i < additional.arraySize; i++) points.Add(additional.GetArrayElementAtIndex(i));
                }

                for (int p = 0; p < points.Count; p++)
                {
                    SerializedProperty entries = points[p]?.FindPropertyRelative("_avatarGripPoseEntries");
                    if (entries == null || !entries.isArray) continue;
                    string pointName = points[p].FindPropertyRelative("_editorName")?.stringValue;

                    for (int e = 0; e < entries.arraySize; e++)
                    {
                        string guid = entries.GetArrayElementAtIndex(e).FindPropertyRelative("_avatarPrefabGuid")?.stringValue;
                        if (guid == null || !_descendants.TryGetValue(guid, out string avatarPath)) continue;
                        problems.Add($"{path} :: {HierarchyPath(grabbable.transform, prefab.transform)} / точка {p}" +
                                     (string.IsNullOrEmpty(pointName) ? "" : $" '{pointName}'") +
                                     $" / запись {e}: {Path.GetFileNameWithoutExtension(avatarPath)} ({guid})");
                    }
                }
            }

            return problems;
        }

        private static string HierarchyPath(Transform t, Transform root)
        {
            if (t == root) return t.name + " (корень)";

            string result = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                result = p.name + "/" + result;
            return result;
        }
    }
}
