using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Globalization;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>Кто ведёт манипулятор стенда.</summary>
    public enum PuppetMode
    {
        /// <summary>Человек двигает «голову» и кисти в Scene view.</summary>
        Manual,
        /// <summary>Программа (<see cref="PuppetProgramId"/>): ходьба, повороты, присед, наклоны.</summary>
        Program,
        /// <summary>Сценарий из клипа: голова и кисти — со скрытого рига, который играет клипы с root motion.</summary>
        Clip,
        /// <summary>Просмотр клипа без IK (<see cref="ClipFeetPreview"/>, работает и в Edit Mode).</summary>
        ClipPreview,
    }

    /// <summary>
    /// Набор клипов ног (UltimateXR, <see cref="UxrStandardAvatarController.LegStance"/>): по позе рук стенда (винтовка →
    /// набор винтовки, пистолет → пистолета, иначе без оружия), по захвату оружия (<c>AvatarStanceFromGrabs</c>: оружие
    /// стенда — не из арсенала, без данных оружия набор выходит «без оружия») или задан.
    /// </summary>
    public enum PuppetStance { FromHandPose = -2, FromGrabs = -1, Unarmed = 0, Pistol = 1, Rifle = 2 }

    /// <summary>
    /// Стенд-плейграунд аватаров (T-42): аватары в ряд (основные ноги и эталон VRIK), один манипулятор ведёт их всех —
    /// «тело» (<see cref="body"/>: место и поворот корпуса игрока на полу) с детьми «голова» (<see cref="head"/>) и «кисти»
    /// (<see cref="leftHand"/>, <see cref="rightHand"/>). Крутишь голову — кисти стоят (как контроллеры в руках у игрока,
    /// который оглянулся); крутишь тело — голова и кисти поворачиваются вместе. Аватарам передаются только голова и кисти:
    /// тело аватара строит его IK. Один и тот же стенд для человека и агента.
    ///
    /// <list type="bullet">
    /// <item><b>Ведение</b> (<see cref="mode"/>): вручную в Scene view; программой (<see cref="PuppetPrograms"/>: медленная
    /// ходьба, быстрые движения, полный набор); сценарием из клипа (<see cref="PuppetClipSource"/>: клип с root motion на
    /// скрытом риге — голова и кисти как у живого человека); просмотр клипа без IK (<see cref="ClipFeetPreview"/>).</item>
    /// <item><b>Руки</b> (<see cref="handPose"/>): опущены, согнуты, винтовка, пистолет (<see cref="PuppetHands"/>); с оружием —
    /// настоящий захват UltimateXR (<see cref="PuppetWeapons"/>).</item>
    /// <item><b>Замеры</b> (<see cref="PuppetMetrics"/>) и снимки (<see cref="PuppetShots"/>) каждого аватара при прогоне —
    /// в <c>tmp/PuppetStand/&lt;прогон&gt;/</c>.</item>
    /// <item><b>Агент</b>: в Play — <see cref="RunAll"/>(папка) одной строкой, ждать <see cref="IsFinished"/>.</item>
    /// </list>
    ///
    /// <para>
    /// Аватары — в <see cref="UxrAvatarMode.UpdateExternally"/>, как чужой игрок: камера и кисти ставятся в стадии Update
    /// UltimateXR, тело и ноги строит каждый вариант сам. Корень аватара стоит, ходит голова (физическая ходьба). Сцену
    /// собирает меню <c>Tools/VR Battlegrounds/Debug/Avatar Puppet Stand</c>; корень с меткой <c>[DevStand]</c> — Play
    /// стартует в ней, EditorPrefs не трогаются.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AvatarPuppetStand : MonoBehaviour
    {
        private const float SettleTime = 2.5f;

        [Header("Аватары в ряду")]
        [Tooltip("Какие аватары стоят в ряду (префабы игрока). Один префаб можно поставить несколько раз — для сравнения настроек (см. «Подмены по слоту»).")]
        public GameObject[] avatarPrefabs;

        [Tooltip("Галочка у каждого аватара ряда: снятая — аватар не появляется. Меняется до запуска Play.")]
        public bool[] avatarEnabled;

        [Tooltip("Расстояние между аватарами, м. Обычно 4: при меньшем на снимках сбоку виден сосед.")]
        public float spacing = 1.6f;

        [Header("Кукла: тело, голова и руки")]
        [Tooltip("«Тело» — место и поворот корпуса игрока на полу; голова и кисти — его дети. Двигаешь/крутишь тело — голова и кисти идут вместе. Аватарам тело не передаётся (его строит IK аватара).")]
        public Transform body;

        [Tooltip("«Голова» (ребёнок тела) — её поворачиваешь и наклоняешь отдельно от тела; все аватары повторяют её, каждый на своём месте.")]
        public Transform head;

        [Tooltip("«Кисти» — сферы, дети тела (не головы): при повороте головы стоят на месте. В программах ставятся по позе рук.")]
        public Transform leftHand, rightHand;

        [Tooltip("Рост глаз «игрока» в программах, м. MEF по модели — 1,72; меньше — ноги аватара сильнее согнуты.")]
        public float eyeHeight = 1.65f;

        [Header("Кто ведёт куклу")]
        [Tooltip("Вручную — двигаешь голову и кисти сам; Программа — заданная ходьба/повороты/присед; Клип — голову и руки ведёт анимация живого человека; Просмотр клипа — клип без IK на манекене и MEF (и без Play).")]
        public PuppetMode mode = PuppetMode.Manual;

        [Tooltip("Какую программу запускает кнопка: медленная ходьба во все стороны, ход вбок, быстрые рывки или полный набор (повороты, присед, наклоны).")]
        public PuppetProgramId program = PuppetProgramId.SlowWalk;

        [Tooltip("Множитель скорости ходьбы в программах. 1 — как записано (0,3/0,6/1,0 м/с и т. д.).")]
        [Range(0.1f, 3f)]
        public float speedMultiplier = 1f;

        [Tooltip("Сценарий из клипа: анимации по очереди (сколько секунд и с какой скоростью). Скорость 0,4 — шаг медленнее обычного.")]
        public PuppetClipEntry[] clipScenario;

        [Tooltip("Невидимый скелет, который играет сценарий из клипа (копия скелета MEF). Руками не менять.")]
        public GameObject clipRigPrefab;

        [Tooltip("Объект просмотра клипа (дочерний): там выбираются клип, фаза и Foot IK. Включается режимом «Просмотр клипа».")]
        public GameObject clipPreview;

        [Header("Руки")]
        [Tooltip("Как держать руки: опущены, согнуты перед собой, с винтовкой, с пистолетом; «из клипа» — только в сценарии из клипа.")]
        public PuppetHandPose handPose = PuppetHandPose.Ready;

        [Tooltip("Давать аватарам настоящее оружие в руки (поза «Винтовка»/«Пистолет»): пальцы обхватывают оружие, как в игре.")]
        public bool grabWeapon = true;

        [Tooltip("Оружие для позы «Винтовка» и «Пистолет».")]
        public GameObject rifleWeapon, pistolWeapon;

        [Header("Ноги (UltimateXR, раздел «Ноги» контроллера)")]
        [Tooltip("Какими клипами ходят ноги: по позе рук (винтовка → шаг с винтовкой), по оружию в руках, или задано вручную. Что играло на деле — колонка set в frames.csv.")]
        public PuppetStance uxrLegsStance = PuppetStance.FromHandPose;

        [Tooltip("Другой набор анимаций ходьбы для ног (контракт Legs_* UxrLegLocomotion); пусто — как в префабе. Меняется до запуска Play.")]
        public RuntimeAnimatorController uxrLegsController;

        [Tooltip("Для опытов: поменять настройки всем аватарам, не трогая префабы. Запись «Компонент.поле=значение» через «;», например UxrStandardAvatarController.Legs.torsoFromClip=0 или UxrStandardAvatarController.Legs.locomotion.blendRing=0. Меняется до запуска Play.")]
        public string avatarOverrides;

        [Tooltip("То же для каждого аватара ряда отдельно (по порядку в списке аватаров) — так сравниваются настройки бок о бок.")]
        public string[] slotOverrides;

        [Tooltip("Подпись над каждым аватаром ряда и его имя в замерах (по порядку в списке). Пусто — имя префаба.")]
        public string[] slotLabels;

        [Header("Следы стоп")]
        [Tooltip("Рисовать следы на полу в Scene view: зелёная точка — стопа встала, красная линия — стопа скользит, стоя на полу.")]
        public bool showFootprints = true;

        [Tooltip("Сколько секунд держать следы.")]
        public float footprintSeconds = 10f;

        [Header("Запись замеров")]
        [Tooltip("Куда писать замеры и снимки прогонов. Пусто — папка tmp/PuppetStand проекта.")]
        public string outputFolder;

        // ---------- Вход для агента ----------

        /// <summary>Стенд в сцене (Play).</summary>
        public static AvatarPuppetStand Instance { get; private set; }

        /// <summary>Все прогоны <see cref="RunAll"/> записаны.</summary>
        public static bool IsFinished { get; private set; }

        /// <summary>Куда пишет текущий прогон.</summary>
        public static string LastOutput { get; private set; }

        /// <summary>
        /// Агенту: в Play проиграть все программы и сценарий из клипа подряд и записать в <paramref name="folder"/>
        /// (<c>&lt;программа&gt;/metrics.txt, gait.txt, frames.csv, *.png</c>). Ждать <see cref="IsFinished"/>.
        /// </summary>
        public static string RunAll(string folder)
        {
            IsFinished = false;
            if (Instance == null) return "нет стенда (Play в сцене [DevStand] AvatarPuppetStand)";
            Instance.QueueAll(folder);
            return "запущено: " + LastOutput;
        }

        /// <summary>
        /// Агенту: в Play проиграть только программы <paramref name="ids"/> подряд и записать в <paramref name="folder"/>.
        /// Ждать <see cref="IsFinished"/>.
        /// </summary>
        public static string RunPrograms(string folder, params PuppetProgramId[] ids)
        {
            IsFinished = false;
            if (Instance == null || !Instance._ready) return "нет стенда или аватары ещё устаиваются";
            Instance._runner.Stop();
            Instance.Begin(string.IsNullOrEmpty(folder) ? Instance.OutputRoot() : folder, true);
            foreach (PuppetProgramId id in ids)
                Instance._runner.Enqueue(new PuppetRunner.Run { Name = id.ToString(), Steps = PuppetPrograms.Build(id, Instance.speedMultiplier) });
            return "запущено: " + LastOutput;
        }

        // ---------- Состояние ----------

        private sealed class Puppet
        {
            public UxrAvatar Avatar;
            public Transform Root;
            public Transform LeftHand, RightHand;
            public Quaternion LeftAxes, RightAxes;   // универсальные оси кисти → оси кости
            public PuppetWeapons Weapons;
            public bool IsUxrLegs;
            public string Name;
        }

        private readonly List<Puppet> _puppets = new List<Puppet>();
        private readonly PuppetRunner _runner = new PuppetRunner();
        private PuppetMetrics _metrics;
        private PuppetShots _shots;
        private PuppetClipSource _clip;
        private readonly PuppetFootprints _footprints = new PuppetFootprints();
        private bool _savedFocusPause;
        private float _age;
        private bool _ready;
        private PuppetHandPose _appliedHands = (PuppetHandPose)(-1);
        private PuppetStance _appliedStance = (PuppetStance)(-2);
        private bool _queueAllAfterReady;
        private string _queueAllFolder;

        /// <summary>Наибольшее дрожание оружия в руке по аватарам с прошлого вызова: «имя мм/°» (для агента).</summary>
        public string TakeWeaponJitter()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Puppet p in _puppets)
            {
                if (p.Weapons == null) continue;
                (float mm, float deg) = p.Weapons.TakeJitter();
                sb.Append(Inv($"{p.Name}: {mm:0.0} мм / {deg:0.0}°; "));
            }
            return sb.ToString();
        }

        private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        public string Status => !_ready ? "аватары устаиваются…" : _runner.IsRunning ? "прогон: " + _runner.Current : "ручной";

        private void Start()
        {
            Instance = this;
            _savedFocusPause = UxrManager.EditorFocusPauseEnabled;
            UxrManager.EditorFocusPauseEnabled = false;
            if (clipPreview != null) clipPreview.SetActive(false);

            for (int i = 0; i < avatarPrefabs.Length; i++)
            {
                if (avatarPrefabs[i] == null) continue;
                if (avatarEnabled != null && i < avatarEnabled.Length && !avatarEnabled[i]) continue;
                string slot = slotOverrides != null && i < slotOverrides.Length ? slotOverrides[i] : null;
                string label = slotLabels != null && i < slotLabels.Length && !string.IsNullOrWhiteSpace(slotLabels[i]) ? slotLabels[i] : null;
                SpawnPuppet(avatarPrefabs[i], transform.TransformPoint(Vector3.right * spacing * _puppets.Count), slot, i, label);
            }

            _metrics = new PuppetMetrics();
            foreach (Puppet p in _puppets) _metrics.Add(p.Name, p.Root, p.Avatar.CameraTransform);
            _runner.Metrics = _metrics;
            foreach (PuppetMetrics.Subject s in _metrics.Subjects)
            {
                _footprints.Add(s.LeftContact);
                _footprints.Add(s.RightContact);
            }
            _shots = new PuppetShots();
            _clip = new PuppetClipSource(clipRigPrefab, transform.position + Vector3.down * 50f);
            if (body == null) GameLog.Debug.Warning("[AvatarPuppetStand] Нет «тела» манипулятора — пересобери сцену (Tools/VR Battlegrounds/Debug/Avatar Puppet Stand).", this);
            if (_puppets.Count > 0) _clip.SetHandAxes(_puppets[0].LeftAxes, _puppets[0].RightAxes);

            new GameObject("PuppetStand_FrameEnd").AddComponent<FrameEndProbe>().FrameEnded = OnFrameEnd;
            UxrManager.StageUpdated += OnStageUpdated;
            PlaceHandles(new Pose(transform.InverseTransformPoint(head.position), Quaternion.Inverse(transform.rotation) * head.rotation));
            GameLog.Debug.Info($"[AvatarPuppetStand] Аватаров в ряду: {_puppets.Count}. Ручной режим — двигай '{head.name}' в Scene view; программы — кнопки в инспекторе.", this);
        }

        private void OnDestroy()
        {
            UxrManager.StageUpdated -= OnStageUpdated;
            UxrManager.EditorFocusPauseEnabled = _savedFocusPause;
            Time.captureFramerate = 0;
            _shots?.Dispose();
            _clip?.Dispose();
            foreach (Puppet p in _puppets) p.Weapons?.Drop();
            if (Instance == this) Instance = null;
        }

        private void SpawnPuppet(GameObject prefab, Vector3 origin, string slotOverride, int slot, string slotLabel)
        {
            GameObject go = Instantiate(prefab, origin, transform.rotation);
            go.name = $"Puppet{slot}_{prefab.name}";
            var avatar = go.GetComponent<UxrAvatar>();
            if (avatar == null)
            {
                GameLog.Debug.Warning($"[AvatarPuppetStand] '{prefab.name}' без UxrAvatar — пропущен.", this);
                Destroy(go);
                return;
            }

            avatar.AvatarMode = UxrAvatarMode.UpdateExternally;
            foreach (Camera cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
            foreach (AudioListener listener in go.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;

            string label = slotLabel ?? prefab.name.Replace("Optimized_MEF_Player_", "") + (string.IsNullOrWhiteSpace(slotOverride) ? "" : $"#{slot}");
            var p = new Puppet { Avatar = avatar, Root = go.transform, Weapons = new PuppetWeapons(avatar), Name = label };
            // Кисти VRIK-аватара решает VRIK после стадий UltimateXR — оружие за решённой кистью (см. PuppetWeapons.AfterIK).
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb.GetType().Name == "AvatarVrikDriver") p.Weapons.AfterIK = true;
            p.LeftHand = avatar.GetHandBone(UxrHandSide.Left);
            p.RightHand = avatar.GetHandBone(UxrHandSide.Right);
            p.LeftAxes = HandAxes(avatar, UxrHandSide.Left);
            p.RightAxes = HandAxes(avatar, UxrHandSide.Right);

            // До появления копии рига ног (первый кадр PostProcess): подмены полей и контроллера ног UltimateXR.
            var uxr = go.GetComponent<UxrStandardAvatarController>();
            if (uxr != null && uxr.UseNativeLegIK && uxr.AnimatedLegs != null)
            {
                p.IsUxrLegs = true;
                if (uxrLegsController != null) uxr.Legs.locomotionController = uxrLegsController;
            }
            ApplyOverrides(go, avatarOverrides);
            ApplyOverrides(go, slotOverride);
            _puppets.Add(p);
            AddLabel(go.transform, slotLabel != null || string.IsNullOrWhiteSpace(slotOverride) ? label : label + "\n" + slotOverride.Replace(";", "\n"));
        }

        private static Quaternion HandAxes(UxrAvatar avatar, UxrHandSide side)
        {
            var info = avatar.AvatarRigInfo?.GetArmInfo(side);
            return info?.HandUniversalLocalAxes != null ? info.HandUniversalLocalAxes.UniversalToActualAxesRotation : Quaternion.identity;
        }

        // ---------- Кнопки ----------

        /// <summary>Запустить программу (прогон пишется в папку стенда).</summary>
        public void RunProgram(PuppetProgramId id)
        {
            _runner.Stop();
            mode = PuppetMode.Program;
            Begin(OutputRoot(), false);
            _runner.Enqueue(new PuppetRunner.Run { Name = Stamp(id.ToString()), Steps = PuppetPrograms.Build(id, speedMultiplier) });
        }

        /// <summary>Запустить сценарий из клипа (<see cref="clipScenario"/>).</summary>
        public void RunClipScenario()
        {
            _runner.Stop();
            mode = PuppetMode.Clip;
            Begin(OutputRoot(), false);
            _runner.Enqueue(ClipRun(Stamp("Clip")));
        }

        /// <summary>Остановить прогон — ручной режим (манипулятор остаётся, где был).</summary>
        public void StopToManual()
        {
            _runner.Stop();
            _active = _all = false;
            mode = PuppetMode.Manual;
            Time.captureFramerate = 0;
        }

        private void QueueAll(string folder)
        {
            if (!_ready)
            {
                _queueAllAfterReady = true;
                _queueAllFolder = folder;
                LastOutput = folder;
                return;
            }

            _runner.Stop();
            Begin(string.IsNullOrEmpty(folder) ? OutputRoot() : folder, true);
            foreach (PuppetProgramId id in Enum.GetValues(typeof(PuppetProgramId)))
                _runner.Enqueue(new PuppetRunner.Run { Name = id.ToString(), Steps = PuppetPrograms.Build(id, speedMultiplier) });
            _runner.Enqueue(ClipRun("Clip"));
        }

        private PuppetRunner.Run ClipRun(string name)
        {
            var steps = new List<PuppetStep> { PuppetPrograms.Hold("00_idle", 1.5f, 1f) };
            steps.AddRange(PuppetRunner.ClipSteps(clipScenario ?? new PuppetClipEntry[0]));
            // Первый шаг — покой на первом клипе: риг встаёт в его позу, аватары подтягиваются.
            if (steps.Count > 1) steps[0].Clip = steps[1].Clip;
            return new PuppetRunner.Run { Name = name, Steps = steps, FromClip = true };
        }

        private bool _all, _active;

        private void Begin(string root, bool all)
        {
            _all = all;
            _active = true;
            IsFinished = false;
            _runner.OutputRoot = root;
            LastOutput = root;
            Time.captureFramerate = 60;   // шаг времени одинаковый при любом FPS — прогоны сравнимы
        }

        private string OutputRoot() => string.IsNullOrEmpty(outputFolder) ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tmp", "PuppetStand")) : outputFolder;

        private static string Stamp(string name) => DateTime.Now.ToString("MMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + name;

        // ---------- Кадр ----------

        private void Update()
        {
            _age += Time.deltaTime;
            if (!_ready && _age >= SettleTime)
            {
                _metrics.CaptureBaselines();
                _ready = true;
                if (_queueAllAfterReady) QueueAll(_queueAllFolder);
            }

            ApplyHandsAndStance();
            if (!_ready) return;

            if (_runner.Tick(Time.deltaTime, out PuppetClipEntry clipEntry, out bool firstClip))
            {
                if (_runner.FromClip)
                {
                    if (clipEntry != null) _clip.Play(clipEntry.clip, clipEntry.speed, firstClip);
                    _clip.Tick(Time.deltaTime);
                    _clip.Read(out Pose b, out Pose h, out Pose l, out Pose r);
                    SetHandle(body, b);
                    head.SetPositionAndRotation(transform.TransformPoint(h.position), transform.rotation * h.rotation);
                    if (handPose == PuppetHandPose.FromClip)
                    {
                        SetHandle(leftHand, l);
                        SetHandle(rightHand, r);
                    }
                    else PlaceHands(h.position, b.rotation);
                }
                else
                {
                    SetHandle(body, _runner.Motion.BodyOnFloor);
                    PuppetCrouchProbe.ManipulatorPelvis = _runner.Motion.PelvisHeight;
                    Pose h = _runner.Motion.Head(eyeHeight);
                    head.SetPositionAndRotation(transform.TransformPoint(h.position), transform.rotation * h.rotation);
                    PlaceHands(_runner.Motion.BodyEye(eyeHeight), _runner.Motion.Hands);
                }
            }
            else if (_active)
            {
                // Очередь кончилась.
                _active = false;
                Time.captureFramerate = 0;
                if (_all) IsFinished = true;
                _all = false;
                mode = PuppetMode.Manual;
                GameLog.Debug.Info($"[AvatarPuppetStand] Прогоны записаны: {LastOutput}", this);
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v : Vector3.forward;
        }

        /// <summary>Кисти по позе <see cref="handPose"/>: в осях корпуса от глаз (<paramref name="eye"/> — в осях стенда).</summary>
        private void PlaceHands(Vector3 eye, Quaternion body)
        {
            PuppetHandPose pose = handPose == PuppetHandPose.FromClip ? PuppetHandPose.Ready : handPose;
            Pose l = PuppetHands.Get(pose, true), r = PuppetHands.Get(pose, false);
            SetHandle(leftHand, new Pose(eye + body * l.position, body * l.rotation));
            SetHandle(rightHand, new Pose(eye + body * r.position, body * r.rotation));
        }

        /// <summary>
        /// Ручной режим: кисти — в позу <see cref="handPose"/> от текущей головы по повороту тела (дальше человек двигает сам;
        /// кисти — дети тела, поворот головы их не трогает).
        /// </summary>
        private void PlaceHandles(Pose headLocal)
        {
            Quaternion trunk = body != null ? Quaternion.Inverse(transform.rotation) * body.rotation
                                            : Quaternion.LookRotation(Flat(headLocal.rotation * Vector3.forward), Vector3.up);
            PlaceHands(headLocal.position, Quaternion.LookRotation(Flat(trunk * Vector3.forward), Vector3.up));
        }

        private void SetHandle(Transform handle, Pose standLocal)
        {
            if (handle == null) return;
            handle.SetPositionAndRotation(transform.TransformPoint(standLocal.position), transform.rotation * standLocal.rotation);
        }

        /// <summary>Поза рук и стойка UxrLegs изменились (инспектор) — оружие и стойка заново.</summary>
        private void ApplyHandsAndStance()
        {
            if (_age < 0.5f) return;   // захват — когда UltimateXR проинициализировал аватары
            if (_appliedHands != handPose)
            {
                _appliedHands = handPose;
                if (!_runner.IsRunning) PlaceHandles(new Pose(transform.InverseTransformPoint(head.position), Quaternion.Inverse(transform.rotation) * head.rotation));
                GameObject weapon = !grabWeapon ? null : handPose == PuppetHandPose.Rifle ? rifleWeapon : handPose == PuppetHandPose.Pistol ? pistolWeapon : null;
                foreach (Puppet p in _puppets)
                {
                    if (weapon == null) p.Weapons.Drop();
                    else if (!p.Weapons.Hold(weapon, handPose == PuppetHandPose.Rifle || handPose == PuppetHandPose.Pistol))
                        GameLog.Debug.Warning($"[AvatarPuppetStand] {p.Root.name}: оружие не взято — руки без него.", this);
                }
            }

            PuppetStance stance = uxrLegsStance != PuppetStance.FromHandPose ? uxrLegsStance
                                : handPose == PuppetHandPose.Rifle ? PuppetStance.Rifle
                                : handPose == PuppetHandPose.Pistol ? PuppetStance.Pistol
                                : PuppetStance.Unarmed;
            if (_appliedStance != stance)
            {
                _appliedStance = stance;
                foreach (Puppet p in _puppets)
                {
                    if (!p.IsUxrLegs) continue;
                    foreach (MonoBehaviour mb in p.Root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (mb.GetType().Name == "AvatarStanceFromGrabs") SetField(mb, "followGrabs", stance == PuppetStance.FromGrabs);
                    var uxr = p.Avatar.GetComponent<UxrStandardAvatarController>();
                    if (uxr != null && stance != PuppetStance.FromGrabs) uxr.LegStance = (int)stance;
                }
            }
        }

        /// <summary>Стадия Update UltimateXR — до IK этого кадра: камеры и кисти всех аватаров по манипулятору.</summary>
        private void OnStageUpdated(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.Update || head == null) return;

            Vector3 headPos = transform.InverseTransformPoint(head.position);
            Quaternion headRot = Quaternion.Inverse(transform.rotation) * head.rotation;
            Pose l = ToStand(leftHand), r = ToStand(rightHand);

            foreach (Puppet p in _puppets)
            {
                if (p.Avatar == null) continue;
                Transform cam = p.Avatar.CameraTransform;
                if (cam == null) continue;
                cam.SetPositionAndRotation(p.Root.TransformPoint(headPos), p.Root.rotation * headRot);
                if (p.LeftHand != null && leftHand != null) p.LeftHand.SetPositionAndRotation(p.Root.TransformPoint(l.position), p.Root.rotation * l.rotation * p.LeftAxes);
                if (p.RightHand != null && rightHand != null)
                {
                    Pose target = new Pose(p.Root.TransformPoint(r.position), p.Root.rotation * r.rotation * p.RightAxes);
                    p.RightHand.SetPositionAndRotation(target.position, target.rotation);
                    p.Weapons?.Place(target);
                }
            }
        }

        private Pose ToStand(Transform t) =>
            t == null ? default : new Pose(transform.InverseTransformPoint(t.position), Quaternion.Inverse(transform.rotation) * t.rotation);

        private void OnFrameEnd()
        {
            foreach (Puppet p in _puppets)
            {
                p.Weapons?.PlaceAfterIK();
                p.Weapons?.MeasureJitter();
            }
            if (!_ready) return;
            if (_runner.IsRunning) _runner.EndOfFrame(Time.deltaTime, transform.InverseTransformPoint(head.position), Capture);
            else _metrics.Measure(Time.deltaTime, 0f, transform.InverseTransformPoint(head.position), false);
            _footprints.Seconds = footprintSeconds;
            _footprints.Record(Time.time);
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying && showFootprints) _footprints.Draw();
        }

        private bool Capture(string path)
        {
            var hips = new List<Transform>();
            foreach (PuppetMetrics.Subject s in _metrics.Subjects) hips.Add(s.Hips);
            _shots.Capture(path, hips, transform.rotation * Quaternion.Euler(0f, _runner.Motion.Yaw, 0f));
            return true;
        }

        [DefaultExecutionOrder(32000)]
        private sealed class FrameEndProbe : MonoBehaviour
        {
            public Action FrameEnded;
            private void LateUpdate() => FrameEnded?.Invoke();
        }

        // ---------- Edit Mode: режим просмотра клипа ----------

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying || clipPreview == null) return;
            bool on = mode == PuppetMode.ClipPreview;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (clipPreview != null && clipPreview.activeSelf != on) clipPreview.SetActive(on);
            };
        }
