using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Две ветки игровых аватаров по типу кисти.
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
    /// </para>
    /// </summary>
    public class AvatarHandPoseChainTests
    {
        private const string PlayerBase     = "Assets/Prefabs/Player/PlayerBase.prefab";
        private const string SdkHands       = "Assets/Prefabs/Player/PlayerBase_SdkHands.prefab";
        private const string NonSdkHands    = "Assets/Prefabs/Player/PlayerBase_NonSdkHands.prefab";
        private const string HandsPackPoses = "Assets/Art/HandPoses/HandsPack/";

        private static readonly Dictionary<string, string> Parents = new Dictionary<string, string>
        {
            { SdkHands, PlayerBase },
            { NonSdkHands, PlayerBase },
            { "Assets/Prefabs/Player/Heavy_Soldier_Base_Avatar.prefab", SdkHands },
            { "Assets/Prefabs/Player/MEF_Base_Avatar.prefab", NonSdkHands }
        };

        public static IEnumerable<string> Variants() => Parents.Keys;

        [TestCaseSource(nameof(Variants))]
        public void Вариант_наследует_свою_базу(string path)
        {
            GameObject prefab = Load(path);
            GameObject parent = PrefabUtility.GetCorrespondingObjectFromSource(prefab);

            Assert.That(parent, Is.Not.Null, $"{path} не вариант");
            Assert.That(AssetDatabase.GetAssetPath(parent), Is.EqualTo(Parents[path]));
        }

        [TestCaseSource(nameof(Variants))]
        public void Цепочка_UxrAvatar_совпадает_с_цепочкой_вариантов(string path)
        {
            UxrAvatar avatar = Load(path).GetComponent<UxrAvatar>();

            Assert.That(avatar.ParentPrefab, Is.Not.Null, "UxrAvatar._parentPrefab пуст");
            Assert.That(AssetDatabase.GetAssetPath(avatar.ParentPrefab), Is.EqualTo(Parents[path]));
        }

        [Test]
        public void PlayerBase_без_поз_кисти()
        {
            Assert.That(Load(PlayerBase).GetComponent<UxrAvatar>().GetHandPoses().Select(p => p.name), Is.Empty);
        }

        [Test]
        public void Ветка_без_SDK_не_наследует_позы_под_скелет_SDK()
        {
            string[] foreign = Load(NonSdkHands).GetComponent<UxrAvatar>().GetAllHandPoses()
                                                .Where(p => !AssetDatabase.GetAssetPath(p).StartsWith(HandsPackPoses))
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
