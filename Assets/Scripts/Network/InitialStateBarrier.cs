using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Клиентская половина протокола начального снимка состояния UltimateXR (<see cref="NetworkStateRelay"/>).
    /// Чистая логика без Mirror и Unity: решает, когда просить снимок, какой ответ применить и когда
    /// открыть канал инкрементов.
    ///
    /// <para>
    /// Снимок просится только для запуска карты, который локально готов
    /// (<c>MapBootstrap.IsLocallyReady(MapRunKey)</c>): тогда адресаты снимка — авторские станции, а после
    /// задачи 7 и сгенерированные — уже зарегистрированы. Сетевые объекты, созданные сервером до снимка
    /// (аватары, предметы), приходят раньше ответа: спавн и TargetRpc идут одним надёжным упорядоченным
    /// каналом. Ещё не созданный объект в снимке не упоминается и барьер не держит.
    /// </para>
    ///
    /// <para>
    /// Каждый запрос получает собственный номер, ответ несёт номер и <see cref="MapRunKey"/>. Ответ применяется,
    /// только если совпадают оба: ответ старого запуска (перезагрузка той же сцены, отмена, смена карты)
    /// не применяется и канал не открывает. Инкременты до открытия канала отбрасываются: снимок свежий,
    /// он снят сервером уже после них, а всё, что сервер разошлёт позже, придёт после ответа.
    /// </para>
    /// </summary>
    public sealed class InitialStateBarrier
    {
        private uint _lastRequest;

        /// <summary>Номер ожидаемого ответа; 0 — запроса нет.</summary>
        public uint PendingRequest { get; private set; }

        /// <summary>Запуск, для которого отправлен ожидаемый запрос.</summary>
        public MapRunKey PendingKey { get; private set; }

        /// <summary>Канал открыт: снимок применён, инкременты применяются.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Запуск, снимок которого применён. default — сцена без MapBootstrap.</summary>
        public MapRunKey OpenKey { get; private set; }

        /// <summary>Применять ли входящий инкремент.</summary>
        public bool AcceptsIncrements => IsOpen;

        /// <summary>Открыт ли канал именно для этого запуска.</summary>
        public bool IsOpenFor(MapRunKey key) => IsOpen && OpenKey == key;

        /// <summary>
        /// Смена сцены или потеря запуска: всё, что было, описывало прежнюю сцену. Канал закрыт,
        /// ответ на прежний запрос будет отброшен.
        /// </summary>
        public void Reset()
        {
            IsOpen = false;
            OpenKey = default;
            PendingRequest = 0;
            PendingKey = default;
        }

        /// <summary>
        /// Локальная готовность запуска изменилась. Возвращает номер запроса, который надо отправить, либо 0.
        /// </summary>
        /// <param name="key">Ключ локального запуска; default — сцена без MapBootstrap.</param>
        /// <param name="ready">Запуск локально готов и клиент может отправить команду.</param>
        public uint Evaluate(MapRunKey key, bool ready)
        {
            if (!ready)
            {
                // Запуск закрыт (Closing, отказ) — его ответ уже не нужен. Открытый канал живёт до смены сцены:
                // инкременты закрывающейся карты ещё описывают объекты, которые у клиента есть.
                PendingRequest = 0;
                PendingKey = default;
                return 0;
            }

            if (IsOpen && OpenKey == key) return 0;
            if (PendingRequest != 0 && PendingKey == key) return 0;

            return Request(key);
        }

        /// <summary>
        /// Клиенту понадобился новый снимок того же запуска (пересинхронизация боезапаса). Канал закрывается
        /// до ответа. Возвращает номер запроса либо 0, если канал не открыт — тогда свежий снимок и так придёт.
        /// </summary>
        public uint RequestResynchronization()
        {
            if (!IsOpen) return 0;
            return Request(OpenKey);
        }

        /// <summary>
        /// Ответ на запрос. true — применить снимок и открыть канал; false — ответ чужого запроса или запуска.
        /// </summary>
        public bool TryAcceptResponse(MapRunKey key, uint requestId)
        {
            if (requestId == 0 || requestId != PendingRequest || key != PendingKey) return false;

            PendingRequest = 0;
            PendingKey = default;
            IsOpen = true;
            OpenKey = key;
            return true;
        }

        /// <summary>
        /// Снимок, разосланный сервером без запроса (пересинхронизация боезапаса всем клиентам). Применяется
        /// только поверх уже открытого канала того же запуска; иначе свежий снимок придёт ответом на запрос.
        /// </summary>
        public bool AcceptsUnsolicited(MapRunKey key) => IsOpen && OpenKey == key && PendingRequest == 0;

        private uint Request(MapRunKey key)
        {
            IsOpen = false;
            OpenKey = default;
            _lastRequest = _lastRequest == uint.MaxValue ? 1u : _lastRequest + 1;
            PendingRequest = _lastRequest;
            PendingKey = key;
            return PendingRequest;
        }
    }
}
