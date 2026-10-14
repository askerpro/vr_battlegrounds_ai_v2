// Ярус C (два процесса) — общий помощник явного ожидания условий.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Итог одного ожидания. Хранит всё, что нужно для внятного вердикта:
    /// что ждали, сколько ждали, что видели в момент истечения.
    ///
    /// Отдельный объект нужен потому, что метод-итератор (<c>IEnumerator</c>)
    /// не может иметь <c>out</c>-параметра: результат некуда вернуть, кроме
    /// как в переданную наружу ссылку.
    /// </summary>
    public sealed class E2EWaitOutcome
    {
        /// <summary>Условие выполнилось до истечения срока.</summary>
        public bool Succeeded;

        /// <summary>Ожидание прервано досрочно: ждать дальше стало бессмысленно.</summary>
        public bool Aborted;

        /// <summary>
        /// Ожидание упало: условие или <c>abortIf</c> бросили исключение. Вместе с ним стоит <see cref="Aborted"/>,
        /// причина — в <see cref="AbortReason"/>.
        /// </summary>
        public bool Faulted;

        /// <summary>Сколько реально прождали, секунд.</summary>
        public float Elapsed;

        /// <summary>Отведённый срок, секунд.</summary>
        public float Timeout;

        /// <summary>Чего ждали — человеческим языком.</summary>
        public string What = string.Empty;

        /// <summary>Что наблюдали в момент выхода из ожидания.</summary>
        public string Observed = string.Empty;

        /// <summary>Почему ожидание прервано досрочно (пусто, если не прерывалось).</summary>
        public string AbortReason = string.Empty;

        /// <summary>
        /// Готовая диагностика неудачи — ровно те три вопроса, на которые обязан
        /// отвечать красный вердикт: что ждали, что получили, сколько ждали.
        /// </summary>
        public string Diagnosis
        {
            get
            {
                if (Succeeded)
                    return $"дождались «{What}» за {Elapsed:F2} с. {Observed}";

                if (Faulted)
                    return $"ждали «{What}», но ожидание упало на {Elapsed:F2} с: {AbortReason}. На этот момент: {Observed}";

                if (Aborted)
                    return $"ждали «{What}» до {Timeout:F0} с, но ожидание оборвалось на {Elapsed:F2} с: " +
                           $"{AbortReason}. На этот момент: {Observed}";

                return $"ждали «{What}» и не дождались за {Elapsed:F2} с (срок {Timeout:F0} с). " +
                       $"На момент истечения: {Observed}";
            }
        }
    }

    /// <summary>
    /// Ожидание условия для сценариев яруса C.
    ///
    /// <para>Зачем отдельный помощник. Сценарии писались с ручными циклами
    /// <c>while (!условие &amp;&amp; Now &lt; deadline) yield return null;</c>, и у такого цикла
    /// два врождённых изъяна. Первый: при истечении срока в вердикт попадает только
    /// заготовленная заранее строка, а фактическое состояние мира — нет, и красный
    /// прогон приходится расследовать по логам. Второй: цикл не отличает «условие
    /// не наступило» от «наблюдать стало нечем» — например, сервер погас и ответа
    /// уже физически не будет. Ровно на этом обжёгся TEST-01.</para>
    ///
    /// <para>Поэтому <see cref="Until"/> принимает две функции сверх условия:
    /// <c>observe</c> снимает состояние мира в момент выхода, <c>abortIf</c>
    /// отвечает на вопрос «есть ли ещё смысл ждать».</para>
    ///
    /// <para>Исключение внутри <c>condition</c> или <c>abortIf</c> — не обрыв прогона без вердикта, а именованный
    /// провал ожидания (<see cref="E2EWaitOutcome.Faulted"/>) и ошибка в журнале Unity. Общий таймаут сценария
    /// проверяет <see cref="E2ERunner"/> каждый кадр, в том числе внутри длинного ожидания: вложенные итераторы он
    /// прокручивает сам (<see cref="E2EScenarioStepper"/>), а в вердикт таймаута пишет текущее ожидание
    /// (<see cref="CurrentWait"/>).</para>
    /// </summary>
    public static class E2EWait
    {
        /// <summary>
        /// Часы прогона. Именно <c>realtimeSinceStartup</c>: <c>Time.time</c>
        /// зависит от <c>timeScale</c> и встаёт на паузах загрузки сцены.
        /// </summary>
        public static float Now => Time.realtimeSinceStartup;

        /// <summary>Что сценарий ждёт прямо сейчас (для вердикта общего таймаута); null — не ждёт.</summary>
        public static string CurrentWait { get; private set; }

        /// <summary>Когда началось текущее ожидание, по <see cref="Now"/>.</summary>
        public static float CurrentWaitStarted { get; private set; }

        /// <summary>
        /// Отмечает текущее ожидание для диагностики общего таймаута. Dispose снимает отметку, если она ещё своя.
        /// </summary>
        public static IDisposable Track(string what, float timeoutSeconds)
        {
            var mark = new WaitMark(what + $" (срок {timeoutSeconds:F0} с)");
            CurrentWait = mark.Text;
            CurrentWaitStarted = Now;
            return mark;
        }

        private sealed class WaitMark : IDisposable
        {
            public readonly string Text;
            public WaitMark(string text) => Text = text;
            public void Dispose()
            {
                if (ReferenceEquals(CurrentWait, Text)) CurrentWait = null;
            }
        }

        /// <summary>
        /// Ждёт выполнения <paramref name="condition"/> не дольше
        /// <paramref name="timeoutSeconds"/>, складывая итог в <paramref name="outcome"/>.
        /// </summary>
        /// <param name="outcome">Куда положить итог. Создаётся вызывающим.</param>
        /// <param name="what">Чего ждём — попадёт в вердикт как есть.</param>
        /// <param name="timeoutSeconds">Отведённый срок.</param>
        /// <param name="condition">Условие успеха. Опрашивается раз в кадр.</param>
        /// <param name="observe">
        ///     Снимок состояния мира для диагностики. Вызывается один раз — при выходе
        ///     из ожидания, — поэтому может быть сколь угодно подробным.
        /// </param>
        /// <param name="abortIf">
        ///     Причина прекратить ожидание досрочно, или <c>null</c>, если ждать
        ///     ещё имеет смысл. Опрашивается раз в кадр.
        /// </param>
        public static IEnumerator Until(E2EWaitOutcome outcome,
                                        string        what,
                                        float         timeoutSeconds,
                                        Func<bool>    condition,
                                        Func<string>  observe = null,
                                        Func<string>  abortIf = null)
        {
            float started  = Now;
            float deadline = started + timeoutSeconds;

            outcome.What        = what;
            outcome.Timeout     = timeoutSeconds;
            outcome.Succeeded   = false;
            outcome.Aborted     = false;
            outcome.Faulted     = false;
            outcome.AbortReason = string.Empty;

            using (Track($"«{what}»", timeoutSeconds))
            {
                while (true)
                {
                    string fault = null;
                    bool met = false;
                    try
                    {
                        met = condition();
                    }
                    catch (Exception e)
                    {
                        fault = $"условие бросило исключение {e.GetType().Name}: {e.Message}";
                        GameLog.Debug.Error($"[E2E] Ожидание «{what}»: {fault}\n{e}");
                    }

                    if (met)
                    {
                        outcome.Succeeded = true;
                        break;
                    }

                    if (fault == null && abortIf != null)
                    {
                        try
                        {
                            string reason = abortIf();
                            if (!string.IsNullOrEmpty(reason))
                            {
                                outcome.Aborted     = true;
                                outcome.AbortReason = reason;
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            fault = $"abortIf бросило исключение {e.GetType().Name}: {e.Message}";
                            GameLog.Debug.Error($"[E2E] Ожидание «{what}»: {fault}\n{e}");
                        }
                    }

                    if (fault != null)
                    {
                        outcome.Aborted     = true;
                        outcome.Faulted     = true;
                        outcome.AbortReason = fault;
                        break;
                    }

                    if (Now >= deadline)
                        break;

                    yield return null;
                }
            }

            outcome.Elapsed  = Now - started;
            outcome.Observed = Describe(observe);
        }

        /// <summary>
        /// Выдержка на заданное время. Нужна там, где условия нет: дать другой
        /// стороне записать свою проверку, дать фазе прожить хотя бы кадр.
        /// </summary>
        public static IEnumerator Hold(float seconds)
        {
            float until = Now + seconds;
            using (Track($"выдержка {seconds:F0} с", seconds))
            {
                while (Now < until)
                    yield return null;
            }
        }

        /// <summary>
        /// Снимает состояние мира, не давая исключению внутри снимка обрушить
        /// сценарий: диагностика не вправе быть причиной падения прогона.
        /// </summary>
        private static string Describe(Func<string> observe)
        {
            if (observe == null)
                return "(состояние не снималось)";

            try
            {
                string snapshot = observe();
                return string.IsNullOrEmpty(snapshot) ? "(состояние пустое)" : snapshot;
            }
            catch (Exception e)
            {
                return $"(снимок состояния упал: {e.GetType().Name}: {e.Message})";
            }
        }
    }

    /// <summary>
    /// Ручная прокрутка сценария вместе с вложенными итераторами (<c>yield return E2EWait.Until(...)</c>,
    /// <c>yield return E2EBarrier.WaitClients(...)</c>). Раньше вложенный итератор уходил Unity как вложенная корутина:
    /// исключение в нём не доходило до исполнителя, а общий таймаут не проверялся, пока ожидание не кончится.
    ///
    /// <para>Вложенный <see cref="IEnumerator"/> (в том числе <c>CustomYieldInstruction</c>) прокручивается здесь же;
    /// остальное (<c>null</c>, <c>WaitForSeconds</c>, <c>AsyncOperation</c>…) отдаётся Unity как есть. Исключение
    /// любого уровня выходит из <see cref="MoveNext"/> к вызывающему. <see cref="Dispose"/> закрывает все открытые
    /// итераторы сверху вниз — их <c>finally</c> выполняются и при обрыве прогона.</para>
    /// </summary>
    public sealed class E2EScenarioStepper : IDisposable
    {
        private const int MaxDepth = 64;
        private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();

        public E2EScenarioStepper(IEnumerator root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _stack.Push(root);
        }

        /// <summary>Глубина вложенности открытых итераторов (0 — сценарий закончен).</summary>
        public int Depth => _stack.Count;

        /// <summary>
        /// Один шаг: true — <paramref name="yielded"/> надо отдать Unity (кадр, ожидание движка); false — сценарий
        /// закончен. Закончившийся вложенный итератор сразу продолжает родителя в том же шаге.
        /// </summary>
        public bool MoveNext(out object yielded)
        {
            while (_stack.Count > 0)
            {
                IEnumerator top = _stack.Peek();
                if (!top.MoveNext())
                {
                    _stack.Pop();
                    continue;
                }

                object current = top.Current;
                if (current is IEnumerator nested)
                {
                    if (_stack.Count >= MaxDepth)
                        throw new InvalidOperationException($"вложенность итераторов сценария больше {MaxDepth}");
                    _stack.Push(nested);
                    continue;
                }

                yielded = current;
                return true;
            }

            yielded = null;
            return false;
        }

        /// <summary>Закрывает открытые итераторы (их <c>finally</c>); исключения при закрытии не пробрасываются.</summary>
        public void Dispose()
        {
            while (_stack.Count > 0)
            {
                IEnumerator top = _stack.Pop();
                try
                {
                    (top as IDisposable)?.Dispose();
                }
                catch (Exception e)
                {
                    GameLog.Debug.Warning($"[E2E] Закрытие итератора сценария: {e.GetType().Name}: {e.Message}");
                }
            }
        }
    }
}
#endif
