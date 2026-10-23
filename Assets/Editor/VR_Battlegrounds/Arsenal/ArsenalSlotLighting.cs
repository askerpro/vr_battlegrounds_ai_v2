using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    ///     Свет на оружие станций арсенала в сцене карты (задача arsenal-slot-lighting, решения пользователя 2026-10-23).
    ///     <para>
    ///     Слоты станции создаёт код при загрузке карты, поэтому запечённый свет и пробы живут в сцене, а не в префабе
    ///     слота. Инструмент считает позы слотов тем же кодом, что запуск карты и превью станций
    ///     (<see cref="MapArsenalStationDescriber" />, <see cref="ArsenalStationComposer.PlanComposition" />), в открытой
    ///     позе оборудования (<see cref="ArsenalEquipmentPoses" />).
    ///     </para>
    ///     <para>
    ///     Свет «со стороны смотрящего» (как Scene view без освещения сцены): большинство стволов металлические, их вид —
    ///     отражение. У станции своя проба отражений; в неё запечены узкие светящиеся полосы перед стеной и над полкой —
    ///     металл отражает их бликами с тёмными промежутками. Полосы существуют только на время запекания этой пробы
    ///     (<see cref="BakeReflections" />, сразу после Generate Lighting), в игре их нет. Проба — в режиме Custom со своим
    ///     EXR: Generate Lighting её не перезапекает. Рассеянный свет — 4 запечённые лампы на слот в точках LampPoints
    ///     префаба слота, через пробы света у слотов. Пробу станции берут только оружие и магазины: доски, бортики и карточки
    ///     слота и корпус станции её не используют (настроено в префабах слотов и станции Lobby) — иначе полосы бликуют
    ///     на них. Runtime-цена: одна кубмапа на станцию, источники запечены.
    ///     Состояние слота — боковая лента <see cref="ArsenalSlotGlow" /> в префабе слота.
    ///     </para>
    ///     <para>
    ///     Владелец <c>Environment/ArsenalSlotLighting</c> — только этот инструмент; контейнер пересобирается целиком.
    ///     После смены пресета, раскладки или станции — запустить снова и перезапечь свет сцены.
    ///     </para>
    /// </summary>
    [InitializeOnLoad]
    public static class ArsenalSlotLighting
    {
        public const string MenuPath = "Tools/VR Battlegrounds/Arsenal/Подсветка слотов (свет и пробы в сцене)";
        public const string BakeMenuPath = "Tools/VR Battlegrounds/Arsenal/Подсветка слотов: запечь отражения станций";
        public const string ContainerName = "ArsenalSlotLighting";
        public const string ProbeGroupName = "SlotProbes";
        public const string StripsName = "BakeStrips", ReflectionName = "SlotReflections";
        public const string StripMaterialPath = "Assets/Materials/Arsenal/ArsenalBakeStrip.mat";
        public const string ReflectionFolder = "Assets/Materials/Arsenal/Reflections";
        public const string BoardName = "PegboardSection";
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";

        // Пробы света в осях слота (лицо — −Z, верх — +Y): 7 см перед лицевой стороной доски и 25 см перед ней.
        internal const float AimOffset = 0.07f, ProbeForward = 0.25f;

        // Полосы подобраны с пользователем в Play (tools/softbox_prototype.cs): большая ровная панель делает гладкий металл
        // «серебряным», узкие полосы с тёмными промежутками дают тёмный металл с бликами. Стена: 3 полосы по 15 см в 1,2 м
        // перед стеной, на −0,4 / +0,3 / +1,0 м от центра досок — верх стволов отражает область выше глаз. Полка: полоса
        // над ней, наклонена к ней. Яркость >1 — в HDR-пробе это блик; 4 давало оружию вид хрома (пользователь в шлеме-
        // режиме Play 2026-10-23) — 1,5.
        internal const float StripDistance = 1.2f, StripTall = 0.15f, StripOverhang = 0.2f, BoardWidth = 0.45f;
        internal static readonly float[] WallStripHeights = { -0.4f, 0.3f, 1.0f };
        internal const float ShelfStripUp = 0.55f, ShelfStripForward = 0.15f;
        internal const float StripBrightness = 3f;
        internal static readonly Color StripColor = new Color(1f, .98f, .95f);

        // Рассеянный свет — 4 запечённые лампы на слот в точках LampPoints префаба слота (углы доски, 20 см перед ней),
        // каждая на свою четверть доски в 5 см перед ней (там висит оружие), мягкий край конуса — схема пользователя
        // (прототип ламп 2026-10-23). Лампа, чья цель ближе LampRefDistance, слабее по квадрату расстояния. Один общий
        // прожектор с места игрока не освещал станцию 4,4 м ровно: края в 2–4 раза темнее центра, металлическая доска
        // (WeaponRackMaterial, metallic 0,7) без пробы станции — почти чёрная (запекания 2026-10-23).
        public const string LampPointsName = "LampPoints";
        // Тёплые (1; 0,85; 0,6) лампы 1,0 в ночном шатре делали оружие тусклым и коричневым; пользователь сравнил с видом
        // Scene view (белый ровный свет) — нейтральный белый и вдвое ярче (2026-10-23). Тёплый — только у ленты слота.
        internal const float LampIntensity = 2f, LampRange = 1f, LampAngle = 50f, LampInner = 0f, LampAimForward = 0.05f;
        internal const float LampRefDistance = 0.35f;
        internal static readonly Color LampColor = new Color(1f, .97f, .93f);

        // «Налобный» прожектор: образец вида пользователь выбрал в Play realtime-светом (2026-10-23) — широкий белый
        // прожектор с места игрока (1,3 м перед стеной, 1,75 м высоты) на середину между стеной и полкой. Здесь он
        // запечён: рассеянный свет — через пробы света у слотов, блики металла — полосы в пробе станции.
        internal const float HeadDistance = 1.3f, HeadHeight = 1.75f, HeadIntensity = 2.5f, HeadRange = 5f;
        internal const float HeadAngle = 140f, HeadInnerAngle = 110f;

        // Влияние пробы станции — только слой, где висит оружие: от 3 см за лицом доски до 25 см перед ним, выше доски на
        // 25 см (ствол снайперской винтовки). Коробка во всю станцию с запасом 0,4 м накрывала место игрока: его руки и
        // оружие в руках отражали полосы «хромом» (пользователь 2026-10-23). Проба своя у каждого ряда (стена наклонена,
        // полка горизонтальна), снимок — с места игрока (ReflectionEyeDistance перед стеной, на высоте центра досок).
        // Box Projection выключена: в тонкой коробке параллакс исказил бы полосы, которые висят за её пределами.
        internal const float VolumeBehind = 0.03f, VolumeFront = 0.25f, VolumeAbove = 0.25f, ReflectionEyeDistance = 1f;
        internal const int ReflectionResolution = 128, ReflectionImportance = 2;
        public const string WallReflectionName = ReflectionName + "_Wall", ShelfReflectionName = ReflectionName + "_Shelf";

        /// <summary>Расчётные точки слота в мире.</summary>
        public readonly struct SlotPoints
        {
            public readonly ArsenalSlotRow Row;
            public readonly Vector3 Aim, Front, BoardTop, Up, Forward, Along;
            public readonly Vector3[] Lamps, LampAims;
            /// <summary>Объём, где висит содержимое слота: влияние пробы отражений станции.</summary>
            public readonly Bounds Volume;

            public SlotPoints(ArsenalSlotRow row, Vector3 aim, Vector3 front, Vector3 boardTop, Vector3 up, Vector3 forward, Vector3 along,
                Vector3[] lamps, Vector3[] lampAims, Bounds volume)
            {
                Row = row; Aim = aim; Front = front; BoardTop = boardTop; Up = up; Forward = forward; Along = along;
                Lamps = lamps; LampAims = lampAims; Volume = volume;
            }

            public bool IsShelf => Row != null && Row.Zone == ArsenalPresentationZone.Shelf;
        }

        static ArsenalSlotLighting()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        [MenuItem(MenuPath)]
        private static void Menu()
        {
            string report = Apply(SceneManager.GetActiveScene());
            GameLog.Arsenal.Info("[ArsenalSlotLighting] " + report);
            EditorUtility.DisplayDialog("Подсветка слотов арсенала", report, "OK");
        }

        [MenuItem(BakeMenuPath)]
        private static void BakeMenu()
        {
            string report = BakeReflections(SceneManager.GetActiveScene());
            GameLog.Arsenal.Info("[ArsenalSlotLighting] " + report);
            EditorUtility.DisplayDialog("Отражения станций арсенала", report, "OK");
        }

        [MenuItem(MenuPath, true)]
        [MenuItem(BakeMenuPath, true)]
        private static bool MenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Отражения станций снимаются после GI: в них должны попасть готовые лайтмапы шатра.</summary>
        private static void OnBakeCompleted()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || Container(scene) == null) continue;
                GameLog.Arsenal.Info("[ArsenalSlotLighting] после Generate Lighting: " + BakeReflections(scene));
            }
        }

        /// <summary>Пересобрать свет и пробы станций сцены. Возвращает отчёт.</summary>
        public static string Apply(Scene scene)
        {
            GameObject environment = Environment(scene);
            ArsenalCompositionCatalog catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath)?.ArsenalComposition
                                                ?? throw new InvalidOperationException("Нет каталога оформления станций (" + CatalogPath + ").");
            Material stripMaterial = StripMaterial();

            Transform old = environment.transform.Find(ContainerName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var container = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(container, "Подсветка слотов арсенала");
            container.transform.SetParent(environment.transform, false);

            var report = new StringBuilder();
            var probes = new List<Vector3>();
            int stations = 0, lights = 0;
            foreach (GameObject top in scene.GetRootGameObjects())
            foreach (MapRoot root in top.GetComponentsInChildren<MapRoot>(true))
            foreach (ArsenalStationCompositionBinding station in root.StationBindings)
            {
                if (station == null) continue;
                IReadOnlyList<SlotPoints> slots = Slots(station, root.Map != null ? root.Map.arsenalPreset : null, catalog);
                if (slots.Count == 0) continue;
                var group = new GameObject(station.StationKey).transform;
                group.SetParent(container.transform, false);
                for (int s = 0; s < slots.Count; s++)
                {
                    SlotPoints slot = slots[s];
                    probes.Add(slot.Aim);
                    probes.Add(slot.Front);
                    for (int i = 0; i < slot.Lamps.Length; i++)
                        CreateLamp(group, "SlotLamp_" + s + "_" + i, slot.Lamps[i], slot.LampAims[i], slot.Up);
                    lights += slot.Lamps.Length;
                }
                StationFrame frame = Frame(slots);
                var strips = new List<Transform>();
                var stripRoot = new GameObject(StripsName).transform;
                stripRoot.SetParent(group, false);
                for (int i = 0; i < WallStripHeights.Length; i++)
                {
                    Vector3 position = frame.WallCenter + frame.Face * (StripDistance - AimOffset) + Vector3.up * WallStripHeights[i];
                    strips.Add(CreateStrip(stripRoot, "WallStrip_" + i, position, frame.Face, frame.Width, stripMaterial));
                }
                if (frame.HasShelf)
                {
                    Vector3 position = frame.ShelfCenter + Vector3.up * (ShelfStripUp - AimOffset) + frame.Face * ShelfStripForward;
                    strips.Add(CreateStrip(stripRoot, "ShelfStrip", position, (Vector3.up * .7f + frame.Face * .3f).normalized,
                        frame.Width, stripMaterial));
                }
                stripRoot.gameObject.SetActive(false);
                CreateHead(group, frame);
                Vector3 eye = frame.WallCenter + frame.Face * (ReflectionEyeDistance - AimOffset);
                CreateReflectionProbe(group, WallReflectionName, slots.Where(s => !s.IsShelf), eye);
                CreateReflectionProbe(group, ShelfReflectionName, slots.Where(s => s.IsShelf), eye);
                stations++;
                report.Append(station.StationKey).Append(": ").Append(slots.Count).Append(" слотов. ");
            }

            var probeGroup = new GameObject(ProbeGroupName).AddComponent<LightProbeGroup>();
            probeGroup.transform.SetParent(container.transform, false);
            probeGroup.probePositions = probes.Select(p => container.transform.InverseTransformPoint(p)).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            return $"Станций {stations}, ламп {lights}, проб света {probes.Count}. " + report +
                   "Перезапеките свет сцены (Generate Lighting): отражения станций запекутся следом сами.";
        }

        /// <summary>
        ///     Запечь пробы отражений станций с полосами: полосы включаются только на время снимка. Пробы рядов снимают
        ///     с одной точки, поэтому у станции один EXR на все её пробы (режим Custom). Вызывается сам после Generate
        ///     Lighting; вручную — из меню. Возвращает отчёт.
        /// </summary>
        public static string BakeReflections(Scene scene)
        {
            Transform container = Container(scene) ?? throw new InvalidOperationException("В сцене " + scene.name + " нет " + ContainerName + ".");
            Directory.CreateDirectory(ReflectionFolder);
            var report = new StringBuilder();
            foreach (Transform group in container)
            {
                ReflectionProbe[] probes = group.GetComponentsInChildren<ReflectionProbe>(true)
                    .Where(p => p.name.StartsWith(ReflectionName, StringComparison.Ordinal)).ToArray();
                Transform strips = group.Find(StripsName);
                if (probes.Length == 0 || strips == null) continue;
                string path = ReflectionFolder + "/" + scene.name + "_" + group.name + ".exr";
                strips.gameObject.SetActive(true);
                bool baked;
                try { baked = Lightmapping.BakeReflectionProbe(probes[0], path); }
                finally { strips.gameObject.SetActive(false); }
                if (!baked) { report.Append(group.name).Append(": проба не запеклась. "); continue; }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                foreach (ReflectionProbe probe in probes)
                {
                    probe.mode = ReflectionProbeMode.Custom;
                    probe.customBakedTexture = texture;
                    EditorUtility.SetDirty(probe);
                }
                report.Append(group.name).Append(": ").Append(path).Append(" (").Append(probes.Length).Append(" проб). ");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return report.Length == 0 ? "Станций с пробой нет." : report + "Сохраните сцену.";
        }

        /// <summary>Точки подсветки каждого слота станции в открытой позе оборудования, в порядке плана.</summary>
        public static IReadOnlyList<SlotPoints> Slots(ArsenalStationCompositionBinding station, ArsenalPreset preset,
            ArsenalCompositionCatalog catalog)
        {
            ArsenalStationDescription description = MapArsenalStationDescriber.Describe(station, preset, catalog);
            if (!description.Success)
                throw new InvalidOperationException(station.StationKey + ": описание станции — " + string.Join(", ",
                    description.Failures.Select(f => f.Kind + (string.IsNullOrEmpty(f.LogicalSlotKey) ? "" : "/" + f.LogicalSlotKey))));
            ArsenalStationPlan plan = ArsenalStationComposer.PlanComposition(description, station);
            ArsenalEquipmentPoses poses = station.GetComponentInChildren<ArsenalEquipmentPoses>(true);
            return plan.Slots.Select(placement => Points(placement.Row, OpenRowMatrix(placement.Row.transform, poses) *
                                                  Matrix4x4.TRS(placement.LocalPose.position, placement.LocalPose.rotation, Vector3.one)))
                       .ToList();
        }

        /// <summary>Точки слота по его доске (<see cref="BoardName" /> в префабе слота ряда).</summary>
        internal static SlotPoints Points(ArsenalSlotRow row, Matrix4x4 frame)
        {
            Transform board = row.SlotPrefab != null ? row.SlotPrefab.transform.Find(BoardName) : null;
            Vector3 center = board != null ? board.localPosition : new Vector3(0f, .5f, 0f);
            Quaternion rotation = board != null ? board.localRotation : Quaternion.identity;
            float halfHeight = board != null ? board.localScale.y * .5f : .5f;
            // Лицевая сторона доски — та, что смотрит к игроку: вперёд у стены, вверх у полки.
            Vector3 face = rotation * Vector3.back;
            if (Vector3.Dot(face, Vector3.up + Vector3.back) < 0f) face = -face;
            Vector3 boardUp = rotation * Vector3.up;
            if (Vector3.Dot(boardUp, Vector3.up) < 0f) boardUp = -boardUp;

            Vector3 aim = center + face * AimOffset;
            Vector3 front = aim + Vector3.back * ProbeForward;
            Vector3 top = center + boardUp * halfHeight;

            // Объём содержимого: доска от VolumeBehind за лицом до VolumeFront перед ним, вверх по доске ещё VolumeAbove.
            Vector3 boardRight = rotation * Vector3.right;
            float halfWidth = board != null ? board.localScale.x * .5f : BoardWidth * .5f;
            var volume = new Bounds(frame.MultiplyPoint3x4(center), Vector3.zero);
            foreach (float x in new[] { -halfWidth, halfWidth })
            foreach (float y in new[] { -halfHeight, halfHeight + VolumeAbove })
            foreach (float z in new[] { -VolumeBehind, VolumeFront })
                volume.Encapsulate(frame.MultiplyPoint3x4(center + boardRight * x + boardUp * y + face * z));

            // Лампы: точки LampPoints/Lamp_0..3 префаба (низ-лево, низ-право, верх-лево, верх-право), цель — центр
            // четверти доски того же угла в LampAimForward перед ней.
            Transform points = row.SlotPrefab != null ? row.SlotPrefab.transform.Find(LampPointsName) : null;
            var lamps = new List<Vector3>();
            var lampAims = new List<Vector3>();
            if (points != null && board != null)
            {
                Vector3 right = rotation * Vector3.right;
                if (Vector3.Dot(right, Vector3.right) < 0f) right = -right;
                for (int i = 0; i < points.childCount; i++)
                {
                    Vector3 lamp = points.GetChild(i).localPosition;
                    float cx = Mathf.Sign(Vector3.Dot(lamp - center, right)), cy = Mathf.Sign(Vector3.Dot(lamp - center, boardUp));
                    Vector3 target = center + right * (cx * board.localScale.x * .25f) + boardUp * (cy * board.localScale.y * .25f)
                                     + face * LampAimForward;
                    lamps.Add(frame.MultiplyPoint3x4(lamp));
                    lampAims.Add(frame.MultiplyPoint3x4(target));
                }
            }
            return new SlotPoints(row, frame.MultiplyPoint3x4(aim), frame.MultiplyPoint3x4(front), frame.MultiplyPoint3x4(top),
                frame.MultiplyVector(Vector3.up).normalized, frame.MultiplyVector(Vector3.back).normalized,
                frame.MultiplyVector(Vector3.right).normalized, lamps.ToArray(), lampAims.ToArray(), volume);
        }

        /// <summary>Мировая матрица ряда, когда подвижное оборудование станции полностью выдвинуто.</summary>
        internal static Matrix4x4 OpenRowMatrix(Transform row, ArsenalEquipmentPoses poses)
        {
            if (poses != null)
                foreach (ArsenalEquipmentPoses.PoseTarget pose in poses.Targets)
                {
                    if (pose.Target == null || pose.OpenPose == null || !row.IsChildOf(pose.Target)) continue;
                    Matrix4x4 open = Matrix4x4.TRS(pose.OpenPose.position, pose.OpenPose.rotation, pose.Target.lossyScale);
                    return open * pose.Target.worldToLocalMatrix * row.localToWorldMatrix;
                }
            return row.localToWorldMatrix;
        }

        /// <summary>Центр стены и полки станции (точки прицела слотов), горизонтальное направление к игроку, ширина рядов.</summary>
        internal readonly struct StationFrame
        {
            public readonly Vector3 WallCenter, ShelfCenter, Face;
            public readonly float Width;
            public readonly bool HasShelf;

            public StationFrame(Vector3 wall, Vector3 shelf, bool hasShelf, Vector3 face, float width)
            {
                WallCenter = wall; ShelfCenter = shelf; HasShelf = hasShelf; Face = face; Width = width;
            }
        }

        internal static StationFrame Frame(IReadOnlyList<SlotPoints> slots)
        {
            List<SlotPoints> wall = slots.Where(s => !s.IsShelf).ToList();
            List<SlotPoints> shelf = slots.Where(s => s.IsShelf).ToList();
            if (wall.Count == 0) wall = shelf;
            Vector3 face = wall.Aggregate(Vector3.zero, (sum, s) => sum + s.Forward);
            face.y = 0f;
            face = face.sqrMagnitude > 1e-6f ? face.normalized : Vector3.back;
            Vector3 along = Vector3.Cross(Vector3.up, face).normalized;
            Vector3 wallCenter = wall.Aggregate(Vector3.zero, (sum, s) => sum + s.Aim) / wall.Count;
            Vector3 shelfCenter = shelf.Count > 0 ? shelf.Aggregate(Vector3.zero, (sum, s) => sum + s.Aim) / shelf.Count : wallCenter;
            float min = slots.Min(s => Vector3.Dot(s.Aim - wallCenter, along)), max = slots.Max(s => Vector3.Dot(s.Aim - wallCenter, along));
            float width = max - min + BoardWidth + StripOverhang * 2f;
            wallCenter += along * ((min + max) * .5f);
            shelfCenter += along * Vector3.Dot(wallCenter - shelfCenter, along);
            return new StationFrame(wallCenter, shelfCenter, shelf.Count > 0, face, width);
        }

        /// <summary>Светящаяся полоса для пробы отражений: квад, лицо — к станции, только ReflectionProbeStatic.</summary>
        private static Transform CreateStrip(Transform parent, string name, Vector3 position, Vector3 away, float width, Material material)
        {
            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.DestroyImmediate(strip.GetComponent<Collider>());
            strip.name = name;
            strip.transform.SetParent(parent, false);
            strip.transform.SetPositionAndRotation(position, Quaternion.LookRotation(away, Vector3.up));
            strip.transform.localScale = new Vector3(width, StripTall, 1f);
            var renderer = strip.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(strip, StaticEditorFlags.ReflectionProbeStatic);
            return strip.transform;
        }

        /// <summary>Запечённый «налобный» прожектор станции с места игрока (см. <see cref="HeadDistance" />).</summary>
        private static void CreateHead(Transform parent, StationFrame frame)
        {
            var go = new GameObject("HeadLight");
            go.transform.SetParent(parent, false);
            Vector3 position = new Vector3(frame.WallCenter.x, HeadHeight, frame.WallCenter.z) + frame.Face * HeadDistance;
            Vector3 target = frame.HasShelf ? (frame.WallCenter + frame.ShelfCenter) * .5f : frame.WallCenter;
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = Color.white;
            light.intensity = HeadIntensity;
            light.range = HeadRange;
            light.spotAngle = HeadAngle;
            light.innerSpotAngle = HeadInnerAngle;
            light.shadows = LightShadows.None;
        }

        private static void CreateLamp(Transform parent, string name, Vector3 position, Vector3 target, Vector3 up)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, up));
            float distance = Vector3.Distance(position, target);
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = LampColor;
            light.intensity = LampIntensity * (distance < LampRefDistance ? distance * distance / (LampRefDistance * LampRefDistance) : 1f);
            light.range = LampRange;
            light.spotAngle = LampAngle;
            light.innerSpotAngle = LampInner;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        ///     Проба отражений ряда: влияние — объёмы содержимого его слотов (<see cref="SlotPoints.Volume" />), снимок —
        ///     с места игрока <paramref name="eye" /> (точка съёмки пробы может лежать вне её коробки).
        /// </summary>
        private static void CreateReflectionProbe(Transform parent, string name, IEnumerable<SlotPoints> slots, Vector3 eye)
        {
            List<SlotPoints> row = slots.ToList();
            if (row.Count == 0) return;
            Bounds bounds = row[0].Volume;
            foreach (SlotPoints slot in row) bounds.Encapsulate(slot.Volume);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = eye;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.boxProjection = false;
            probe.size = bounds.size;
            probe.center = bounds.center - go.transform.position;
            probe.resolution = ReflectionResolution;
            probe.hdr = true;
            probe.importance = ReflectionImportance;
            probe.blendDistance = 0f;
        }

        /// <summary>Материал полос: Unlit, двусторонний, тёплый белый с яркостью выше 1. Писатель — этот инструмент.</summary>
        private static Material StripMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(StripMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, StripMaterialPath);
            }
            Color color = StripColor * StripBrightness;
            color.a = 1f;
            if (material.GetColor("_BaseColor") != color || material.GetFloat("_Cull") != 0f)
            {
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Cull", 0f);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            return material;
        }

        private static GameObject Environment(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Сцена не загружена.");
            return scene.GetRootGameObjects().SingleOrDefault(r => r.name == "Environment")
                   ?? throw new InvalidOperationException("В сцене " + scene.name + " нет корня Environment.");
        }

        private static Transform Container(Scene scene) =>
            scene.GetRootGameObjects().SingleOrDefault(r => r.name == "Environment")?.transform.Find(ContainerName);
    }
}
