using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Единственный владелец экземпляров вылета (<see cref="WeaponEjecta"/>, этап ejection): пул с потолком, без выделения
    /// памяти на выстрел после прогрева. Экземпляры локальные — не сетевые: каждый клиент спавнит свои по сигналу машины,
    /// который приходит и автору, и наблюдателю (фиксация учёта SDK). Живут <see cref="WeaponEjectile.Lifetime"/> и
    /// возвращаются в пул; при переполнении первым уходит самый старый. Смена активной сцены (карты) убирает все.
    ///
    /// <para>
    /// Квота Quest: не больше <see cref="MaxActive"/> на сцене всего и <see cref="MaxPerPrefab"/> экземпляров одного префаба.
    /// Без графики (сервер без окна) вылета нет.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponEjectaPool : MonoBehaviour
    {
        public const int MaxActive = 40;
        public const int MaxPerPrefab = 24;

        private sealed class PrefabPool
        {
            public readonly Stack<WeaponEjecta> Free = new Stack<WeaponEjecta>(MaxPerPrefab);
            public int Created;
        }

        private static WeaponEjectaPool _instance;
        private static bool _headlessChecked, _headless;

        private readonly Dictionary<GameObject, PrefabPool> _pools = new Dictionary<GameObject, PrefabPool>(8);
        private readonly List<WeaponEjecta> _active = new List<WeaponEjecta>(MaxActive); // в порядке вылета: [0] — самый старый

        /// <summary>Сколько экземпляров на сцене сейчас (для проб).</summary>
        public static int ActiveCount => _instance != null ? _instance._active.Count : 0;

        /// <summary>
        /// Выпустить экземпляр префаба. <paramref name="ignore"/> — коллайдеры ствола, с которыми вылет не сталкивается
        /// (окно выброса внутри его коллайдеров). false — префаб негоден или графики нет.
        /// </summary>
        public static bool Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity,
            float lifetime, List<Collider> ignore)
        {
            if (prefab == null || !Application.isPlaying || IsHeadless()) return false;
            if (_instance == null)
            {
                var host = new GameObject("[WeaponEjecta]");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<WeaponEjectaPool>();
            }
            return _instance.SpawnInternal(prefab, position, rotation, velocity, angularVelocity, lifetime, ignore);
        }

        private static bool IsHeadless()
        {
            if (!_headlessChecked) { _headless = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null; _headlessChecked = true; }
            return _headless;
        }

        private void OnEnable() => SceneManager.activeSceneChanged += OnActiveSceneChanged;

        private void OnDisable() => SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnActiveSceneChanged(Scene from, Scene to) => ReleaseAll();

        private bool SpawnInternal(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity,
            float lifetime, List<Collider> ignore)
        {
            if (!_pools.TryGetValue(prefab, out PrefabPool pool))
            {
                if (prefab.GetComponent<WeaponEjecta>() == null || prefab.GetComponent<Rigidbody>() == null)
                {
                    GameLog.WeaponSystem.Error($"[WeaponEjecta] Префаб вылета {prefab.name} без WeaponEjecta/Rigidbody на корне — вылет пропущен.", prefab);
                    _pools[prefab] = null; // запомнить отказ: одна ошибка на префаб
                    return false;
                }
                pool = new PrefabPool();
                _pools[prefab] = pool;
            }
            if (pool == null) return false;

            if (_active.Count >= MaxActive) Release(0);
            WeaponEjecta item = Take(prefab, pool);
            if (item == null) return false;

            item.Source = prefab;
            item.ExpiresAt = Time.time + Mathf.Max(0.1f, lifetime);
            item.Rearm();
            item.transform.SetPositionAndRotation(position, rotation);
            item.gameObject.SetActive(true);
            Rigidbody body = item.Body;
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
            Collider shape = item.Shape;
            if (shape != null && ignore != null)
                for (int index = 0; index < ignore.Count; index++)
                {
                    Collider other = ignore[index];
                    if (other != null && other.enabled && other.gameObject.activeInHierarchy) Physics.IgnoreCollision(shape, other, true);
                }
            _active.Add(item);
            return true;
        }

        private WeaponEjecta Take(GameObject prefab, PrefabPool pool)
        {
            while (pool.Free.Count > 0)
            {
                WeaponEjecta free = pool.Free.Pop();
                if (free != null) return free;
                pool.Created--; // уничтожен извне
            }
            if (pool.Created < MaxPerPrefab)
            {
                GameObject created = Instantiate(prefab, transform);
                created.SetActive(false);
                pool.Created++;
                return created.GetComponent<WeaponEjecta>();
            }
            // Потолок префаба: забрать самый старый экземпляр этого же префаба.
            for (int index = 0; index < _active.Count; index++)
            {
                if (_active[index] == null || _active[index].Source != prefab) continue;
                Release(index);
                return pool.Free.Count > 0 ? pool.Free.Pop() : null;
            }
            return null;
        }

        private void Update()
        {
            float now = Time.time;
            for (int index = _active.Count - 1; index >= 0; index--)
                if (_active[index] == null || now >= _active[index].ExpiresAt) Release(index);
        }

        private void Release(int index)
        {
            WeaponEjecta item = _active[index];
            _active.RemoveAt(index);
            if (item == null) return;
            // IgnoreCollision с прежним стволом может пережить возврат в пул — это безвредно: вылет и так не толкает стволы.
            item.gameObject.SetActive(false);
            if (item.Source != null && _pools.TryGetValue(item.Source, out PrefabPool pool) && pool != null) pool.Free.Push(item);
            else Destroy(item.gameObject);
        }

        private void ReleaseAll()
        {
            for (int index = _active.Count - 1; index >= 0; index--) Release(index);
        }
    }
}
