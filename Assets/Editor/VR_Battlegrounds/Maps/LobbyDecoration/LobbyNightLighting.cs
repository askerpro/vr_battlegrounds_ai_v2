using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Ночной пресет Lobby. Единственный владелец ночных параметров: RenderSettings (небо, ambient, туман),
    /// LightingSettings сцены, режимов, значений и положения источников Environment, ночных материалов ламп,
    /// GI-флагов статичного декора и группы light probes.
    /// «Ночной пресет освещения» — ночь на realtime-источниках, видна сразу без запекания.
    /// «Ночь: подготовить запекание» — переводит лампы и прожекторы базы в Baked, ставит GI-флаги и пробы;
    /// само запекание (Generate Lighting) — отдельный ручной шаг.
    /// </summary>
    public static class LobbyNightLighting
    {
        private const string MenuRoot = "Tools/VR Battlegrounds/Lobby Decoration/";
        private const string ScenePath = "Assets/Scenes/Lobby.unity";
        private const string NightFolder = "Assets/Materials/Lobby/Night";
        private const string LightingAssetPath = "Assets/Scenes/Lobby/LobbyNight.lighting";
        private const string Report = "tasks/lobby-decoration/reports/implementation/night-lighting/";
        private const string ContainerName = "NightLighting";
        private const string ProbeGroupName = "NightLightProbes";
        private const string WindowGlowPrefix = "WindowGlow__";

        // Сокет светильника шатра: группа светильника — его родитель с тем же именем. BayLamp — LobbyCanopyBaysBuilder,
        // TentFill/TentAccent — LobbyTentLightFixtures.
        private static readonly Regex TentFixtureSocket = new Regex("^LightingSocket__((?:BayLamp|TentFill|TentAccent)_[A-Za-z0-9]+)__CommonLightingOwner$", RegexOptions.CultureInvariant);

        // Свет шатра (lobby-reflections, решение пользователя 2026-10-22): база ~0,35 по SH на высоте рук — лампы CampLighting
        // и BayLamp притушены до уровня сетки TentFill, над акцентами (стена арсенала) — яркая лампа с короткой дальностью.
        // Начальные значения — модель tasks/lobby-reflections/tools/tent_light_model.py; итог — карта освещённости после запекания.
        private const string TentLampLight = "InteriorWarmLight", TentFillLight = "InteriorFillLight", TentAccentLight = "InteriorAccentLight";
        private const float TentLampIntensity = 3.75f, TentLampRange = 8f;
        private const float TentFillIntensity = 5f, TentFillRange = 10f;
        private const float TentAccentIntensity = 22f, TentAccentRange = 4.5f;

        // Значения цветов — как в инспекторе (sRGB); проект в Linear, Unity переводит сам.
        private static readonly Color FogColor = new Color(.050f, .062f, .088f);
        private static readonly Color AmbientSky = new Color(.12f, .15f, .24f);
        private static readonly Color AmbientEquator = new Color(.07f, .08f, .11f);
        private static readonly Color AmbientGround = new Color(.03f, .028f, .025f);
        private static readonly Color MoonColor = new Color(.62f, .71f, .95f);
        private static readonly Color WarmLamp = new Color(1f, .74f, .46f);
        private static readonly Color FloodWhite = new Color(1f, .93f, .80f);
        // Эмиссия рассеивателя ярче оттенка лампы: без bloom насыщенный тёплый почти белый читается как свет.
        private const float LampGlowBoost = 1.6f;
        private const string LampLongEmissionMap = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Textures/T_Lamp_long_EM.png";

        // Масштаб в лайтмапе относительно базовых 10 texel/м. Зона вне таблицы не получает GI (отчёт предупреждает).
        private static readonly Dictionary<string, float> ZoneLightmapScale = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "ArmoryRangeZone", 1.0f },
            { "RestCanopyZone", .5f },
            { "BarracksZone", .4f },
            { "WarehouseZone", .4f },
            { "CheckpointZone", .4f },
            { "VehicleYardZone", .3f },
            { "ShippingDepotZone", .3f },
            { "ConstructionZone", .3f },
            { "PerimeterZone", .15f },
            { "OuterGround", .15f },
            { "SharedBase", .3f },
        };

        // Запечённая ночь без realtime-света темнее превью: источники ярче (решение пользователя 2026-10-13).
        private const float BakedBoost = 1.6f;

        private static float Boost(bool bake) => bake ? BakedBoost : 1f;

        // Ограда базы в плане (X, Z), как в раскладке зон 2026-10-11.
        private static readonly Vector2 BaseMin = new Vector2(-15f, -26f);
        private static readonly Vector2 BaseMax = new Vector2(57f, 27f);

        // Периметр за оградой — без проб: ambient вместо экстраполяции проб базы.
        private const string AmbientOnlyZone = "PerimeterZone";

        [MenuItem(MenuRoot + "Ночной пресет освещения")]
        public static void Apply() => Run(false);

        [MenuItem(MenuRoot + "Ночь: подготовить запекание")]
        public static void PrepareBake() => Run(true);

        private static void Run(bool bake)
        {
            var scene = Check();
            var environment = scene.GetRootGameObjects().SingleOrDefault(r => r.name == "Environment")
                ?? throw new InvalidOperationException("В Lobby нет корня Environment.");
            var log = new StringBuilder();
            log.AppendLine((bake ? "Подготовка запекания" : "Ночной пресет") + " Lobby " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            // Ассеты создаются до Undo-группы: AssetDatabase.CreateAsset не откатывается Undo.
            var materials = EnsureMaterials(log);
            var lighting = bake ? EnsureLightingSettings(log) : null;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(bake ? "Ночь: подготовка запекания Lobby" : "Ночной пресет освещения Lobby");
            try
            {
                EnsureTentFixtureLights(environment.transform, log);
                var moon = ApplyLights(environment.transform, bake, log);
                ApplyRenderSettings(materials.Sky, moon, bake, log);
                ApplyEmissiveMaterials(environment.transform, materials, log);
                if (bake)
                {
                    ApplySceneLightingSettings(lighting, log);
                    ApplyStaticGi(environment.transform, log);
                    ApplyLightProbes(scene, environment.transform, log);
                }
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory(Report);
            File.WriteAllText(Report + (bake ? "night-bake-prep.txt" : "night-preset.txt"), log.ToString());
            GameLog.Debug.Info("[LobbyNightLighting] " + (bake ? "Сцена подготовлена к запеканию; запустите Generate Lighting отдельно." : "Ночь на realtime-источниках применена.")
                + " Сохраните Lobby. Отчёт: " + Report);
        }

        private static Scene Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Нужен свободный Editor вне Play/компиляции/prefab stage.");
            if (Lightmapping.isRunning)
                throw new InvalidOperationException("Идёт запекание освещения; дождитесь окончания.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Откройте только " + ScenePath + ".");
            if (scene.isDirty)
                throw new InvalidOperationException("Сначала сохраните свои изменения сцены; пресет не перезаписывает dirty Lobby.");
            return scene;
        }

        // ---------- Материалы ----------

        private sealed class NightMaterials
        {
            public Material Sky, LampLong, DoorLamp, FloodFace, WindowGlow;
            public readonly Dictionary<string, Material> SwapByName = new Dictionary<string, Material>(StringComparer.Ordinal);
        }

        private static NightMaterials EnsureMaterials(StringBuilder log)
        {
            EnsureFolder(NightFolder);
            var result = new NightMaterials();

            result.Sky = LoadOrCreate("LobbyNightSky", "Skybox/Procedural", log, m =>
            {
                m.SetFloat("_SunDisk", 1);              // Simple: дешёвый диск «луны» от RenderSettings.sun.
                m.SetFloat("_SunSize", .03f);
                m.SetFloat("_SunSizeConvergence", 6);
                m.SetFloat("_AtmosphereThickness", .35f);
                m.SetColor("_SkyTint", new Color(.16f, .20f, .33f));
                m.SetColor("_GroundColor", new Color(.02f, .022f, .028f));
                m.SetFloat("_Exposure", .18f);
            });

            // URP Lit с картой эмиссии из того же пака: исходные M_* сделаны на Shader Graph без эмиссии.
            result.LampLong = CopyEmissive("LobbyNightLampLong", "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Assets/Materials/MI_Lamp_long1.mat", log);
            result.DoorLamp = CopyEmissive("LobbyNightDoorLamp", "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Assets/Materials/MI_Door_lamp.mat", log);
            result.FloodFace = LoadOrCreate("LobbyNightFloodFace", "Universal Render Pipeline/Unlit", log, m => m.SetColor("_BaseColor", new Color(1f, .95f, .84f)));
            result.WindowGlow = LoadOrCreate("LobbyNightWindowGlow", "Universal Render Pipeline/Unlit", log, m => m.SetColor("_BaseColor", new Color(.85f, .58f, .30f)));

            if (result.LampLong != null) result.SwapByName["M_Lamp_long"] = result.LampLong;
            if (result.DoorLamp != null) result.SwapByName["M_Door_lamp"] = result.DoorLamp;
            result.SwapByName["PlaceholderFloodlightFace"] = result.FloodFace;
            ApplyLampGlow(result.LampLong, log);
            AssetDatabase.SaveAssets();
            return result;
        }

        private static Material LoadOrCreate(string name, string shaderName, StringBuilder log, Action<Material> configure)
        {
            string path = NightFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(shaderName) ?? throw new InvalidOperationException("Не найден шейдер " + shaderName + ".");
                material = new Material(shader) { name = name };
                configure(material);
                AssetDatabase.CreateAsset(material, path);
                log.AppendLine("Создан материал " + path);
            }
            else log.AppendLine("Материал уже есть, не перезаписан: " + path);
            return material;
        }

        private static Material CopyEmissive(string name, string sourcePath, StringBuilder log)
        {
            string path = NightFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) { log.AppendLine("Материал уже есть, не перезаписан: " + path); return material; }
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null) { log.AppendLine("ПРЕДУПРЕЖДЕНИЕ: нет источника " + sourcePath + "; замена пропущена."); return null; }
            material = new Material(source) { name = name };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", WarmLamp);
            // RealtimeEmissive, а не EmissiveIsBlack: с ним URP выключает _EMISSION (QPM-SHADER-006, правило аудитора
            // material.emission-off). Realtime GI выключен — эмиссия видна, но второй раз в свет не идёт (свет ламп запекают
            // настоящие источники). Пресет создаёт материал один раз; потерянное свечение возвращает фиксер аудитора
            // EmissionOffFixer (правило material.emission-off).
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(material, path);
            log.AppendLine("Создан материал " + path + " из " + sourcePath);
            return material;
        }

        // Плафоны ламп должны читаться источником света. Baked Lit эмиссии не имеет, поэтому фиксер шейдеров аудитора
        // (BakedLitMaterialFixer) их не трогает: длинная лампа — Simple Lit без бликов с картой эмиссии (корпус
        // освещён, светится только рассеиватель). Гражданские плафоны (SM_TopLight и др., стекло M_Emissive) удалены из
        // базы 2026-10-22; колбы ламп шатра — Unlit LobbyNightWindowGlow (LobbyTentLightFixtures).
        // GI-флаг RealtimeEmissive: URP (BaseShaderGUI.SetMaterialKeywords) включает _EMISSION только при AnyEmissive,
        // а с None или EmissiveIsBlack при импорте/валидации выключает его — плафон гас. Realtime GI в Lobby выключен,
        // поэтому эмиссия видна, но в свет не идёт (свет ламп запекают настоящие источники); BakedEmissive дал бы
        // вклад в лайтмапы.
        private static void ApplyLampGlow(Material lampLong, StringBuilder log)
        {
            if (lampLong != null)
            {
                var simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit") ?? throw new InvalidOperationException("Нет шейдера URP Simple Lit.");
                lampLong.shader = simpleLit;
                // У Baked Lit нет _EmissionMap: карта берётся из пака, а не из текущего материала.
                var emissionMap = AssetDatabase.LoadAssetAtPath<Texture>(LampLongEmissionMap);
                lampLong.shaderKeywords = Array.Empty<string>();
                OpaqueSurface(lampLong);
                lampLong.SetFloat("_SpecularHighlights", 1f); // SpecularSource.NoSpecular
                lampLong.SetFloat("_ReceiveShadows", 0f);
                lampLong.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                lampLong.SetTexture("_BumpMap", null);
                lampLong.SetTexture("_EmissionMap", emissionMap);
                lampLong.SetColor("_EmissionColor", WarmLamp * LampGlowBoost);
                lampLong.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                lampLong.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(lampLong);
                log.AppendLine("Плафон: " + AssetDatabase.GetAssetPath(lampLong) + " — Simple Lit, эмиссия по карте " + (emissionMap != null ? emissionMap.name : "нет") + ".");
            }
        }

        private static void OpaqueSurface(Material m)
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", 1f);
            m.SetFloat("_DstBlend", 0f);
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", 2f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------- LightingSettings ----------

        private static LightingSettings EnsureLightingSettings(StringBuilder log)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingAssetPath);
            if (settings != null) { log.AppendLine("LightingSettings уже есть, не перезаписан: " + LightingAssetPath); return settings; }
            settings = new LightingSettings
            {
                name = "LobbyNight",
                bakedGI = true,
                realtimeGI = false,
                lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                mixedBakeMode = MixedLightingMode.IndirectOnly,   // Baked Indirect: без shadowmask-текстур.
                directionalityMode = LightmapsMode.NonDirectional, // половина памяти и дешевле выборка на Quest.
                lightmapResolution = 10f,
                lightmapPadding = 4,
                lightmapMaxSize = 1024,
                lightmapCompression = LightmapCompression.NormalQuality,
                ao = true,
                aoMaxDistance = .8f,
                aoExponentIndirect = 1f,
                aoExponentDirect = 0f,
                directSampleCount = 32,
                indirectSampleCount = 256,
                environmentSampleCount = 128,
                maxBounces = 2,
                albedoBoost = 1f,
                indirectScale = 1f,
                filteringMode = LightingSettings.FilterMode.Auto,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(LightingAssetPath));
            AssetDatabase.CreateAsset(settings, LightingAssetPath);
            log.AppendLine("Создан " + LightingAssetPath);
            return settings;
        }

        private static void ApplySceneLightingSettings(LightingSettings settings, StringBuilder log)
        {
            var lightmapSettings = SceneLightmapSettingsObject();
            Undo.RecordObject(lightmapSettings, "Ночной LightingSettings");
            Lightmapping.lightingSettings = settings;
            log.AppendLine("Сцене назначен " + LightingAssetPath + " (только ручной Generate Lighting).");
        }

        // ---------- RenderSettings ----------

        private static void ApplyRenderSettings(Material sky, Light moon, bool bake, StringBuilder log)
        {
            Undo.RecordObject(SceneRenderSettingsObject(), "Ночные RenderSettings");
            RenderSettings.skybox = sky;
            RenderSettings.sun = moon;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky * Boost(bake);
            RenderSettings.ambientEquatorColor = AmbientEquator * Boost(bake);
            RenderSettings.ambientGroundColor = AmbientGround * Boost(bake);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 105f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 64;
            RenderSettings.reflectionIntensity = .4f;
            log.AppendLine("RenderSettings: ночное небо, Trilight ambient, линейный туман 22–105 м, отражения неба 64px x0.4.");
        }

        // Внутренние объекты сцены нужны только для Undo; при смене API — явная ошибка вместо тихой правки без отката.
        private static UnityEngine.Object SceneRenderSettingsObject()
        {
            var method = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
            var value = method?.Invoke(null, null) as UnityEngine.Object;
            return value != null ? value : Resources.FindObjectsOfTypeAll<RenderSettings>().FirstOrDefault()
                ?? throw new InvalidOperationException("Не найден объект RenderSettings для Undo.");
        }

        private static UnityEngine.Object SceneLightmapSettingsObject()
        {
            var method = typeof(LightmapEditorSettings).GetMethod("GetLightmapSettings", BindingFlags.NonPublic | BindingFlags.Static);
            var value = method?.Invoke(null, null) as UnityEngine.Object;
            return value != null ? value : Resources.FindObjectsOfTypeAll<LightmapSettings>().FirstOrDefault()
                ?? throw new InvalidOperationException("Не найден объект LightmapSettings для Undo.");
        }

        // ---------- Источники ----------

        // Стрельбища по обе стороны навеса: прожектор, чьё пятно попадает сюда, освещает мишени.
        private static bool LitsRange(Light light)
        {
            if (light.type != LightType.Spot || light.transform.forward.y > -.05f) return false;
            var hit = light.transform.position + light.transform.forward * (-light.transform.position.y / light.transform.forward.y);
            return Mathf.Abs(hit.x) < 12f && Mathf.Abs(hit.z) > 14f && Mathf.Abs(hit.z) < 32f;
        }

        private static Light ApplyLights(Transform environment, bool bake, StringBuilder log)
        {
            Light moon = null;
            int baked = 0, mixed = 0, realtime = 0, snapped = 0;
            foreach (var light in environment.GetComponentsInChildren<Light>(true))
            {
                Undo.RecordObject(light, "Ночной свет");
                Undo.RecordObject(light.gameObject, "Ночной свет");
                Undo.RecordObject(light.transform, "Ночной свет");
                string name = light.name;
                if (light.type == LightType.Directional && (name == "CampDaylight" || name == "CampMoonlight"))
                {
                    if (moon != null) throw new InvalidOperationException("В Environment больше одного главного направленного света.");
                    moon = light;
                    light.name = "CampMoonlight";
                    // Бюджет GPU Quest (2026-10-13, пользователь): в запечённой ночи realtime-света нет совсем,
                    // динамика (игроки, оружие) освещается пробами.
                    light.lightmapBakeType = bake ? LightmapBakeType.Baked : LightmapBakeType.Realtime;
                    light.color = MoonColor;
                    light.intensity = .22f * Boost(bake);
                    light.shadows = LightShadows.None;
                    light.transform.rotation = Quaternion.Euler(32f, 135f, 0f); // луна на северо-западе, над стрельбищем.
                    if (bake) baked++; else realtime++;
                }
                // Шатёр (lobby-reflections, 2026-10-22): лампы CampLighting/BayLamp и сетка TentFill дают ровную базу,
                // TentAccent — яркое пятно над точкой акцента; в запечённой ночи runtime-стоимость любого числа ламп — ноль.
                else if (name == TentLampLight) { Warm(light, bake, TentLampIntensity, TentLampRange); if (bake) baked++; else realtime++; }
                else if (name == TentFillLight) { Warm(light, bake, TentFillIntensity, TentFillRange); if (bake) baked++; else realtime++; }
                else if (name == TentAccentLight) { Warm(light, bake, TentAccentIntensity, TentAccentRange); if (bake) baked++; else realtime++; }
                else if (name == "ExteriorWarmLight" || name == "CanopyWarmLight")
                {
                    if (SnapToFixture(light, environment, log)) snapped++;
                    Warm(light, bake, name == "CanopyWarmLight" ? 4f : 5f, name == "CanopyWarmLight" ? 6f : 8f);
                    if (bake) baked++; else realtime++;
                }
                else if (LitsRange(light))
                {
                    // Стрельбище — главный объект: в превью прямой свет realtime, в запечённой ночи — Baked
                    // (мишени и оружие освещаются пробами стрельбища).
                    light.lightmapBakeType = bake ? LightmapBakeType.Baked : LightmapBakeType.Mixed;
                    light.color = FloodWhite;
                    light.intensity = 180f * Boost(bake);
                    light.range = 30f;
                    light.spotAngle = 100f;
                    light.innerSpotAngle = 70f;
                    light.shadows = bake ? LightShadows.Soft : LightShadows.None;
                    light.renderMode = bake ? LightRenderMode.Auto : LightRenderMode.ForcePixel;
                    if (bake) baked++; else mixed++;
                }
                else if (light.type == LightType.Spot)
                {
                    // Прожекторы территории (КПП, казармы): пятно на подходах.
                    light.color = FloodWhite;
                    light.intensity = 110f * Boost(bake);
                    light.range = Mathf.Max(light.range, 24f);
                    light.spotAngle = 80f;
                    light.innerSpotAngle = 55f;
                    if (bake) { light.lightmapBakeType = LightmapBakeType.Baked; light.shadows = LightShadows.Soft; light.shapeRadius = .15f; baked++; }
                    else { light.lightmapBakeType = LightmapBakeType.Realtime; light.shadows = LightShadows.None; realtime++; }
                }
                else log.AppendLine("ПРЕДУПРЕЖДЕНИЕ: источник без правила оставлен как есть: " + HierarchyPath(light.transform, environment) + " (" + light.lightmapBakeType + ")");
            }
            if (moon == null) throw new InvalidOperationException("Не найден Environment/CampDaylight (или CampMoonlight).");
            if (mixed > 4) throw new InvalidOperationException("Больше четырёх realtime-прожекторов стрельбищ: на мишень с одной стороны должно приходиться не больше двух (лимит URP на объект).");
            log.AppendLine("Свет: realtime " + realtime + ", прожекторы стрельбищ (mixed) " + mixed + ", baked " + baked + "; перенесено к светильникам " + snapped + ".");
            return moon;
        }

        // Светильники шатра (BayLamp_N — LobbyCanopyBaysBuilder, TentFill_N/TentAccent_X — LobbyTentLightFixtures) приходят
        // без Light: источник ставит этот пресет к их сокету «LightingSocket__<светильник>__CommonLightingOwner». Имя
        // источника задаёт правило яркости в ApplyLights: BayLamp — как лампы шатра, TentFill — база, TentAccent — акцент.
        private static string TentLightName(string fixture) =>
            fixture.StartsWith("TentFill_", StringComparison.Ordinal) ? TentFillLight
            : fixture.StartsWith("TentAccent_", StringComparison.Ordinal) ? TentAccentLight : TentLampLight;

        private static void EnsureTentFixtureLights(Transform environment, StringBuilder log)
        {
            int created = 0, present = 0;
            foreach (var socket in environment.GetComponentsInChildren<Transform>(true))
            {
                var match = TentFixtureSocket.Match(socket.name);
                if (!match.Success) continue;
                var fixture = socket.parent;
                if (fixture == null || fixture.name != match.Groups[1].Value) continue;
                if (fixture.GetComponentsInChildren<Light>(true).Length > 0) { present++; continue; }
                var go = new GameObject(TentLightName(fixture.name));
                Undo.RegisterCreatedObjectUndo(go, "Свет светильника шатра");
                Undo.SetTransformParent(go.transform, fixture, "Свет светильника шатра");
                go.transform.SetPositionAndRotation(socket.position + Vector3.down * .1f, Quaternion.identity);
                go.isStatic = false;
                var light = Undo.AddComponent<Light>(go);
                light.type = LightType.Point;
                created++;
                log.AppendLine("Свет светильника шатра создан: " + HierarchyPath(go.transform, environment) + " " + go.transform.position.ToString("F2"));
            }
            log.AppendLine("Светильники шатра: создано источников " + created + ", уже со светом " + present + ".");
        }

        // Тёплая лампа: в превью — realtime без теней, при подготовке запекания — Baked с мягкими тенями.
        private static void Warm(Light light, bool bake, float intensity, float range)
        {
            light.color = WarmLamp;
            light.intensity = intensity * Boost(bake);
            light.range = range;
            light.bounceIntensity = 1f;
            if (bake) { light.lightmapBakeType = LightmapBakeType.Baked; light.shadows = LightShadows.Soft; light.shapeRadius = .15f; }
            else { light.lightmapBakeType = LightmapBakeType.Realtime; light.shadows = LightShadows.None; }
        }

        // Свет зоны, отставший от своего светильника после перестановки, переносится к ближайшему сокету
        // «LightingSocket__…CommonLightingOwner» той же зоны (или к лампе-плафону, если сокета нет).
        private static bool SnapToFixture(Light light, Transform environment, StringBuilder log)
        {
            var zone = light.transform;
            while (zone.parent != null && zone.parent != environment) zone = zone.parent;
            var fixtures = zone.GetComponentsInChildren<Transform>(true)
                .Where(t => t != light.transform && (t.name.StartsWith("LightingSocket", StringComparison.Ordinal) || t.name.StartsWith("SM_Door_lamp_a", StringComparison.Ordinal) || t.name.StartsWith("RidgeFixture", StringComparison.Ordinal)))
                .ToList();
            if (fixtures.Count == 0) return false;
            var nearest = fixtures.OrderBy(t => (t.position - light.transform.position).sqrMagnitude).First();
            if ((nearest.position - light.transform.position).magnitude < .6f) return false;
            var socket = fixtures.Where(t => t.name.StartsWith("LightingSocket", StringComparison.Ordinal)).OrderBy(t => (t.position - light.transform.position).sqrMagnitude).FirstOrDefault() ?? nearest;
            log.AppendLine("Перенесён к светильнику: " + HierarchyPath(light.transform, environment) + " " + light.transform.position.ToString("F1") + " → " + socket.name + " " + socket.position.ToString("F1"));
            light.transform.position = socket.position + Vector3.down * .1f;
            return true;
        }

        // ---------- Эмиссия ----------

        private static void ApplyEmissiveMaterials(Transform environment, NightMaterials materials, StringBuilder log)
        {
            int swapped = 0, windows = 0;
            foreach (var renderer in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                var shared = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    if (materials.SwapByName.TryGetValue(shared[i].name, out var night)) { shared[i] = night; changed = true; swapped++; }
                }
                if (renderer.name.StartsWith(WindowGlowPrefix, StringComparison.Ordinal))
                {
                    for (int i = 0; i < shared.Length; i++) shared[i] = materials.WindowGlow;
                    changed = true; windows++;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Ночная эмиссия");
                renderer.sharedMaterials = shared;
            }
            log.AppendLine("Эмиссия: заменено слотов ламп/прожекторов " + swapped + ", окон " + WindowGlowPrefix + "* " + windows + ".");
            if (windows == 0) log.AppendLine("Окна: карточек " + WindowGlowPrefix + "* нет; их добавляет владелец зданий, материал " + NightFolder + "/LobbyNightWindowGlow.mat готов.");
        }

        // ---------- Static GI ----------

        private static void ApplyStaticGi(Transform environment, StringBuilder log)
        {
            int lightmapped = 0, probeLit = 0, skipped = 0;
            foreach (Transform zone in environment)
            {
                if (zone.GetComponent<Light>() != null || zone.name == ContainerName) continue;
                if (!ZoneLightmapScale.TryGetValue(zone.name, out float scale))
                {
                    log.AppendLine("ПРЕДУПРЕЖДЕНИЕ: зона без правила GI пропущена: " + zone.name);
                    continue;
                }
                foreach (var renderer in zone.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var go = renderer.gameObject;
                    var flags = GameObjectUtility.GetStaticEditorFlags(go);
                    bool authoredStatic = (flags & (StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic)) != 0;
                    if (!authoredStatic || !go.activeInHierarchy || IsDynamic(renderer) || IsCutoutOrTransparent(renderer)
                        || go.GetComponent<TextMesh>() != null)
                    {
                        skipped++;
                        continue;
                    }
                    var filter = go.GetComponent<MeshFilter>();
                    var mesh = filter != null ? filter.sharedMesh : null;
                    bool small = renderer.bounds.size.magnitude < .35f;
                    bool hasUv2 = mesh != null && mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
                    Undo.RecordObject(go, "Ночной static GI");
                    Undo.RecordObject(renderer, "Ночной static GI");
                    GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.ContributeGI);
                    // Только для запекания теней от baked-источников: realtime-теней в пресете нет, shadow pass не рисуется.
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    if (small || !hasUv2)
                    {
                        renderer.receiveGI = ReceiveGI.LightProbes;
                        // Пробы покрывают арену, стрельбища и базу внутри ограды; периметр — ambient.
                        renderer.lightProbeUsage = zone.name == AmbientOnlyZone ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
                        probeLit++;
                    }
                    else
                    {
                        renderer.receiveGI = ReceiveGI.Lightmaps;
                        renderer.scaleInLightmap = scale;
                        lightmapped++;
                    }
                }
            }
            log.AppendLine("Static GI: в лайтмапе " + lightmapped + ", ContributeGI через probes " + probeLit + ", пропущено (не static/динамика/cutout/текст) " + skipped + ".");
        }

        private static bool IsDynamic(Renderer renderer) =>
            renderer.GetComponentInParent<Rigidbody>(true) != null
            || renderer.GetComponentInParent<Animator>(true) != null
            || renderer.GetComponentsInParent<MonoBehaviour>(true).Any(c => c != null && c.GetType().Name == "NetworkIdentity");

        private static bool IsCutoutOrTransparent(Renderer renderer) =>
            renderer.sharedMaterials.Any(m => m != null && (m.renderQueue >= (int)RenderQueue.AlphaTest || m.IsKeywordEnabled("_ALPHATEST_ON")));

        // ---------- Light probes ----------

        private static void ApplyLightProbes(Scene scene, Transform environment, StringBuilder log)
        {
            var arena = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Single();
            if (!arena.TryFloorBounds(out var floor)) throw new InvalidOperationException("Нет границ пола PhysicalArenaDefinition.");

            var container = environment.Find(ContainerName);
            if (container == null)
            {
                var go = new GameObject(ContainerName);
                Undo.RegisterCreatedObjectUndo(go, "Ночной контейнер освещения");
                Undo.SetTransformParent(go.transform, environment, "Ночной контейнер освещения");
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                container = go.transform;
            }
            var probes = container.Find(ProbeGroupName)?.GetComponent<LightProbeGroup>();
            if (probes == null)
            {
                var go = new GameObject(ProbeGroupName);
                Undo.RegisterCreatedObjectUndo(go, "Ночные light probes");
                Undo.SetTransformParent(go.transform, container, "Ночные light probes");
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                probes = Undo.AddComponent<LightProbeGroup>(go);
            }
            Undo.RecordObject(probes, "Ночные light probes");
            var positions = new List<Vector3>();
            float y0 = floor.max.y;
            // Арена: игроки, руки, оружие. Шаг 2.5 м, три высоты (пол, пояс, над головой).
            for (float x = floor.min.x + .25f; x <= floor.max.x - .2f; x += 2.5f)
            for (float z = floor.min.z + .25f; z <= floor.max.z - .2f; z += 2.5f)
                foreach (float h in new[] { .3f, 1.3f, 2.5f })
                    positions.Add(new Vector3(x, y0 + h, z));
            // Стрельбища: мишени и пули на север и на юг от арены. Шаг 4 м, две высоты.
            for (float x = floor.min.x - 3f; x <= floor.max.x + 3f; x += 4f)
            for (float d = 2f; d <= 26f; d += 4f)
                foreach (float z in new[] { floor.max.z + d, floor.min.z - d })
                foreach (float h in new[] { .5f, 2.5f })
                    positions.Add(new Vector3(x, y0 + h, z));
            // База внутри ограды: реквизит под запечёнными прожекторами и лампами. Шаг 5 м, две высоты;
            // арена и стрельбища уже покрыты плотнее.
            var dense = new Bounds(floor.center, floor.size + new Vector3(8f, 0f, 56f));
            for (float x = BaseMin.x; x <= BaseMax.x; x += 5f)
            for (float z = BaseMin.y; z <= BaseMax.y; z += 5f)
            {
                if (dense.Contains(new Vector3(x, floor.center.y, z))) continue;
                foreach (float h in new[] { .5f, 2.5f })
                    positions.Add(new Vector3(x, y0 + h, z));
            }
            // Точки акцента (lobby-reflections, 2026-10-22): шаг 1 м на высотах рук, чтобы оружие и руки получали пятно
            // акцентной лампы, а не среднее по сетке 2.5 м. Стена арсенала — по её поднятому корпусу, точки стрельбы —
            // полосы у северного и южного торцов шатра.
            int dense = 0;
            foreach (var anchor in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalStationAnchor>(true)))
            {
                var wall = anchor.RaisedBoundsWorld;
                float front = wall.center.x < 0 ? wall.max.x + .3f : wall.min.x - .3f, side = wall.center.x < 0 ? 1f : -1f;
                for (float d = 0f; d <= 2.01f; d += 1f)
                for (float z = wall.min.z; z <= wall.max.z + .01f; z += 1f)
                    foreach (float h in new[] { .9f, 1.5f, 2.1f })
                    { positions.Add(new Vector3(front + side * d, y0 + h, z)); dense++; }
            }
            var tent = LobbyMilitaryCampSources.TentBounds(floor);
            foreach (float s in new[] { 1f, -1f })
            for (float x = tent.min.x + .75f; x <= tent.max.x - .7f; x += 1.5f)
            for (float d = -1f; d <= 1.01f; d += 1f)
                foreach (float h in new[] { 1.2f, 1.8f })
                { positions.Add(new Vector3(x, y0 + h, s * (tent.max.z - .6f + d))); dense++; }
            var local = probes.transform;
            probes.probePositions = positions.Select(p => local.InverseTransformPoint(p)).ToArray();
            log.AppendLine("Light probes: " + positions.Count + " точек (из них у акцентов " + dense + ") в " + ContainerName + "/" + ProbeGroupName + ".");
            ApplyTentReflectionProbe(container, tent, y0, log);
        }

        // Запечённая проба отражений шатра (QPM-LIGHT-008, правило аудитора lighting.dark-reflection-zone): без неё металл
        // оружия отражает ночное небо и чернеет. Box Projection по объёму под кровлей; смешивание проб не нужно — проба одна.
        private const string TentReflectionProbeName = "TentReflectionProbe";
        private static void ApplyTentReflectionProbe(Transform container, Bounds tent, float floorY, StringBuilder log)
        {
            var go = container.Find(TentReflectionProbeName)?.gameObject;
            if (go == null)
            {
                go = new GameObject(TentReflectionProbeName);
                Undo.RegisterCreatedObjectUndo(go, "Проба отражений шатра");
                Undo.SetTransformParent(go.transform, container, "Проба отражений шатра");
            }
            var probe = go.GetComponent<ReflectionProbe>();
            if (probe == null) probe = Undo.AddComponent<ReflectionProbe>(go);
            Undo.RecordObject(go.transform, "Проба отражений шатра");
            Undo.RecordObject(probe, "Проба отражений шатра");
            const float height = 4.6f;
            go.transform.SetPositionAndRotation(new Vector3(tent.center.x, floorY + 1.6f, tent.center.z), Quaternion.identity);
            probe.mode = ReflectionProbeMode.Baked;
            probe.boxProjection = true;
            probe.size = new Vector3(tent.size.x, height, tent.size.z);
            probe.center = new Vector3(0f, height / 2 - 1.6f, 0f);
            probe.resolution = 128;
            probe.hdr = true;
            probe.importance = 1;
            probe.blendDistance = 0f;
            probe.intensity = 1f;
            log.AppendLine("Проба отражений шатра: " + probe.size + ", Box Projection, 128 px, запекается с Generate Lighting.");
        }

        private static string HierarchyPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
