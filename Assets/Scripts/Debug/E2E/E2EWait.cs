// Ярус C (два процесса) — общий помощник явного ожидания условий.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using UnityEngine;

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
    /// </summary>
    public static class E2EWait
    {
        /// <summary>
        /// Часы прогона. Именно <c>realtimeSinceStartup</c>: <c>Time.time</c>
        /// зависит от <c>timeScale</c> и встаёт на паузах загрузки сцены.
        /// </summary>
        public static float Now => Time.realtimeSinceStartup;

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
            outcome.AbortReason = string.Empty;

            while (true)
            {
                if (condition())
                {
                    outcome.Succeeded = true;
                    break;
                }

                if (abortIf != null)
                {
                    string reason = abortIf();
                    if (!string.IsNullOrEmpty(reason))
                    {
                        outcome.Aborted     = true;
                        outcome.AbortReason = reason;
                        break;
                    }
                }

                if (Now >= deadline)
                    break;

                yield return null;
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
            while (Now < until)
                yield return null;
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
}
#endif
