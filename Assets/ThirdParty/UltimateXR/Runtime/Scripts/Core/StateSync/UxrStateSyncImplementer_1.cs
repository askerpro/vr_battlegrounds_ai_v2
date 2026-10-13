// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrStateSyncImplementer_1.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UltimateXR.Core.Components;
using UltimateXR.Core.Settings;
using UltimateXR.Extensions.Unity;
using UnityEngine;

namespace UltimateXR.Core.StateSync
{
    /// <summary>
    ///     Helper class simplifying the implementation of the <see cref="IUxrStateSync" /> interface.
    ///     This class includes functionality for automatic synchronization of property changes and method calls
    ///     through reflection, using a convenient BeginSync/EndSync pattern.
    ///     It is utilized by <see cref="UxrComponent" /> to implement <see cref="IUxrStateSync" />.
    ///     In scenarios where custom classes cannot inherit from <see cref="UxrComponent" /> for automatic sync capabilities,
    ///     this class is designed to implement the interface.
    /// </summary>
    public class UxrStateSyncImplementer<T> : UxrStateSyncImplementer where T : Component, IUxrStateSync
    {
        #region Constructors & Finalizer

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="targetComponent">Target component for all the methods called on this object</param>
        public UxrStateSyncImplementer(T targetComponent)
        {
            _targetComponent = targetComponent;
        }

        /// <summary>
        ///     Default constructor is private to use public constructor with target component.
        /// </summary>
        private UxrStateSyncImplementer()
        {
        }

        #endregion

        #region Protected Overrides UxrStateSyncImplementer

        /// <summary>
        ///     VR Battlegrounds patch 74: владелец кадра в логах утечки — путь компонента в сцене.
        /// </summary>
        protected override string FrameOwnerDescription => _targetComponent != null ? _targetComponent.GetPathUnderScene() : $"(destroyed {typeof(T).Name})";

        #endregion

        #region Public Methods

