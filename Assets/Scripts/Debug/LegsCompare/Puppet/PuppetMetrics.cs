using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Замеры стенда <see cref="AvatarPuppetStand"/> — по каждому аватару и сегменту программы:
    /// <list type="bullet">
    /// <item>скольжение в опоре по подошве (<see cref="FootContactProbe"/>): см на метр пути тела, макс. скорость, за касание;</item>
    /// <item>ритм шагов (<see cref="GaitProbe"/>): шагов, шаг/с, длина переноса стопы, шагов на метр;</item>
    /// <item>стопа (<see cref="FootProbe"/>): разворот и завал в опоре, подошва в полу;</item>
    /// <item>отставание груди от головы и уход головы аватара от шлема, см; сгиб колен, °.</item>
    /// </list>
    /// Итог — <c>metrics.txt</c> (сегменты), <c>gait.txt</c> (ровные участки ходьбы), <c>frames.csv</c> (покадрово).
    /// </summary>
    public sealed class PuppetMetrics
    {
        /// <summary>Один аватар под замером.</summary>
        public sealed class Subject
        {
            public string Name;
            public Transform Root, Camera;
            public Animator Animator;
            public UltimateXR.Avatar.Rig.UxrAvatarRig Rig;   // запасной источник костей (humanoid-разметка Heavy не находит костей)
            public Transform Hips, Chest, Head, LeftFoot, RightFoot, LeftThigh, LeftCalf, RightThigh, RightCalf;
            public FootProbe LeftProbe, RightProbe;
            public FootContactProbe LeftContact, RightContact;
            public GaitProbe Gait;
            public float LeftFootY0, RightFootY0;
            public Vector3 HeadInCamera, NeckOffsetRest;
            public float LagMax, DriftMax, KneeSum;
            public float MinGap;   // правая − левая стопа поперёк корпуса, м: < 0 — ноги скрещены
            public float PelvisYawSum, PelvisYawMax;   // рысканье таза (линия бёдер) от корпуса, °
            public Transform HipBone, PelvisBone;      // MEF: CC_Base_Hip (корень позвоночника и ног), CC_Base_Pelvis (корень ног)
            public Quaternion HipRest, PelvisRest;     // их поворот в осях корня в покое
            public float HipYawSum, HipYawMax, PelvisBoneYawSum, PelvisBoneYawMax;
            public float KneeYawL, KneeYawR, ToeYawL, ToeYawR;
            public float TorsoTilt, ClipTorsoTilt;   // сумма × dt: наклон таз→грудь от вертикали у аватара и у копии рига, °
            public Transform RigHips, RigChest;
            public float SideTilt, ClipSideTilt;          // сумма × dt: крен таз→грудь вправо (+) / влево (−) в осях корпуса, °
            public float RigKneeL, RigKneeR;              // сумма × dt: колено копии рига (клип) от корпуса копии, ° (> 0 наружу)
            public Transform RigThighL, RigCalfL, RigFootL, RigThighR, RigCalfR, RigFootR;
            public Transform LeftUpperArm, RightUpperArm, LeftHand, RightHand;
            public float ArmRestL, ArmRestR, ArmStretchMax;   // плечо→кисть в покое и наибольшее растяжение за сегмент, %   // сумма × dt: колено (плоскость сгиба) и носок от корпуса, ° (> 0 наружу)
            public int KneeCount;
            public Animator LocomotionRig;   // аниматор локомоции (копия рига ног UltimateXR или сам риг VRIK)
            public Quaternion ChestRest;     // поворот груди в осях корня в покое (тело стенда смотрит вперёд)
            public float ChestBodySum, ChestBodyMax, ChestHeadSum, HipLineSignedSum;   // сумма × dt и макс. |.|: рысканье груди от тела / от головы, таза (линия бёдер) от тела, °
            public Transform Pivot;                      // опора тела UltimateXR («Dummy Forward»), если есть
            public Quaternion PivotRest;                 // её поворот в осях корня в покое
            public float PivotBodySum, PivotBodyMax, HipLineMax;   // рысканье опоры от тела ср/макс, таза (линия бёдер) макс |.|, °
            public PuppetCrouchProbe Crouch;             // замер приседа (crouch.txt, crouch.csv)
            public float ChestPitchRest, ChestBackMax;   // наклон таз→грудь вперёд-назад в покое и наибольший наклон НАЗАД от покоя за сегмент, °
        }

        private readonly List<Subject> _subjects = new List<Subject>();
        private readonly StringBuilder _report = new StringBuilder();
        private readonly StringBuilder _gait = new StringBuilder();
        private readonly StringBuilder _frames = new StringBuilder();
        private readonly StringBuilder _crouch = new StringBuilder();
        private readonly StringBuilder _crouchCsv = new StringBuilder();
        private readonly StringBuilder _crouchEvents = new StringBuilder();
        private string _segment = "";
        private float _segmentTime, _headPath;
        private Vector3 _headPrev;
        private bool _hasHeadPrev;
        private bool _steady, _baselined;
        private int _frame;

        public IReadOnlyList<Subject> Subjects => _subjects;

        public PuppetMetrics() => ResetReport();

        /// <summary>Отчёт — с чистого листа (новый прогон); аватары и их покой остаются.</summary>
        public void ResetReport()
        {
            _report.Clear();
            _gait.Clear();
            _frames.Clear();
            _crouch.Clear();
            _crouchCsv.Clear();
            _crouchEvents.Clear();
            _crouch.AppendLine(PuppetCrouchProbe.Header);
            _crouchCsv.AppendLine(PuppetCrouchProbe.CsvHeader);
            _frame = 0;
            _report.AppendLine("сегмент | аватар | шагов | шаг/с | длина шага, м | шагов на м | скольжение в опоре Л/П, см/м | макс. скорость проскальзывания Л/П, см/с | касаний Л/П | скольжение за касание ср/макс Л · П, см | разворот носка в опоре Л/П, ° | завал Л/П, ° | подошва мин Л/П, см | подошва в опоре Л/П, см | отставание груди, см | уход головы, см | колени, ° | мин. зазор стоп поперёк, м (< 0 — скрещены) | рысканье таза от корпуса ср/макс, ° (линия бёдер · кость Hip · кость Pelvis) | колено (плоскость сгиба) Л/П ср, ° (> 0 наружу) | носок Л/П ср, ° | наклон корпуса ср аватар/клип, ° | крен корпуса ср аватар/клип, ° (> 0 вправо) | колено клипа (копия рига) Л/П, ° | растяжение руки макс, % | грудь от тела ср/макс, ° (> 0 вправо) | грудь от головы ср, ° | таз от тела ср, ° | опора тела от тела ср/макс, ° | таз от тела макс, ° | грудь НАЗАД от покоя макс, °");
            _gait.AppendLine("сегмент | аватар | скорость, м/с | шагов Л+П | шаг/с | длина шага (перенос стопы), м | шагов на метр | скольжение в опоре Л/П, см/м | макс. проскальзывание Л/П, см/с | касаний Л/П | за касание ср/макс Л · П, см");
            _frames.AppendLine("frame;t;segment;avatar;head_x;head_z;foot_l_x;foot_l_y;foot_l_z;foot_r_x;foot_r_y;foot_r_z;lag_cm;yaw_l;roll_l;sole_l_cm;plant_l;yaw_r;roll_r;sole_r_cm;plant_r;moving;h;v;speed;clip");
        }

        /// <summary>Аватар в позе префаба (сразу после появления): подошва и стопы — для <see cref="FootProbe"/>.</summary>
        public Subject Add(string name, Transform root, Transform camera)
        {
            Animator animator = Array.Find(root.GetComponentsInChildren<Animator>(true), a => a.isHuman);
            if (animator == null) return null;
            UltimateXR.Avatar.Rig.UxrAvatarRig rig = root.GetComponent<UltimateXR.Avatar.UxrAvatar>()?.AvatarRig;
            Transform B(HumanBodyBones b) => Bone(animator, rig, b);
            var s = new Subject
            {
                Name = name, Root = root, Camera = camera, Animator = animator, Rig = rig,
                Hips = B(HumanBodyBones.Hips),
                Chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest),
                Head = B(HumanBodyBones.Head),
                LeftFoot = B(HumanBodyBones.LeftFoot), RightFoot = B(HumanBodyBones.RightFoot),
                LeftThigh = B(HumanBodyBones.LeftUpperLeg), LeftCalf = B(HumanBodyBones.LeftLowerLeg),
                RightThigh = B(HumanBodyBones.RightUpperLeg), RightCalf = B(HumanBodyBones.RightLowerLeg),
            };
            if (s.LeftFoot == null || s.RightFoot == null) return null;
            s.LeftProbe = new FootProbe(root, s.LeftFoot, B(HumanBodyBones.LeftToes), true);
            s.RightProbe = new FootProbe(root, s.RightFoot, B(HumanBodyBones.RightToes), false);
            s.Gait = new GaitProbe(s.LeftFoot, s.RightFoot);
            s.HipBone = s.Hips;
            s.LeftUpperArm = B(HumanBodyBones.LeftUpperArm); s.RightUpperArm = B(HumanBodyBones.RightUpperArm);
            s.LeftHand = B(HumanBodyBones.LeftHand); s.RightHand = B(HumanBodyBones.RightHand);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "CC_Base_Pelvis") s.PelvisBone = t;
            s.LeftContact = new FootContactProbe(s.LeftProbe);
            s.RightContact = new FootContactProbe(s.RightProbe);
            s.Crouch = new PuppetCrouchProbe(name);
            _subjects.Add(s);
            return s;
        }

        /// <summary>Покой: высоты стоп, голова в осях шлема, грудь→голова. Звать, когда аватары постояли (IK устоялся).</summary>
        public void CaptureBaselines()
        {
            foreach (Subject s in _subjects)
            {
                s.LeftFootY0 = s.LeftFoot.position.y;
                s.RightFootY0 = s.RightFoot.position.y;
                if (s.Camera != null && s.Head != null) s.HeadInCamera = s.Camera.InverseTransformPoint(s.Head.position);
                s.NeckOffsetRest = s.Head != null && s.Chest != null ? Quaternion.Inverse(s.Root.rotation) * (s.Head.position - s.Chest.position) : Vector3.zero;
                if (s.HipBone != null) s.HipRest = Quaternion.Inverse(s.Root.rotation) * s.HipBone.rotation;
                // Длина руки — сумма костей (плечо + предплечье) в позе префаба не известна после IK; берём в покое стенда
                // наибольшую из прямой плечо→кисть: руки согнуты, поэтому «растяжение» — рост расстояния сверх длины костей.
                Transform lf = Bone(s.Animator, s.Rig, HumanBodyBones.LeftLowerArm), rf = Bone(s.Animator, s.Rig, HumanBodyBones.RightLowerArm);
                if (s.LeftUpperArm != null && lf != null && s.LeftHand != null)
                    s.ArmRestL = Vector3.Distance(s.LeftUpperArm.position, lf.position) + Vector3.Distance(lf.position, s.LeftHand.position);
                if (s.RightUpperArm != null && rf != null && s.RightHand != null)
                    s.ArmRestR = Vector3.Distance(s.RightUpperArm.position, rf.position) + Vector3.Distance(rf.position, s.RightHand.position);
                if (s.PelvisBone != null) s.PelvisRest = Quaternion.Inverse(s.Root.rotation) * s.PelvisBone.rotation;
                if (s.Chest != null) s.ChestRest = Quaternion.Inverse(s.Root.rotation) * s.Chest.rotation;
                s.Pivot = s.Root.Find("Dummy Forward");
                s.Crouch.Baseline(s.Root, s.Camera);
                if (s.Chest != null && s.Hips != null) s.ChestPitchRest = Pitch(s.Chest.position - s.Hips.position, s.Pivot != null ? s.Pivot.rotation : s.Root.rotation);
                if (s.Pivot != null) s.PivotRest = Quaternion.Inverse(s.Root.rotation) * s.Pivot.rotation;
            }
            _baselined = true;
        }

        /// <summary>Новый сегмент; <paramref name="steady"/> — ровный ход, его ритм идёт ещё и в gait.txt.</summary>
        public void BeginSegment(string name, bool steady)
        {
            _segment = name;
            _steady = steady;
            _segmentTime = _headPath = 0f;
            _hasHeadPrev = false;
            foreach (Subject s in _subjects)
            {
                s.LagMax = s.DriftMax = s.KneeSum = 0f;
                s.LeftContact.ResetSegment();
                s.RightContact.ResetSegment();
                s.MinGap = float.MaxValue;
                s.PelvisYawSum = s.PelvisYawMax = 0f;
                s.HipYawSum = s.HipYawMax = s.PelvisBoneYawSum = s.PelvisBoneYawMax = 0f;
                s.KneeYawL = s.KneeYawR = s.ToeYawL = s.ToeYawR = 0f;
                s.TorsoTilt = s.ClipTorsoTilt = 0f;
                s.SideTilt = s.ClipSideTilt = s.RigKneeL = s.RigKneeR = s.ArmStretchMax = 0f;
                s.KneeCount = 0;
                s.ChestBodySum = s.ChestBodyMax = s.ChestHeadSum = s.HipLineSignedSum = 0f;
                s.PivotBodySum = s.PivotBodyMax = s.HipLineMax = 0f;
                s.LeftProbe.ResetSegment();
                s.RightProbe.ResetSegment();
                s.Gait.ResetSegment();
                s.Crouch.ResetSegment();
                s.ChestBackMax = 0f;
            }
        }

        /// <summary>
        /// Кадр — в конце кадра, когда позы готовы (после Animator, IK UltimateXR и VRIK). <paramref name="bodyYaw"/> — поворот
        /// корпуса «игрока», °; <paramref name="head"/> — голова манипулятора в осях стенда (путь головы — для шагов на метр);
        /// <paramref name="headYaw"/> — поворот головы «игрока», ° (NaN — как тело).
        /// </summary>
        public void Measure(float dt, float bodyYaw, Vector3 head, bool record = true, float headYaw = float.NaN)
        {
            if (float.IsNaN(headYaw)) headYaw = bodyYaw;
            if (!_baselined) return;
            if (!record)
            {
                // Ручной режим: только стопы (следы на полу), без отчёта.
                Quaternion y = Quaternion.Euler(0f, bodyYaw, 0f);
                foreach (Subject s in _subjects)
                {
                    Vector3 fw = s.Root.rotation * y * Vector3.forward;
                    s.LeftProbe.Measure(s.Root.position, fw, dt);
                    s.RightProbe.Measure(s.Root.position, fw, dt);
                    s.LeftContact.Measure(dt);
                    s.RightContact.Measure(dt);
                }
                return;
            }
            _frame++;
            _segmentTime += dt;
            if (_hasHeadPrev) _headPath += Flat(head - _headPrev);
            _headPrev = head;
            _hasHeadPrev = true;
            Quaternion yaw = Quaternion.Euler(0f, bodyYaw, 0f);
            foreach (Subject s in _subjects)
            {
                Vector3 l = s.LeftFoot.position, r = s.RightFoot.position;

                s.Gait.Measure(dt);
                s.MinGap = Mathf.Min(s.MinGap, Vector3.Dot(r - l, s.Root.rotation * yaw * Vector3.right));
                if (s.LeftThigh != null && s.RightThigh != null)
                {
                    // Таз — по линии бёдер (кости у аватаров разные, оси — тоже): её разворот от «вправо» корпуса.
                    Vector3 across = Vector3.ProjectOnPlane(s.RightThigh.position - s.LeftThigh.position, Vector3.up);
                    float signedPy = Vector3.SignedAngle(s.Root.rotation * yaw * Vector3.right, across, Vector3.up);
                    float py = Mathf.Abs(signedPy);
                    s.HipLineSignedSum += signedPy * dt;
                    s.HipLineMax = Mathf.Max(s.HipLineMax, Mathf.Abs(signedPy));
                    s.PelvisYawSum += py * dt;
                    s.PelvisYawMax = Mathf.Max(s.PelvisYawMax, py);
                }
                // Рысканье костей таза от покоя относительно корпуса «игрока» (оси костей у аватаров разные — от покоя).
                Quaternion body = s.Root.rotation * yaw;
                if (s.HipBone != null)
                {
                    float hy = Mathf.Abs(BoneYaw(Quaternion.Inverse(body) * s.HipBone.rotation, s.HipRest));
                    s.HipYawSum += hy * dt;
                    s.HipYawMax = Mathf.Max(s.HipYawMax, hy);
                }
                if (s.Pivot != null)
                {
                    float vy = BoneYaw(Quaternion.Inverse(body) * s.Pivot.rotation, s.PivotRest);
                    s.PivotBodySum += vy * dt;
                    s.PivotBodyMax = Mathf.Max(s.PivotBodyMax, Mathf.Abs(vy));
                }
                if (s.Chest != null)
                {
                    // Рысканье груди от покоя — от тела «игрока» и от его головы (оси костей разные — от покоя).
                    float cy = BoneYaw(Quaternion.Inverse(body) * s.Chest.rotation, s.ChestRest);
                    s.ChestBodySum += cy * dt;
                    s.ChestBodyMax = Mathf.Max(s.ChestBodyMax, Mathf.Abs(cy));
                    s.ChestHeadSum += Mathf.DeltaAngle(headYaw - bodyYaw, cy) * dt;
                }
                if (s.LeftThigh != null && s.LeftCalf != null) s.KneeYawL += KneeYaw(s.LeftThigh.position, s.LeftCalf.position, l, body, true) * dt;
                if (s.RightThigh != null && s.RightCalf != null) s.KneeYawR += KneeYaw(s.RightThigh.position, s.RightCalf.position, r, body, false) * dt;
                s.ToeYawL += s.LeftProbe.Yaw * dt;
                if (s.Chest != null)
                {
                    s.TorsoTilt += Vector3.Angle(s.Chest.position - s.Hips.position, Vector3.up) * dt;
                    s.SideTilt += SideTilt(s.Chest.position - s.Hips.position, body) * dt;
                    s.ChestBackMax = Mathf.Max(s.ChestBackMax, s.ChestPitchRest - Pitch(s.Chest.position - s.Hips.position, s.Pivot != null ? s.Pivot.rotation : body));
                }
                // Растяжение руки: плечо→кисть сверх суммы костей (плечо + предплечье), %.
                if (s.ArmRestL > 0f) s.ArmStretchMax = Mathf.Max(s.ArmStretchMax, (Vector3.Distance(s.LeftUpperArm.position, s.LeftHand.position) / s.ArmRestL - 1f) * 100f);
                if (s.ArmRestR > 0f) s.ArmStretchMax = Mathf.Max(s.ArmStretchMax, (Vector3.Distance(s.RightUpperArm.position, s.RightHand.position) / s.ArmRestR - 1f) * 100f);
                if (s.LocomotionRig != null && s.LocomotionRig != s.Animator && s.LocomotionRig.isHuman)
                {
                    if (s.RigHips == null)
                    {
                        s.RigHips = s.LocomotionRig.GetBoneTransform(HumanBodyBones.Hips);
                        s.RigChest = s.LocomotionRig.GetBoneTransform(HumanBodyBones.UpperChest) ?? s.LocomotionRig.GetBoneTransform(HumanBodyBones.Chest);
                    }
                    if (s.RigHips != null && s.RigChest != null)
                    {
                        s.ClipTorsoTilt += Vector3.Angle(s.RigChest.position - s.RigHips.position, s.LocomotionRig.transform.up) * dt;
                        s.ClipSideTilt += SideTilt(s.RigChest.position - s.RigHips.position, s.LocomotionRig.transform.rotation) * dt;
                    }
                    if (s.RigThighL == null)
                    {
                        Animator a = s.LocomotionRig;
                        s.RigThighL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); s.RigCalfL = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); s.RigFootL = a.GetBoneTransform(HumanBodyBones.LeftFoot);
                        s.RigThighR = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); s.RigCalfR = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); s.RigFootR = a.GetBoneTransform(HumanBodyBones.RightFoot);
                    }
                    Quaternion rigBody = s.LocomotionRig.transform.rotation;
                    s.RigKneeL += KneeYaw(s.RigThighL.position, s.RigCalfL.position, s.RigFootL.position, rigBody, true) * dt;
                    s.RigKneeR += KneeYaw(s.RigThighR.position, s.RigCalfR.position, s.RigFootR.position, rigBody, false) * dt;
                }
                s.ToeYawR += s.RightProbe.Yaw * dt;
                if (s.PelvisBone != null)
                {
                    float by = Mathf.Abs(BoneYaw(Quaternion.Inverse(body) * s.PelvisBone.rotation, s.PelvisRest));
                    s.PelvisBoneYawSum += by * dt;
                    s.PelvisBoneYawMax = Mathf.Max(s.PelvisBoneYawMax, by);
                }
                Vector3 forward = s.Root.rotation * yaw * Vector3.forward;
                s.LeftProbe.Measure(s.Root.position, forward, dt);
                s.RightProbe.Measure(s.Root.position, forward, dt);
                s.LeftContact.Measure(dt);
                s.RightContact.Measure(dt);

                float lag = 0f;
                if (s.Head != null && s.Chest != null)
                {
                    Vector3 rest = s.Root.rotation * yaw * s.NeckOffsetRest;
                    lag = Flat(s.Head.position - s.Chest.position - rest);
                    s.LagMax = Mathf.Max(s.LagMax, lag);
                }
                if (s.Camera != null && s.Head != null)
                    s.DriftMax = Mathf.Max(s.DriftMax, Vector3.Distance(s.Camera.TransformPoint(s.HeadInCamera), s.Head.position));
                if (s.LeftThigh != null && s.LeftCalf != null && s.RightThigh != null && s.RightCalf != null)
                {
                    s.KneeSum += Vector3.Angle(s.LeftCalf.position - s.LeftThigh.position, l - s.LeftCalf.position)
                               + Vector3.Angle(s.RightCalf.position - s.RightThigh.position, r - s.RightCalf.position);
                    s.KneeCount += 2;
                }

                Vector3 h = s.Camera != null ? s.Root.InverseTransformPoint(s.Camera.position) : Vector3.zero;
                Vector3 fl = s.Root.InverseTransformPoint(l), fr = s.Root.InverseTransformPoint(r);
                _frames.Append(Inv($"{_frame};{Time.timeSinceLevelLoad:0.000};{_segment};{s.Name};{h.x:0.000};{h.z:0.000};{fl.x:0.000};{fl.y:0.000};{fl.z:0.000};{fr.x:0.000};{fr.y:0.000};{fr.z:0.000};{lag * 100f:0.0};"));
                _frames.Append(s.LeftProbe.Frame()).Append(';').Append(s.RightProbe.Frame()).AppendLine(RigState(s));
                s.Crouch.Measure(_frame, _segment, s, body, _crouchCsv, _crouchEvents);
            }
        }

        public void EndSegment()
        {
            if (!_baselined) return;
            foreach (Subject s in _subjects)
            {
                s.LeftContact.Flush();
                s.RightContact.Flush();
                string gait = s.Gait.Summary(_segmentTime, _headPath) + " | " + FootContactProbe.Summary(s.LeftContact, s.RightContact, _headPath);
                _report.AppendLine(Inv($"{_segment} | {s.Name} | ") + gait + " | " +
                                   FootProbe.Summary(s.LeftProbe, s.RightProbe) +
                                   Inv($" | {s.LagMax * 100f:0.0} | {s.DriftMax * 100f:0.0} | {(s.KneeCount > 0 ? s.KneeSum / s.KneeCount : 0f):0} | {s.MinGap:0.00} | {s.PelvisYawSum / Mathf.Max(_segmentTime, 1e-3f):0}/{s.PelvisYawMax:0} · {s.HipYawSum / Mathf.Max(_segmentTime, 1e-3f):0}/{s.HipYawMax:0} · {s.PelvisBoneYawSum / Mathf.Max(_segmentTime, 1e-3f):0}/{s.PelvisBoneYawMax:0} | {s.KneeYawL / Mathf.Max(_segmentTime, 1e-3f):0}/{s.KneeYawR / Mathf.Max(_segmentTime, 1e-3f):0} | {s.ToeYawL / Mathf.Max(_segmentTime, 1e-3f):0}/{s.ToeYawR / Mathf.Max(_segmentTime, 1e-3f):0} | {s.TorsoTilt / Mathf.Max(_segmentTime, 1e-3f):0.0}/{s.ClipTorsoTilt / Mathf.Max(_segmentTime, 1e-3f):0.0} | {s.SideTilt / Mathf.Max(_segmentTime, 1e-3f):0.0}/{s.ClipSideTilt / Mathf.Max(_segmentTime, 1e-3f):0.0} | {s.RigKneeL / Mathf.Max(_segmentTime, 1e-3f):0}/{s.RigKneeR / Mathf.Max(_segmentTime, 1e-3f):0} | {s.ArmStretchMax:0} | {s.ChestBodySum / Mathf.Max(_segmentTime, 1e-3f):0.0}/{s.ChestBodyMax:0.0} | {s.ChestHeadSum / Mathf.Max(_segmentTime, 1e-3f):0.0} | {s.HipLineSignedSum / Mathf.Max(_segmentTime, 1e-3f):0.0} | {s.PivotBodySum / Mathf.Max(_segmentTime, 1e-3f):0.0}/{s.PivotBodyMax:0.0} | {s.HipLineMax:0.0} | {s.ChestBackMax:0.0}"));
                _crouch.AppendLine(s.Crouch.Summary(_segment));
                if (_steady) _gait.AppendLine(Inv($"{_segment} | {s.Name} | {_headPath / Mathf.Max(_segmentTime, 1e-3f):0.00} | ") + gait);
            }
        }

        public void Note(string line) => _report.AppendLine(line);

        public void Write(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "metrics.txt"), _report.ToString());
            File.WriteAllText(Path.Combine(folder, "gait.txt"), _gait.ToString());
            File.WriteAllText(Path.Combine(folder, "frames.csv"), _frames.ToString());
            File.WriteAllText(Path.Combine(folder, "crouch.txt"), _crouch + "\nПересечения Legs_Crouch:\n" + _crouchEvents);
            File.WriteAllText(Path.Combine(folder, "crouch.csv"), _crouchCsv.ToString());
        }

        /// <summary>
        /// Параметры локомоции (<c>Legs_*</c> копии рига ног UltimateXR или <c>VRIK_*</c> рига VRIK) и главный клип: идёт ли
        /// шаг, вектор смешивания, скорость проигрывания, клип, стойка.
        /// </summary>
        private static string RigState(Subject s)
        {
            if (s.LocomotionRig == null)
            {
                foreach (Animator a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    if (a.name.Contains("LocomotionRig") && a.name.Contains(s.Root.name)) s.LocomotionRig = a;
                if (s.LocomotionRig == null && s.Animator.runtimeAnimatorController != null) s.LocomotionRig = s.Animator;
            }
            Animator rig = s.LocomotionRig;
            string pre = rig != null && rig.runtimeAnimatorController != null && HasParam(rig, "Legs_Speed") ? "Legs_" : "VRIK_";
            if (rig == null || rig.runtimeAnimatorController == null || !HasParam(rig, pre + "Speed")) return ";;;;;";
            string top = "";
            float best = 0f;
            foreach (AnimatorClipInfo info in rig.GetCurrentAnimatorClipInfo(0))
                if (info.weight > best) { best = info.weight; top = info.clip.name; }
            string set = HasParam(rig, "Legs_Stance") ? Inv($" set{rig.GetFloat("Legs_Stance"):0.0}") : "";
            string x = pre == "Legs_" ? "Legs_MoveX" : "VRIK_Horizontal", z = pre == "Legs_" ? "Legs_MoveZ" : "VRIK_Vertical";
            return Inv($";{(rig.GetBool(pre + "IsMoving") ? 1 : 0)};{rig.GetFloat(x):0.00};{rig.GetFloat(z):0.00};{rig.GetFloat(pre + "Speed"):0.00};{top} {best:0.00}") + set;
        }

        /// <summary>
        /// Кость по humanoid-разметке, а если её нет (у Heavy корень костей переименован и разметка не находит ни одной) — по
        /// скелету UltimateXR аватара.
        /// </summary>
        private static Transform Bone(Animator animator, UltimateXR.Avatar.Rig.UxrAvatarRig rig, HumanBodyBones bone)
        {
            Transform t = animator != null ? animator.GetBoneTransform(bone) : null;
            if (t != null || rig == null) return t;
            switch (bone)
            {
                case HumanBodyBones.Hips: return rig.Hips;
                case HumanBodyBones.Chest: return rig.Chest;
                case HumanBodyBones.UpperChest: return rig.UpperChest;
                case HumanBodyBones.Head: return rig.Head?.Head;
                case HumanBodyBones.LeftUpperLeg: return rig.LeftLeg?.UpperLeg;
                case HumanBodyBones.LeftLowerLeg: return rig.LeftLeg?.LowerLeg;
                case HumanBodyBones.LeftFoot: return rig.LeftLeg?.Foot;
                case HumanBodyBones.LeftToes: return rig.LeftLeg?.Toes;
                case HumanBodyBones.RightUpperLeg: return rig.RightLeg?.UpperLeg;
                case HumanBodyBones.RightLowerLeg: return rig.RightLeg?.LowerLeg;
                case HumanBodyBones.RightFoot: return rig.RightLeg?.Foot;
                case HumanBodyBones.RightToes: return rig.RightLeg?.Toes;
                case HumanBodyBones.LeftUpperArm: return rig.LeftArm?.UpperArm;
                case HumanBodyBones.LeftLowerArm: return rig.LeftArm?.Forearm;
                case HumanBodyBones.LeftHand: return rig.LeftArm?.Hand?.Wrist;
                case HumanBodyBones.RightUpperArm: return rig.RightArm?.UpperArm;
                case HumanBodyBones.RightLowerArm: return rig.RightArm?.Forearm;
                case HumanBodyBones.RightHand: return rig.RightArm?.Hand?.Wrist;
                default: return null;
            }
        }

        private static bool HasParam(Animator a, string name)
        {
            foreach (AnimatorControllerParameter p in a.parameters)
                if (p.name == name) return true;
            return false;
        }

        /// <summary>
        /// Куда смотрит колено: направление сгиба (колено от прямой бедро—стопа) по горизонтали против «вперёд» корпуса, °;
        /// &gt; 0 — наружу (левое влево, правое вправо). Прямая нога — 0.
        /// </summary>
        private static float KneeYaw(Vector3 hip, Vector3 knee, Vector3 foot, Quaternion body, bool left)
        {
            Vector3 axis = (foot - hip).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(knee - hip, axis), Vector3.up);
            if (bend.sqrMagnitude < 1e-6f) return 0f;
            float a = Vector3.SignedAngle(body * Vector3.forward, bend, Vector3.up);
            return left ? -a : a;
        }

        /// <summary>Наклон вектора таз→грудь вперёд (+) / назад (−) в осях корпуса <paramref name="body"/>, °.</summary>
        private static float Pitch(Vector3 spine, Quaternion body)
        {
            Vector3 local = Quaternion.Inverse(body) * spine;
            return Mathf.Atan2(local.z, local.y) * Mathf.Rad2Deg;
        }

        /// <summary>Крен вектора таз→грудь в осях корпуса <paramref name="body"/>: &gt; 0 — вправо, °.</summary>
        private static float SideTilt(Vector3 spine, Quaternion body)
        {
            Vector3 local = Quaternion.Inverse(body) * spine;
            return Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;
        }

        /// <summary>Рысканье поворота <paramref name="current"/> от <paramref name="rest"/> (оба — в осях корпуса), °.</summary>
        private static float BoneYaw(Quaternion current, Quaternion rest)
        {
            Quaternion delta = current * Quaternion.Inverse(rest);
            Vector3 f = Vector3.ProjectOnPlane(delta * Vector3.forward, Vector3.up);
            return f.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(Vector3.forward, f, Vector3.up) : 0f;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }
}
