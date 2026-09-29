using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Отклик оружия игроку: звуки и подсветка точек хвата.
    ///
    /// <para>
    /// <b>Что проверяется.</b> Только оружие, зарегистрированное в игре, — <see cref="WeaponInfo" />
    /// из <see cref="WeaponRegistry" />. Префабы вне реестра могут лежать ненастроенными.
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item>Выстрел и сухой щелчок без патронов — <c>Shot Audio</c> и <c>Shot Audio No Ammo</c> у каждого
    ///   спуска <see cref="UxrFirearmWeapon" />.</item>
    ///   <item>Перезарядка — два звука затвора: оттягивание и обратный ход
    ///   (<see cref="AutomaticWeaponSlideFeedback" /> или <see cref="UxrShotgunPump" />).</item>
    ///   <item>Магазин — вставка и снятие: <see cref="AnchorSound" /> на гнезде магазина каждого спуска,
    ///   <c>clip</c> его источника и <c>Take Out Clip</c>.</item>
    ///   <item>Каждая точка хвата (основная, дополнительные, точки деталей) — объект
    ///   <c>Enable When Hand Near</c>, который UltimateXR включает при поднесённой руке.</item>
    ///   <item>Подсветка — копия рукояти с материалом подсветки, а не блок: сетка не примитив Unity,
    ///   материал — не материал корпуса оружия.</item>
    /// </list>
    /// </summary>
    public class WeaponFeedbackTests
    {
        // Имена сеток примитивов Unity: копия куба в отдельный ассет — всё тот же блок.
        private static readonly HashSet<string> PrimitiveMeshNames = new HashSet<string> { "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad" };

        [Test]
        public void Есть_что_проверять()
        {
            Assert.IsNotEmpty(RegisteredWeapons().ToList(), "В WeaponRegistry нет ни одного оружия с префабом — тесты отклика молча ничего не проверяют.");
        }

        [TestCaseSource(nameof(Cases))]
        public void Звуки_выстрела_и_пустого_магазина(string weaponPath)
        {
            GameObject weapon = Load(weaponPath);
            UxrFirearmWeapon firearm = weapon.GetComponent<UxrFirearmWeapon>();
            Assert.IsNotNull(firearm, $"{weapon.name}: нет UxrFirearmWeapon на корне — оружию нечем стрелять и звучать.");

            var missing = new List<string>();
            SerializedProperty triggers = new SerializedObject(firearm).FindProperty("_triggers");
            Assert.Greater(triggers.arraySize, 0, $"{weapon.name}: у UxrFirearmWeapon нет ни одного спуска.");

            for (int i = 0; i < triggers.arraySize; ++i)
            {
                SerializedProperty trigger = triggers.GetArrayElementAtIndex(i);
                RequireClip(trigger, "_shotAudio", $"спуск {i}: Shot Audio (выстрел)", missing);
                RequireClip(trigger, "_shotAudioNoAmmo", $"спуск {i}: Shot Audio No Ammo (нет патронов)", missing);
            }

            Assert.IsEmpty(missing, $"{weapon.name}: не назначены звуки:\n  " + string.Join("\n  ", missing));
        }

        [TestCaseSource(nameof(Cases))]
        public void Звуки_перезарядки_оттягивание_и_обратный_ход(string weaponPath)
        {
            GameObject weapon = Load(weaponPath);
            var missing = new List<string>();
            int mechanisms = 0;

            foreach (AutomaticWeaponSlideFeedback slide in weapon.GetComponentsInChildren<AutomaticWeaponSlideFeedback>(true))
            {
                mechanisms++;
                var so = new SerializedObject(slide);
                RequireClip(so, "_audioSlideBack", $"{slide.name} (AutomaticWeaponSlideFeedback): Audio Slide Back (оттягивание)", missing);
                RequireClip(so, "_audioSlideForward", $"{slide.name} (AutomaticWeaponSlideFeedback): Audio Slide Forward (обратный ход)", missing);
            }

            foreach (UxrShotgunPump pump in weapon.GetComponentsInChildren<UxrShotgunPump>(true))
            {
                mechanisms++;
                var so = new SerializedObject(pump);
                RequireClip(so, "_audioSlideBack", $"{pump.name} (UxrShotgunPump): Audio Slide Back (оттягивание)", missing);
                RequireClip(so, "_audioSlide", $"{pump.name} (UxrShotgunPump): Audio Slide (обратный ход)", missing);
            }

            Assert.Greater(mechanisms, 0,
                           $"{weapon.name}: нет механизма перезарядки (AutomaticWeaponSlideFeedback или UxrShotgunPump) — звукам затвора неоткуда играть.");
            Assert.IsEmpty(missing, $"{weapon.name}: не назначены звуки перезарядки:\n  " + string.Join("\n  ", missing));
        }

        [TestCaseSource(nameof(Cases))]
        public void Звуки_вставки_и_снятия_магазина(string weaponPath)
        {
            GameObject weapon = Load(weaponPath);
            UxrFirearmWeapon firearm = weapon.GetComponent<UxrFirearmWeapon>();
            Assert.IsNotNull(firearm, $"{weapon.name}: нет UxrFirearmWeapon на корне.");

            var missing = new List<string>();
            SerializedProperty triggers = new SerializedObject(firearm).FindProperty("_triggers");
            int anchors = 0;

            for (int i = 0; i < triggers.arraySize; ++i)
            {
                var anchor = triggers.GetArrayElementAtIndex(i).FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;

                if (anchor == null)
                {
                    missing.Add($"спуск {i}: не задан Ammunition Mag Anchor — магазину некуда вставляться");
                    continue;
                }

                anchors++;
                AnchorSound sound = anchor.GetComponent<AnchorSound>();

                if (sound == null)
                {
                    missing.Add($"{anchor.name}: нет AnchorSound");
                    continue;
                }

                if (sound.Source == null || sound.Source.clip == null)
                {
                    missing.Add($"{anchor.name}: AnchorSound — нет источника или его clip (вставка магазина)");
                }

                if (sound.TakeOutClip == null)
                {
                    missing.Add($"{anchor.name}: AnchorSound — пустой Take Out Clip (снятие магазина)");
                }
            }

            Assert.Greater(anchors, 0, $"{weapon.name}: ни у одного спуска нет гнезда магазина.");
            Assert.IsEmpty(missing, $"{weapon.name}: звуки магазина не настроены:\n  " + string.Join("\n  ", missing));
        }

        [TestCaseSource(nameof(Cases))]
        public void У_каждой_точки_хвата_есть_подсветка(string weaponPath)
        {
            GameObject weapon = Load(weaponPath);
            var missing = new List<string>();

            foreach ((UxrGrabbableObject grabbable, int point, GameObject highlight) in GrabPoints(weapon))
            {
                if (highlight == null)
                {
                    missing.Add($"{PointName(weapon, grabbable, point)}: пустой Enable When Hand Near");
                }
                else if (!highlight.transform.IsChildOf(weapon.transform))
                {
                    missing.Add($"{PointName(weapon, grabbable, point)}: подсветка '{highlight.name}' вне префаба оружия");
                }
            }

            Assert.IsEmpty(missing, $"{weapon.name}: точки хвата без подсветки при поднесённой руке:\n  " + string.Join("\n  ", missing));
        }

        [TestCaseSource(nameof(Cases))]
        public void Подсветка_точки_хвата_копия_рукояти_а_не_блок(string weaponPath)
        {
            GameObject weapon = Load(weaponPath);
            var highlights = GrabPoints(weapon).Where(p => p.highlight != null && p.highlight.transform.IsChildOf(weapon.transform)).ToList();

            if (highlights.Count == 0)
            {
                Assert.Pass("Подсветок нет — их отсутствие ловит У_каждой_точки_хвата_есть_подсветка.");
            }

            // Материалы корпуса: всё, что рисуется вне объектов подсветки.
            var highlightRoots = highlights.Select(p => p.highlight.transform).Distinct().ToList();
            var bodyMaterials = new HashSet<Material>(weapon.GetComponentsInChildren<Renderer>(true)
                                                            .Where(r => !highlightRoots.Any(h => r.transform.IsChildOf(h)))
                                                            .SelectMany(r => r.sharedMaterials)
                                                            .Where(m => m != null));

            var problems = new List<string>();

            foreach ((UxrGrabbableObject grabbable, int point, GameObject highlight) in highlights)
            {
                string where = $"{PointName(weapon, grabbable, point)} → '{highlight.name}'";
                Renderer[] renderers = highlight.GetComponentsInChildren<Renderer>(true);

                if (renderers.Length == 0)
                {
                    problems.Add($"{where}: у подсветки нет рендерера — подсвечивать нечем");
                    continue;
                }

                foreach (Renderer renderer in renderers)
                {
                    Mesh mesh = MeshOf(renderer);

                    if (mesh == null)
                    {
                        problems.Add($"{where}: у '{renderer.name}' нет сетки");
                    }
                    else if (IsPrimitive(mesh))
                    {
                        problems.Add($"{where}: '{renderer.name}' — примитив '{mesh.name}'. Нужна копия рукояти (та же сетка, что у модели) с материалом подсветки");
                    }

                    Material[] shared = renderer.sharedMaterials.Where(m => m != null).ToArray();

                    if (shared.Length == 0)
                    {
                        problems.Add($"{where}: у '{renderer.name}' нет материала");
                    }
                    else if (shared.Any(bodyMaterials.Contains))
                    {
                        problems.Add($"{where}: '{renderer.name}' с материалом корпуса — это копия без подсветки. Нужен материал подсветки");
                    }
                }
            }

            Assert.IsEmpty(problems, $"{weapon.name}: подсветка точек хвата настроена неверно:\n  " + string.Join("\n  ", problems));
        }

        // ── Кейсы ──────────────────────────────────────────────────────

        private static IEnumerable<TestCaseData> Cases()
        {
            return RegisteredWeapons().Select(p => new TestCaseData(AssetDatabase.GetAssetPath(p)).SetName($"{{m}}({p.name})"));
        }

        private static IEnumerable<GameObject> RegisteredWeapons()
        {
            return AssetDatabase.FindAssets("t:WeaponRegistry")
                                .Select(AssetDatabase.GUIDToAssetPath)
                                .Select(AssetDatabase.LoadAssetAtPath<WeaponRegistry>)
                                .Where(r => r != null)
                                .SelectMany(r => r.Weapons)
                                .Where(w => w != null && w.WeaponPrefab != null)
                                .Select(w => w.WeaponPrefab)
                                .Distinct();
        }

        private static GameObject Load(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Контроль: префаб '{path}' загружается.");
            return prefab;
        }

        /// <summary>
        /// Точки хвата оружия и его деталей. Предметы в якорях (вложенный магазин) — не части оружия,
        /// у них свои точки и своя подсветка.
        /// </summary>
        private static IEnumerable<(UxrGrabbableObject grabbable, int point, GameObject highlight)> GrabPoints(GameObject weapon)
        {
            foreach (UxrGrabbableObject grabbable in weapon.GetComponentsInChildren<UxrGrabbableObject>(true))
            {
                if (grabbable.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Any(a => a.transform != grabbable.transform && a.transform.IsChildOf(weapon.transform)))
                {
                    continue;
                }

                for (int point = 0; point < grabbable.GrabPointCount; ++point)
                {
                    yield return (grabbable, point, grabbable.GetGrabPoint(point).EnableOnHandNear);
                }
            }
        }

        private static string PointName(GameObject weapon, UxrGrabbableObject grabbable, int point)
        {
            string owner = grabbable.gameObject == weapon ? "корень" : grabbable.name;
            string kind = point == 0 ? "основная" : $"дополнительная {point - 1}";
            return $"{owner}, точка {point} ({kind})";
        }

        private static void RequireClip(SerializedObject so, string sampleField, string label, List<string> missing)
        {
            SerializedProperty sample = so.FindProperty(sampleField);
            Assert.IsNotNull(sample, $"Контроль: у {so.targetObject.GetType().Name} есть поле {sampleField} (переименовали в SDK?).");
            RequireSampleClip(sample, label, missing);
        }

        private static void RequireClip(SerializedProperty parent, string sampleField, string label, List<string> missing)
        {
            SerializedProperty sample = parent.FindPropertyRelative(sampleField);
            Assert.IsNotNull(sample, $"Контроль: у спуска есть поле {sampleField} (переименовали в SDK?).");
            RequireSampleClip(sample, label, missing);
        }

        private static void RequireSampleClip(SerializedProperty sample, string label, List<string> missing)
        {
            SerializedProperty clip = sample.FindPropertyRelative("_clip");
            Assert.IsNotNull(clip, "Контроль: у UxrAudioSample есть поле _clip.");

            if (clip.objectReferenceValue == null)
            {
                missing.Add(label);
            }
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinned: return skinned.sharedMesh;
                case MeshRenderer _: return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                default: return null;
            }
        }

        private static bool IsPrimitive(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            return path == "Library/unity default resources" || PrimitiveMeshNames.Contains(mesh.name);
        }
    }
}