#endif

        // ---------- Подмены полей ----------

        private void ApplyOverrides(GameObject go, string overrides)
        {
            if (string.IsNullOrWhiteSpace(overrides)) return;
            foreach (string pair in overrides.Split(';'))
            {
                string[] kv = pair.Split('=');
                string[] tf = kv.Length == 2 ? kv[0].Trim().Split('.') : null;
                if (tf == null || tf.Length < 2) continue;
                foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb.GetType().Name != tf[0]) continue;
                    // «Тип.поле» или «Тип.вложенный.поле» (вложенный — класс или свойство, например
                    // UxrStandardAvatarController.Legs.locomotion.blendRing).
                    object owner = mb;
                    for (int i = 1; i < tf.Length - 1 && owner != null; i++)
                    {
                        Type t = owner.GetType();
                        owner = t.GetField(tf[i], BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner)
                                ?? t.GetProperty(tf[i], BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);
                    }
                    FieldInfo field = owner?.GetType().GetField(tf[tf.Length - 1], BindingFlags.Public | BindingFlags.Instance);
                    if (field == null)
                    {
                        GameLog.Debug.Warning($"[AvatarPuppetStand] Нет поля '{kv[0].Trim()}'.", this);
                        continue;
                    }
                    string raw = kv[1].Trim();
                    object value = typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                                 ? UnityEditor.AssetDatabase.LoadAssetAtPath(raw, field.FieldType)   // ассет — путем Assets/...
                                 : field.FieldType == typeof(bool) ? bool.Parse(raw)
                                 : field.FieldType == typeof(int) ? int.Parse(raw, CultureInfo.InvariantCulture)
                                 : (object)float.Parse(raw, CultureInfo.InvariantCulture);
                    field.SetValue(owner, value);
                }
            }
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.SetValue(target, value);

        private static void AddLabel(Transform root, string text)
        {
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(root, false);
            label.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            label.text = text;
            label.characterSize = 0.03f;
            label.fontSize = 64;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = Color.black;
        }
    }
}