        /// <summary>
        ///     Executes the state change described by <see cref="e" /> on the component.<br />
        ///     First it will check if they are built-in state sync events such as property change (
        ///     <see cref="UxrPropertyChangedSyncEventArgs" />, using <see cref="BeginSync" />/<see cref="EndSyncProperty" />) and
        ///     method call (<see cref="UxrMethodInvokedSyncEventArgs" />, using <see cref="BeginSync" />/
        ///     <see cref="EndSyncMethod" />).
        ///     If it's a different, custom event, it will be handled using the provided
        ///     <paramref name="fallbackSyncStateHandler" />.
        /// </summary>
        /// <param name="e">State change</param>
        /// <param name="fallbackSyncStateHandler">Fallback event handler</param>
        public void SyncState(UxrSyncEventArgs e, Action<UxrSyncEventArgs> fallbackSyncStateHandler)
        {
            // First check if it's a synchronization that can be solved at the base level

            if (e is UxrPropertyChangedSyncEventArgs propertyChangedEventArgs)
            {
                try
                {
                    // Set new property value using reflection
                    // VR Battlegrounds patch 80: PropertyInfo из кеша (тип компонента, имя) вместо GetProperty на каждое событие.
                    // Не найдено — null не кешируется, SetValue на null даёт тот же NullReferenceException.
                    GetCachedProperty(_targetComponent.GetType(), propertyChangedEventArgs.PropertyName).SetValue(_targetComponent, propertyChangedEventArgs.Value);
                }
                catch (Exception exception)
                {
                    if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
                    {
                        Debug.LogError($"{UxrConstants.CoreModule} Error trying to sync property {propertyChangedEventArgs.PropertyName} to value {propertyChangedEventArgs.Value} . Component: {_targetComponent.GetPathUnderScene()}. Exception: {exception}");
                    }
                }
            }
            else if (e is UxrMethodInvokedSyncEventArgs methodInvokedEventArgs)
            {
                try
                {
                    // VR Battlegrounds patch 80: выбор метода через кеш MethodEntry (тип компонента, имя) — те же
                    // вызовы рефлексии и тот же порядок веток, но каждый найденный MethodInfo ищется один раз.
                    // Отрицательные результаты (null, исключения) не кешируются и повторяются как раньше.
                    Type        componentType = _targetComponent.GetType();
                    string      methodName    = methodInvokedEventArgs.MethodName;
                    object[]    parameters    = methodInvokedEventArgs.Parameters;
                    MethodEntry methodEntry   = GetMethodEntry(componentType, methodName);

                    if (parameters == null || parameters.Length == 0)
                    {
                        // Invoke without arguments
                        methodEntry.GetByName().Invoke(_targetComponent, null);
                    }
                    else
                    {
                        // Invoke method using same parameters using reflection. Make sure we select the correct overload.

                        bool anyIsNull = AnyIsNull(parameters);

                        if (methodEntry.GetCount() == 1)
                        {
                            // There are no overloads
                            methodEntry.GetByName().Invoke(_targetComponent, parameters);
                        }
                        else if (!anyIsNull)
                        {
                            // We can look for a method specifying the parameter types.
                            methodEntry.GetByArgumentTypes(parameters).Invoke(_targetComponent, parameters);
                        }
                        else
                        {
                            // We have a call where a parameter is null, so we can't infer the parameter type. Try to find a method with the same parameter count.

                            MethodInfo method = methodEntry.GetByParameterCount(parameters.Length);

                            if (method != null)
                            {
                                method.Invoke(_targetComponent, parameters);
                            }
                            else
                            {
                                throw new Exception("Could not find a method with the given name and parameter count");
                            }
                        }
                    }
                }
                catch (AmbiguousMatchException ambiguousMatchException)
                {
                    if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
                    {
                        Debug.LogError($"{UxrConstants.CoreModule} Trying to sync a method that has ambiguous call. {e}. Component: {_targetComponent.GetPathUnderScene()}. Exception: {ambiguousMatchException}");
                    }
                }
                catch (Exception exception)
                {
                    if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
                    {
                        Debug.LogError($"{UxrConstants.CoreModule} Error trying to sync method. It could be that an exception inside the method was thrown, that {nameof(EndSyncMethod)} was used with the wrong parameters or it has an overload that could not be resolved. {e}. Component: {_targetComponent.GetPathUnderScene()}. Exception: {exception}");
                    }
                }
            }
            else
            {
                // It's a synchronization using a custom UxrSyncEventArgs object. Pass it to the fallback handler.

                try
                {
                    fallbackSyncStateHandler?.Invoke(e);
                }
                catch (Exception exception)
                {
                    if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
                    {
                        Debug.LogError($"{UxrConstants.CoreModule} Error trying to sync state. {e}. Component: {_targetComponent.GetPathUnderScene()}. Exception: {exception}");
                    }
                }
            }
        }

        /// <summary>
        ///     <para>
        ///         Starts a synchronization block that will end with an EndSync method like <see cref="EndSyncProperty" />,
        ///         <see cref="EndSyncMethod" /> or <see cref="EndSyncState" />, which causes the
        ///         <see cref="IUxrStateSync.StateChanged" /> event to be triggered.
        ///     </para>
        ///     <para>
        ///         See <see cref="UxrComponent.BeginSync" />.
        ///     </para>
        /// </summary>
        /// <param name="options">Options. It's saved/used in all environments by default.</param>
        public void BeginSync(UxrStateSyncOptions options = UxrStateSyncOptions.Default)
        {
            // VR Battlegrounds patch 74: кадр в общей стопке вместо счётчика и локального стека опций.
            PushFrame(options);

            if (SyncCallDepth > StateSyncCallDepthErrorThreshold)
            {
                if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
                {
                    Debug.LogError($"{UxrConstants.CoreModule} BeginSync/EndSync mismatch when calling BeginSync. Did you forget an EndSync call? Component: {_targetComponent.GetPathUnderScene()}");
                }
            }
        }

        /// <summary>
        ///     <para>
        ///         Cancels a <see cref="BeginSync" /> to escape when a condition is found that makes it not require to sync.
        ///     </para>
        ///     <para>
        ///         See <see cref="UxrComponent.CancelSync" />.
        ///     </para>
        /// </summary>
        public void CancelSync()
        {
            // VR Battlegrounds patch 74: снимается только свой кадр; оставленные над ним — с ошибкой в логе.
            int index = TakeOwnFrame(nameof(CancelSync));

            if (index >= 0)
            {
                CloseOwnFrame(index, nameof(CancelSync));
            }
            else
            {
                if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
                {
                    Debug.LogError($"{UxrConstants.CoreModule} BeginSync/CancelSync mismatch when calling CancelSync. Did you forget a BeginSync call? State call depth is < 1. Component: {_targetComponent.GetPathUnderScene()}");
                }
            }
        }

