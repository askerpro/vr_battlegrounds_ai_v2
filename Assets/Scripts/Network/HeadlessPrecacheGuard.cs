using UltimateXR.Core;
using UltimateXR.Core.Unique;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Выключает прогрев UltimateXR (<see cref="UxrManager.UsePrecaching"/>) на машине без графики —
    /// выделенном сервере.
    ///
    /// <para>
    /// <b>Зачем.</b> Прогрев запускается, когда включается аватар в режиме <c>Local</c> и он один такой
    /// (<c>UxrManager.Avatar_Enabled</c>). На выделенном сервере своего аватара нет, а каждый входящий
    /// игрок и каждая кукла стресс-теста в момент <c>Instantiate</c> ещё <c>Local</c> (в <c>External</c> их
    /// переводит <c>NetworkServer.Spawn</c>) — прогрев перезапускался на каждом спавне. Каждый запуск
    /// заводит под <c>UxrManager</c> (DontDestroyOnLoad) копии «прогреваемых» префабов и обходит в том числе
    /// DDOL-сцену, а прошлые копии удаляются только в конце кадра: при нескольких спавнах в одном кадре
    /// копии копировались с удвоением — 23 → 4129 объектов, затем рост памяти ~150 МБ/с и зависание сервера
    /// (стресс-тест, фаза «по карте», 2026-09-29). Попутно прогрев затемнял экран через
    /// <c>UxrCameraFade</c>, а шейдеров на сервере нет — ошибка на каждый спавн.
    /// </para>
    ///
    /// <para>
    /// Прогревать шейдеры и пулы эффектов без рендера бессмысленно, поэтому на такой машине прогрев
    /// выключен целиком. На хосте и шлеме всё как было: там есть свой локальный аватар.
    /// </para>
    /// </summary>
    public static class HeadlessPrecacheGuard
    {
        /// <summary>Нужен ли прогрев UltimateXR этой машине.</summary>
        public static bool ShouldPrecache(bool isBatchMode, GraphicsDeviceType graphics)
        {
            return !isBatchMode && graphics != GraphicsDeviceType.Null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (ShouldPrecache(Application.isBatchMode, SystemInfo.graphicsDeviceType)) return;

            // UxrManager лежит в сценах (Lobby, карты): обращение к Instance до их загрузки создало бы
            // свой экземпляр вместо настроенного в сцене. Флаг ставим после загрузки каждой сцены — до
            // первого спавна аватара, который и запускает прогрев.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            int registered = RegisterDisabledUniqueIds(scene);

            if (!UxrManager.HasInstance || !UxrManager.Instance.UsePrecaching) return;

            UxrManager.Instance.UsePrecaching = false;
            GameLog.Network.Info($"[HeadlessPrecacheGuard] Машина без графики — прогрев UltimateXR выключен (сцена {scene.name}), " +
                                 $"выключенных компонентов с id зарегистрировано: {registered}.");
        }

        /// <summary>
        /// Машина без графики, состав карты собран (<c>MapBootstrap</c>): зарегистрировать выключенные компоненты с
        /// id ещё раз. <see cref="OnSceneLoaded"/> видел сцену до сборки — службы, созданные и перенесённые в неё
        /// позже, он не застал. Повтор безопасен: <c>RegisterIfNecessary</c> идемпотентна. На машине с графикой
        /// ничего не делает — там регистрацию выполняет прогрев SDK.
        /// </summary>
        public static int RegisterAfterComposition(Scene scene)
        {
            if (ShouldPrecache(Application.isBatchMode, SystemInfo.graphicsDeviceType)) return 0;
            return RegisterDisabledUniqueIds(scene);
        }

        /// <summary>
        /// Побочная работа прогрева, которую нельзя терять: <c>UxrManager.AddScenePrecachedInstances</c>
        /// регистрирует выключенные компоненты с <see cref="IUxrUniqueId"/> (в том числе на неактивных
        /// объектах, где <c>Awake</c> не вызывался), чтобы события синхронизации находили адресата. Без
        /// прогрева делаем это сами — то же условие, та же сцена.
        /// </summary>
        public static int RegisterDisabledUniqueIds(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            int count = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null && !behaviour.enabled && behaviour is IUxrUniqueId unique)
                    {
                        unique.RegisterIfNecessary();
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
