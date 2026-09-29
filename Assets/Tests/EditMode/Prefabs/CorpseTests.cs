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

        /// <summary>
        /// Толчок учитывает силу оружия (скорость пули × множитель описания выстрела, Патч 30) и урон,
        /// с пределом. Цифры — реальные параметры оружия проекта.
        /// </summary>
        [Test]
        public void Толчок_от_силы_оружия_и_урона_с_пределом()
        {
            float pistolWeak = DeathImpact.Magnitude(5f, 300f);     // Gun
            float pistol = DeathImpact.Magnitude(25f, 300f);        // Gun_real
            float rifle = DeathImpact.Magnitude(25f, 400f);         // M16
            float pellet = DeathImpact.Magnitude(14f, 300f * 50f);  // дробина Shotgun_real

            Assert.Greater(rifle, pistol, "Винтовка с тем же уроном толкает не сильнее пистолета — сила оружия не учтена.");
            Assert.Greater(pistol, pistolWeak, "Больший урон при той же силе не толкает сильнее.");
            Assert.AreEqual(DeathImpact.MaxImpulse, pellet, 1e-3f, "Дробина дробовика (множитель 50) не упирается в предел — тело улетит.");
        }

        /// <summary>
        /// Толчок — по полёту пули (сила, которую SDK кладёт в событие урона, Патч 30); дробины одного
        /// выстрела складываются, сумма ограничена.
        /// </summary>
        [Test]
        public void Толчок_по_полёту_пули_и_дробины_складываются()
        {
            var target = new GameObject("HitProbe");
            try
            {
                target.transform.position = new Vector3(0f, 1.5f, 300f);
                target.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                Assert.IsTrue(Physics.Raycast(new Vector3(0f, 1.5f, 295f), Vector3.forward, out RaycastHit hit, 10f),
                    "Контроль: луч не попал в пробный коллайдер.");

                var args = new UxrDamageEventArgs(null, null, hit, 25f, true, Vector3.right * 400f);
                Assert.AreEqual(Vector3.right * 400f, args.ImpactForce, "Сила пули не доехала до события урона (Патч 30).");

                DeathImpact impact = DeathImpact.From(args, Vector3.up);
                Assert.Greater(Vector3.Dot(impact.Impulse.normalized, Vector3.right), 0.99f, "Толчок не по направлению полёта пули.");

                var weak = new DeathImpact(Vector3.zero, Vector3.right * 20f);
                DeathImpact sum = DeathImpact.Combine(weak, weak);
                Assert.AreEqual(40f, sum.Impulse.magnitude, 1e-3f, "Дробины одного выстрела не сложились.");
                Assert.AreEqual(DeathImpact.MaxImpulse,
                    DeathImpact.Combine(sum, new DeathImpact(Vector3.zero, Vector3.right * 500f)).Impulse.magnitude, 1e-3f,
                    "Сумма дробин не ограничена.");
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void Снаряжение_погибшего_отлетает_от_тела()
        {
            Vector3 v = DeathDropEjection.VelocityFor(Vector3.up, new Vector3(0.3f, 1.2f, 0f), Vector3.forward);
            Assert.Greater(v.x, 1f, "Ствол справа от тела не отлетает вправо — упадёт под труп.");
            Assert.Greater(v.y, 0f, "Нет подброса вверх.");

            Vector3 centered = DeathDropEjection.VelocityFor(Vector3.up, Vector3.up, Vector3.forward);
            Assert.Greater(centered.z, 1f, "Ствол в центре тела не отлетает вперёд — упадёт под труп.");
        }

        [Test]
        public void Силуэт_снаряжения_только_ничьего_и_у_трупа()
        {
            Assert.IsTrue(LootXray.Wanted(true, 0.1f), "Ничьё снаряжение у трупа не подсвечено.");
            Assert.IsFalse(LootXray.Wanted(false, 0.1f), "Подсвечено снаряжение в руке или кобуре.");
            Assert.IsFalse(LootXray.Wanted(true, LootXray.PingLifetime + 0.1f), "Силуэт остался без трупа рядом — видно сквозь стены по карте.");

            var material = Resources.Load<Material>(LootXray.MaterialResource);
            Assert.IsNotNull(material, $"Нет Resources/{LootXray.MaterialResource}.mat — силуэт не нарисуется.");
            Assert.AreEqual("VrBattlegrounds/LootXray", material.shader.name, "Материал силуэта не на шейдере LootXray.");
            string shader = System.IO.File.ReadAllText("Assets/Shaders/LootXray.shader");
            StringAssert.Contains("UNITY_VERTEX_OUTPUT_STEREO", shader, "Шейдер силуэта без стерео-инстансинга — в шлеме будет виден одним глазом.");
            StringAssert.Contains("ZTest Greater", shader, "Шейдер силуэта рисуется не только сквозь заслоняющее.");
        }

        [Test]
        public void Труп_уходит_под_пол_и_исчезает()
        {
            var go = new GameObject("SinkProbe");
            Corpse corpse = go.AddComponent<Corpse>();
            Vector3 start = go.transform.position;

            Assert.IsFalse(corpse.AdvanceSink(0.01f), "Труп исчез на первом шаге.");
            Assert.Less(go.transform.position.y, start.y, "Труп не опускается.");

            bool gone = false;
            for (int i = 0; i < 1000 && !gone; i++) gone = corpse.AdvanceSink(0.05f);
            Assert.IsTrue(gone, "Труп так и не ушёл под пол.");
            Assert.IsTrue(go == null, "Ушедший под пол труп не уничтожен.");
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
