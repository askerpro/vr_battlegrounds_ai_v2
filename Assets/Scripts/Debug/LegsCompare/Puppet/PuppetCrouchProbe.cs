using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Замер приседа одного аватара стенда (<see cref="PuppetMetrics"/>, программа <see cref="PuppetProgramId.Crouch"/>):
    /// <list type="bullet">
    /// <item>на каком опускании головы <c>Legs_Crouch</c> пересекает 0,1 / 0,5 / 0,9 (вниз и вверх);</item>
    /// <item>колено (сустав — начало кости голени) и подошва над полом, мин.;</item>
    /// <item>зазор стоп и коленей поперёк корпуса (&lt; 0 — скрещены) и куда смотрит колено (0° — вперёд, &gt; 90° — назад:
    /// плоскость сгиба перевернулась);</item>
    /// <item>таз копии рига минус таз аватара по высоте (&gt; 0 — аватар ниже позы клипа).</item>
    /// </list>
    /// Пишет строки в <c>crouch.txt</c> (сегменты и пересечения) и <c>crouch.csv</c> (покадрово).
    /// </summary>
    public sealed class PuppetCrouchProbe
    {
        private static readonly float[] Levels = { 0.1f, 0.5f, 0.9f };

        /// <summary>«Таз» манипулятора стенда (точка наклона над полом), м — ставит стенд каждый кадр программы.</summary>
        public static float ManipulatorPelvis = float.NaN;

        private readonly string _name;
        private float _cameraY0;
        private float _prevCrouch = float.NaN;

        // Сегмент.
        private float _dropMax, _crouchMax, _kneeMinL, _kneeMinR, _soleMinL, _soleMinR, _feetGapMin, _kneeGapMin, _kneeBackMax, _hipsDiffAtMax;
        private float _pelvisErrMax, _leanMax, _tiltMax, _pelvisEstMin, _crouchMin, _hipsMin, _shoulderYawMax;
        private UltimateXR.Animation.IK.UxrAnimatedLegs _legs;
        private bool _legsLooked;

        public PuppetCrouchProbe(string name) => _name = name;

        public void Baseline(Transform root, Transform camera)
        {
            if (camera != null) _cameraY0 = root.InverseTransformPoint(camera.position).y;
        }

        public void ResetSegment()
        {
            _dropMax = _crouchMax = _kneeBackMax = 0f;
            _kneeMinL = _kneeMinR = _soleMinL = _soleMinR = _feetGapMin = _kneeGapMin = float.MaxValue;
            _hipsDiffAtMax = float.NaN;
            _pelvisErrMax = _leanMax = _tiltMax = _shoulderYawMax = 0f;
            _pelvisEstMin = _crouchMin = _hipsMin = float.MaxValue;
        }

        /// <summary>Кадр. <paramref name="body"/> — поворот корпуса «игрока» (мировой).</summary>
        public void Measure(int frame, string segment, PuppetMetrics.Subject s, Quaternion body, StringBuilder csv, StringBuilder events)
        {
            Transform root = s.Root;
            float drop = s.Camera != null ? _cameraY0 - root.InverseTransformPoint(s.Camera.position).y : 0f;
            Animator rig = s.LocomotionRig != null && s.LocomotionRig != s.Animator ? s.LocomotionRig : null;
            float crouch = rig != null && HasParam(rig, "Legs_Crouch") ? rig.GetFloat("Legs_Crouch") : float.NaN;

            Vector3 right = body * Vector3.right, forward = body * Vector3.forward;
            float floor = root.position.y;
            float kneeL = s.LeftCalf != null ? s.LeftCalf.position.y - floor : float.NaN;
            float kneeR = s.RightCalf != null ? s.RightCalf.position.y - floor : float.NaN;
            float soleL = s.LeftProbe.Sole, soleR = s.RightProbe.Sole;
            float feetGap = Vector3.Dot(s.RightFoot.position - s.LeftFoot.position, right);
            float kneeGap = s.LeftCalf != null && s.RightCalf != null ? Vector3.Dot(s.RightCalf.position - s.LeftCalf.position, right) : float.NaN;
            float backL = KneeBack(s.LeftThigh, s.LeftCalf, s.LeftFoot, forward);
            float backR = KneeBack(s.RightThigh, s.RightCalf, s.RightFoot, forward);
            float hipsDiff = float.NaN;
            if (rig != null && s.Hips != null)
            {
                Transform rh = rig.GetBoneTransform(HumanBodyBones.Hips);
                if (rh != null) hipsDiff = rh.position.y - s.Hips.position.y;
            }

            // Оценка таза игрока (UltimateXR, патч 39) против «таза» манипулятора; наклон корпуса — оценка и у аватара (таз→грудь).
            if (!_legsLooked)
            {
                _legsLooked = true;
                _legs = root.GetComponent<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>()?.AnimatedLegs;
            }
            float pelvisEst = float.NaN, lean = float.NaN;
            if (_legs != null && _legs.IsReady)
            {
                float scale = root.Find("Dummy Forward") is Transform pivot ? pivot.lossyScale.y : 1f;
                pelvisEst = _legs.PelvisEstimate.RawPelvisHeight * scale;
                lean = _legs.PelvisEstimate.LeanAngle;
            }
            float tilt = s.Chest != null && s.Hips != null ? Vector3.Angle(s.Chest.position - s.Hips.position, Vector3.up) : float.NaN;
            float manip = ManipulatorPelvis;
            if (!float.IsNaN(pelvisEst))
            {
                _pelvisEstMin = Mathf.Min(_pelvisEstMin, pelvisEst);
                if (!float.IsNaN(manip)) _pelvisErrMax = Mathf.Max(_pelvisErrMax, Mathf.Abs(pelvisEst - manip));
                _leanMax = Mathf.Max(_leanMax, lean);
            }
            if (!float.IsNaN(tilt)) _tiltMax = Mathf.Max(_tiltMax, tilt);
            if (s.Hips != null) _hipsMin = Mathf.Min(_hipsMin, s.Hips.position.y - floor);
            // Скрутка корпуса: линия плеч по горизонтали против «вправо» тела (не зависит от наклона вперёд).
            if (s.LeftUpperArm != null && s.RightUpperArm != null)
            {
                Vector3 sh = Vector3.ProjectOnPlane(s.RightUpperArm.position - s.LeftUpperArm.position, Vector3.up);
                Transform pv = root.Find("Dummy Forward");   // от опоры корпуса UltimateXR (она сама идёт за головой)
                Vector3 pr = pv != null ? Vector3.ProjectOnPlane(pv.right, Vector3.up) : right;
                if (sh.sqrMagnitude > 1e-4f) _shoulderYawMax = Mathf.Max(_shoulderYawMax, Mathf.Abs(Vector3.SignedAngle(pr, sh, Vector3.up)));
            }

            if (drop > _dropMax)
            {
                _dropMax = drop;
                _hipsDiffAtMax = hipsDiff;
            }
            if (!float.IsNaN(crouch)) { _crouchMax = Mathf.Max(_crouchMax, crouch); _crouchMin = Mathf.Min(_crouchMin, crouch); }
            _kneeMinL = Min(_kneeMinL, kneeL);
            _kneeMinR = Min(_kneeMinR, kneeR);
            _soleMinL = Min(_soleMinL, soleL);
            _soleMinR = Min(_soleMinR, soleR);
            _feetGapMin = Min(_feetGapMin, feetGap);
            _kneeGapMin = Min(_kneeGapMin, kneeGap);
            _kneeBackMax = Mathf.Max(_kneeBackMax, Mathf.Max(float.IsNaN(backL) ? 0f : backL, float.IsNaN(backR) ? 0f : backR));

            if (!float.IsNaN(crouch) && !float.IsNaN(_prevCrouch))
                foreach (float level in Levels)
                {
                    if (_prevCrouch < level && crouch >= level) events.AppendLine(Inv($"{segment} | {_name} | Legs_Crouch ↑ {level:0.0} | голова −{drop:0.000} м"));
                    else if (_prevCrouch >= level && crouch < level) events.AppendLine(Inv($"{segment} | {_name} | Legs_Crouch ↓ {level:0.0} | голова −{drop:0.000} м"));
                }
            _prevCrouch = crouch;

            csv.AppendLine(Inv($"{frame};{segment};{_name};{drop:0.000};{crouch:0.000};{kneeL:0.000};{kneeR:0.000};{soleL:0.000};{soleR:0.000};{feetGap:0.000};{kneeGap:0.000};{backL:0};{backR:0};{hipsDiff:0.000};{pelvisEst:0.000};{manip:0.000};{lean:0.0};{tilt:0.0}"));
        }

        /// <summary>Строка сегмента для crouch.txt.</summary>
        public string Summary(string segment) =>
            Inv($"{segment} | {_name} | {_dropMax:0.00} | {_crouchMax:0.00} | {_kneeMinL * 100f:0.0}/{_kneeMinR * 100f:0.0} | {_soleMinL * 100f:0.0}/{_soleMinR * 100f:0.0} | {_feetGapMin:0.00} | {_kneeGapMin:0.00} | {_kneeBackMax:0} | {_hipsDiffAtMax * 100f:0.0} | {(_pelvisEstMin < float.MaxValue ? _pelvisEstMin : float.NaN):0.00} | {_pelvisErrMax * 100f:0.0} | {_leanMax:0} | {_tiltMax:0} | {(_crouchMin < float.MaxValue ? _crouchMin : float.NaN):0.00} | {(_hipsMin < float.MaxValue ? _hipsMin : float.NaN):0.00} | {_shoulderYawMax:0}");

        public const string Header = "сегмент | аватар | голова вниз макс, м | Legs_Crouch макс | колено над полом мин Л/П, см | подошва мин Л/П, см | зазор стоп мин, м | зазор коленей мин, м | колено от «вперёд» макс, ° (> 90 — назад) | таз клипа − таз аватара при макс. опускании, см | оценка таза мин, м | ошибка оценки таза (против манипулятора) макс, см | наклон корпуса (оценка) макс, ° | наклон таз→грудь аватара макс, ° | Legs_Crouch мин | таз аватара (Hips) над полом мин, м | скрутка плеч от опоры корпуса макс, °";
        public const string CsvHeader = "frame;segment;avatar;head_drop;crouch;knee_l;knee_r;sole_l;sole_r;feet_gap;knee_gap;knee_dir_l;knee_dir_r;hips_clip_minus_avatar;pelvis_est;pelvis_manip;lean_est;torso_tilt";

        /// <summary>Угол направления сгиба колена (колено от прямой бедро—стопа) от «вперёд» корпуса, °; прямая нога — NaN.</summary>
        private static float KneeBack(Transform thigh, Transform calf, Transform foot, Vector3 forward)
        {
            if (thigh == null || calf == null || foot == null) return float.NaN;
            Vector3 axis = (foot.position - thigh.position).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(calf.position - thigh.position, axis);
            if (bend.magnitude < 0.03f) return float.NaN;   // почти прямая нога — направление сгиба не определено
            return Vector3.Angle(Vector3.ProjectOnPlane(forward, axis), bend);
        }

        private static float Min(float a, float b) => float.IsNaN(b) ? a : Mathf.Min(a, b);

        private static bool HasParam(Animator a, string name)
        {
            foreach (AnimatorControllerParameter p in a.parameters)
                if (p.name == name) return true;
            return false;
        }

        private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }
}
