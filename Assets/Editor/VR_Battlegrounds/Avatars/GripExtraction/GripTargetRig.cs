using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Риг кисти цели: содержимое префаба аватара в изолированной preview-сцене (<c>LoadPrefabContents</c>, без сохранения),
    /// исходные локальные повороты костей кистей и геометрия посадки — запястье, центр ладони, нормаль ладони, граббер.
    /// </summary>
    public sealed class GripTargetRig : IDisposable
    {
        private readonly GameObject                        _root;
        private readonly Dictionary<Transform, Quaternion> _bind = new Dictionary<Transform, Quaternion>();
        private readonly PreviewRenderUtility _scenePreview;
        private bool _disposed;

        public GripTargetRig(string prefabPath)
        {
            PrefabPath = prefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                throw new ArgumentException($"Нет префаба рига {prefabPath}");
            }

            _root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Avatar = _root.GetComponentInChildren<UxrAvatar>(true);
                if (Avatar == null)
                {
                    throw new ArgumentException($"{prefabPath}: нет UxrAvatar");
                }

                foreach (UxrHandSide side in Sides)
                {
                    Transform wrist = Avatar.GetHand(side).Wrist;
                    if (wrist == null)
                    {
                        throw new ArgumentException($"{prefabPath}: нет запястья {side}");
                    }

                    // Исходная поза кисти: восстанавливается перед каждой позой (как pose-retarget).
                    foreach (Transform t in wrist.GetComponentsInChildren<Transform>(true))
                    {
                        _bind[t] = t.localRotation;
                    }
                }
            }
            catch
            {
                PrefabUtility.UnloadPrefabContents(_root);
                throw;
            }
        }

        public static readonly UxrHandSide[] Sides = { UxrHandSide.Left, UxrHandSide.Right };

        /// <summary>Временная копия сценового аватара, включая несохранённые пальцы. Сценовый оригинал не меняется.</summary>
        public GripTargetRig(UxrAvatar sceneAvatar)
        {
            if (!sceneAvatar || EditorUtility.IsPersistent(sceneAvatar)) throw new ArgumentException("Нужен сценовый SDK-аватар.");
            Vector3 scale = sceneAvatar.transform.lossyScale;
            if (scale.x <= 0 || Mathf.Abs(scale.x - scale.y) > 1e-5f || Mathf.Abs(scale.x - scale.z) > 1e-5f)
                throw new ArgumentException("Временной копии нужен положительный uniform scale аватара.");
            var source = PrefabUtility.GetCorrespondingObjectFromSource(sceneAvatar.gameObject);
            PrefabPath = source ? AssetDatabase.GetAssetPath(source) : "";
            _scenePreview = new PreviewRenderUtility();
            try
            {
                _root = _scenePreview.InstantiatePrefabInScene(sceneAvatar.gameObject);
                _root.transform.SetPositionAndRotation(sceneAvatar.transform.position, sceneAvatar.transform.rotation);
                _root.transform.localScale = scale;
                foreach (var behaviour in _root.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                Avatar = _root.GetComponent<UxrAvatar>();
                if (!Avatar) throw new ArgumentException("В копии отсутствует UxrAvatar.");
                foreach (var side in Sides)
                {
                    var wrist = Avatar.GetHand(side).Wrist;
                    if (!wrist) throw new ArgumentException("В копии отсутствует запястье " + side);
                    foreach (var bone in wrist.GetComponentsInChildren<Transform>(true)) _bind[bone] = bone.localRotation;
                }
            }
            catch { _scenePreview.Cleanup(); throw; }
        }

        public string PrefabPath { get; }

        public UxrAvatar Avatar { get; }
        public Transform RootTransform => _root.transform;

        public UxrAvatarHand Hand(UxrHandSide side) => Avatar.GetHand(side);

        /// <summary>Пястные у указательного, среднего, безымянного и мизинца.</summary>
        public bool HasMetacarpals(UxrHandSide side)
        {
            UxrAvatarHand hand = Hand(side);
            return hand.Index.Metacarpal != null && hand.Middle.Metacarpal != null && hand.Ring.Metacarpal != null && hand.Little.Metacarpal != null;
        }

        /// <summary>GUID префаба рига и его родителей по цепочке вариантов (сверху вниз от самого рига).</summary>
        public List<string> PrefabGuidChain()
        {
            var        chain = new List<string>();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            for (int i = 0; asset != null && i < 16; i++)
            {
                chain.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
                asset = PrefabUtility.GetCorrespondingObjectFromSource(asset);
            }

            return chain;
        }

        public void RestoreBind()
        {
            foreach (KeyValuePair<Transform, Quaternion> pair in _bind)
            {
                pair.Key.localRotation = pair.Value;
            }
        }

        /// <summary>Мировой путь SDK (редактор): proximal без пястной информации ставится в мире от запястья.</summary>
        public void Apply(UxrHandSide side, UxrHandDescriptor descriptor) => UxrAvatarRig.UpdateHandUsingDescriptor(Avatar, side, descriptor);

        public Transform Grabber(UxrHandSide side) =>
            _root.GetComponentsInChildren<UxrGrabber>(true).FirstOrDefault(g => g.Side == side)?.transform
            ?? throw new ArgumentException($"{PrefabPath}: нет UxrGrabber {side}");

        /// <summary>Универсальные оси кисти в мире (те, в которых записаны поза и кадр источника).</summary>
        public Quaternion HandRotation(UxrHandSide side) => Avatar.AvatarRigInfo.GetArmInfo(side).HandUniversalLocalAxes.UniversalRotation;

        public Vector3 WristPosition(UxrHandSide side) => Hand(side).Wrist.position;

        /// <summary>Центр ладони SDK в мире (зависит от позы, если поза поворачивает пястные).</summary>
        public Vector3 PalmCenter(UxrHandSide side) => HandsPackPoseExtractor.PalmCenterWorld(Hand(side));

        /// <summary>Нормаль «из ладони» SDK (<c>GetPalmOutDirection</c>) в мире.</summary>
        public Vector3 PalmOut(UxrHandSide side)
        {
            if (!Hand(side).GetPalmOutDirection(side, out Vector3 direction))
            {
                throw new ArgumentException($"{PrefabPath}: нормаль ладони {side} не считается");
            }

            return direction;
        }

        /// <summary>Кости пальцев (пястная, proximal, intermediate, distal — какие есть), порядок стабильный.</summary>
        public List<Transform> FingerBones(UxrHandSide side)
        {
            UxrAvatarHand hand  = Hand(side);
            var           bones = new List<Transform>();
            foreach (UxrAvatarFinger finger in new[] { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little })
            {
                foreach (Transform bone in new[] { finger.Metacarpal, finger.Proximal, finger.Intermediate, finger.Distal })
                {
                    if (bone != null) bones.Add(bone);
                }
            }

            return bones;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_scenePreview != null) { _scenePreview.Cleanup(); return; }
            if (_root != null)
            {
                PrefabUtility.UnloadPrefabContents(_root);
            }
        }
    }
}
