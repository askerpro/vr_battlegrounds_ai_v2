using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>Перевод кисти аватара в превью у предмета через пару align → grabber (как в DrawScene панели).</summary>
    internal readonly struct GripPreviewMapping
    {
        public readonly bool Valid;
        readonly Vector3 _snapPos;
        readonly Vector3 _grabberPos;
        readonly Quaternion _toPreview;

        public GripPreviewMapping(Transform snap, Transform grabber)
        {
            Valid = snap != null && grabber != null;
            _snapPos = Valid ? snap.position : Vector3.zero;
            _grabberPos = Valid ? grabber.position : Vector3.zero;
            _toPreview = Valid ? snap.rotation * Quaternion.Inverse(grabber.rotation) : Quaternion.identity;
        }

        public Vector3 ToPreview(Vector3 avatarWorld) => Valid ? _snapPos + _toPreview * (avatarWorld - _grabberPos) : avatarWorld;
        public Vector3 ToAvatar(Vector3 previewWorld) => Valid ? _grabberPos + Quaternion.Inverse(_toPreview) * (previewWorld - _snapPos) : previewWorld;
        public Vector3 DirToPreview(Vector3 avatarDir) => Valid ? _toPreview * avatarDir : avatarDir;
        public Vector3 DirToAvatar(Vector3 previewDir) => Valid ? Quaternion.Inverse(_toPreview) * previewDir : previewDir;
        public Quaternion RotToPreview(Quaternion avatarRot) => Valid ? _toPreview * avatarRot : avatarRot;
        public Quaternion RotToAvatar(Quaternion previewRot) => Valid ? Quaternion.Inverse(_toPreview) * previewRot : previewRot;
    }

    /// <summary>
    /// Режим разметки центра ладони в SceneView: клик по кисти ставит рамку на поверхность (нормаль — нормаль поверхности,
    /// «вперёд» — от запястья к проксимальной кости среднего пальца, ортогонально нормали), затем ручки позиции и поворота
    /// всей рамки. Черновик хранится в системе запястья и пишется в профиль только по «Сохранить».
    /// </summary>
    internal static class GripPalmMarking
    {
        static readonly int ControlHint = "VrBattlegrounds.GripPalmMarking".GetHashCode();
        static readonly Color MarkColor = new Color(1f, 0.55f, 0.1f, 1f);
        static readonly Color ForwardColor = new Color(0.25f, 0.6f, 1f, 1f);

        static int _avatarId;
        static UxrHandSide _side;
        static List<GripPalmSkinPoint.BakedSkin> _skins;

        public static bool Active { get; private set; }
        public static bool HasDraft { get; private set; }
        public static UxrHandSide Side => _side;
        public static Vector3 DraftPositionWrist { get; private set; }
        public static Quaternion DraftRotationWrist { get; private set; } = Quaternion.identity;
        /// <summary>Растёт при любом изменении черновика; входит в сигнатуру пересчёта.</summary>
        public static int Version { get; private set; }
        public static string Status { get; set; } = "";

        static GripPalmMarking()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => Cancel(null);
        }

        public static bool IsFor(GripAnalysisHost host) =>
            Active && host.Avatar != null && host.Avatar.GetHashCode() == _avatarId && host.Side == _side;

        /// <summary>Включает разметку для текущей руки; черновик начинается с сохранённой ручной точки, если она есть.</summary>
        public static void Begin(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            UxrAvatarHand hand = host.Avatar != null ? host.Avatar.GetHand(host.Side) : null;
            if (hand == null || hand.Wrist == null)
            {
                Status = "Нет запястья выбранной руки.";
                return;
            }

            _skins = GripPalmSkinPoint.BakeHandSkins(host.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), hand);
            _avatarId = host.Avatar.GetHashCode();
            _side = host.Side;
            Active = true;
            HasDraft = false;
            GripPalmProbe saved = profile != null ? profile.Get(host.Side).Palm : null;
            if (saved != null && saved.ManuallySet)
            {
                DraftPositionWrist = saved.PositionWrist;
                DraftRotationWrist = GripPalmProbe.FrameFrom(saved.NormalWrist, saved.ForwardWrist);
                HasDraft = true;
            }

            Status = _skins.Count == 0
                ? "Не найдено видимых SMR этой руки — клик по мешу не сработает."
                : "Разметка: кликните по ладони в SceneView (кисть аватара или её превью у предмета). Esc — выход без сохранения.";
            Version++;
            SceneView.RepaintAll();
        }

        public static void Cancel(string status)
        {
            if (!Active && !HasDraft)
            {
                return;
            }

            Active = false;
            HasDraft = false;
            _skins = null;
            Status = status ?? "";
            Version++;
            SceneView.RepaintAll();
        }

        /// <summary>Источник ручной точки ладони для P1: черновик разметки, затем профиль (в режиме «Вручную»).</summary>
        public static GripPalmOverride GetOverride(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            if (IsFor(host) && HasDraft)
            {
                return new GripPalmOverride
                {
                    Active = true,
                    PositionWrist = DraftPositionWrist,
                    NormalWrist = DraftRotationWrist * Vector3.up,
                    ForwardWrist = DraftRotationWrist * Vector3.forward,
                    Source = "вручную (черновик разметки, не сохранён)"
                };
            }

            if (!GripAnalysisSettings.PalmUseManual)
            {
                return default;
            }

            GripPalmProbe palm = profile != null ? profile.Get(host.Side).Palm : null;
            if (palm == null || !palm.ManuallySet)
            {
                return new GripPalmOverride { Note = "режим «Вручную»: разметки для этой руки нет — используется авто" };
            }

            return new GripPalmOverride
            {
                Active = true,
                PositionWrist = palm.PositionWrist,
                NormalWrist = palm.NormalWrist,
                ForwardWrist = palm.ForwardWrist,
                Source = $"вручную (профиль {profile.name})"
            };
        }

        static void SetDraft(Vector3 positionWrist, Quaternion rotationWrist)
        {
            DraftPositionWrist = positionWrist;
            DraftRotationWrist = GripPalmProbe.FrameFrom(rotationWrist * Vector3.up, rotationWrist * Vector3.forward);
            HasDraft = true;
            Version++;
        }

        /// <summary>«Вперёд» по умолчанию: от запястья к проксимальной кости среднего пальца в текущей позе.</summary>
        static Vector3 DefaultForward(UxrAvatarHand hand)
        {
            Transform middle = hand.Middle != null ? hand.Middle.Proximal : null;
            return middle != null ? middle.position - hand.Wrist.position : hand.Wrist.forward;
        }

        /// <summary>Обработка событий SceneView в режиме разметки; вызывается из SceneGUI-хука панели.</summary>
        public static void OnSceneGUI(GripAnalysisHost host, GripPreviewMapping map)
        {
            if (!Active)
            {
                return;
            }

            if (!IsFor(host))
            {
                Cancel("Разметка прервана: сменился аватар или рука.");
                return;
            }

            UxrAvatarHand hand = host.Avatar.GetHand(host.Side);
            Transform wrist = hand != null ? hand.Wrist : null;
            if (wrist == null)
            {
                Cancel("Разметка прервана: нет запястья.");
                return;
            }

            Event e = Event.current;
            int id = GUIUtility.GetControlID(ControlHint, FocusType.Passive);
            if (e.type == EventType.Layout)
            {
                // Клик по мешу не должен выделять объекты сцены
                HandleUtility.AddDefaultControl(id);
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Cancel("Разметка отменена (Esc), профиль не изменён.");
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id && _skins != null)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                const float MaxDistance = 1000f;
                // Луч против реальной кисти аватара и против её превью у предмета; берём ближайшее попадание
                bool hit = GripPalmSkinPoint.RaycastNearest(_skins, ray.origin, ray.direction, MaxDistance, out float bestDist, out Vector3 point, out Vector3 normal);
                if (map.Valid &&
                    GripPalmSkinPoint.RaycastNearest(_skins, map.ToAvatar(ray.origin), map.DirToAvatar(ray.direction), hit ? bestDist : MaxDistance,
                        out float previewDist, out Vector3 previewPoint, out Vector3 previewNormal))
                {
                    hit = true;
                    point = previewPoint;
                    normal = previewNormal;
                }

                if (hit)
                {
                    Quaternion frameWorld = GripPalmProbe.FrameFrom(normal, DefaultForward(hand));
                    SetDraft(wrist.InverseTransformPoint(point), Quaternion.Inverse(wrist.rotation) * frameWorld);
                    Status = "Точка поставлена. Подвиньте ручки или кликните снова; «Сохранить в профиль» — записать.";
                    e.Use();
                }
            }

            if (!HasDraft)
            {
                return;
            }

            Vector3 positionAvatar = wrist.TransformPoint(DraftPositionWrist);
            Vector3 position = map.ToPreview(positionAvatar);
            Quaternion frame = map.RotToPreview(wrist.rotation * DraftRotationWrist);
            Vector3 normalDir = frame * Vector3.up;
            Vector3 forwardDir = frame * Vector3.forward;
            float size = HandleUtility.GetHandleSize(position);
            Color oldColor = Handles.color;
            try
            {
                if (e.type == EventType.Repaint)
                {
                    // Две стрелки рамки: нормаль ладони (оранжевая) и «вперёд» к среднему пальцу (синяя)
                    Handles.color = MarkColor;
                    Handles.ArrowHandleCap(0, position, Quaternion.LookRotation(normalDir), size * 0.7f, EventType.Repaint);
                    Handles.SphereHandleCap(0, position, Quaternion.identity, size * 0.06f, EventType.Repaint);
                    Handles.color = ForwardColor;
                    Handles.ArrowHandleCap(0, position, Quaternion.LookRotation(forwardDir), size * 0.55f, EventType.Repaint);
                }

                EditorGUI.BeginChangeCheck();
                Vector3 newPosition = Handles.PositionHandle(position, frame);
                if (EditorGUI.EndChangeCheck())
                {
                    SetDraft(wrist.InverseTransformPoint(map.ToAvatar(newPosition)), DraftRotationWrist);
                }

                // Поворот всей рамки — ручкой на конце стрелки нормали
                Vector3 tip = position + normalDir * size * 0.7f;
                EditorGUI.BeginChangeCheck();
                Quaternion newFrame = Handles.RotationHandle(frame, tip);
                if (EditorGUI.EndChangeCheck())
                {
                    SetDraft(DraftPositionWrist, Quaternion.Inverse(wrist.rotation) * map.RotToAvatar(newFrame));
                }
            }
            finally
            {
                Handles.color = oldColor;
            }
        }

        /// <summary>Сохраняет черновик в профиль (создаёт ассет при первом сохранении) и выходит из разметки.</summary>
        public static void Save(GripAnalysisHost host)
        {
            if (!IsFor(host) || !HasDraft)
            {
                Status = "Нет черновика для сохранения.";
                return;
            }

            GripHandProbeProfile profile = GripHandProbeProfiles.SavePalm(host.Avatar, host.Side, DraftPositionWrist, DraftRotationWrist, out string error);
            if (profile == null)
            {
                Status = "Не сохранено: " + error;
                return;
            }

            GripAnalysisSettings.PalmUseManual = true;
            Cancel($"Сохранено в профиль {AssetDatabase.GetAssetPath(profile)}.");
        }
    }
}
