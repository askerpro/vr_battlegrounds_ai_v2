using System;
using System.Collections.Generic;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    /// <summary>Кооперативная editor-задача. Один Evaluate/Bake неделим; бюджет ограничивает число оценок, не время внутри них.</summary>
    public interface IContactIkCooperativeJob : IDisposable
    {
        long Generation { get; }
        bool IsCompleted { get; }
        bool IsCancelled { get; }
        int Evaluations { get; }
        string Progress { get; }
        bool Step(int maxEvaluations);
        void Cancel();
    }

    public sealed class ContactIkCooperativeJob<TResult> : IContactIkCooperativeJob
    {
        private readonly IEnumerator<int> _steps;
        private Func<bool> _isCurrent;
        private Action _onCancel;
        private TResult _result;
        private bool _hasResult, _disposed;
        private Exception _failure;
        public long Generation { get; }
        public bool IsCompleted { get; private set; }
        public bool IsCancelled { get; private set; }
        public int Evaluations { get; private set; }
        public string Progress => IsCancelled ? "cancelled" : _failure != null ? "failed" : IsCompleted ? "completed" : "evaluations=" + Evaluations;
        public TResult Result => IsCompleted && !IsCancelled && _failure == null && _hasResult
            ? _result : throw new InvalidOperationException("Результат недоступен до завершения актуальной задачи.", _failure);

        internal ContactIkCooperativeJob(long generation, Func<Action<TResult>, IEnumerator<int>> create)
        {
            Generation = generation;
            _steps = create(value => { _result = value; _hasResult = true; });
        }

        internal void SetLifetime(Func<bool> isCurrent, Action onCancel)
        { _isCurrent = isCurrent; _onCancel = onCancel; }

        public bool Step(int maxEvaluations)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContactIkCooperativeJob<TResult>));
            if (maxEvaluations < 1) throw new ArgumentOutOfRangeException(nameof(maxEvaluations));
            if (IsCompleted || IsCancelled) return true;
            try
            {
                for (int i = 0; i < maxEvaluations; i++)
                {
                    if (_isCurrent != null && !_isCurrent()) { Cancel(); return true; }
                    if (!_steps.MoveNext())
                    {
                        if (!_hasResult) throw new InvalidOperationException("Алгоритм завершился без результата.");
                        IsCompleted = true; _steps.Dispose(); return true;
                    }
                    if (_steps.Current != 1) throw new InvalidOperationException("Шаг должен соответствовать одной фактической оценке.");
                    Evaluations++;
                }
                return false;
            }
            catch (Exception error)
            {
                _failure = error; IsCompleted = true;
                _steps.Dispose(); _onCancel?.Invoke();
                throw;
            }
        }

        public void Cancel()
        {
            if (IsCompleted || IsCancelled) return;
            IsCancelled = true; _steps.Dispose(); _onCancel?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Cancel(); _steps.Dispose(); _disposed = true;
        }
    }
}
