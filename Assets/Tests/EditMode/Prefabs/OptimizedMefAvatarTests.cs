using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// <c>Optimized_MEF_Player</c> — вариант <c>MEF_Base_Avatar</c>, у которого 49 мешей MEF
    /// (106k треугольников, 49 материалов) заменены на 6 мешей из Blender (тело и голова по 3 LOD,
    /// один материал). Геометрию пересаживает <c>SkinnedMeshTransplant</c>
    /// (<c>Tools/VR Battlegrounds/Avatars/Transplant Skinned Meshes...</c>); тест сторожит то, что
    /// инструмент обязан оставить в префабе.
    ///
    /// <para>
    /// Позы хвата оружия у варианта не копируются: <c>UxrGrabPointInfo.GetGripPoseInfo(avatar)</c>
    /// идёт по <c>UxrAvatar.GetPrefabGuidChain()</c> (свой GUID → <c>_parentPrefab</c> → …) и
    /// находит запись MEF. Поэтому <c>_parentPrefab</c> обязан смотреть на <c>MEF_Base_Avatar</c>.
    /// </para>
    /// </summary>
    public class OptimizedMefAvatarTests
    {
        private const string VariantPath = "Assets/Prefabs/Player/Optimized_MEF_Player.prefab";
        private const string BasePath    = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";
        private const string FbxPath     = "Assets/Models/Avatars/MEF_Optimized/MEF_Optimized.fbx";
        private const string MatPath     = "Assets/Models/Avatars/MEF_Optimized/MEF_Optimized.mat";
        private const string DataPath    = "Assets/Data/Player/Avatars/OptimizedMEF.asset";

        private static readonly string[] Parts = { "Body", "Head" };
        private const int LodCount = 3;

        /// <summary>
        /// Родные часы MEF (skinned <c>SEModelMesh_207</c> с правого запястья, материал <c>vm_wrist_gps</c>),
        /// запечённые в жёсткий меш и отзеркаленные на левую руку (<c>MEF_Watch_L.asset</c>). Стоят
        /// экземпляром префаба <c>WristWatch</c> (модель + якорь HUD) дочерним объектом кости предплечья:
        /// двигаются обычным Transform, без Blender, а табло <c>WristDisplay</c> живёт в том же префабе.
        /// Видны на LOD0 и LOD1, дальше отсекаются.
        /// </summary>
        private const string WatchMesh = "WristWatch";
        private const string WatchPrefabPath = "Assets/Prefabs/Player/WristWatch.prefab";
        private const string WatchHudPrefabPath = "Assets/Prefabs/Player/WristWatch_HUD.prefab";
        private const string WatchBone = "CC_Base_L_ForearmTwist02";
        private const int WatchLastLod = 1;

        private static readonly string[] WeaponPaths =
        {
            "Assets/Prefabs/Weapons/GunReal/Gun_real.prefab",
            "Assets/Prefabs/Weapons/M16/M16_Rifle_prefab.prefab",
            "Assets/Prefabs/Weapons/ShotgunReal/Shotgun_real.prefab",
        };

        private static string MeshName(string part, int lod) => $"MEF_Optimized_{part}_LOD{lod}";

        private static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(go, Is.Not.Null, $"Нет префаба {path}");
            return go;
        }

        [Test]
        public void Вариант_от_MEF_и_цепочка_UxrAvatar_на_него()
        {
            GameObject variant = Load(VariantPath);
            GameObject parent  = PrefabUtility.GetCorrespondingObjectFromSource(variant);
            Assert.That(AssetDatabase.GetAssetPath(parent), Is.EqualTo(BasePath));

            UxrAvatar avatar = variant.GetComponent<UxrAvatar>();
            Assert.That(avatar.PrefabGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(VariantPath)), "_prefabGuid — GUID самого варианта");
            Assert.That(avatar.ParentPrefab, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(avatar.ParentPrefab), Is.EqualTo(BasePath), "_parentPrefab — MEF_Base_Avatar");
        }

        [Test]
        public void Позы_хвата_оружия_приходят_от_MEF_по_цепочке()
        {
            UxrAvatar opt = Load(VariantPath).GetComponent<UxrAvatar>();
            UxrAvatar mef = Load(BasePath).GetComponent<UxrAvatar>();
            var problems = new List<string>();

            foreach (string path in WeaponPaths)
            {
                UxrGrabbableObject grabbable = Load(path).GetComponent<UxrGrabbableObject>();
                for (int i = 0; i < grabbable.GrabPointCount; i++)
                {
                    UxrGrabPointInfo point = grabbable.GetGrabPoint(i);
                    UxrGripPoseInfo  ofMef = point.GetGripPoseInfo(mef);
                    if (ofMef == point.DefaultGripPoseInfo) continue; // у MEF своей записи нет — сравнивать не с чем

                    if (!ReferenceEquals(point.GetGripPoseInfo(opt), ofMef))
                        problems.Add($"{path} точка {i}: запись MEF не найдена по цепочке варианта");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Под_Geo_только_шесть_новых_скинов_и_все_кости_найдены()
        {
            GameObject variant = Load(VariantPath);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            Assert.That(mat, Is.Not.Null, "Нет материала " + MatPath);

            SkinnedMeshRenderer[] body = variant.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                                .Where(s => s.GetComponentInParent<UxrHandIntegration>(true) == null)
                                                .ToArray();

            Assert.That(body.Select(s => s.name).OrderBy(n => n),
                        Is.EqualTo(Parts.SelectMany(p => Enumerable.Range(0, LodCount).Select(l => MeshName(p, l))).OrderBy(n => n)),
                        "Старые SEModelMesh_* удалены, новые — 6 шт.");

            MeshRenderer watch = variant.GetComponentsInChildren<MeshRenderer>(true).SingleOrDefault(r => r.name == WatchMesh);
            Assert.That(watch, Is.Not.Null, "Нет часов на запястье");
            Assert.That(watch.transform.parent.name, Is.EqualTo(WatchBone), "Часы — дочерний объект кости предплечья");
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(watch.gameObject)),
                        Is.EqualTo(WatchPrefabPath), "Часы — экземпляр префаба WristWatch (модель + HUD)");
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(watch.gameObject)),
                        Is.EqualTo(WatchHudPrefabPath), "Часы — вариант WristWatch_HUD, с табло на экране");
            Assert.That(watch.GetComponentInChildren<WristDisplay>(true), Is.Not.Null, "На часах нет табло WristDisplay");

            Assert.That(variant.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "handgunHolster_geo" || r.name == "strap_geo"),
                        Is.Empty, "Кобура теперь в меше тела");

            var fbxSkins = fbx.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(s => s.name);
            var problems = new List<string>();

            foreach (SkinnedMeshRenderer skin in body)
            {
                SkinnedMeshRenderer source = fbxSkins[skin.name];
                if (skin.sharedMesh != source.sharedMesh) problems.Add($"{skin.name}: меш не из {FbxPath}");
                if (skin.sharedMaterials.Length != 1 || skin.sharedMaterial != mat) problems.Add($"{skin.name}: материал не MEF_Optimized");
                if (skin.transform.parent == null || skin.transform.parent.name != "Geo") problems.Add($"{skin.name}: не под Geo");
                if (skin.rootBone == null || skin.rootBone.name != source.rootBone.name) problems.Add($"{skin.name}: rootBone");
                if (skin.bones.Length != source.bones.Length) { problems.Add($"{skin.name}: число костей"); continue; }

                for (int i = 0; i < skin.bones.Length; i++)
                {
                    Transform bone = skin.bones[i];
                    if (bone == null) { problems.Add($"{skin.name}: кость {source.bones[i].name} не найдена"); continue; }
                    if (bone.name != source.bones[i].name) problems.Add($"{skin.name}: кость {i} {bone.name} ≠ {source.bones[i].name}");
                    if (!bone.IsChildOf(variant.transform)) problems.Add($"{skin.name}: кость {bone.name} вне аватара");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void LODGroup_тело_и_голова_по_уровням()
        {
            LODGroup group = Load(VariantPath).GetComponentsInChildren<LODGroup>(true).SingleOrDefault();
            Assert.That(group, Is.Not.Null, "Нет LODGroup");

            LOD[] lods = group.GetLODs();
            Assert.That(lods.Length, Is.EqualTo(LodCount));

            for (int l = 0; l < LodCount; l++)
            {
                Assert.That(lods[l].renderers.Where(r => r != null).Select(r => r.name).OrderBy(n => n),
                            Is.EqualTo(Parts.Select(p => MeshName(p, l))
                                            .Concat(l <= WatchLastLod ? new[] { WatchMesh } : new string[0])
                                            .OrderBy(n => n)), $"LOD{l}");
                if (l > 0) Assert.That(lods[l].screenRelativeTransitionHeight, Is.LessThan(lods[l - 1].screenRelativeTransitionHeight));
            }
        }

        [Test]
        public void Своя_голова_выключается_ровно_тремя_LOD_головы()
        {
            GameObject variant = Load(VariantPath);
            UxrMirrorAvatar net = variant.GetComponent<UxrMirrorAvatar>();

            Assert.That(net.LocalDisabledGameObjects.Select(g => g == null ? "null" : g.name).OrderBy(n => n),
                        Is.EqualTo(Enumerable.Range(0, LodCount).Select(l => MeshName("Head", l))));
        }

        [Test]
        public void Список_рендереров_UxrAvatar_без_пустых_и_с_новыми_мешами()
        {
            GameObject variant = Load(VariantPath);
            List<Renderer> renderers = variant.GetComponent<UxrAvatar>().AvatarRenderers.ToList();

            Assert.That(renderers.Count(r => r == null), Is.Zero, "В _avatarRenderers остались ссылки на удалённые меши");
            foreach (string part in Parts)
            {
                for (int l = 0; l < LodCount; l++)
                    Assert.That(renderers.Any(r => r.name == MeshName(part, l)), $"Нет {MeshName(part, l)} в _avatarRenderers");
            }
        }

        [Test]
        public void Зарегистрирован_в_реестре_и_обеих_командах()
        {
            var data = AssetDatabase.LoadAssetAtPath<AvatarData>(DataPath);
            Assert.That(data, Is.Not.Null, "Нет " + DataPath);
            Assert.That(data.prefab, Is.EqualTo(Load(VariantPath)));

            var registry = AssetDatabase.LoadAssetAtPath<AvatarRegistry>("Assets/Data/Player/Avatars/AvatarsRegistry.asset");
            Assert.That(registry.avatars, Does.Contain(data));

            foreach (string team in new[] { "CounterTerrorists_Team", "Terrorists_Team" })
            {
                var teamData = AssetDatabase.LoadAssetAtPath<TeamData>($"Assets/Data/Teams/{team}.asset");
                Assert.That(teamData.avatars, Does.Contain(data), team);
            }
        }
    }
}
