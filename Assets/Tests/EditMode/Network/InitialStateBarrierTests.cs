using System;
using NUnit.Framework;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Барьер начального снимка UltimateXR (клиентская половина <see cref="NetworkStateRelay"/>).
    ///
    /// <para>
    /// Что доказывает. Снимок просится только после локальной готовности запуска карты; ответ применяется и
    /// открывает канал, только если совпадают номер запроса и ключ запуска; ответ старого запуска (перезагрузка,
    /// отмена, смена карты) отбрасывается и канал не открывает; инкременты до открытия канала не применяются;
    /// повторной готовности того же запуска не нужен второй запрос. Порядок «снимок раньше инкрементов» на
    /// проводе гарантирует надёжный упорядоченный канал Mirror — здесь проверяется решение клиента.
    /// </para>
    /// </summary>
    public class InitialStateBarrierTests
    {
        private static readonly Guid Session = Guid.NewGuid();
        private static MapRunKey Run(ulong sequence) => new MapRunKey(Session, sequence);

        [Test]
        public void Снимок_не_просится_до_локальной_готовности_запуска()
        {
            var barrier = new InitialStateBarrier();

            Assert.AreEqual(0u, barrier.Evaluate(Run(1), ready: false), "Запрос ушёл до готовности запуска.");
            Assert.IsFalse(barrier.AcceptsIncrements, "Канал открыт без снимка.");

            uint request = barrier.Evaluate(Run(1), ready: true);
            Assert.AreNotEqual(0u, request, "Готовый запуск не попросил снимок.");
            Assert.AreEqual(Run(1), barrier.PendingKey);
        }

        [Test]
        public void Повторная_готовность_того_же_запуска_не_шлёт_второй_запрос()
        {
            var barrier = new InitialStateBarrier();
            uint request = barrier.Evaluate(Run(1), true);

            Assert.AreEqual(0u, barrier.Evaluate(Run(1), true), "Ожидающий ответа запуск попросил снимок снова.");
            Assert.IsTrue(barrier.TryAcceptResponse(Run(1), request));
            Assert.AreEqual(0u, barrier.Evaluate(Run(1), true), "Открытый канал попросил снимок снова.");
        }

        [Test]
        public void Свежий_снимок_открывает_канал_и_только_потом_инкременты()
        {
            var barrier = new InitialStateBarrier();
            uint request = barrier.Evaluate(Run(1), true);

            Assert.IsFalse(barrier.AcceptsIncrements, "Инкремент до снимка был бы применён к чужому состоянию.");
            Assert.IsTrue(barrier.TryAcceptResponse(Run(1), request), "Ответ на текущий запрос отброшен.");
            Assert.IsTrue(barrier.AcceptsIncrements);
            Assert.IsTrue(barrier.IsOpenFor(Run(1)));
        }

        [Test]
        public void Ответ_старого_запуска_после_перезагрузки_не_открывает_канал()
        {
            var barrier = new InitialStateBarrier();
            uint old = barrier.Evaluate(Run(1), true);

            // Перезагрузка той же сцены: смена сцены у клиента, новый запуск готов.
            barrier.Reset();
            uint fresh = barrier.Evaluate(Run(2), true);

            Assert.IsFalse(barrier.TryAcceptResponse(Run(1), old), "Ответ старого запуска применён.");
            Assert.IsFalse(barrier.AcceptsIncrements, "Ответ старого запуска открыл канал.");
            Assert.IsFalse(barrier.TryAcceptResponse(Run(2), old), "Старый номер запроса принят с новым ключом.");
            Assert.IsTrue(barrier.TryAcceptResponse(Run(2), fresh));
        }

        [Test]
        public void Ответ_после_закрытия_запуска_не_применяется()
        {
            var barrier = new InitialStateBarrier();
            uint request = barrier.Evaluate(Run(1), true);

            // Сервер закрыл запуск (Closing): клиент больше не готов, ожидаемый ответ отменён.
            barrier.Evaluate(Run(1), ready: false);

            Assert.IsFalse(barrier.TryAcceptResponse(Run(1), request), "Ответ закрытого запуска применён.");
            Assert.IsFalse(barrier.AcceptsIncrements);
        }

        [Test]
        public void Открытый_канал_живёт_до_смены_сцены_и_закрывается_ею()
        {
            var barrier = new InitialStateBarrier();
            Assert.IsTrue(barrier.TryAcceptResponse(Run(1), barrier.Evaluate(Run(1), true)));

            barrier.Evaluate(Run(1), ready: false); // Closing: объекты сцены ещё живы, инкременты применимы
            Assert.IsTrue(barrier.AcceptsIncrements, "Closing закрыл канал раньше выгрузки сцены.");

            barrier.Reset(); // смена сцены
            Assert.IsFalse(barrier.AcceptsIncrements, "После смены сцены канал остался открыт.");
        }

        [Test]
        public void Разосланный_сервером_снимок_применяется_только_поверх_открытого_канала_того_же_запуска()
        {
            var barrier = new InitialStateBarrier();
            barrier.Evaluate(Run(1), true);
            Assert.IsFalse(barrier.AcceptsUnsolicited(Run(1)), "Пересинхронизация открыла бы канал в обход запроса.");

            Assert.IsTrue(barrier.TryAcceptResponse(Run(1), barrier.PendingRequest));
            Assert.IsTrue(barrier.AcceptsUnsolicited(Run(1)));
            Assert.IsFalse(barrier.AcceptsUnsolicited(Run(2)), "Пересинхронизация другого запуска принята.");
        }

        [Test]
        public void Пересинхронизация_клиента_закрывает_канал_до_свежего_ответа()
        {
            var barrier = new InitialStateBarrier();
            Assert.AreEqual(0u, barrier.RequestResynchronization(), "Пересинхронизация без открытого канала.");

            Assert.IsTrue(barrier.TryAcceptResponse(Run(1), barrier.Evaluate(Run(1), true)));
            uint request = barrier.RequestResynchronization();

            Assert.AreNotEqual(0u, request);
            Assert.IsFalse(barrier.AcceptsIncrements, "До свежего ответа инкременты применялись бы к старому снимку.");
            Assert.IsTrue(barrier.TryAcceptResponse(Run(1), request));
            Assert.IsTrue(barrier.IsOpenFor(Run(1)));
        }

        [Test]
        public void Сцена_без_запуска_карты_просит_снимок_с_пустым_ключом()
        {
            var barrier = new InitialStateBarrier();
            uint request = barrier.Evaluate(default, true);

            Assert.AreNotEqual(0u, request);
            Assert.IsFalse(barrier.TryAcceptResponse(Run(1), request), "Снимок запуска карты принят для сцены без запуска.");
            Assert.IsTrue(barrier.TryAcceptResponse(default, request));
        }
    }
}
