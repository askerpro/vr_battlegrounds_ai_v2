using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Тело погибшего — рэгдолл (T-35), сгенерированный из модели аватара
    /// (<c>Tools/VR Battlegrounds/Avatars/Build Corpses</c>).
    ///
    /// <para>
    /// Что доказывает. У каждого аватара, которого можно выбрать в команде, есть труп, собранный из
    /// его модели (поза копируется по путям костей). Труп — только визуал: без сети и UltimateXR,
    /// на своём слое, который сталкивается лишь со статичным миром, и пули сквозь него проходят.
    /// Толчок растёт с уроном и ограничен, толкается ближайшая к попаданию кость.
    /// </para>
    /// </summary>
    public class CorpseTests
    {
        private static IEnumerable<GameObject> TeamAvatars() =>
            AssetDatabase.FindAssets("t:TeamData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TeamData>)
                .Where(t => t != null && t.avatars != null)
                .SelectMany(t => t.avatars)
                .Where(a => a != null && a.prefab != null)
                .Select(a => a.prefab)
                .Distinct();

        private static IEnumerable<TestCaseData> TeamAvatarCases() =>
            TeamAvatars().Select(p => new TestCaseData(AssetDatabase.GetAssetPath(p)).SetName($"{{m}}({p.name})"));

        private static Corpse CorpseOf(GameObject avatar)
        {
            var source = avatar.GetComponent<CorpseSource>();
            Assert.IsNotNull(source, $"{avatar.name}: нет CorpseSource — погибший исчезнет без тела. Собери: Tools/VR Battlegrounds/Avatars/Build Corpses.");
            Assert.IsNotNull(source.CorpsePrefab, $"{avatar.name}: у CorpseSource не задан труп.");
            Assert.IsNotNull(source.ModelRoot, $"{avatar.name}: у CorpseSource не задана модель.");
            return source.CorpsePrefab;
        }

        [Test]
        public void Аватары_команд_найдены()
        {
            Assert.That(TeamAvatars().Count(), Is.GreaterThan(0), "Нет ни одного аватара в командах — трупы проверять не на ком.");
        }

        [TestCaseSource(nameof(TeamAvatarCases))]
        public void У_аватара_команды_есть_труп_из_его_модели(string path)
        {
            var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Corpse corpse = CorpseOf(avatar);
            Transform model = avatar.GetComponent<CorpseSource>().ModelRoot;

            Animator animator = model.GetComponent<Animator>();
            Assert.IsTrue(animator != null && animator.avatar != null && animator.avatar.isHuman,
                $"{avatar.name}: модель CorpseSource — не гуманоидный Animator.");

            var so = new SerializedObject(corpse);
            SerializedProperty paths = so.FindProperty("_nodePaths");
            Assert.Greater(paths.arraySize, 10, "У трупа нет костей для позы.");

            var missing = new List<string>();
            for (int i = 0; i < paths.arraySize; i++)
            {
                string p = paths.GetArrayElementAtIndex(i).stringValue;
                if (model.Find(p) == null) missing.Add(p);
            }
            Assert.That(missing, Is.Empty, $"{avatar.name}: кости трупа не находятся в модели аватара — труп не встанет в позу погибшего (пересобери трупы).");
        }

        [TestCaseSource(nameof(TeamAvatarCases))]
        public void Труп_только_визуал(string path)
        {
            Corpse corpse = CorpseOf(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            GameObject root = corpse.gameObject;

            Assert.IsEmpty(root.GetComponentsInChildren<NetworkIdentity>(true), "У трупа NetworkIdentity — он стал бы сетевым объектом.");
            Assert.IsEmpty(root.GetComponentsInChildren<NetworkBehaviour>(true), "У трупа сетевые компоненты.");
            Assert.IsEmpty(root.GetComponentsInChildren<Animator>(true), "У трупа Animator — он спорил бы с рэгдоллом.");
            List<string> uxr = root.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(c => c != null && c.GetType().Namespace != null && c.GetType().Namespace.StartsWith("UltimateXR"))
                .Select(c => c.GetType().Name).Distinct().ToList();
            Assert.That(uxr, Is.Empty, "У трупа компоненты UltimateXR — он зарегистрировался бы в менеджерах SDK.");

            int layer = CorpsePhysics.Layer;
            Assert.GreaterOrEqual(layer, 0, $"Нет слоя '{CorpsePhysics.LayerName}'.");
            List<string> offLayer = root.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.layer != layer).Select(t => t.name).ToList();
            Assert.That(offLayer, Is.Empty, "Части трупа не на слое трупа — будут сталкиваться с игроками или ловить пули.");

            Assert.GreaterOrEqual(corpse.Bodies.Count, 10, "У рэгдолла меньше 10 тел — кости не размечены.");
            Assert.IsTrue(corpse.Bodies.All(b => b != null && b.isKinematic), "Тело рэгдолла в префабе не кинематическое — упадёт до позы погибшего.");
        }

        [Test]
        public void Слой_трупа_сталкивается_только_со_статичным_миром()
        {
            int corpse = CorpsePhysics.Layer;
            Assert.GreaterOrEqual(corpse, 0, $"Нет слоя '{CorpsePhysics.LayerName}'.");

            Assert.IsTrue(CorpsePhysics.CollidesWith(LayerMask.NameToLayer("Default")), "Труп проваливается сквозь карту.");
            Assert.IsFalse(CorpsePhysics.CollidesWith(LayerMask.NameToLayer("Player")), "Труп сталкивается с игроком.");
            Assert.IsFalse(CorpsePhysics.CollidesWith(corpse), "Трупы сталкиваются друг с другом.");

            var wrong = new List<string>();
            for (int layer = 0; layer < 32; layer++)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer))) continue;
                bool collides = !Physics.GetIgnoreLayerCollision(corpse, layer);
                if (collides != CorpsePhysics.CollidesWith(layer)) wrong.Add($"{LayerMask.LayerToName(layer)}: {(collides ? "сталкивается" : "не сталкивается")}");
            }
            Assert.That(wrong, Is.Empty, "Матрица столкновений слоя трупа в Physics Settings расходится с правилом (пересобери трупы).");
        }

        [Test]
        public void Пули_проходят_сквозь_труп()
        {
            int corpse = CorpsePhysics.Layer;
            Assume.That(corpse, Is.GreaterThanOrEqualTo(0));

            int checkedShots = 0;
            var hitting = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" }))
            {
                var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (UxrProjectileSource source in weapon.GetComponentsInChildren<UxrProjectileSource>(true))
                {
                    foreach (UxrShotDescriptor shot in source.ShotTypes)
                    {
                        checkedShots++;
                        if ((shot.CollisionLayerMask.value & (1 << corpse)) != 0) hitting.Add(weapon.name);
                    }
                }
            }
            Assert.Greater(checkedShots, 0, "Не нашлось ни одного оружия.");
            Assert.That(hitting.Distinct(), Is.Empty, "Пуля попадает в труп — он станет укрытием.");
        }

        [Test]
        public void Толчок_растёт_с_уроном_и_ограничен()
        {
            Assert.Greater(DeathImpact.Magnitude(50f), DeathImpact.Magnitude(10f), "Сильный урон толкает не сильнее слабого.");
            Assert.AreEqual(DeathImpact.MaxImpulse, DeathImpact.Magnitude(100000f), 1e-3f, "Толчок не ограничен — труп улетит.");
            Assert.Greater(DeathImpact.Magnitude(0f), 0f, "Смертельный урон без толчка.");
        }

        [Test]
        public void Труп_встаёт_в_позу_толкается_у_попадания_и_замерзает()
        {
            GameObject avatarPrefab = TeamAvatars().FirstOrDefault();
            Assume.That(avatarPrefab, Is.Not.Null);
            Corpse prefab = CorpseOf(avatarPrefab);

            GameObject avatar = Object.Instantiate(avatarPrefab);
            Corpse corpse = null;
            try
            {
                Transform model = avatar.GetComponent<CorpseSource>().ModelRoot;
                Animator animator = model.GetComponent<Animator>();
                Transform arm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                arm.localRotation = Quaternion.Euler(0f, 0f, 73f);
                avatar.transform.position = new Vector3(100f, 0f, 100f);

                corpse = Object.Instantiate(prefab);
                Vector3 hand = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                corpse.Launch(model, new DeathImpact(hand, Vector3.forward * 10f));

                Transform corpseArm = corpse.transform.Find(AnimationUtility.CalculateTransformPath(arm, model));
                Assert.IsNotNull(corpseArm, "В трупе нет кости предплечья.");
                Assert.Less(Quaternion.Angle(arm.localRotation, corpseArm.localRotation), 0.5f, "Труп не встал в позу погибшего.");
                Assert.Less(Vector3.Distance(model.position, corpse.transform.position), 1e-3f, "Труп появился не на месте погибшего.");

                Assert.IsTrue(corpse.Bodies.All(b => !b.isKinematic), "Рэгдолл не отпущен — тело не упадёт.");
                Rigidbody nearest = corpse.NearestBody(hand);
                Assert.IsTrue(nearest != null && nearest.name == corpseArm.name, $"Толкается не кость у попадания, а {(nearest != null ? nearest.name : "ничего")}.");

                corpse.Freeze();
                Assert.IsTrue(corpse.Bodies.All(b => b.isKinematic), "Замёрзший труп продолжает симулироваться.");
                Assert.IsTrue(corpse.Bodies.SelectMany(b => b.GetComponents<Collider>()).All(c => !c.enabled),
                    "У замёрзшего трупа включены коллайдеры — позже появившиеся тела будут с ним сталкиваться.");
            }
            finally
            {
                if (corpse != null) Corpse.ClearAll("тест");
                Object.DestroyImmediate(avatar);
            }
        }
    }
}
