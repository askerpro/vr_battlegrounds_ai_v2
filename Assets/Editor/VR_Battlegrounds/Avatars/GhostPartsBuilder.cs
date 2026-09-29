using System.Collections.Generic;
using System.Linq;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Собирает части призрака выбывшего (<see cref="GhostBody"/>) в <c>Prefabs/Player/Ghost/</c>:
    /// <list type="bullet">
    /// <item><b>Кисти</b> — префабы <see cref="GhostHand"/> из FBX SmallHands UltimateXR: только меш
    ///       кисти (без перчатки), материал призрака, ссылки на кости.</item>
    /// <item><b>Голова и торс</b> — статичные меши из Cyborg UltimateXR (шлем с визором, корпус),
    ///       запечённые в позе модели. Начало координат обоих — в глазах: в игре они ставятся в
    ///       камеру, и корпус сам оказывается под головой на своём месте.</item>
    /// <item><b>Префаб призрака</b> <c>GhostBody</c> (<see cref="GhostModel"/>) — только если его нет:
    ///       дальше он правится руками.</item>
    /// </list>
    /// </summary>
    public static class GhostPartsBuilder
    {
        public const string OutFolder = "Assets/Prefabs/Player/Ghost";

        private const string SmallHandsFolder = "Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/SmallHands/";
        private const string CyborgFbx = "Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/Cyborg/CyborgGeo.fbx";
        private const string MaterialPath = OutFolder + "/GhostMaterial.mat";

        private static readonly string[] HeadParts = { "HelmetGeo", "VisorGeo" };
        private static readonly string[] TorsoParts = { "BodyGeo" };

        /// <summary>Глаза Cyborg относительно центра визора: чуть глубже внутрь шлема.</summary>
        private const float EyesBehindVisor = 0.04f;

        [MenuItem("Tools/VR Battlegrounds/Avatars/Build Ghost Parts")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/Prefabs/Player", "Ghost");

            BuildHand(UxrHandSide.Left, "Left");
            BuildHand(UxrHandSide.Right, "Right");
            BuildHeadAndTorso();
            AssetDatabase.SaveAssets();
            BuildModelIfMissing();
        }

        // ── Префаб призрака ─────────────────────────────────────────────────

        /// <summary>Шлем и корпус Cyborg чуть меньше оригинала — призрак не крупнее живого. Дальше — правится в префабе.</summary>
        private const float InitialBodyScale = 0.85f;

        /// <summary>
        /// Префаб <see cref="GhostModel"/> собирается только если его нет: дальше его правят руками
        /// (масштаб, смещения), и повторная сборка не должна это стереть. Меши и кисти — отдельные
        /// ассеты, их пересборка до префаба доходит сама.
        /// </summary>
        private static void BuildModelIfMissing()
        {
            string path = $"{OutFolder}/GhostBody.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            var root = new GameObject("GhostBody");
            try
            {
                var model = root.AddComponent<GhostModel>();
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                model.Torso = MeshPart(root.transform, "Torso", $"{OutFolder}/GhostTorso.asset", material);
                model.Head = MeshPart(root.transform, "Head", $"{OutFolder}/GhostHead.asset", material);
                model.LeftHand = HandPart(root.transform, "Left");
                model.RightHand = HandPart(root.transform, "Right");

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform MeshPart(Transform parent, string name, string meshPath, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * InitialBodyScale;
            go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static GhostHand HandPart(Transform parent, string side)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutFolder}/GhostHand{side}.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = "Hand_" + side;
            // Место для просмотра префаба; в игре кисть ставится по кисти аватара.
            instance.transform.localPosition = new Vector3(side == "Left" ? -0.2f : 0.2f, -0.5f, 0.3f);
            return instance.GetComponent<GhostHand>();
        }

        // ── Кисти ───────────────────────────────────────────────────────────

        private static void BuildHand(UxrHandSide side, string s)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SmallHandsFolder + "SmallHand" + s + ".fbx");
            var instance = (GameObject)Object.Instantiate(fbx);
            instance.name = "GhostHand" + s;

            try
            {
                Object.DestroyImmediate(instance.transform.Find("Glove" + s).gameObject);

                // В FBX запястье — узел SmallHand<Side>, пальцы — прямо под ним.
                Transform wrist = instance.transform.Find("SmallHand" + s);
                var hand = instance.AddComponent<GhostHand>();
                hand.Side = side;
                hand.Wrist = wrist;
                hand.Thumb = Finger(wrist, "Thumb", s, hasMetacarpal: false);
                hand.Index = Finger(wrist, "Index", s, hasMetacarpal: true);
                hand.Middle = Finger(wrist, "Middle", s, hasMetacarpal: true);
                hand.Ring = Finger(wrist, "Ring", s, hasMetacarpal: true);
                hand.Little = Finger(wrist, "Little", s, hasMetacarpal: true);

                hand.Renderer = instance.transform.Find("Hand" + s).GetComponent<SkinnedMeshRenderer>();
                hand.Renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                hand.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hand.Renderer.receiveShadows = false;

                PrefabUtility.SaveAsPrefabAsset(instance, $"{OutFolder}/GhostHand{s}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Кости пальца. У большого пальца нет пястной: <c>Thumb_Palm</c> — уже проксимальная
        /// (так размечен SmallHandsAvatar).
        /// </summary>
        private static GhostHand.Finger Finger(Transform wrist, string name, string s, bool hasMetacarpal)
        {
            Transform palm = wrist.Find($"{name}_Palm_{s}");
            Transform b0 = palm.Find($"{name}_0_{s}");
            Transform b1 = b0.Find($"{name}_1_{s}");
            Transform b2 = b1.Find($"{name}_2_{s}");

            return hasMetacarpal
                ? new GhostHand.Finger { Metacarpal = palm, Proximal = b0, Intermediate = b1, Distal = b2 }
                : new GhostHand.Finger { Proximal = palm, Intermediate = b0, Distal = b1 };
        }

        // ── Голова и торс ───────────────────────────────────────────────────

        private static void BuildHeadAndTorso()
        {
            var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CyborgFbx));
            try
            {
                Dictionary<string, SkinnedMeshRenderer> parts = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                                                        .ToDictionary(r => r.name);

                // Cyborg смотрит в +Z. Глаза — за центром визора.
                Vector3 eyes = parts["VisorGeo"].bounds.center - Vector3.forward * EyesBehindVisor;

                SaveMesh(Bake(HeadParts.Select(n => parts[n]), eyes, "GhostHead"), "GhostHead");
                SaveMesh(Bake(TorsoParts.Select(n => parts[n]), eyes, "GhostTorso"), "GhostTorso");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Запекает скин-меши в позе модели в один статичный меш с началом в <paramref name="origin"/> (мир).</summary>
        private static Mesh Bake(IEnumerable<SkinnedMeshRenderer> renderers, Vector3 origin, string name)
        {
            var combine = new List<CombineInstance>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                var baked = new Mesh();
                renderer.BakeMesh(baked, true);
                combine.Add(new CombineInstance
                {
                    mesh = baked,
                    transform = Matrix4x4.Translate(-origin) * Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one)
                });
            }

            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(combine.ToArray(), mergeSubMeshes: true, useMatrices: true);
            mesh.RecalculateBounds();
            foreach (CombineInstance c in combine) Object.DestroyImmediate(c.mesh);
            return mesh;
        }

        private static void SaveMesh(Mesh mesh, string name)
        {
            string path = $"{OutFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Перезапись на месте — ссылки и GUID сохраняются.
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return;
            }
            AssetDatabase.CreateAsset(mesh, path);
        }
    }
}
