// Ярус C (два процесса) — сценарий weapon-hit-damage: попадание из настоящего оружия отнимает здоровье.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Mirror;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>weapon-hit-damage</c> — сторож находки <b>T-28</b>.
    ///
    /// <para>
    /// <b>Что доказывает.</b> Выстрел из настоящего <see cref="UxrFirearmWeapon" /> в упор
    /// по живому игроку обязан отнять у него здоровье. Сегодня не отнимает: у действующего
    /// <see cref="UxrProjectileSource" /> на <c>M16_Rifle_prefab</c> и <c>Gun_real</c>
    /// поле <c>CollisionLayerMask</c> равно нулю, а <c>Physics.Raycast</c> с нулевой маской
    /// не проверяет ни одного слоя. Ни урона, ни декалей, ни рикошетов — оба ствола чистые
    /// трассеры. Соседний измерительный <c>shot-pipeline-budget</c> это только <i>показывал</i>
    /// числом; здесь оно становится фальсифицируемой проверкой.
    /// </para>
    ///
    /// <para>
    /// <b>Почему стрельба идёт через <see cref="UxrFirearmWeapon.TryToShootRound" />,
    /// а не через <c>UxrProjectileSource.Shoot</c> напрямую.</b> Нужен весь тракт целиком:
    /// проверка боезапаса, темпа стрельбы и <c>CanUse</c>, выбор источника снарядов
    /// (<c>GetCachedComponent&lt;UxrProjectileSource&gt;()</c> — а их на префабе <b>два</b>,
    /// и берётся первый), затем <c>RegisterNewProjectileShot</c> и симуляция снаряда
    /// в <c>UxrWeaponManager.UpdateProjectiles</c>. Прямой <c>UxrActor.ReceiveDamage</c>
    /// проверил бы не тот тракт: он входит в цепочку урона ниже трассировки и зелен даже
    /// при нулевой маске.
    /// </para>
    ///
    /// <para>
    /// <b>Один клиент, без матча.</b> Двух клиентов требует только старт матча
    /// (<c>EliminationMode.IsPlayersReady</c>), а матч здесь не нужен и вреден: вне матча
    /// <c>GameplayManager</c> держит <c>WeaponSystemEnabled = true</c>, а в матче гасит
    /// оружие всюду, кроме фазы <c>Combat</c>. Жертва — единственный клиент.
    /// </para>
    ///
    /// <para>
    /// <b>Почему сценарий сам выбирает скин жертвы.</b> Поражаемые коллайдеры есть
    /// не у всех аватаров: у <c>Heavy_Soldier_Base_Avatar</c>, <c>Military_Soldier_Base_Avatar</c>,
    /// <c>Military_Cap_Base_Avatar</c> и <c>Spy_Base_Avatar</c> нет ни одного непроходного
    /// коллайдера, и попасть в них нельзя ни при какой маске. Это отдельный дефект,
    /// не тот, что проверяется здесь. Чтобы он не подменял вердикт, сценарий выбирает
    /// жертве первый скин команды с непроходным коллайдером, а сам обзор скинов печатает
    /// отдельной измерительной проверкой.
    /// </para>
    ///
    /// <para>
    /// <b>Что не проверяется.</b> Декали, рикошеты и <c>PrefabInstantiateOnImpact</c>:
    /// они рождаются только на попадании в <b>не</b>-актора, и их место — в отдельном
    /// сценарии про геометрию, а не здесь.
    /// </para>
    /// </summary>
    public class WeaponHitDamageScenario : IE2EScenario
    {
        public string Name => "weapon-hit-damage";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients   = "клиент подключился и сервер создал сессию";
        private const string CheckAvatar    = "карта загружена, у жертвы есть живой аватар с поражаемым коллайдером";
        private const string CheckSkins     = "замер: у каких скинов вообще есть поражаемый коллайдер";
        private const string CheckWeapon    = "на карте нашлось оружие с UxrFirearmWeapon и патронами";
        private const string CheckShotConf  = "замер: настройки выстрела у всех стволов реестра";
        private const string CheckAim       = "оружие наведено: эталонный луч из дула упирается в жертву";
        private const string CheckFired     = "выстрел состоялся: UxrFirearmWeapon.TryToShootRound вернул true";
        private const string CheckDamage    = "попадание отняло здоровье у игрока";
        private const string CheckControl   = "контроль: тот же выстрел с эталонной маской отнимает здоровье";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientAvatar    = "у клиента есть свой аватар";
        private const string CheckClientHealth    = "если сервер засчитал урон, здоровье убыло и у клиента";

        // ── Фазы, которые сервер раздаёт клиенту через PlayerSession.Score ─

        /// <summary>Сервер ещё не стрелял.</summary>
        private const int PhaseIdle = 0;

        /// <summary>Сервер отстрелялся и урон на сервере <b>засчитан</b>.</summary>
        private const int PhaseDamaged = 2;

        /// <summary>Сервер отстрелялся, урона <b>нет</b> — клиенту наблюдать нечего.</summary>
        private const int PhaseNoDamage = 3;

        // ── Сроки и параметры ─────────────────────────────────────────────

        /// <summary>Сколько сервер ждёт отчёта «клиент готов, аватар на месте».</summary>
        private const float ArmedWait = 120f;

        /// <summary>Сколько сервер ждёт убыли здоровья после очереди.</summary>
        private const float DamageWait = 5f;

        /// <summary>Сколько клиент ждёт фазы от сервера.</summary>
        private const float PhaseWait = 90f;

        /// <summary>Сколько клиент ждёт, что убыль здоровья доедет до него.</summary>
        private const float ClientHealthWait = 15f;

        /// <summary>Сколько сервер ждёт, пока клиент запишет вердикт.</summary>
        private const float ClientVerdictWait = 45f;

        /// <summary>
        /// Желаемая и минимально допустимая дистанция выстрела, метров. Обе «в упор»:
        /// урон у стволов не зависит от дистанции (<c>DamageNear == DamageFar</c>),
        /// а короткий путь не даёт снаряду зацепить постороннюю геометрию. Минимум
        /// нужен, чтобы дуло не оказалось внутри самой жертвы.
        /// </summary>
        private static readonly float[] ShotDistances = { 2.5f, 0.7f };

        /// <summary>
        /// Углы подъёма, под которыми ищется свободное направление, градусы.
        /// Одних азимутов мало: после смены карты аватар создаётся в начале координат
        /// (<c>AvatarManager.ChangeAvatar</c> берёт позицию старого аватара, а его уже нет),
        /// и там он вполне может стоять вплотную к реквизиту. Сверху путь свободнее.
        /// </summary>
        private static readonly float[] AimElevations = { 0f, 20f, 40f, 60f, -20f };

        /// <summary>Насколько дуло отводится от препятствия, метров.</summary>
        private const float MuzzleClearance = 0.3f;

        /// <summary>Как далеко смотрит замер свободного места, метров.</summary>
        private const float ProbeReach = 8f;

        /// <summary>Сколько раз пробуем выстрелить. Урона 25 за попадание — три выстрела не убивают.</summary>
        private const int ShotAttempts = 3;

        /// <summary>Пауза между выстрелами: <c>_maxShotFrequency = 10</c> на префабе M16.</summary>
        private const float ShotInterval = 0.3f;

        /// <summary>
        /// Эталонная маска слоёв: <c>Default</c> (0) и <c>Ground</c> (3), значение <b>9</b>.
        ///
        /// <para>
        /// Ею сценарий <b>не стреляет</b> — она нужна ровно для контрольного луча,
        /// который отвечает на вопрос «а попал бы выстрел, будь маска настроена».
        /// Состав выведен из замера слоёв проекта: поражаемые коллайдеры аватаров
        /// и вся геометрия карт лежат на <c>Default</c>, пол — на <c>Ground</c>.
        /// Остальные слои исключены осознанно: <c>Ignore Raycast</c> по назначению,
        /// <c>UI</c> и <c>TransparentFX</c> не для пуль, <c>SpawnZone</c> и <c>Player</c>
        /// содержат одни триггеры (а <c>UpdateProjectiles</c> трассирует
        /// с <c>QueryTriggerInteraction.Ignore</c>), безымянный слой 29 — сантиметровые
        /// метки выравнивания физического пространства.
        /// </para>
        /// </summary>
        private const int ReferenceCollisionMask = (1 << 0) | (1 << 3);

        // ── Наблюдение клиента ────────────────────────────────────────────

        /// <summary>Наименьшее здоровье своего аватара, увиденное клиентом.</summary>
        private float _clientMinHealth = float.MaxValue;

        /// <summary>Здоровье своего аватара в момент, когда клиент поднял флаг готовности.</summary>
        private float _clientHealthAtArm = -1f;


        public IEnumerator Run(E2EContext context, E2EResult result)
        {
            if (context.IsServerRole)
                return RunServer(context, result);

            return RunClient(context, result);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль сервера
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckDedicated, CheckClients, CheckAvatar, CheckSkins,
                           CheckWeapon, CheckShotConf, CheckAim, CheckFired, CheckDamage, CheckControl);

            float deadline = Now + 60f;
            while (!NetworkServer.active && Now < deadline)
                yield return null;

            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated,
                dedicated
                    ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                    : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                      "Урон вычитается только на сервере (UxrActor.OnReceiveDamage под NoSessionOrSessionOwner), " +
                      "и на хосте роль сервера неотличима от роли клиента.");

            if (!dedicated)
            {
                result.Summary = "конфигурация не выделенный сервер, вердикт недействителен";
                yield break;
            }

            DisableDebugOrchestrator();

            deadline = Now + 90f;
            while (SessionCount() < context.ExpectedClients && Now < deadline)
                yield return null;

            int sessions = SessionCount();
            bool clientsOk = sessions >= context.ExpectedClients;
            result.Set(CheckClients, clientsOk,
                clientsOk
                    ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients})"
                    : $"за 90 с подключилось сессий: {sessions} из {context.ExpectedClients}");

            if (!clientsOk)
            {
                result.Summary = "клиент не подключился, стрелять не в кого";
                yield break;
            }

            SessionManager sessionManager = SessionManager.Instance;
            if (sessionManager == null || MapManager.Instance == null)
            {
                result.Set(CheckAvatar, false,
                    $"SessionManager.Instance={(sessionManager == null ? "null" : "есть")}, " +
                    $"MapManager.Instance={(MapManager.Instance == null ? "null" : "есть")}");
                result.Summary = "менеджеры не поднялись, прогон недействителен";
                yield break;
            }

            // Режим выбирается не ради матча (матч здесь не запускается), а ради двух
            // вещей: списка команд, из которого берутся скины, и валидного индекса
            // команды — без него AvatarManager.ChangeAvatar молча выходит и аватара нет.
            sessionManager.SetSession(context.Map, "elimination");
            yield return null;

            // ── Обзор скинов и выбор поражаемого ──────────────────────────
            // Делается после SetSession и до раздачи команд: список команд берётся
            // из выбранного режима, а результат обзора определяет, какой AvatarIndex
            // получит жертва.
            string skinSurvey = SurveySkins(sessionManager, out TeamData victimTeam, out int victimAvatarIndex);

            result.Set(CheckSkins, true, skinSurvey);

            // Команда назначается ради AvatarManager.ChangeAvatar: без известного
            // TeamRegistry индекса он молча выходит и аватар не спавнится вовсе.
            // Индекс скина — ради этого сценария: попасть можно не в каждый скин.
            string assignment = AssignVictim(victimTeam, victimAvatarIndex);
            GameLog.Debug.Info($"[E2E] Жертва снаряжена: {assignment}");

            MapManager.Instance.LoadMap(context.Map);

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            PlayerSession victim = null;
            deadline = Now + 90f;
            while (Now < deadline)
            {
                victim = FirstSessionWithAvatar();
                if (victim != null) break;

                yield return null;
            }

            // Пара кадров форы, чтобы аватар жертвы прошёл Start. Раньше выдержка
            // защищала ещё и от отладочных кубов ColliderVisualizer (удалён).
            yield return E2EWait.Hold(0.5f);

            int solidCount = 0;
            string colliderList = "аватара нет";
            if (victim != null)
                SolidBounds(victim.ActiveAvatar, out solidCount, out colliderList);

            bool avatarOk = victim != null && solidCount > 0;

            result.Set(CheckAvatar, avatarOk,
                avatarOk
                    ? $"сцена='{SceneManager.GetActiveScene().name}', жертва {victim.PlayerName}: " +
                      $"аватар '{victim.ActiveAvatar.name}' netId={victim.ActiveAvatar.netId}, " +
                      $"Life={victim.ActiveAvatar.Health:F0}. {assignment}. " +
                      $"Поражаемых коллайдеров {solidCount}: {colliderList}."
                    : victim == null
                        ? $"за 90 с на сервере не нашлось сессии с живым аватаром. " +
                          $"Сцена='{SceneManager.GetActiveScene().name}', сессий: {SessionCount()}. {DescribeSessions()}"
                        : $"у аватара '{victim.ActiveAvatar.name}' нет ни одного непроходного коллайдера — " +
                          $"попасть в него нельзя ни при какой маске. Это НЕ находка T-28, а отдельный дефект " +
                          $"состава префаба скина. Обзор скинов — в проверке «{CheckSkins}».");

            if (!avatarOk)
            {
                result.Summary = "поражаемой жертвы нет, вердикт по T-28 вынести нельзя";
                yield break;
            }

            // ── Оружие ────────────────────────────────────────────────────
            UxrFirearmWeapon firearm = null;
            NetworkIdentity item = null;

            deadline = Now + 60f;
            while (Now < deadline)
            {
                item = PickFirearm(out firearm);
                if (firearm != null) break;

                yield return null;
            }

            UxrProjectileSource source = firearm != null ? firearm.GetComponent<UxrProjectileSource>() : null;
            UxrShotDescriptor shot = source != null && source.ShotTypes.Count > 0 ? source.ShotTypes[0] : null;
            int ammo = firearm != null ? firearm.GetAmmoLeft(0) : 0;

            bool weaponOk = firearm != null && shot != null && shot.ShotSource != null && ammo > 0;
            result.Set(CheckWeapon, weaponOk,
                weaponOk
                    ? $"оружие '{item.name}' netId={item.netId}, {WeaponIdOf(item)}; патронов в магазине: {ammo}; " +
                      $"{DescribeSources(item.gameObject)}. " +
                      $"UxrFirearmWeapon берёт источник через GetCachedComponent<UxrProjectileSource>() — " +
                      $"это первый компонент нужного типа на объекте, и стреляет именно он."
                    : firearm == null
                        ? $"за 60 с на карте не нашлось заспавненного предмета с UxrFirearmWeapon. " +
                          $"Заспавненных предметов: {CountSpawnedItems()}."
                        : $"оружие '{item.name}' найдено, но стрелять им нельзя: описаний выстрела " +
                          $"{(source == null ? "нет компонента" : source.ShotTypes.Count.ToString())}, " +
                          $"ShotSource={(shot == null || shot.ShotSource == null ? "не задан" : "есть")}, патронов {ammo}. " +
                          $"Патроны берутся из магазина на UxrFirearmTrigger.AmmunitionMagAnchor: " +
                          $"без вставленного магазина TryToShootRound всегда возвращает false.");

            result.Set(CheckShotConf, true, SurveyRegistryWeapons(shot, ReferenceCollisionMask));

            if (!weaponOk)
            {
                result.Summary = "стрелять нечем, вердикт по T-28 вынести нельзя";
                yield break;
            }

            // ── Отмашка клиента ───────────────────────────────────────────
            // Клиент поднимает флаг, когда его аватар на месте: бить раньше нельзя,
            // иначе «здоровье не убыло у клиента» окажется про отсутствующий аватар.
            E2EWaitOutcome armed = new E2EWaitOutcome();
            yield return E2EWait.Until(armed,
                "клиент отчитался, что его аватар на месте",
                ArmedWait,
                () => ReportedFlags() >= context.ExpectedClients,
                () => $"отчитались {ReportedFlags()} из {context.ExpectedClients}; {DescribeSessions()}",
                () => NetworkServer.connections.Count > 0
                    ? null
                    : "на сервере не осталось подключений — стрелять уже не в кого");

            if (!armed.Succeeded)
            {
                result.Set(CheckAim, false, armed.Diagnosis);
                result.Summary = "клиент не подтвердил готовность, стрелять рано";
                yield break;
            }

            // ── Наводка ───────────────────────────────────────────────────
            PlayerController avatar = victim.ActiveAvatar;
            UxrActor victimActor = avatar._actor;

            DetachFromSlot(item.gameObject);

            // Точка прицеливания пересчитывается здесь, а не при поиске аватара:
            // между тем и этим прошли отмашка клиента и десятки кадров, за которые
            // клиент-авторитетный трансформ аватара успел уехать.
            Bounds body = SolidBounds(avatar, out solidCount, out colliderList);
            Vector3 aimPoint = body.center;

            bool aimed = TryAim(item.gameObject, shot, victimActor, aimPoint,
                                out Vector3 muzzle, out Vector3 direction, out float distance, out string aimReport);

            Physics.SyncTransforms();

            float probeLength = (distance > 0f ? distance : ShotDistances[0]) * 1.5f;
            string ownMaskProbe = Probe(shot.ShotSource.position, shot.ShotSource.forward,
                                        shot.CollisionLayerMask.value, victimActor, probeLength);
            string refMaskProbe = Probe(shot.ShotSource.position, shot.ShotSource.forward,
                                        ReferenceCollisionMask, victimActor, probeLength);

            result.Set(CheckAim, aimed,
                aimed
                    ? $"дуло в {muzzle}, направление {direction}, цель {aimPoint} (дистанция {distance:F1} м). {aimReport} " +
                      $"Контрольный луч эталонной маской 0x{ReferenceCollisionMask:X}: {refMaskProbe}. " +
                      $"Тот же луч маской самого оружия 0x{shot.CollisionLayerMask.value:X}: {ownMaskProbe}. " +
                      $"Расхождение этих двух строк и есть находка T-28 в чистом виде."
                    : $"навести оружие на жертву не удалось. {aimReport} " +
                      $"Целились в {aimPoint}, поражаемых коллайдеров у жертвы {solidCount}: {colliderList}. " +
                      $"Прогон недействителен: без гарантии, что путь свободен, «урона нет» ничего не доказывает.");

            if (!aimed)
            {
                result.Summary = "навести оружие не удалось, вердикт вынести нельзя";
                yield return WaitForClientVerdicts(context, PhaseNoDamage, victim);
                yield break;
            }

            // ── Выстрел ───────────────────────────────────────────────────
            float lifeBefore = avatar.Health;
            int fired = 0;
            List<string> attempts = new List<string>();

            for (int i = 0; i < ShotAttempts; i++)
            {
                // Позиция подтверждается перед каждым выстрелом: предмет сетевой,
                // и любой чужой подписчик мог сдвинуть его между кадрами.
                Hold(item.gameObject, shot, muzzle, direction);

                bool ok = firearm.TryToShootRound(0);
                if (ok) fired++;

                attempts.Add($"#{i}: {(ok ? "выстрел" : "отказ")} (патронов {firearm.GetAmmoLeft(0)}, Life {avatar.Health:F0})");

                // Снаряду хватает пары кадров: 2.5 м при 150 м/с — это 0.017 с,
                // но трассировка начинается только со второго кадра жизни снаряда
                // (первый UxrWeaponManager отдаёт под отрисовку у ствола).
                float until = Now + ShotInterval;
                while (Now < until)
                {
                    Hold(item.gameObject, shot, muzzle, direction);
                    yield return null;
                }

                if (avatar.Health < lifeBefore) break;
            }

            bool firedOk = fired > 0;
            result.Set(CheckFired, firedOk,
                firedOk
                    ? $"выстрелов принято: {fired} из {ShotAttempts}. {string.Join("; ", attempts.ToArray())}. " +
                      $"WeaponSystemEnabled={(UxrWeaponManager.HasInstance ? UxrWeaponManager.Instance.WeaponSystemEnabled.ToString() : "менеджера нет")}."
                    : $"ни один выстрел не принят. {string.Join("; ", attempts.ToArray())}. " +
                      $"TryToShootRound отказывает при CanUse=false (владелец мёртв или выключен WeaponSystemEnabled), " +
                      $"при пустом магазине и при непогашенном таймере темпа стрельбы. " +
                      $"WeaponSystemEnabled={(UxrWeaponManager.HasInstance ? UxrWeaponManager.Instance.WeaponSystemEnabled.ToString() : "менеджера нет")}.");

            E2EWaitOutcome damaged = new E2EWaitOutcome();
            yield return E2EWait.Until(damaged,
                "здоровье жертвы убыло на сервере",
                DamageWait,
                () => avatar == null || avatar.Health < lifeBefore,
                () => $"Life было {lifeBefore:F0}, стало {(avatar == null ? "аватар уничтожен" : avatar.Health.ToString("F0"))}; " +
                      $"живых снарядов в сцене: {CountProjectiles(shot)}",
                () => NetworkServer.connections.Count > 0 ? null : "на сервере не осталось подключений");

            bool damageOk = firedOk && damaged.Succeeded;
            result.Set(CheckDamage, damageOk,
                damageOk
                    ? $"{damaged.Diagnosis} Урон за попадание по описанию выстрела: " +
                      $"{shot.ProjectileDamageNear:F0}..{shot.ProjectileDamageFar:F0}. " +
                      $"Тракт целиком: UxrFirearmWeapon.TryToShootRound -> UxrProjectileSource.Shoot -> " +
                      $"RegisterNewProjectileShot -> UpdateProjectiles.Physics.Raycast -> UxrActor.ReceiveImpact -> Life."
                    : !firedOk
                        ? "выстрела не было, судить о попадании нечем — см. проверку про TryToShootRound"
                        : $"{damaged.Diagnosis} Выстрелов принято {fired}, а здоровье не тронуто. " +
                          $"Маска столкновений у стреляющего описания выстрела: 0x{shot.CollisionLayerMask.value:X}. " +
                          $"Нулевая маска означает, что Physics.Raycast в UxrWeaponManager.UpdateProjectiles " +
                          $"не проверяет ни одного слоя: попадание невозможно ни во что и никогда — это T-28. " +
                          $"Контрольный луч эталонной маской в ту же цель: {refMaskProbe}");

            // ── Контроль: тот же выстрел с эталонной маской ────────────────
            // Единственное отличие от проверки выше — подменённая на время опыта
            // маска слоёв у живого описания выстрела. Всё остальное то же самое:
            // то же оружие, тот же тракт, та же жертва, та же наводка. Поэтому
            // расхождение исходов не с чем спутать: причина ровно одна — маска.
            bool controlOk = false;

            if (firedOk)
            {
                E2EWaitOutcome control = new E2EWaitOutcome();
                yield return RunMaskControl(item.gameObject, firearm, shot, avatar, victimActor, control);

                controlOk = control.Succeeded;
                result.Set(CheckControl, controlOk,
                    controlOk
                        ? $"{control.Diagnosis} Маска на время опыта подменялась на эталонную 0x{ReferenceCollisionMask:X} " +
                          $"и возвращена обратно (0x{shot.CollisionLayerMask.value:X}). " +
                          $"Опыт отличался от проверки выше ровно одним полем, значит причина отсутствия урона — " +
                          $"именно CollisionLayerMask, а не тракт стрельбы, не коллайдеры жертвы и не наводка. " +
                          $"После правки префабов ту же зелёную строку обязана дать проверка «{CheckDamage}»."
                        : $"{control.Diagnosis} Урона нет даже с эталонной маской: значит дело не только в маске. " +
                          $"Смотреть надо на состав коллайдеров жертвы, на попадание снаряда в собственный ствол " +
                          $"и на то, симулируются ли снаряды на сервере вообще (WeaponSystemEnabled).");
            }
            else
            {
                result.Set(CheckControl, false,
                    "выстрела не было, ставить опыт не на чем — см. проверку про TryToShootRound");
            }

            yield return WaitForClientVerdicts(context, damageOk || controlOk ? PhaseDamaged : PhaseNoDamage, victim);

            result.Summary = result.AllChecksGreen
                ? $"выстрел из '{item.name}' отнял здоровье: {lifeBefore:F0} -> {avatar.Health:F0}"
                : damageOk
                    ? "урон прошёл, но есть другие красные проверки — см. detail"
                    : controlOk
                        ? "T-28 воспроизведена: выстрел состоялся, урона нет, а с эталонной маской тот же выстрел бьёт — " +
                          "причина ровно в CollisionLayerMask префабов"
                        : "T-28 воспроизведена: выстрел состоялся, урона нет — см. detail";
        }

        /// <summary>
        /// Опыт «а с правильной маской?»: подменяет маску слоёв у живого
        /// <see cref="UxrShotDescriptor" /> на эталонную, повторяет выстрел и возвращает
        /// маску обратно.
        ///
        /// <para>
        /// Почему рефлексией. Маска — <c>[SerializeField] private</c>, сеттера у неё нет,
        /// и это правильно: настройка живёт в префабе. Но опыт нужен именно на живом
        /// экземпляре в том же процессе — иначе «зелёное после правки» пришлось бы
        /// доказывать другой сборкой, другим прогоном и другой машиной, и сравнивать
        /// было бы нечего. Подмена держится несколько секунд и снимается в конце.
        /// </para>
        ///
        /// <para>
        /// Правку префаба это <b>не</b> заменяет: снаряд читает маску в момент
        /// выстрела (<c>ProjectileInfo.ShotLayerMask</c> копируется из дескриптора),
        /// поэтому опыт влияет только на снаряды, выпущенные внутри него.
        /// </para>
        /// </summary>
        private static IEnumerator RunMaskControl(GameObject weapon, UxrFirearmWeapon firearm, UxrShotDescriptor shot,
                                                  PlayerController avatar, UxrActor victimActor, E2EWaitOutcome outcome)
        {
            LayerMask savedCollision = shot.CollisionLayerMask;
            LayerMask savedDecal = shot.CreateDecalLayerMask;

            bool substituted = SetMask(shot, "_collisionLayerMask", ReferenceCollisionMask) &&
                               SetMask(shot, "_createDecalLayerMask", ReferenceCollisionMask);

            if (!substituted)
            {
                outcome.What = "подмена маски";
                outcome.Observed = "поля _collisionLayerMask / _createDecalLayerMask не нашлись рефлексией — " +
                                   "в SDK переименовали поле, опыт поставить нечем";
                yield break;
            }

            Bounds body = SolidBounds(avatar, out int _, out string _);
            bool aimed = TryAim(weapon, shot, victimActor, body.center,
                                out Vector3 muzzle, out Vector3 direction, out float _, out string aimReport);

            float lifeBefore = avatar.Health;
            int fired = 0;

            if (aimed)
            {
                for (int i = 0; i < ShotAttempts; i++)
                {
                    Hold(weapon, shot, muzzle, direction);

                    if (firearm.TryToShootRound(0)) fired++;

                    float until = Now + ShotInterval;
                    while (Now < until)
                    {
                        Hold(weapon, shot, muzzle, direction);
                        yield return null;
                    }

                    if (avatar.Health < lifeBefore) break;
                }
            }

            int shots = fired;
            yield return E2EWait.Until(outcome,
                "здоровье жертвы убыло от выстрела с эталонной маской",
                DamageWait,
                () => avatar == null || avatar.Health < lifeBefore,
                () => $"наводка: {aimReport} Выстрелов в опыте: {shots}. Life было {lifeBefore:F0}, " +
                      $"стало {(avatar == null ? "аватар уничтожен" : avatar.Health.ToString("F0"))}; " +
                      $"живых снарядов в сцене: {CountProjectiles(shot)}",
                () => NetworkServer.connections.Count > 0 ? null : "на сервере не осталось подключений");

            SetMask(shot, "_collisionLayerMask", savedCollision.value);
            SetMask(shot, "_createDecalLayerMask", savedDecal.value);
        }

        /// <summary>Пишет значение в приватное поле маски дескриптора выстрела.</summary>
        private static bool SetMask(UxrShotDescriptor shot, string fieldName, int value)
        {
            FieldInfo field = typeof(UxrShotDescriptor).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                return false;

            LayerMask mask = value;
            field.SetValue(shot, mask);
            return true;
        }

        /// <summary>
        /// Объявляет клиенту фазу и держит серверный процесс живым, пока тот не запишет
        /// вердикт. Гасить сервер сразу нельзя: обрыв связи посреди клиентского ожидания
        /// выглядит как «состояние не доехало» и даёт мигающие ворота.
        /// </summary>
        private static IEnumerator WaitForClientVerdicts(E2EContext context, int phase, PlayerSession victim)
        {
            BroadcastPhase(phase);

            E2EWaitOutcome barrier = new E2EWaitOutcome();
            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиент(а) отчитались, что записали вердикт",
                ClientVerdictWait,
                () => ReportedFlags() == 0 && SessionCount() >= context.ExpectedClients,
                () => $"фаза {phase}; держат флаг: {ReportedFlags()} из {SessionCount()} сессий; " +
                      $"подключений: {NetworkServer.connections.Count}; " +
                      $"жертва: Life={(victim != null && victim.ActiveAvatar != null ? victim.ActiveAvatar.Health.ToString("F0") : "нет аватара")}");

            GameLog.Debug.Info($"[E2E] Барьер клиентских вердиктов: {barrier.Diagnosis}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientAvatar, CheckClientHealth);

            DisableDebugOrchestrator();

            float deadline = Now + 30f;
            while (!NetworkClient.isConnected && Now < deadline)
                yield return null;

            if (!NetworkClient.isConnected && !string.IsNullOrEmpty(context.ServerAddress))
            {
                GameLog.Debug.Info($"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");

                Mirror.Discovery.NetworkDiscovery discovery = Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
                if (discovery != null)
                    discovery.StopDiscovery();

                if (NetworkManager.singleton != null && !NetworkClient.active)
                {
                    NetworkManager.singleton.networkAddress = context.ServerAddress;
                    NetworkManager.singleton.StartClient();
                }

                deadline = Now + 30f;
                while (!NetworkClient.isConnected && Now < deadline)
                    yield return null;
            }

            bool connected = NetworkClient.isConnected;
            result.Set(CheckClientConnected, connected,
                connected
                    ? $"подключён к {(NetworkManager.singleton != null ? NetworkManager.singleton.networkAddress : "?")}"
                    : $"за 60 с подключиться не удалось: ни Discovery, ни прямое подключение к '{context.ServerAddress}'");

            if (!connected)
            {
                result.Summary = "клиент не подключился";
                yield break;
            }

            deadline = Now + 60f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;

            PlayerSession local = PlayerSession.LocalSession;
            result.Set(CheckClientSession, local != null,
                local != null
                    ? $"PlayerSession.LocalSession netId={local.netId}"
                    : "за 60 с сервер не создал PlayerSession для этого клиента");

            if (local == null)
            {
                result.Summary = "сессии нет, отчитываться нечем";
                yield break;
            }

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            bool onMap = SceneManager.GetActiveScene().name == context.Map;
            result.Set(CheckClientMap, onMap,
                onMap
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}'"
                    : $"за 120 с клиент не переехал на '{context.Map}', остался в '{SceneManager.GetActiveScene().name}'");

            if (!onMap)
            {
                result.Summary = "клиент не на карте, наблюдать нечего";
                yield break;
            }

            deadline = Now + 90f;
            while (Now < deadline)
            {
                if (local.ActiveAvatar != null && local.ActiveAvatar.IsAlive)
                    break;

                yield return null;
            }

            PlayerController mine = local.ActiveAvatar;
            bool avatarOk = mine != null;
            result.Set(CheckClientAvatar, avatarOk,
                avatarOk
                    ? $"аватар '{mine.name}' netId={mine.netId}, Life={mine.Health:F0}"
                    : "за 90 с у клиента не появился свой аватар (PlayerSession.ActiveAvatar == null)");

            if (!avatarOk)
            {
                result.Summary = "у клиента нет аватара";
                ReportFlag(true);
                yield return E2EWait.Hold(1f);
                ReportFlag(false);
                yield break;
            }

            _clientHealthAtArm = mine.Health;
            _clientMinHealth = mine.Health;

            // Флаг поднимается только теперь: сервер стреляет сразу после него.
            ReportFlag(true);

            // ── Ждём вердикта сервера через фазу ──────────────────────────
            E2EWaitOutcome phase = new E2EWaitOutcome();
            yield return E2EWait.Until(phase,
                "сервер объявил, чем кончился выстрел",
                PhaseWait,
                () =>
                {
                    Sample(local);
                    return local.Score >= PhaseDamaged;
                },
                () => DescribeClient(local),
                () => NetworkClient.isConnected ? null : "связь с сервером пропала");

            if (!phase.Succeeded)
            {
                // Фазы нет — сервер не дошёл до выстрела. Проверка сформулирована
                // импликацией, поэтому наблюдать было нечего и красным это не делает.
                result.Set(CheckClientHealth, true,
                    phase.Diagnosis + " Сервер не объявил исход выстрела, поэтому клиенту нечего было проверять: " +
                    "вердикт по T-28 выносит серверная сторона.");
                result.Summary = "сервер не дошёл до выстрела — см. server.json";
                ReportFlag(false);
                yield break;
            }

            if (local.Score == PhaseNoDamage)
            {
                result.Set(CheckClientHealth, true,
                    $"сервер объявил, что урона не было (фаза {PhaseNoDamage}) — доставлять нечего. " +
                    $"Здоровье на клиенте: было {_clientHealthAtArm:F0}, минимум за прогон {_clientMinHealth:F0}. " +
                    $"Красным это не делает: находка T-28 живёт на сервере, здесь она только не опровергнута.");
                result.Summary = "урона не было — клиенту наблюдать нечего, вердикт в server.json";
                ReportFlag(false);
                yield break;
            }

            E2EWaitOutcome health = new E2EWaitOutcome();
            yield return E2EWait.Until(health,
                "убыль здоровья доехала до клиента",
                ClientHealthWait,
                () =>
                {
                    Sample(local);
                    return _clientMinHealth < _clientHealthAtArm;
                },
                () => DescribeClient(local),
                () => NetworkClient.isConnected ? null : "связь с сервером пропала");

            result.Set(CheckClientHealth, health.Succeeded,
                health.Succeeded
                    ? $"{health.Diagnosis} Здоровье на клиенте: {_clientHealthAtArm:F0} -> {_clientMinHealth:F0}. " +
                      "Канал состояния UltimateXR довёз UxrActor.Life до жертвы."
                    : $"{health.Diagnosis} Сервер засчитал урон, а клиент его не увидел: " +
                      "это уже не T-28, а обрыв канала состояния — сверься с avatar-swap-death-replication.");

            result.Summary = health.Succeeded
                ? $"клиент увидел убыль здоровья: {_clientHealthAtArm:F0} -> {_clientMinHealth:F0}"
                : "сервер засчитал урон, до клиента он не доехал — см. detail";

            ReportFlag(false);
        }

        private void Sample(PlayerSession local)
        {
            PlayerController mine = local != null ? local.ActiveAvatar : null;
            if (mine == null)
                return;

            if (mine.Health < _clientMinHealth)
                _clientMinHealth = mine.Health;
        }

        private string DescribeClient(PlayerSession local)
        {
            PlayerController mine = local != null ? local.ActiveAvatar : null;

            return $"фаза от сервера={(local == null ? "нет сессии" : local.Score.ToString())}, " +
                   $"Life сейчас={(mine == null ? "аватара нет" : mine.Health.ToString("F0"))}, " +
                   $"минимум за прогон={_clientMinHealth:F0}, было при готовности={_clientHealthAtArm:F0}, " +
                   $"связь={(NetworkClient.isConnected ? "есть" : "нет")}";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Наводка
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Снимает предмет со слота арсенала: пока он висит под якорем, любой сдвиг
        /// читается как «предмет всё ещё в слоте», а <c>Rigidbody</c> может уронить
        /// его на пол в момент отвязки.
        /// </summary>
        private static void DetachFromSlot(GameObject weapon)
        {
            // Только кинематика: detectCollisions выключать нельзя — он снял бы
            // коллайдеры оружия и с трассировки тоже, а тогда сценарий перестал бы
            // видеть, попадает ли ствол сам в себя (ShotSource у M16 лежит внутри
            // габаритов Rifle_Body_Mesh).
            Rigidbody body = weapon.GetComponent<Rigidbody>();
            if (body != null)
                body.isKinematic = true;

            UxrGrabbableObject grabbable = weapon.GetComponent<UxrGrabbableObject>();
            if (grabbable != null && grabbable.CurrentAnchor != null && UxrGrabManager.Instance != null)
                UxrGrabManager.Instance.RemoveObjectFromAnchor(grabbable, false);

            weapon.transform.SetParent(null, true);
        }

        /// <summary>
        /// Ставит оружие так, чтобы луч из его дула упирался в жертву.
        ///
        /// <para>
        /// Двигается именно оружие, а не жертва: трансформы аватаров клиент-авторитетные
        /// (<c>NetworkTransformUnreliable</c>, <c>syncDirection = ClientToServer</c>),
        /// и серверный сдвиг аватара был бы затёрт ближайшим снимком клиента.
        /// </para>
        ///
        /// <para>
        /// Направление подбирается перебором по кругу: арена тесная, и первый попавшийся
        /// луч может упереться в стену, в ящик или в саму стену арсенала. Годным считается
        /// направление, у которого точка дула не внутри геометрии, а первый же коллайдер
        /// на пути принадлежит жертве.
        /// </para>
        /// </summary>
        private static bool TryAim(GameObject weapon, UxrShotDescriptor shot, UxrActor victimActor,
                                   Vector3 aimPoint, out Vector3 muzzle, out Vector3 direction,
                                   out float distance, out string report)
        {
            StringBuilder tried = new StringBuilder();
            muzzle = Vector3.zero;
            direction = Vector3.forward;
            distance = 0f;
            int checkedCount = 0;

            // Оружие временно убирается в сторону: пока оно лежит рядом с жертвой,
            // оно само считается препятствием и портит замер свободного места.
            weapon.transform.position = aimPoint + Vector3.up * 100f;
            Physics.SyncTransforms();

            foreach (float elevation in AimElevations)
            {
                for (int i = 0; i < 12; i++)
                {
                    checkedCount++;

                    // Направление «от жертвы наружу»: стрелять будем обратно по нему.
                    Vector3 outward = Quaternion.Euler(-elevation, i * 30f, 0f) * Vector3.forward;

                    float free = FreeDistance(aimPoint, outward, victimActor, out string blocker);
                    float candidateDistance = Mathf.Min(ShotDistances[0], free - MuzzleClearance);

                    if (candidateDistance < ShotDistances[1])
                    {
                        tried.Append($" [{elevation:F0}°/{i * 30}° свободно всего {free:F2} м до '{blocker}']");
                        continue;
                    }

                    Vector3 candidateMuzzle = aimPoint + outward * candidateDistance;

                    if (Physics.CheckSphere(candidateMuzzle, 0.1f, ReferenceCollisionMask, QueryTriggerInteraction.Ignore))
                    {
                        tried.Append($" [{elevation:F0}°/{i * 30}° дуло в геометрии]");
                        continue;
                    }

                    Vector3 candidateDir = -outward;

                    if (!Physics.Raycast(candidateMuzzle, candidateDir, out RaycastHit hit, candidateDistance * 2f,
                                         ReferenceCollisionMask, QueryTriggerInteraction.Ignore))
                    {
                        tried.Append($" [{elevation:F0}°/{i * 30}° обратный луч ни во что не упёрся]");
                        continue;
                    }

                    if (hit.collider.GetComponentInParent<UxrActor>() != victimActor)
                    {
                        tried.Append($" [{elevation:F0}°/{i * 30}° обратным лучом первым '{hit.collider.name}']");
                        continue;
                    }

                    muzzle = candidateMuzzle;
                    direction = candidateDir;
                    distance = candidateDistance;
                    Place(weapon, shot, muzzle, direction);

                    report = $"выбрано: подъём {elevation:F0}°, азимут {i * 30}°, дистанция {candidateDistance:F2} м " +
                             $"(свободного места было {free:F2} м, проверено вариантов {checkedCount}).";
                    return true;
                }
            }

            report = $"ни один из {checkedCount} вариантов не подошёл.{tried}";
            return false;
        }

        /// <summary>
        /// Сколько свободного места вдоль <paramref name="outward" /> от точки внутри жертвы
        /// до первого чужого коллайдера. Коллайдеры самой жертвы отбрасываются: луч выходит
        /// изнутри её тела, и упереться в него значило бы «свободного места ноль».
        /// </summary>
        private static float FreeDistance(Vector3 origin, Vector3 outward, UxrActor victimActor, out string blocker)
        {
            blocker = "ничего";
            float best = ProbeReach;

            RaycastHit[] hits = Physics.RaycastAll(origin, outward, ProbeReach,
                                                   ReferenceCollisionMask, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<UxrActor>() == victimActor) continue;

                if (hit.distance < best)
                {
                    best = hit.distance;
                    blocker = hit.collider.name;
                }
            }

            return best;
        }

        /// <summary>
        /// Ставит оружие так, чтобы <c>ShotSource</c> оказался в <paramref name="muzzle" />
        /// и смотрел в <paramref name="direction" />. Поворот и сдвиг раздельно: сначала
        /// доворачиваем корень минимальным поворотом, потом компенсируем уехавшую точку дула.
        /// </summary>
        private static void Place(GameObject weapon, UxrShotDescriptor shot, Vector3 muzzle, Vector3 direction)
        {
            Transform root = weapon.transform;
            Transform src = shot.ShotSource;

            root.rotation = Quaternion.FromToRotation(src.forward, direction) * root.rotation;
            root.position += muzzle - src.position;

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Подтверждает положение оружия. Зовётся каждый кадр стрельбы: предмет сетевой,
        /// у него есть <c>Rigidbody</c> и подписчики UltimateXR, и «поставили и забыли»
        /// здесь не работает.
        /// </summary>
        private static void Hold(GameObject weapon, UxrShotDescriptor shot, Vector3 muzzle, Vector3 direction)
        {
            if (weapon == null || shot == null || shot.ShotSource == null)
                return;

            if (Vector3.Distance(shot.ShotSource.position, muzzle) > 0.01f ||
                Vector3.Angle(shot.ShotSource.forward, direction) > 0.5f)
            {
                Place(weapon, shot, muzzle, direction);
            }
        }

        /// <summary>Куда упирается луч из дула при заданной маске — человеческим языком.</summary>
        private static string Probe(Vector3 origin, Vector3 direction, int mask, UxrActor victimActor, float length)
        {
            if (mask == 0)
                return "маска нулевая, Physics.Raycast не проверяет ни одного слоя — попадания нет по построению";

            if (!Physics.Raycast(origin, direction, out RaycastHit hit, length, mask, QueryTriggerInteraction.Ignore))
                return $"луч не упёрся ни во что на {length:F1} м";

            UxrActor owner = hit.collider.GetComponentInParent<UxrActor>();

            return $"'{hit.collider.name}' на слое {LayerName(hit.collider.gameObject.layer)}, " +
                   $"дистанция {hit.distance:F2} м, актор={(owner == null ? "не актор" : owner.name)}" +
                   $"{(owner != null && owner == victimActor ? " — это жертва" : string.Empty)}";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Обзоры (измерительная часть)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Обходит скины всех команд режима и считает у каждого непроходные коллайдеры.
        /// Заодно выбирает жертве команду и индекс скина: попасть можно только в тот,
        /// у которого коллайдеры есть.
        /// </summary>
        private static string SurveySkins(SessionManager sessionManager, out TeamData victimTeam, out int victimAvatarIndex)
        {
            victimTeam = null;
            victimAvatarIndex = 0;

            GameModeData mode = sessionManager.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0)
                return "режим не отдал список команд — обзор скинов невозможен";

            StringBuilder builder = new StringBuilder();
            int hittable = 0;
            int total = 0;

            foreach (TeamData team in mode.teams)
            {
                if (team == null || team.avatars == null) continue;

                for (int i = 0; i < team.avatars.Count; i++)
                {
                    GameObject prefab = team.GetAvatarPrefab(i);
                    if (prefab == null) continue;

                    total++;
                    int solid = CountSolidColliders(prefab);
                    if (solid > 0)
                    {
                        hittable++;
                        if (victimTeam == null)
                        {
                            victimTeam = team;
                            victimAvatarIndex = i;
                        }
                    }

                    if (builder.Length > 0) builder.Append("; ");
                    builder.Append($"{team.displayName}[{i}] {prefab.name}: непроходных коллайдеров {solid}");
                }
            }

            if (victimTeam == null && mode.teams.Length > 0)
                victimTeam = mode.teams[0];

            return $"поражаемых скинов {hittable} из {total}. {builder}. " +
                   "Скин без непроходного коллайдера не поражается ни при какой маске: " +
                   "UxrWeaponManager.UpdateProjectiles трассирует с QueryTriggerInteraction.Ignore, " +
                   "поэтому триггеры (Camera на слое Player, GrabProxy, MagazinePocket) не считаются. " +
                   "Жертве назначается первый поражаемый скин, иначе этот дефект подменил бы вердикт по T-28.";
        }

        /// <summary>Снимает настройки выстрела со всех стволов реестра — без стрельбы.</summary>
        private static string SurveyRegistryWeapons(UxrShotDescriptor used, int referenceMask)
        {
            WeaponRegistry registry = WeaponRegistry.Instance;
            if (registry == null)
                return "WeaponRegistry не загрузился";

            StringBuilder builder = new StringBuilder();
            builder.Append($"эталонная маска для сверки — 0x{referenceMask:X} (Default|Ground). ");

            if (used != null)
            {
                builder.Append($"Стреляющее описание: маска 0x{used.CollisionLayerMask.value:X}, ")
                       .Append($"декали 0x{used.CreateDecalLayerMask.value:X}, ")
                       .Append($"скорость {used.ProjectileSpeed:F0} м/с, дальность {used.ProjectileMaxDistance:F0} м, ")
                       .Append($"время жизни {(used.ProjectileSpeed > 0f ? used.ProjectileMaxDistance / used.ProjectileSpeed : -1f):F2} с. ");
            }

            builder.Append("Реестр: ");

            for (int i = 0; i < registry.Weapons.Count; i++)
            {
                WeaponInfo info = registry.Weapons[i];
                if (info == null || info.WeaponPrefab == null) continue;

                if (i > 0) builder.Append("; ");
                builder.Append(info.WeaponId).Append(' ').Append(DescribeSources(info.WeaponPrefab));
            }

            return builder.ToString();
        }

        /// <summary>Состав источников снарядов на объекте — здесь всплывает дубль компонента.</summary>
        private static string DescribeSources(GameObject go)
        {
            UxrProjectileSource[] sources = go.GetComponentsInChildren<UxrProjectileSource>(true);
            StringBuilder builder = new StringBuilder();
            builder.Append("(UxrProjectileSource: ").Append(sources.Length);

            for (int i = 0; i < sources.Length; i++)
            {
                UxrShotDescriptor shot = sources[i].ShotTypes != null && sources[i].ShotTypes.Count > 0
                    ? sources[i].ShotTypes[0]
                    : null;

                builder.Append(", #").Append(i).Append(' ')
                       .Append(shot == null
                           ? "без описаний выстрела"
                           : $"ShotSource={(shot.ShotSource == null ? "нет" : "есть")}, " +
                             $"скорость={shot.ProjectileSpeed:F0}, дальность={shot.ProjectileMaxDistance:F0}, " +
                             $"маска=0x{shot.CollisionLayerMask.value:X}, декали=0x{shot.CreateDecalLayerMask.value:X}");
            }

            return builder.Append(')').ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        private static string LayerName(int layer)
        {
            string name = LayerMask.LayerToName(layer);
            return $"{layer}({(string.IsNullOrEmpty(name) ? "без имени" : name)})";
        }

        /// <summary>Непроходные коллайдеры: только они и участвуют в трассировке снаряда.</summary>
        private static int CountSolidColliders(GameObject prefabOrInstance)
        {
            int count = 0;
            foreach (Collider collider in prefabOrInstance.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger) continue;

                count++;
            }

            return count;
        }

        /// <summary>
        /// Объединённые границы поражаемых коллайдеров аватара — в их центр и целимся.
        ///
        /// <para>
        /// Границы, а не ссылка на «самый крупный коллайдер»: ссылка на отдельный
        /// коллайдер может умереть между обзором и наводкой, границы — нет.
        /// </para>
        /// </summary>
        private static Bounds SolidBounds(PlayerController avatar, out int count, out string list)
        {
            count = 0;
            Bounds bounds = new Bounds();
            StringBuilder builder = new StringBuilder();

            if (avatar == null)
            {
                list = "аватара нет";
                return bounds;
            }

            foreach (Collider collider in avatar.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger || !collider.enabled) continue;

                if (count == 0) bounds = collider.bounds;
                else bounds.Encapsulate(collider.bounds);

                count++;

                if (builder.Length > 0) builder.Append(", ");
                builder.Append(collider.name).Append(' ').Append(LayerName(collider.gameObject.layer));
            }

            list = builder.Length == 0 ? "ни одного" : builder.ToString();
            return bounds;
        }

        /// <summary>
        /// Заспавненное в рантайме оружие с <see cref="UxrFirearmWeapon" />.
        /// Предпочтение — M16: именно у него (и у Gun_real) маска нулевая, и именно
        /// он обязан покраснеть до правки T-28. Среди равных берётся наименьший
        /// <c>netId</c>: его раздаёт сервер, поэтому выбор воспроизводим от прогона к прогону.
        /// </summary>
        private static NetworkIdentity PickFirearm(out UxrFirearmWeapon firearm)
        {
            firearm = null;
            NetworkIdentity best = null;
            bool bestPreferred = false;

            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null || identity.sceneId != 0) continue;

                UxrFirearmWeapon candidate = identity.GetComponentInChildren<UxrFirearmWeapon>(true);
                if (candidate == null) continue;
                if (candidate.GetAmmoLeft(0) <= 0) continue;

                bool preferred = WeaponIdOf(identity) == "оружие M16";

                if (best == null ||
                    (preferred && !bestPreferred) ||
                    (preferred == bestPreferred && identity.netId < best.netId))
                {
                    best = identity;
                    firearm = candidate;
                    bestPreferred = preferred;
                }
            }

            return best;
        }

        private static string WeaponIdOf(NetworkIdentity identity)
        {
            WeaponComponent component = identity != null ? identity.GetComponent<WeaponComponent>() : null;
            return component != null && component.WeaponData != null
                ? "оружие " + component.WeaponData.WeaponId
                : "без WeaponComponent";
        }

        /// <summary>Живые снаряды в корне активной сцены — по имени инстанса префаба.</summary>
        private static int CountProjectiles(UxrShotDescriptor shot)
        {
            if (shot == null || shot.ProjectilePrefab == null)
                return 0;

            string instanceName = shot.ProjectilePrefab.name + "(Clone)";
            int count = 0;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root != null && root.name.StartsWith(instanceName, System.StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        private static int CountSpawnedItems()
        {
            int count = 0;
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity != null && identity.sceneId == 0) count++;
            }

            return count;
        }

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        private static PlayerSession FirstSessionWithAvatar()
        {
            if (PlayersManager.Instance == null)
                return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.ActiveAvatar != null && session.ActiveAvatar.IsAlive)
                    return session;
            }

            return null;
        }

        /// <summary>Раскладывает единственную сессию в выбранную команду и скин.</summary>
        private static string AssignVictim(TeamData team, int avatarIndex)
        {
            if (team == null || PlayersManager.Instance == null)
                return "команду назначить не удалось";

            List<string> report = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                session.TeamIndex = team.teamIndex;
                session.AvatarIndex = avatarIndex;
                report.Add($"{session.PlayerName}->{team.displayName}({team.teamIndex}), скин {avatarIndex}");
            }

            return string.Join(", ", report.ToArray());
        }

        private static string DescribeSessions()
        {
            if (PlayersManager.Instance == null)
                return "PlayersManager отсутствует";

            StringBuilder builder = new StringBuilder();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                if (builder.Length > 0) builder.Append(", ");
                builder.Append(session.PlayerName)
                       .Append(": аватар=")
                       .Append(session.ActiveAvatar == null
                           ? "нет"
                           : $"'{session.ActiveAvatar.name}' Life={session.ActiveAvatar.Health:F0}")
                       .Append(", флаг=")
                       .Append(session.HasGrabbedDogTag ? "поднят" : "опущен");
            }

            return builder.Length == 0 ? "сессий нет" : builder.ToString();
        }

        private static int ReportedFlags()
        {
            if (PlayersManager.Instance == null)
                return 0;

            int reported = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    reported++;
            }

            return reported;
        }

        /// <summary>
        /// Канал сервер → клиент: <c>PlayerSession.Score</c> — SyncVar, независимый
        /// от проверяемого канала состояния UltimateXR. Матч не запускается,
        /// игрового смысла у поля здесь нет.
        /// </summary>
        private static void BroadcastPhase(int phase)
        {
            if (PlayersManager.Instance == null)
                return;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null) session.Score = phase;
            }
        }

        /// <summary>
        /// Обратный канал клиент → сервер: <c>HasGrabbedDogTag</c> — единственное поле
        /// сессии, которое клиент вправе менять командой. Поднятый флаг значит
        /// «аватар на месте, можно стрелять», опущенный — «вердикт записан».
        /// </summary>
        private static void ReportFlag(bool raised)
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null)
            {
                GameLog.Debug.Info("[E2E] Отчитаться нечем: локальной сессии нет");
                return;
            }

            local.CmdSetDogTagGrabbed(raised);
        }

        private static void DisableDebugOrchestrator()
        {
            DebugOrchestrator orchestrator = Object.FindFirstObjectByType<DebugOrchestrator>();
            if (orchestrator == null || !orchestrator.enabled)
                return;

            orchestrator.enabled = false;
            GameLog.Debug.Info("[E2E] DebugOrchestrator отключён: дирижёром прогона выступает сценарий");
        }
    }
}
#endif
