using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Память бота, которая переживает смену тела (T-48): тела (живое, призрак, новое после возрождения или смены
    /// скина) приходят и уходят, а бот — один. Хранит директор (<see cref="BotDirector"/>), по одной на сессию.
    /// </summary>
    public sealed class BotMind
    {
        /// <summary>Место на базе — индекс в <see cref="BotHomeSlot"/>, раздаёт директор по порядку ботов.</summary>
        public int HomeSlot;

        /// <summary>Директор управляет закупкой и вооружением; задаётся при добавлении бота.</summary>
        public bool AutoEquip = true;

        /// <summary>
        /// Комплект бот-компонентов текущего тела (<see cref="BotKit"/>): один раз на тело, ссылки без поиска в тике.
        /// Пишет только директор (по <c>AvatarSpawned</c>, повышение пассивного — в тике).
        /// </summary>
        public BotKitRefs Kit;

        /// <summary>Страховка тика (комплект собран не по событию) уже предупредила — второй раз не пишем.</summary>
        public bool KitFallbackWarned;

        /// <summary>Где стоял бот (ноги) и куда смотрел — для нового тела.</summary>
        public bool HasPlace;
        public Vector3 Feet;
        public float Yaw;

        /// <summary>Сцена, где запомнено место: после смены карты место не переносится.</summary>
        public int SceneHandle = -1;

        /// <summary>
        /// Кеш зоны спавна своей команды (место на базе): ищется по сцене только при смене сцены или команды, либо когда
        /// зона перестала принадлежать команде (смена сторон). Пишет директор.
        /// </summary>
        public TeamSpawnZone HomeZone;
        public TeamData HomeTeam;
        public int HomeScene = -1;
        /// <summary>Зоны нет — следующий поиск не раньше этого времени (без поиска по сцене в каждом тике).</summary>
        public float HomeRetryAt;

        private MapRunKey _shopRunKey;
        private ulong _shopModeEpoch;
        private PlayerController _shopBody;

        /// <summary>Закупка внутри запуска карты и эпохи режима (номер раунда; без раундов — тело). -1 — не покупал.</summary>
        public int ShopToken = -1;

        /// <summary>Купленное в этой закупке; null — воюет стартовым пистолетом.</summary>
        public WeaponInfo Purchase;

        /// <summary>Тело, которому директор уже предложил покупку (включая отказ стрелка).</summary>
        public PlayerController PurchaseOfferedBody;

        /// <summary>Закупка последнего предложения: одно предложение на пару (тело, закупка).</summary>
        public int PurchaseOfferedToken = -1;

        /// <summary>Решение о покупке в закупке <paramref name="token"/> принято.</summary>
        public bool ShoppedFor(int token) => ShopToken == token;

        /// <summary>
        /// Директор сменил контекст закупки: новый запуск карты, режим или жизнь без раундов — новое решение.
        /// В Elimination <paramref name="body"/> — null: смена тела в том же раунде не оплачивает покупку повторно.
        /// </summary>
        public void SyncPurchaseContext(MapRunKey runKey, ulong modeEpoch, PlayerController body)
        {
            if (_shopRunKey == runKey && _shopModeEpoch == modeEpoch && ReferenceEquals(_shopBody, body)) return;

            _shopRunKey = runKey;
            _shopModeEpoch = modeEpoch;
            _shopBody = body;
            ShopToken = -1;
            Purchase = null;
            PurchaseOfferedBody = null;
            PurchaseOfferedToken = -1;
        }

        public void Remember(BotBody body, int sceneHandle)
        {
            if (body == null) return;
            HasPlace = true;
            Feet = body.Feet;
            Yaw = body.Yaw;
            SceneHandle = sceneHandle;
        }
    }
}
