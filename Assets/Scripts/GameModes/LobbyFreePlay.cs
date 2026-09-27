using Mirror;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Правило сцены лобби: свободная игра. Арсенал всегда открыт и пополняется,
    /// оружие стреляет, жетона готовности нет, карман магазинов не пустеет — в нём
    /// всегда есть магазин к каждому оружию игрока.
    ///
    /// <para>
    /// <b>Почему не наследник <see cref="GameMode"/>.</b> Режим — это матч: его спавнит
    /// <c>GameplayManager</c> из выбранного администратором <see cref="GameModeData"/>,
    /// ему нужны две команды, счёт и победитель. В лобби нет ни <c>GameplayManager</c>
    /// (он живёт в сцене карты), ни матча, а выбор режима в лобби — это выбор
    /// <b>следующего</b> матча. Поэтому лобби описывает не режим, а правило сцены:
    /// компонент лежит в <c>Lobby.unity</c> и существует ровно столько, сколько сцена.
    /// </para>
    ///
    /// <para>
    /// <b>Кто что решает.</b> На картах доступность оружия ведёт <c>GameplayManager</c>
    /// по фазе раунда, а стену — фаза <see cref="EliminationMode"/>. В лобби их нет,
    /// и без этого правила стена стояла закрытой вечно, а <c>WeaponSystemEnabled</c>
    /// оставался таким, каким его бросил прошедший матч: <see cref="UxrWeaponManager"/>
    /// переживает смену сцены, и возврат в лобби из фазы закупки оставлял оружие мёртвым.
    /// </para>
    ///
    /// <para>
    /// Компонент не сетевой. Открытие стены и пополнение слотов делает только машина,
    /// владеющая состоянием стены (сервер или стена вне сети); клиенты получают
    /// результат репликацией. Оружие и жетон — локальное представление, их правило
    /// применяет на каждой машине само.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyFreePlay : MonoBehaviour
    {
        [Tooltip("Через сколько секунд после того, как слот опустел, на стене появляется новый предмет.")]
        [Min(0f)]
        [SerializeField] private float _replenishDelay = 2f;

        [Tooltip("Как часто сервер досыпает магазины в карманы игроков, секунды.")]
        [Min(0.05f)]
        [SerializeField] private float _magazineRefillInterval = 0.5f;

        private float _magazineTimer;

        private ArsenalWallController[] _walls = new ArsenalWallController[0];
        private float[] _emptyTime = new float[0];

        private void OnEnable()
        {
            _walls = FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _emptyTime = new float[_walls.Length];

            foreach (ArsenalWallController wall in _walls)
                wall.SetDogTagSuppressed(true);

            GameLog.Match.Info($"[LobbyFreePlay] Свободная игра: стен арсенала — {_walls.Length}.");

            Tick(0f);
        }

        private void OnDisable()
        {
            foreach (ArsenalWallController wall in _walls)
            {
                if (wall != null)
                    wall.SetDogTagSuppressed(false);
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Один шаг правила. Отдельно от <c>Update</c>, чтобы его прогонял EditMode-тест.
        /// </summary>
        internal void Tick(float deltaTime)
        {
            EnableWeapons();

            for (int i = 0; i < _walls.Length; i++)
            {
                ArsenalWallController wall = _walls[i];
                if (wall == null || !HasStateAuthority(wall)) continue;

                KeepOpen(wall);
                KeepStocked(wall, i, deltaTime);
            }

            KeepMagazinesStocked(deltaTime);
        }

        /// <summary>
        /// Бесконечный карман: сервер держит в кармане каждого игрока по магазину
        /// к каждому его оружию (в руках и в кобурах). Достал — через интервал лежит
        /// новый. Выдачу и её сетевую сторону ведёт <see cref="PlayerLoadoutManager"/>;
        /// правило решает только «сколько» и «когда».
        /// </summary>
        private void KeepMagazinesStocked(float deltaTime)
        {
            if (!NetworkServer.active) return;

            _magazineTimer += deltaTime;
            if (_magazineTimer < _magazineRefillInterval) return;
            _magazineTimer = 0f;

            foreach (PlayerLoadoutManager loadout in PlayerLoadoutManager.ServerInstances)
            {
                if (loadout != null)
                    loadout.ServerEnsureMagazines(1);
            }
        }

        private static void EnableWeapons()
        {
            if (!UxrWeaponManager.HasInstance) return;

            if (!UxrWeaponManager.Instance.WeaponSystemEnabled)
            {
                UxrWeaponManager.Instance.SetWeaponSystemEnabled(true);
                GameLog.WeaponSystem.Info("[LobbyFreePlay] Оружие включено: в лобби стрелять можно всегда.");
            }
        }

        /// <summary>
        /// Вправе ли эта машина вести стену. В сети — только сервер; клиентская стена
        /// до спавна тоже выглядит «вне сети», и открывать её там нельзя: состояние
        /// приедет с сервера.
        /// </summary>
        private static bool HasStateAuthority(ArsenalWallController wall)
        {
            bool networked = NetworkServer.active || NetworkClient.active;
            return networked ? wall.isServer : true;
        }

        private static void KeepOpen(ArsenalWallController wall)
        {
            ArsenalWallController.ArsenalState state = wall.CurrentState;

            if (state == ArsenalWallController.ArsenalState.Closed ||
                state == ArsenalWallController.ArsenalState.Closing)
            {
                wall.OpenArsenal(immediate: true);
            }
        }

        /// <summary>
        /// Пополняет стену, когда слот пустует дольше <see cref="_replenishDelay"/>.
        /// Задержка нужна, чтобы новый предмет не рождался в руке того, кто только что
        /// снял предыдущий. Выдача сетевая, поэтому вне сервера её нет.
        /// </summary>
        private void KeepStocked(ArsenalWallController wall, int index, float deltaTime)
        {
            if (!wall.isServer || !wall.HasEmptySlots())
            {
                _emptyTime[index] = 0f;
                return;
            }

            _emptyTime[index] += deltaTime;
            if (_emptyTime[index] < _replenishDelay) return;

            _emptyTime[index] = 0f;
            wall.ServerReplenishEmptySlots();
        }
    }
}
