using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Покадровая трасса тела СВОЕГО аватара в живой игре (Lobby, шлем) — только чтение: подписка на
    /// <see cref="UxrManager.AvatarsUpdated"/> (после всех стадий UltimateXR, позы этого кадра готовы), ничего не пишет в
    /// сцену. Отвечает на вопрос «что повернуло корпус»: рысканье камеры, опоры тела (<c>Dummy Forward</c>), цели её поворота,
    /// груди и таза; скрутка торса за руками (углы <c>UxrBodyIK</c>); кисти относительно головы и опоры; корень аватара (не
    /// двигает ли его кто-то); оценщик движения (патч 38); ноги (шаг, поворот на месте, корень копии рига).
    /// Запуск/остановка — <c>Tools/VR Battlegrounds/Debug/Body Trace</c> или из кода; файл — <c>tmp/body_trace_*.csv</c>
    /// (дописывается каждые ~2 с; после выхода из Play подписка снимается перезагрузкой домена или <see cref="Stop"/>).
    /// </summary>
    public static class BodyTraceRecorder
    {
        private static StringBuilder _sb;
        private static string _path;
        private static UxrAvatar _avatar;
        private static Transform _pivot, _chest, _hips, _rigRoot;
        private static Quaternion _chestRest, _hipsRest;
        private static float _t0;
        private static Vector3 _prevCam, _prevPivot;
        private static bool _hasPrev;
        private static int _frames;

        private static readonly FieldInfo s_bodyIK = typeof(UxrStandardAvatarController).GetField("_bodyIK", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_target = typeof(UxrBodyIK).GetField("_avatarForwardTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_spineT = typeof(UxrBodyIK).GetField("_spineTorsionAngle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_chestT = typeof(UxrBodyIK).GetField("_chestTorsionAngle", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool IsRecording => _sb != null;
        public static string LastPath => _path;

        /// <summary>Начать запись (Play, свой аватар есть). Покой груди и таза — первый кадр: стоять прямо, смотреть вперёд.</summary>
        public static string Start(string tag = "")
        {
            if (!Application.isPlaying) return "не Play";
            Stop();
            _sb = new StringBuilder();
            _avatar = null;
            _hasPrev = false;
            _frames = 0;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tmp"));
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, $"body_trace_{DateTime.Now:MMdd_HHmmss}{(string.IsNullOrEmpty(tag) ? "" : "_" + tag)}.csv");
            UxrManager.AvatarsUpdated += OnAvatarsUpdated;
            return "пишу: " + _path;
        }

        /// <summary>Остановить и записать файл.</summary>
        public static string Stop()
        {
            if (_sb == null) return "не пишется";
            UxrManager.AvatarsUpdated -= OnAvatarsUpdated;
            File.AppendAllText(_path, _sb.ToString());
            _sb = null;
            GameLog.Debug.Info($"[BodyTraceRecorder] Записано кадров: {_frames} → {_path}");
            return $"кадров {_frames}: {_path}";
        }

        private static void OnAvatarsUpdated()
        {
            if (_sb == null) return;
            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            if (avatar == null || avatar.CameraTransform == null) return;
            var controller = avatar.GetComponent<UxrStandardAvatarController>();
            if (avatar != _avatar)
            {
                _avatar = avatar;
                _pivot = avatar.transform.Find("Dummy Forward");
                _chest = avatar.AvatarRig.UpperChest != null ? avatar.AvatarRig.UpperChest : avatar.AvatarRig.Chest;
                _hips = avatar.AvatarRig.Hips;
                _rigRoot = controller != null && controller.AnimatedLegs != null && controller.AnimatedLegs.RigAnimator != null ? controller.AnimatedLegs.RigAnimator.transform : null;
                Quaternion basis = _pivot != null ? _pivot.rotation : avatar.transform.rotation;
                _chestRest = _chest != null ? Quaternion.Inverse(basis) * _chest.rotation : Quaternion.identity;
                _hipsRest = _hips != null ? Quaternion.Inverse(basis) * _hips.rotation : Quaternion.identity;
                _t0 = Time.time;
                _sb.AppendLine($"avatar={avatar.name} mode={avatar.AvatarMode} legacy={UxrBodyIK.LegacyMovementDecisions}");
                _sb.AppendLine("t;dt;root_x;root_z;root_yaw;cam_x;cam_y;cam_z;cam_yaw;cam_pitch;cam_step_cm;pivot_x;pivot_z;pivot_yaw;pivot_step_cm;target_yaw;chest_yaw;hips_yaw;torsion_spine;torsion_chest;handL_yaw;handR_yaw;hands_yaw;motion_walk;motion_speed;legs_moving;legs_turn;rig_yaw;bend_deg");
            }

            Transform root = avatar.transform, cam = avatar.CameraTransform;
            float rootYaw = Yaw(root.forward);
            Vector3 camPos = cam.position;
            Vector3 pivotPos = _pivot != null ? _pivot.position : root.position;
            float camStep = _hasPrev ? Flat(camPos - _prevCam) * 100f : 0f;
            float pivotStep = _hasPrev ? Flat(pivotPos - _prevPivot) * 100f : 0f;
            _prevCam = camPos;
            _prevPivot = pivotPos;
            _hasPrev = true;

            object bodyIK = controller != null ? s_bodyIK?.GetValue(controller) : null;
            float targetYaw = float.NaN, tSpine = 0f, tChest = 0f;
            if (bodyIK != null)
            {
                if (s_target?.GetValue(bodyIK) is Vector3 target) targetYaw = Yaw(target);
                tSpine = s_spineT != null ? (float)s_spineT.GetValue(bodyIK) : 0f;
                tChest = s_chestT != null ? (float)s_chestT.GetValue(bodyIK) : 0f;
            }


            float chestYaw = _chest != null ? Yaw(_chest.rotation * Quaternion.Inverse(_chestRest) * Vector3.forward) : float.NaN;
            float hipsYaw = _hips != null ? Yaw(_hips.rotation * Quaternion.Inverse(_hipsRest) * Vector3.forward) : float.NaN;
            Transform lh = avatar.GetHandBone(UxrHandSide.Left), rh = avatar.GetHandBone(UxrHandSide.Right);
            Vector3 neck = camPos - Vector3.up * 0.12f;
            float lYaw = lh != null ? Yaw(lh.position - neck) : float.NaN;
            float rYaw = rh != null ? Yaw(rh.position - neck) : float.NaN;
            float hYaw = lh != null && rh != null ? Yaw((lh.position + rh.position) * 0.5f - neck) : float.NaN;

            UxrBodyMotion motion = controller != null ? controller.BodyMotion : null;
            UxrAnimatedLegs legs = controller != null ? controller.AnimatedLegs : null;
            Animator rig = legs != null ? legs.RigAnimator : null;
            float turn = rig != null ? rig.GetFloat(UxrLegLocomotion.TurnParam) : 0f;
            float bend = controller != null ? Quaternion.Angle(Quaternion.identity, controller.ExternalBodyBend) : 0f;

            _frames++;
            _sb.AppendLine(string.Join(";",
                F(Time.time - _t0, "0.000"), F(Time.deltaTime, "0.0000"), F(root.position.x, "0.000"), F(root.position.z, "0.000"), F(rootYaw),
                F(camPos.x, "0.000"), F(camPos.y, "0.000"), F(camPos.z, "0.000"), F(Yaw(cam.forward)), F(-Mathf.Asin(Mathf.Clamp(cam.forward.y, -1f, 1f)) * Mathf.Rad2Deg),
                F(camStep, "0.00"), F(pivotPos.x, "0.000"), F(pivotPos.z, "0.000"), F(_pivot != null ? Yaw(_pivot.forward) : float.NaN), F(pivotStep, "0.00"),
                F(targetYaw), F(chestYaw), F(hipsYaw), F(tSpine), F(tChest), F(lYaw), F(rYaw), F(hYaw),
                motion != null && motion.IsWalking ? "1" : "0", F(motion != null ? motion.Speed : 0f, "0.000"),
                legs != null && legs.IsMoving ? "1" : "0", F(turn, "0.00"), F(_rigRoot != null ? Yaw(_rigRoot.forward) : float.NaN), F(bend)));

            // Сбрасывать на диск каждые ~2 с: выход из Play без Stop не теряет трассу.
            if (_frames % 120 == 0)
            {
                File.AppendAllText(_path, _sb.ToString());
                _sb.Clear();
            }
        }

        private static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        private static string F(float v, string fmt = "0.0") => float.IsNaN(v) ? "" : v.ToString(fmt, CultureInfo.InvariantCulture);
    }
}
