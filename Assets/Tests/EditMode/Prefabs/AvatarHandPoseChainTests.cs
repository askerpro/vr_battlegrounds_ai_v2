using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Принятая иерархия игровых аватаров и баз поз кисти.
    ///
    /// <para>
    /// <c>CyborgHandsBase</c> хранит полный набор поз кисти киборга и наследует общий каркас
    /// <c>PlayerBase</c>. Обе базы не содержат тела или привязок аватарных костей; модели
    /// устройств под <c>UxrHandIntegration</c> остаются допустимы.
    /// <c>PlayerBase_NonSdkHands</c> — родной скелет кисти модели с тремя фалангами. Позы здесь — только
    /// нейтральные к скелету, снятые с пака (<c>HandsPackPoseImporter</c>): позы SDK на таком скелете
    /// выглядят плохо. Сам <c>PlayerBase</c> — сетевой каркас без поз кисти.
    /// </para>
    ///
    /// <para>
    /// Поза наследуется по цепочке <c>UxrAvatar._parentPrefab</c>; она должна совпадать с настоящей
    /// цепочкой вариантов Unity, иначе SDK ищет позы и записи хвата не там.
    /// Controllers и MEF наследуют CyborgHandsBase, Black — MEF, Ghost — Controllers.
    /// Старая SDK-база хранится только в Unused и не участвует в активных цепочках.
    /// </para>
    /// </summary>
    public class AvatarHandPoseChainTests
    {
        private const string PlayerBase     = "Assets/Prefabs/Player/PlayerBase.prefab";
        private const string CyborgHands    = "Assets/Prefabs/Player/CyborgHandsBase.prefab";
        private const string NonSdkHands    = "Assets/Prefabs/Player/PlayerBase_NonSdkHands.prefab";
        private const string Controllers    = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        private const string MefPlayer      = "Assets/Prefabs/Player/MEF_cyborg_hands_Player.prefab";
        private const string MefPlayerBlack = "Assets/Prefabs/Player/MEF_cyborg_hands_Player_Black.prefab";
        private const string Ghost          = "Assets/Prefabs/Player/Ghost/GhostAvatar.prefab";
        private const string LegacySdkHands = "Assets/Prefabs/Player/PlayerBase_SdkHands.prefab";
        private const string ArchivedSdkHands = "Assets/Prefabs/Player/Unused/PlayerBase_SdkHands.prefab";
        private const string CyborgHandsGuid = "71b74b0d08444f49bbb7aa2828d0890a";
        private const string LegacySdkHandsGuid = "a58abb6b878ca5b40b79c14fae6f5645";
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

            Assert.That(chain.Last(), Is.EqualTo(PlayerBase), $"{path}: цепочка вариантов не доходит до PlayerBase — {Describe(chain)}");
            Assert.That(chain.Contains(CyborgHands) ^ chain.Contains(NonSdkHands), Is.True,
                        $"{path}: цепочка обязана идти ровно через одну базу кисти — {Describe(chain)}");
        }

        [TestCase(CyborgHands, PlayerBase)]
        [TestCase(Controllers, CyborgHands)]
        [TestCase(MefPlayer, CyborgHands)]
        [TestCase(MefPlayerBlack, MefPlayer)]
        [TestCase(Ghost, Controllers)]
        [TestCase(NonSdkHands, PlayerBase)]
        public void Непосредственный_родитель_соответствует_контракту(string path, string parentPath)
        {
            GameObject prefab = Load(path);
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant),
                $"{path}: требуется настоящий Unity Variant");
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(prefab)),
                Is.EqualTo(parentPath), $"{path}: неверный непосредственный Unity parent");
            UxrAvatar avatar = prefab.GetComponent<UxrAvatar>();
            Assert.That(avatar, Is.Not.Null, $"{path}: нет UxrAvatar");
            Assert.That(AssetDatabase.GetAssetPath(avatar.ParentPrefab), Is.EqualTo(parentPath),
                $"{path}: неверный непосредственный UXR parent");
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
            UxrAvatar avatar = Load(PlayerBase).GetComponent<UxrAvatar>();
            Assert.That(avatar.GetHandPoses(), Is.Empty);
            Assert.That(avatar.GetAllHandPoses(), Is.Empty);
            Assert.That(avatar.DefaultHandPose, Is.Null);
        }

        [TestCase(PlayerBase)]
        [TestCase(CyborgHands)]
        public void Общая_база_не_содержит_тела_или_аватарного_рига(string path)
        {
            UxrAvatar avatar = Load(path).GetComponent<UxrAvatar>();
            Assert.That(avatar, Is.Not.Null, $"{path}: нет UxrAvatar");
            Assert.That(avatar.AvatarRig.Transforms, Is.Empty, $"{path}: в общей базе остались привязки костей тела");
            // SDK исключает device visuals под UxrHandIntegration из мешей тела аватара.
            Assert.That(avatar.GetAllAvatarRendererComponents(), Is.Empty,
                $"{path}: в общей базе остался skinned mesh тела; device visuals допустимы");
        }

        [Test]
        public void GUID_базы_киборга_сохраняет_ключ_записей_хвата()
        {
            Load(CyborgHands);
            Assert.That(AssetDatabase.AssetPathToGUID(CyborgHands), Is.EqualTo(CyborgHandsGuid));
        }

        [Test]
        public void Прежняя_SDK_база_сохранена_только_в_архиве()
        {
            Assert.That(System.IO.File.Exists(LegacySdkHands), Is.False, "Активный путь прежней SDK-базы должен отсутствовать");
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(LegacySdkHands), Is.Null);
            Load(ArchivedSdkHands);
            Assert.That(AssetDatabase.AssetPathToGUID(ArchivedSdkHands), Is.EqualTo(LegacySdkHandsGuid),
                "Перемещение в Unused должно сохранять GUID прежней SDK-базы");
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
        public void Полный_набор_поз_киборга_принадлежит_чистой_базе()
        {
            UxrAvatar avatar = Load(CyborgHands).GetComponent<UxrAvatar>();
            var poses = avatar.GetHandPoses().ToArray();
            // Принятый набор содержит 65 поз; последующее добавление поз допустимо.
            Assert.That(poses.Length, Is.GreaterThanOrEqualTo(65));
            Assert.That(poses.All(p => p != null), Is.True, "В наборе поз есть потерянный asset");
            string[] names = poses.Select(p => p.name).ToArray();
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length), "Имена поз в базе должны быть однозначны");
            Assert.That(names, Has.Some.StartsWith("Controller"));
            Assert.That(names, Has.Some.StartsWith("Demo"));
            Assert.That(avatar.DefaultHandPose, Is.Not.Null, "У базы киборга должна быть поза по умолчанию");
            Assert.That(poses, Does.Contain(avatar.DefaultHandPose), "Поза по умолчанию должна входить в собственный набор базы");
            Assert.That(avatar.GetAllHandPoses().Count(), Is.EqualTo(poses.Length),
                "Полный набор киборга должен принадлежать CyborgHandsBase, а не предкам");
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"Нет префаба {path}");
            return prefab;
        }
    }
}
