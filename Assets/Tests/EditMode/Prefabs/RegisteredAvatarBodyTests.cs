using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Тело каждого зарегистрированного аватара (<see cref="RegisteredAvatars"/>): меши, LOD, своя
    /// голова, список рендереров UltimateXR, часы с табло, позы хвата оружия по цепочке.
    ///
    /// <para>
    /// Конкретных аватаров тест не знает: новый аватар в реестре проверяется сам, незарегистрированный
    /// скин в проекте тест не валит. Раньше это был <c>OptimizedMefAvatarTests</c> с путями одного префаба.
    /// </para>
    /// </summary>
    public class RegisteredAvatarBodyTests
    {
        public static IEnumerable<string> Avatars() =>
            RegisteredAvatars.Prefabs().Select(AssetDatabase.GetAssetPath).OrderBy(p => p);

        private static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(go, Is.Not.Null, $"Нет префаба {path}");
            return go;
        }

        /// <summary>Рендереры тела — всё, кроме кистей UltimateXR (их модели прячутся позами).</summary>
        private static SkinnedMeshRenderer[] BodySkins(GameObject avatar) =>
            avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                  .Where(s => s.GetComponentInParent<UxrHandIntegration>(true) == null)
                  .ToArray();

        [TestCaseSource(nameof(Avatars))]
        public void Меши_тела_с_костями_внутри_аватара_и_материалами(string path)
        {
            GameObject avatar = Load(path);
            SkinnedMeshRenderer[] body = BodySkins(avatar);
            var problems = new List<string>();

            Assert.That(body, Is.Not.Empty, "У аватара нет мешей тела");

            foreach (SkinnedMeshRenderer skin in body)
            {
                if (skin.sharedMesh == null) problems.Add($"{skin.name}: нет меша");
                if (skin.sharedMaterials.Length == 0 || skin.sharedMaterials.Any(m => m == null)) problems.Add($"{skin.name}: пустой материал");
                if (skin.rootBone == null) problems.Add($"{skin.name}: нет rootBone");

                foreach (Transform bone in skin.bones)
                {
                    if (bone == null) { problems.Add($"{skin.name}: пустая кость"); break; }
                    if (!bone.IsChildOf(avatar.transform)) { problems.Add($"{skin.name}: кость {bone.name} вне аватара"); break; }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [TestCaseSource(nameof(Avatars))]
        public void LOD_без_пустых_ссылок_и_по_убыванию(string path)
        {
            foreach (LODGroup group in Load(path).GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int l = 0; l < lods.Length; l++)
                {
                    Assert.That(lods[l].renderers, Has.None.Null, $"{group.name}: LOD{l} ссылается на удалённый рендерер");
                    if (l > 0)
                        Assert.That(lods[l].screenRelativeTransitionHeight, Is.LessThan(lods[l - 1].screenRelativeTransitionHeight),
                                    $"{group.name}: LOD{l} переключается не позже LOD{l - 1}");
                }
            }
        }

        /// <summary>Своя голова не видна изнутри: сеть выключает у владельца непустой список объектов.</summary>
        [TestCaseSource(nameof(Avatars))]
        public void Своя_голова_выключается(string path)
        {
            UxrMirrorAvatar net = Load(path).GetComponent<UxrMirrorAvatar>();
            Assert.That(net, Is.Not.Null, "Нет UxrMirrorAvatar");
            Assert.That(net.LocalDisabledGameObjects, Is.Not.Empty, "Своя голова не выключается — игрок видит её изнутри");
            Assert.That(net.LocalDisabledGameObjects, Has.None.Null, "В списке своей головы пустые ссылки");
        }

        [TestCaseSource(nameof(Avatars))]
        public void Список_рендереров_UxrAvatar_без_пустых_и_со_всем_телом(string path)
        {
            GameObject avatar = Load(path);
            List<Renderer> renderers = avatar.GetComponent<UxrAvatar>().AvatarRenderers.ToList();

            Assert.That(renderers.Count(r => r == null), Is.Zero, "В _avatarRenderers остались ссылки на удалённые меши");

            string[] missing = BodySkins(avatar).Where(s => !renderers.Contains(s)).Select(s => s.name).ToArray();
            Assert.That(missing, Is.Empty, "Меши тела не в _avatarRenderers — UltimateXR их не спрячет и не подсветит");
        }

        /// <summary>Часы с табло раунда (<see cref="WristDisplay"/>) — у каждого аватара, ровно одни.</summary>
        [TestCaseSource(nameof(Avatars))]
        public void На_руке_часы_с_табло(string path)
        {
            WristDisplay[] displays = Load(path).GetComponentsInChildren<WristDisplay>(true);
            Assert.That(displays.Length, Is.EqualTo(1), "Табло WristDisplay на аватаре должно быть одно");
        }

        /// <summary>
        /// Позы хвата оружия: если у кого-то в цепочке <c>UxrAvatar._parentPrefab</c> есть своя запись точки,
        /// аватар обязан её найти (<c>GetGripPoseInfo</c> идёт по <c>GetPrefabGuidChain</c>). Иначе рука
        /// держит оружие раскрытой ладонью. Оружие — из <see cref="WeaponRegistry"/>.
        /// </summary>
        [TestCaseSource(nameof(Avatars))]
        public void Позы_хвата_оружия_находятся_по_цепочке(string path)
        {
            UxrAvatar avatar = Load(path).GetComponent<UxrAvatar>();
            List<UxrAvatar> ancestors = avatar.GetParentPrefabChain().ToList();
            var problems = new List<string>();

            foreach (GameObject weapon in RegistryWeapons())
            {
                foreach (UxrGrabbableObject grabbable in weapon.GetComponentsInChildren<UxrGrabbableObject>(true))
                {
                    for (int i = 0; i < grabbable.GrabPointCount; i++)
                    {
                        UxrGrabPointInfo point = grabbable.GetGrabPoint(i);
                        bool ancestorHasOwn = ancestors.Any(a => point.GetGripPoseInfo(a) != point.DefaultGripPoseInfo);
                        if (ancestorHasOwn && point.GetGripPoseInfo(avatar) == point.DefaultGripPoseInfo)
                            problems.Add($"{weapon.name}/{grabbable.name} точка {i}: запись предка не найдена по цепочке");
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        private static IEnumerable<GameObject> RegistryWeapons()
        {
            WeaponRegistry registry = WeaponRegistry.Instance;
            Assert.That(registry, Is.Not.Null, "Нет Resources/WeaponRegistry");
            return registry.Weapons.Where(w => w != null && w.WeaponPrefab != null).Select(w => w.WeaponPrefab).Distinct();
        }
    }
}
