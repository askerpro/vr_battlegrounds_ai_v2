using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Ветки игровых аватаров по происхождению кисти.
    ///
    /// <para>
    /// <c>PlayerBase_SdkHands</c> — кисть со скелетом SDK (BigHands/Cyborg: 4 кости на палец с пястной).
    /// Позы, сделанные на этом скелете (<c>Controller*</c>, <c>Demo*</c>), живут здесь.
    /// <c>PlayerBase_NonSdkHands</c> — чужой скелет кисти (3 фаланги, как у MEF). Позы здесь — только
    /// нейтральные к скелету, снятые с пака (<c>HandsPackPoseImporter</c>): позы SDK на таком скелете
    /// выглядят плохо. Сам <c>PlayerBase</c> — сетевой каркас без поз кисти.
    /// </para>
    ///
    /// <para>
    /// Поза наследуется по цепочке <c>UxrAvatar._parentPrefab</c>; она должна совпадать с настоящей
    /// цепочкой вариантов Unity, иначе SDK ищет позы и записи хвата не там.
    /// Исходный Cyborg остаётся вариантом SDK CyborgAvatar_URP: PlayerBasePrefabBuilder
    /// создаёт из него отдельный независимый каркас, а не переносит исходный аватар.
    /// </para>
    /// </summary>
    public class AvatarHandPoseChainTests
    {
        private const string PlayerBase     = "Assets/Prefabs/Player/PlayerBase.prefab";
        private const string SdkHands       = "Assets/Prefabs/Player/PlayerBase_SdkHands.prefab";
        private const string NonSdkHands    = "Assets/Prefabs/Player/PlayerBase_NonSdkHands.prefab";
        // Явный SDK-origin из CyborgLegsBuilder/PlayerBasePrefabBuilder; произвольный
        // ThirdParty-префаб не считается допустимой базой зарегистрированного аватара.
        private static readonly string[] SdkRoots =
        {
            "Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Avatars/CyborgAvatar_URP.prefab"
        };
        // Позы, снятые с паков оружия (Hands — HandsPackPoseImporter, KINEMATION — KinemationWeaponBuilder): нейтральны к скелету.
        private static readonly string[] PackPoses = { "Assets/Art/HandPoses/HandsPack/", "Assets/Art/HandPoses/Kinemation/" };

        /// <summary>
        /// Аватары — из реестра (<see cref="RegisteredAvatars"/>), без списка путей: новый аватар
        /// попадает под проверку сам, незарегистрированный скин тест не валит.
        /// </summary>
        public static IEnumerable<string> Variants() =>
            RegisteredAvatars.Prefabs().Select(AssetDatabase.GetAssetPath).OrderBy(p => p);

        [TestCaseSource(nameof(Variants))]
        public void Вариант_наследует_одну_из_баз_кисти(string path)
        {
            List<string> chain = UnityChain(Load(path));

            if (SdkRoots.Contains(chain.Last()))
            {
                Assert.That(chain.Any(p => p == PlayerBase || p == SdkHands || p == NonSdkHands), Is.False,
                    $"{path}: SDK-origin смешан с веткой игрового каркаса — {Describe(chain)}");
                return;
            }

            Assert.That(chain.Last(), Is.EqualTo(PlayerBase), $"{path}: цепочка вариантов не доходит до PlayerBase — {Describe(chain)}");
            Assert.That(chain.Contains(SdkHands) ^ chain.Contains(NonSdkHands), Is.True,
                        $"{path}: цепочка обязана идти ровно через одну базу кисти — {Describe(chain)}");
        }

        [TestCaseSource(nameof(Variants))]
        public void Цепочка_UxrAvatar_совпадает_с_цепочкой_вариантов(string path)
        {
            List<string> chain = UnityChain(Load(path));

            // Каждый уровень: _parentPrefab = настоящий родитель в Unity, у корня — null.
            for (int i = 0; i < chain.Count - 1; i++)
            {
                UxrAvatar avatar = Load(chain[i]).GetComponent<UxrAvatar>();
                Assert.That(avatar, Is.Not.Null, $"{chain[i]}: нет UxrAvatar");
                Assert.That(avatar.ParentPrefab, Is.Not.Null, $"{chain[i]}: UxrAvatar._parentPrefab пуст");
                Assert.That(AssetDatabase.GetAssetPath(avatar.ParentPrefab), Is.EqualTo(chain[i + 1]),
                            $"{chain[i]}: _parentPrefab не совпадает с вариантом Unity");
            }
            Assert.That(Load(chain.Last()).GetComponent<UxrAvatar>().ParentPrefab, Is.Null,
                $"{chain.Last()}: корень Unity-цепочки наследует позы чужого префаба");
        }

        /// <summary>Путь префаба и всех его родителей-вариантов вверх до корня.</summary>
        private static List<string> UnityChain(GameObject prefab)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>();
            for (GameObject current = prefab; current != null; current = PrefabUtility.GetCorrespondingObjectFromSource(current))
            {
                string path = AssetDatabase.GetAssetPath(current);
                Assert.That(seen.Add(path), Is.True, $"Цикл в цепочке вариантов: {Describe(chain)} → {path}");
                chain.Add(path);
            }
            return chain;
        }

        private static string Describe(List<string> chain) =>
            string.Join(" → ", chain.Select(System.IO.Path.GetFileNameWithoutExtension));

        [Test]
        public void PlayerBase_без_поз_кисти()
        {
            Assert.That(Load(PlayerBase).GetComponent<UxrAvatar>().GetHandPoses().Select(p => p.name), Is.Empty);
        }

        [Test]
        public void Ветка_без_SDK_не_наследует_позы_под_скелет_SDK()
        {
            string[] foreign = Load(NonSdkHands).GetComponent<UxrAvatar>().GetAllHandPoses()
                                                .Where(p => !PackPoses.Any(AssetDatabase.GetAssetPath(p).StartsWith))
                                                .Select(p => $"{p.name} ({AssetDatabase.GetAssetPath(p)})").ToArray();

            Assert.That(foreign, Is.Empty, "У базы без SDK только позы из пака");
        }

        [Test]
        public void Позы_под_скелет_SDK_живут_в_ветке_SDK()
        {
            string[] poses = Load(SdkHands).GetComponent<UxrAvatar>().GetAllHandPoses().Select(p => p.name).ToArray();

            Assert.That(poses, Has.Some.StartsWith("Controller"));
            Assert.That(poses, Has.Some.StartsWith("Demo"));
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"Нет префаба {path}");
            return prefab;
        }
    }
}