        /// <summary>
        ///     <para>
        ///         Ends synchronization for a property change. It notifies that a property was changed in a component that
        ///         requires network/state synchronization, ensuring that the change is performed in all other clients too.
        ///         The synchronization should begin using <see cref="BeginSync" />.
        ///     </para>
        ///     <para>
        ///         See <see cref="UxrComponent.EndSyncProperty" />.
        ///     </para>
        /// </summary>
        /// <param name="raiseChangedEvent">Delegate that will call the <see cref="IUxrStateSync.StateChanged" /> event</param>
        /// <param name="value">New property value</param>
        /// <param name="propertyName">Property name</param>
        public void EndSyncProperty(Action<UxrSyncEventArgs> raiseChangedEvent, in object value, [CallerMemberName] string propertyName = null)
        {
            EndSyncState(raiseChangedEvent, new UxrPropertyChangedSyncEventArgs(propertyName, value));
        }

        /// <summary>
        ///     <para>
        ///         Ends synchronization for a method call. It notifies that a method was invoked in a component that requires
        ///         network/state synchronization, ensuring that the call is performed in all other clients too.
        ///         The synchronization should begin using <see cref="BeginSync" />.
        ///     </para>
        ///     <para>
        ///         See <see cref="UxrComponent.EndSyncMethod" />.
        ///     </para>
        /// </summary>
        public void EndSyncMethod(Action<UxrSyncEventArgs> raiseChangedEvent, object[] parameters = null, [CallerMemberName] string methodName = null)
        {
            EndSyncState(raiseChangedEvent, new UxrMethodInvokedSyncEventArgs(methodName, parameters));
        }

        /// <summary>
        ///     <para>
        ///         Ends a synchronization block for a custom event. The synchronization block should begin using
        ///         <see cref="BeginSync" />. The event ensures that the code is executed in all other receivers too.
        ///     </para>
        ///     <para>
        ///         See <see cref="UxrComponent.EndSyncState" />.
        ///     </para>
        /// </summary>
        /// <param name="raiseChangedEvent">
        ///     The delegate to call to raise the <see cref="IUxrStateSync.StateChanged" /> event. The
        ///     delegate will receive a <see cref="UxrSyncEventArgs" /> as parameter describing the state change.
        /// </param>
        /// <param name="e">The state change</param>
        public void EndSyncState(Action<UxrSyncEventArgs> raiseChangedEvent, UxrSyncEventArgs e)
        {
            // VR Battlegrounds patch 74: свой кадр из общей стопки; чужие кадры над ним — утечка, снимаются с ошибкой.
            int index = TakeOwnFrame(nameof(EndSyncState));

            if (index >= 0)
            {
                e.Options = FrameOptions(index);
                // VR Battlegrounds patch: событие остаётся внутри прежнего nesting depth,
                // но исключение подписчика не оставляет уже извлечённый scope открытым.
                try { raiseChangedEvent?.Invoke(e); }
                finally { CloseOwnFrame(index, nameof(EndSyncState)); }
            }
            else
            {
                if (UxrGlobalSettings.Instance.LogLevelCore >= UxrLogLevel.Errors)
                {
                    Debug.LogError($"{UxrConstants.CoreModule} BeginSync/EndSync mismatch when calling EndSync. Did you forget a BeginSync call? State call depth is < 1. Component: {_targetComponent.GetPathUnderScene()}");
                }
            }
        }

        /// <summary>
        ///     Registers the component if it hasn't been registered already.
        /// </summary>
        public void RegisterIfNecessary()
        {
            if (!Application.isPlaying) return;
            if (_retired) throw new InvalidOperationException("StateSync.Register.Retired");
            if (_registered) return;
            UxrManager manager = UxrManager.Instance;
            if (_retired) throw new InvalidOperationException("StateSync.Register.RetiredDuringManagerCreation");
            _registeredManager = manager;
            _registered = true;
            try
            {
                manager.RegisterStateSyncComponent<T>(_targetComponent);
                if (_retired)
                {
                    manager.UnregisterStateSyncComponent<T>(_targetComponent);
                    throw new InvalidOperationException("StateSync.Register.RetiredDuringSubscription");
                }
            }
            catch { Unregister(); throw; }
        }

