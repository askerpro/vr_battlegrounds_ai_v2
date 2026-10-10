# Инварианты манипуляций и вибрации

Нарушение любого пункта — архитектурный конфликт: не обходить, назвать и предложить решение (AGENTS.md).

## Вибрация

1. **Один писатель мотора.** Мотор пишет только `UnityXRHapticDevice` по команде `HapticMixer` внутри `HapticService`.
   Остальные — `HapticService.Play/PlayBoth/Begin` с `UxrHapticClip`. Сторож — `HapticOwnershipTests` (временные
   исключения — список `Pending` с владельцем задачи; сейчас `WeaponHapticOutput.cs` weapon-system до `sdk-routing`).
2. **Только своя рука.** Рука чужого аватара, бота или `null` — no-op (`HapticService.IsLocalHand`). Сетевых
   вибраций не бывает.
3. **Клип без формы — тишина.** Форма — общий ассет `Assets/Data/Haptics/Waveforms/`; клип ссылается только на формы
   проекта (`HapticWaveformTests`). Импульс короче 30 мс на LRA Quest 2 не ощущается.
4. **Приоритеты:** Low < Normal < High < Critical. Слышен высший; `Normal` смешивается по максимуму; разовый поверх
   непрерывного; непрерывный `Low` под высшим — на паузе; пауза повтора не занимает мотор; кулдаун — на (клип, рука).
   Поведение закреплено `HapticMixerTests`.
5. **Вторая рука** на том же предмете — роль `Secondary`, сила × `SecondaryHandGain` клипа.

## Отклик взаимодействий

6. **Один исполнитель** `InteractionFeedback` (ставит себя сам, `DontDestroyOnLoad`, не в batch mode). Второй
   «читатель близости» или опрос якорей не добавлять.
7. **Готовность — ответ SDK, тот же, что решит grip:** пустая рука — `UxrGrabManager.TryGetGrabCandidate` (патч 67),
   рука с предметом — `AnchorPlacementReadiness` поверх `GetAnchorPlacementCandidate`. Своих расстояний нет.
8. **Самое частное побеждает:** `InteractionFeedbackOverride` предмета → якоря → `InteractionFeedbackConfig`.
   Пустое поле — «как дальше по цепочке»; пустой конфиг — без отклика.
9. **Рука задаётся до включения.** Экземпляр отклика лежит под неактивным пулом; `HapticPlayer.Bind(side, role)`
   вызывается до переноса к цели, иначе `OnEnable` сыграет без руки (тишина) или не той рукой.
10. **Готовность локальна; сетевыми бывают только события.** Для чужого события включаются только дочерние слоты с
    `NetworkedFeedback`; префаб без них для чужого события не создаётся.
11. **Покрытие:** каждый якорь и корневой хватаемый предмет игры имеет отклик готовности или исключение с причиной
    (`ManipulationFeedbackCoverageTests`, сейчас исключений нет).

## SDK

12. **Патч 26 не откатывать:** проход «рука рядом с якорем» выключен (upstream пишет `GrabberNear = null`, поля
    `Activate On Hand Near And Grabbable` / `PlacedObjectRange*` мертвы). Отключение дало прирост производительности.
13. **Патчи 16 и 18:** подсказки хвата (`UpdateAffordances`) считаются только для рук локального аватара
    (`IsLocalAffordanceGrabber`, патч 16) — поэтому и кандидат хвата патча 67 есть только у своих рук. Подсказка
    «можно положить в якорь» — только для предмета в своей руке (`HasLocalAffordanceGrab`, патч 18).
14. **Патч 67:** три вызова в первом проходе `UpdateAffordances` + partial `UxrGrabManager.GrabCandidates.cs`. Новых
    расчётов нет, только сохранение результата. Чужие правки `UxrGrabManager.cs` обязаны их сохранить (условие ACK
    контракта `weapon-grab-lifetime@1`).
15. **Сетевые действия — один автор:** код из Update/таймера/физики/RPC, вызывающий синхронизируемый метод
    (`ReleaseObject`, `IsGrabbable`, `Shoot`…), проверяет `StateEventAuthority.IsAuthorOfItem`/`IsWorldAuthority`.