        /// <summary>
        ///     Unregisters the component.
        /// </summary>
        public void Unregister()
        {
            _retired = true;
            if (!_registered) return;
            _registered = false;
            UxrManager manager = _registeredManager;
            _registeredManager = null;
            if (!ReferenceEquals(manager, null)) manager.UnregisterStateSyncComponent<T>(_targetComponent);
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     VR Battlegrounds patch 80: свойство для синхронизации из кеша. Тот же вызов
        ///     <c>GetProperty(name, PropertyFlags)</c>, что и раньше; в кеш попадает только найденное свойство,
        ///     null и исключения (ArgumentNullException, AmbiguousMatchException) не кешируются.
        /// </summary>
        private static PropertyInfo GetCachedProperty(Type componentType, string propertyName)
        {
            if (s_propertyCache.TryGetValue((componentType, propertyName), out PropertyInfo property))
            {
                return property;
            }

            property = componentType.GetProperty(propertyName, PropertyFlags);

            if (property != null)
            {
                s_propertyCache[(componentType, propertyName)] = property;
            }

            return property;
        }

        /// <summary>
        ///     VR Battlegrounds patch 80: запись кеша методов для пары (тип компонента, имя метода). Новая запись
        ///     попадает в словарь только вместе с первым положительным результатом (см. <see cref="MethodEntry" />):
        ///     имя приходит из сетевого события, и несуществующие имена не должны копить записи.
        /// </summary>
        private static MethodEntry GetMethodEntry(Type componentType, string methodName)
        {
            return s_methodCache.TryGetValue((componentType, methodName), out MethodEntry entry) ? entry : new MethodEntry(componentType, methodName);
        }

        /// <summary>
        ///     VR Battlegrounds patch 80: замена <c>Parameters.Any(p => p == null)</c> без замыкания и перечислителя.
        ///     Сравнение то же — ссылочное с null для object (уничтоженный UnityEngine.Object не считается null).
        /// </summary>
        private static bool AnyIsNull(object[] parameters)
        {
            for (int i = 0; i < parameters.Length; ++i)
            {
                if (parameters[i] == null)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Private Types & Data

        /// <summary>
        ///     VR Battlegrounds patch 80: кеш выбора метода для одной пары (тип компонента, имя метода).
        ///     Каждое поле повторяет ровно один прежний вызов рефлексии и заполняется только успешным результатом:
        ///     null и исключения не кешируются, следующий вызов повторит поиск и даст то же исключение.
        ///     Метаданные типа в рантайме неизменны, поэтому кешированный результат совпадает с повторным поиском.
        ///     Запись без единого положительного результата в словарь не попадает (<see cref="Remember" />).
        /// </summary>
        private sealed class MethodEntry
        {
            public MethodEntry(Type componentType, string methodName)
            {
                _componentType = componentType;
                _methodName    = methodName;
            }

            /// <summary>
            ///     Число методов с этим именем в <c>GetMethods(MethodFlags)</c>. Считается один раз;
            ///     0 не кешируется (любой вызов с этим именем всё равно закончится ошибкой).
            /// </summary>
            public int GetCount()
            {
                if (_count <= 0)
                {
                    _count = _componentType.GetMethods(MethodFlags).Count(m => m.Name.Equals(_methodName));

                    if (_count > 0)
                    {
                        Remember();
                    }
                }

                return _count;
            }

            /// <summary>
            ///     <c>GetMethod(name, MethodFlags)</c>. Прежний код вызывал его в двух ветках — без аргументов и при
            ///     единственном методе с этим именем; вызов один и тот же, поэтому и результат общий.
            ///     AmbiguousMatchException (перегрузки при вызове без аргументов) не кешируется.
            /// </summary>
            public MethodInfo GetByName()
            {
                if (_byName == null)
                {
                    _byName = _componentType.GetMethod(_methodName, MethodFlags);

                    if (_byName != null)
                    {
                        Remember();
                    }
                }

                return _byName;
            }

            /// <summary>
            ///     <c>GetMethod(name, MethodFlags, null, types, null)</c>, где types — точные рантайм-типы аргументов
            ///     (все не null). Результат стандартного binder зависит только от типа, имени и этих типов, поэтому
            ///     ключ — их последовательность. Поиск в кеше без выделения памяти; массив типов создаётся только
            ///     при промахе.
            /// </summary>
            public MethodInfo GetByArgumentTypes(object[] parameters)
            {
                for (int i = 0; i < _byArgumentTypes.Count; ++i)
                {
                    if (TypesMatch(_byArgumentTypes[i].Key, parameters))
                    {
                        return _byArgumentTypes[i].Value;
                    }
                }

                Type[] types = new Type[parameters.Length];

                for (int i = 0; i < parameters.Length; ++i)
                {
                    types[i] = parameters[i].GetType();
                }

                MethodInfo method = _componentType.GetMethod(_methodName, MethodFlags, null, types, null);

                if (method != null)
                {
                    _byArgumentTypes.Add(new KeyValuePair<Type[], MethodInfo>(types, method));
                    Remember();
                }

                return method;
            }

            /// <summary>
            ///     Первый в порядке <c>GetMethods(MethodFlags)</c> метод с этим именем и числом параметров — ветка, где
            ///     среди аргументов есть null. null (не найдено) не кешируется.
            /// </summary>
            public MethodInfo GetByParameterCount(int parameterCount)
            {
                if (_byParameterCount.TryGetValue(parameterCount, out MethodInfo method))
                {
                    return method;
                }

                method = _componentType.GetMethods(MethodFlags).FirstOrDefault(x => x.Name.Equals(_methodName) && x.GetParameters().Length == parameterCount);

                if (method != null)
                {
                    _byParameterCount[parameterCount] = method;
                    Remember();
                }

                return method;
            }

            /// <summary>
            ///     Кладёт запись в словарь при первом положительном результате; повторный вызов ничего не меняет.
            /// </summary>
            private void Remember()
            {
                if (!_remembered)
                {
                    _remembered                                   = true;
                    s_methodCache[(_componentType, _methodName)] = this;
                }
            }

            private static bool TypesMatch(Type[] types, object[] parameters)
            {
                if (types.Length != parameters.Length)
                {
                    return false;
                }

                for (int i = 0; i < types.Length; ++i)
                {
                    if (types[i] != parameters[i].GetType())
                    {
                        return false;
                    }
                }

                return true;
            }

            private readonly List<KeyValuePair<Type[], MethodInfo>> _byArgumentTypes  = new List<KeyValuePair<Type[], MethodInfo>>();
            private readonly Dictionary<int, MethodInfo>            _byParameterCount = new Dictionary<int, MethodInfo>();
            private readonly Type                                   _componentType;
            private readonly string                                 _methodName;
            private          int                                    _count;
            private          MethodInfo                             _byName;
            private          bool                                   _remembered;
        }

        // VR Battlegrounds patch 80: кеши рефлексии SyncState. Ключ — рантайм-тип компонента (T — лишь базовый тип)
        // и имя члена. Только main thread: SyncState вызывается из UxrManager на главном потоке Unity, поэтому
        // словари без lock. Перезагрузка домена (перекомпиляция) сбрасывает статические поля вместе с типами.
        private static readonly Dictionary<(Type, string), PropertyInfo> s_propertyCache = new Dictionary<(Type, string), PropertyInfo>();
        private static readonly Dictionary<(Type, string), MethodEntry>  s_methodCache   = new Dictionary<(Type, string), MethodEntry>();

        private const int          StateSyncCallDepthErrorThreshold = 100;
        private const BindingFlags EventFlags                       = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags MethodFlags                      = EventFlags | BindingFlags.InvokeMethod | BindingFlags.FlattenHierarchy;
        private const BindingFlags PropertyFlags                    = EventFlags | BindingFlags.SetProperty;

        private readonly T                          _targetComponent;
        private          bool                       _registered;
        private UxrManager _registeredManager;
        private bool _retired;

        #endregion
    }
}
