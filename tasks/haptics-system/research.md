# Исследование: единая система вибрации (хаптиков)

Дата: 2026-10-07. Статус: исследование, кода нет. Обозначения: **[факт]** — проверено в коде проекта/SDK
или в первоисточнике по ссылке; **[гипотеза]** — вывод или предположение, требует проверки в шлеме.

## 1. Выводы и рекомендация

1. **На Quest в нашем стеке у вибрации нет частоты, только амплитуда, длительность и ритм.** Стек — Oculus XR Plugin 4.5.4
   и Unity XR `InputDevice`. `SendHapticImpulse` не принимает частоту [факт]. Буфер (`supportsBuffer`) на Quest 2/3 через
   Unity XR, по отчётам сообщества, недоступен [гипотеза, высокая вероятность]. Поэтому частоты `UxrHapticClipType`
   (`RumbleFreq*`, свип у `Shot*`) и `AudioClip` в `UxrHapticClip` на шлеме не действуют.
2. **Режим `Mix` в UltimateXR на Quest не смешивает.** При одном канале новый сигнал всегда идёт в канал 0. OpenXR требует,
   чтобы новый сигнал прерывал текущий [факт: спецификация]. На деле побеждает последний вызов, и слабый сигнал обрывает сильный.
3. **Гипотеза ведущего подтверждается** с двумя поправками:
   - смешивание делать **программно**, через одного владельца мотора (исключительный приоритет, не сумма);
   - фильтровать **по локальной руке**, а не через `StateEventAuthority.IsAuthorOfItem`.
4. Три класса (отрицательный > физический > положительный), двойной импульс для отрицательного и пауза непрерывного
   положительного согласуются с Meta (ритм как носитель смысла, «белые паузы», избегать длинных наложений) и с моделью
   приоритетов Meta Haptics SDK [факт].
5. **Пакет из кеша «VR Haptic Patterns» (Scuttled Tech) не импортировать.** Он завязан на XR Interaction Toolkit 3, которого
   в проекте нет, смешивает сигналы суммой и содержит дефекты. Взять три идеи: кривая-паттерн в ScriptableObject,
   проигрывание по прогрессу (затвор, помпа), множитель для второй руки.
6. **Почему у кармана не чувствуется вибрация** (гипотезы по коду, по убыванию вероятности):
   - с 2026-09-29 карман даёт **один** импульс 0.5 × 0.3 с при входе в зону, а не непрерывный сигнал;
   - если игрок жмёт grip быстрее 0.3 с, импульс перебивает щелчок хвата магазина (`UxrManipulationHapticFeedback`, Click 0.8);
   - готовность «отдать» наступает только на границе досягаемости хвата.

   Диагностика — Verbose-лог `[PocketHaptics] … готов — вибрация`.
7. **Рекомендация:** сервис `HapticService` (единственный писатель `InputDevice`) и каталог-ассет сигналов.
   - SDK-вызовы перехватить одним патчем `UxrControllerInput`/`UxrUnityXRControllerInput`.
   - Состояние оружия (этап D) подаёт `WeaponHapticCue` в сервис.
   - Тест-сторож запрещает прямые вызовы вибрации вне сервиса.
   - Для игрока — выключатель и громкость (XAG 110). Пользователь отказался, см. п. 8.3.
   - Переход на Meta Haptics SDK (PCM, частота Quest 3) требует смены XR-плагина на OpenXR. Это отдельное решение,
     не сейчас.

## 2. Пакет из кеша Asset Store

Поиск: `%APPDATA%\Unity\Asset Store-5.x\**` и `%LOCALAPPDATA%\Unity\**`, маска `*haptic*`/`*vibrat*`. Ключа реестра
`HKCU\Software\Unity Technologies\Unity Editor 5.x` нет, переменной `ASSETSTORE_CACHE_PATH` нет, путь кеша стандартный.
Найден **один** пакет [факт]:
`Asset Store-5.x\Scuttled Tech\ScriptingInput - Output\VR Haptic Patterns.unitypackage` (44.8 МБ, 2025-03-14).
Распакован в scratchpad, в проект не импортировался, ничего из пакета не запускалось.

| Аспект | Что внутри [факт по исходникам] |
|---|---|
| Состав | ~25 КБ кода: `HapticPattern` (SO), `HapticPatternManager`, редактор, `AddDefineSymbols`. Остальное — демосцена (лайтмапы, скайбоксы, руки Oculus) |
| Зависимости | `com.unity.xr.interaction.toolkit` 3.0.1, `com.unity.xr.openxr` 1.10.0. API на `XRBaseInputInteractor`/`XRBaseInteractable` (XRI). В проекте XRI нет — без него пакет не скомпилируется |
| Формат клипа | `AnimationCurve` амплитуды 0..1 по времени; длительность — время последнего ключа. PCM/.haptic/AudioClip нет |
| Воспроизведение | `PlayOverTime(interactor)` — по времени. `PlayGradually(interactor, t01)` — по прогрессу 0..1, пик кривой на интервале кадра (для спуска, затвора). `secondaryHandStrengthMultiplier` — ослабление на второй руке |
| Менеджер | Статический. `async void Heartbeat()` с `Task.Delay(1)` в бесконечном цикле; каждый «кадр» складывает силы всех сигналов руки с `Clamp01` и шлёт `SendHapticImpulse(сумма, Time.deltaTime)` |
| Приоритеты, классы, громкость | Нет |
| Quest/OpenXR | Только через XRI → `SendHapticImpulse`, то есть одна амплитуда, как у нас |
| Лицензия | Отдельного файла нет — стандартный Asset Store EULA: использовать в продукте лицензиата можно, распространять исходники нельзя. Пакет скачан аккаунтом пользователя [гипотеза: куплен/получен им] |

Дефекты [факт по коду]:
- в `SchedulePatternVibrationsThisFrame` при окончании одного паттерна стоит `return` вместо `continue` — остальные
  паттерны теряют кадр;
- heartbeat не привязан к циклу кадров и никогда не останавливается: в редакторе он переживает выход из Play Mode
  [гипотеза: обращения к уничтоженным interactor];
- `_gradualPlaybackLastValue` хранится в общем ScriptableObject — паттерн общий для обеих рук и всех экземпляров;
- `AddDefineSymbols` молча правит define-символы PlayerSettings;
- редактор паттерна в `OnDisable` вызывает `AssetDatabase.SaveAssets`;
- `Debug.Log` (запрещён в проекте);
- сумма с `Clamp01` стирает рисунок: паузы двойного импульса заполняются чужим сигналом.

**Вердикт: не импортировать. Взять идеи:**
- паттерн как кривая или список сегментов в SO;
- `PlayGradually` — вибрация по прогрессу хода затвора или помпы (физический класс);
- множитель второй руки для отдачи при хвате двумя.

## 3. UltimateXR: как устроено

**Путь вызова [факт].**
- Аватар держит `UxrMetaTouchQuest3Input`, `UxrMetaTouchQuest2Input` и `UxrOculusTouchRiftInput`
  (`PlayerBase.prefab`, `Heavy_Soldier…`). Все три наследуют `UxrUnityXRControllerInput`.
- Отправка идёт через Unity XR `InputDevice.SendHapticBuffer`/`SendHapticImpulse`/`StopHaptics`.
- Чужие аватары (`UpdateExternally`) получают `UxrDummyControllerInput`, и вибрация через `hand.Avatar.ControllerInput`
  у них не доходит до мотора (`UxrAvatar.ControllerInput`).
- Зато `UxrAvatar.LocalAvatarInput.SendHapticFeedback(grabber.Side, …)` с чужим `grabber` дёрнет **свой** контроллер.

**API (`IUxrControllerInput`):**
- `SendHapticFeedback(side, UxrHapticClip)`;
- `SendHapticFeedback(side, frequency, amplitude, seconds, mode)` — «сырой»;
- `SendHapticFeedback(side, UxrHapticClipType, amplitude, seconds, mode)` — корутина на `UxrControllerInput`
  из 1–10 сырых вызовов;
- `SendGrabbableHapticFeedback`, `StopHapticFeedback`.

**`UxrHapticClip` [факт]:** `AudioClip _clip` + `_clipAmplitude`, `_hapticMode`, а также `_fallbackClipType`,
`_fallbackAmplitude`, `_fallbackDurationSeconds`.
- `AudioClip` превращается в байтовый буфер (по мотивам `OVRHaptics`) только при `supportsBuffer`.
- Иначе играет fallback. Во всех префабах проекта `_clip` пустой.

**`UxrHapticClipType` (корутина, `UxrControllerInput.cs:934`) [факт]:**

| Тип | Длительность по умолчанию | Форма |
|---|---|---|
| `RumbleFreq*` | 0.5 с | постоянная амплитуда; частоты 10/25/64/160/320 Гц — только в буфере |
| `Click` | 0.05 с | один импульс |
| `Shot` | 0.12 с | 0.06 с + 10 шагов (свип 180→64 Гц) |
| `ShotBig` | 0.25 с | 10 × 25 мс; амплитуда `a·clamp01(a·(2−2t))`: первая половина полная, затем спад |
| `ShotBigger` | 0.4 с | то же, 10 × 40 мс |
| `Slide` | 0.5 с | 10 × 50 мс, линейный спад |
| `Explosion` | 0.5 с | 10 × 50 мс, постоянная амплитуда |

**Mix / Replace (`UxrUnityXRControllerInput.cs:218–312`) [факт по коду].**
- `Replace` вызывает `StopHaptics()` и шлёт в канал 0.
- `Mix` шлёт в «следующий» канал `(prev+1) % numChannels`.
- При `numChannels = 1` это снова канал 0; Quest, вероятно, сообщает один канал [гипотеза].
- Если буфера нет — `SendHapticImpulse(channel, amplitude, seconds)`, частота отбрасывается.
- OpenXR: «If another haptic event … is currently happening on the device … the runtime **must interrupt** that other
  event and replace it with the new one» ([xrApplyHapticFeedback](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrApplyHapticFeedback.html)).
- Oculus XR Plugin 4.x работает поверх OpenXR-бэкенда OVRPlugin [факт: Meta, «OpenXR runtime the default backend»].

**Итог: на Quest `Mix` ≈ «последний побеждает», а сигналы из корутин (`Shot*`, `Slide`) перемежаются с любыми другими
вызовами** [гипотеза высокой вероятности; проверка — лог `TryGetHapticCapabilities` в шлеме].

**Событие `HapticRequesting`/`GlobalHapticRequesting`** поднимают только базовые реализации `UxrControllerInput`.
`UxrUnityXRControllerInput` переопределяет методы без `base.` — у наших контроллеров события нет [факт].
Подписаться на SDK как на поток запросов нельзя, нужен патч.

**Кто в SDK сам шлёт вибрацию [факт]:**

| Источник | Когда | Параметры в проекте | Как отключить или перенаправить |
|---|---|---|---|
| `UxrFirearmWeapon` (`:579`) | выстрел, у локального аватара; хват-рука + вторая, если держит тот же `TriggerGrabbable` | все 23 префаба: `ShotBig`, амплитуда 1, Mix, 0.25 с | данные: `ShotHapticClip.FallbackClipType = None`; или патч-перехват |
| `UxrShotgunPump` | ход помпы туда/обратно (FabarmSDASS) | пусто: Click 1.0; заряжен: Slide 0.15 | данные (клипы) или перехват |
| `UxrManipulationHapticFeedback` | хват/укладка/отпускание, непрерывно при движении | магазины AR15/BHP, гильзы и оружие Fabarm, Herrington: Grab Click 0.8, Place Click 0.4. **Tablet_Base: непрерывно 0.05–0.15 каждые 0.1 с** + Grab Click 1.0 | данные (компонент/поля) или перехват |
| `UxrGrabManager.Manipulation` (`:749`) | рука ушла далеко и хват сорван | Click 1.0, жёстко в коде | только перехват (патч) |
| `UxrPointerInputModule` | UI: нажатие Click 0.2, клик 0.6, перетаскивание — непрерывно, `UxrControlFeedback.HapticClip` | жёстко в коде | только перехват |
| `UxrHapticOnImpact`, `UxrFixedHapticFeedback`, `UxrGrabbableResizable` | — | в префабах и сценах не используются | — |

**Что патчить (номер не занят, см. `Docs/UltimateXR/sdk-patches.md`).** Одна точка перехвата в `UxrControllerInput`.
- Статический делегат-маршрутизатор: `SendHapticFeedback` (clip, clipType, raw) и `StopHapticFeedback` сначала
  отдаются игровому сервису. Если сервис принял запрос, SDK не трогает устройство.
- Так же — в переопределениях `UxrUnityXRControllerInput`, где фактически вызывается `InputDevice`.
- Сервис переводит запрос SDK в сигнал каталога: `Shot*`/`Explosion`/`Slide`/`Click` → физический класс;
  сырые непрерывные (манипуляция, перетаскивание UI) → положительный непрерывный.
- Альтернатива без патча — выключить SDK-вибрацию данными. Но Click 1.0 у `UxrGrabManager` и UI-клики зашиты
  в коде, поэтому патч всё равно нужен.

## 4. Железо и платформа

| | Quest 2 Touch | Quest 3/3S Touch Plus (и Pro) |
|---|---|---|
| Привод | узкополосный LRA: одна частота, управление амплитудой, «simple signals» | широкополосный VCM: частота и амплитуда, «sharp and precise clicks», до 500 Гц |
| Источник | [Meta: haptics technology](https://developers.meta.com/vr/design/haptics-technology/), [Unity guidelines](https://developers.meta.com/horizon/documentation/unity/unity-haptics-design-guidelines/) | там же |
| Что доступно **нам сейчас** | `SendHapticImpulse(channel, amplitude 0..1, duration)`. Частоты нет ([Unity API](https://docs.unity3d.com/ScriptReference/XR.InputDevice.SendHapticImpulse.html)) | то же. VCM играет импульс на частоте по умолчанию [гипотеза] |
| Буфер Unity XR | `supportsBuffer = false` по отчётам ([форум Meta](https://communityforums.atmeta.com/discussions/dev-unity/buffer-based-haptics-are-broken-for-quest-2/987513)) [гипотеза] | то же ([форум](https://communityforums.atmeta.com/discussions/dev-unity/haptic-buffer-no-longer-supported-on-quest-3-controllers/1244405)) [гипотеза] |

**OpenXR-расширения [факт по спецификации].** Оба недоступны через Oculus XR Plugin + Unity XR `InputDevice`
[гипотеза: нужен Unity OpenXR Plugin с Meta-фичами или Meta Haptics SDK].
- `XR_FB_haptic_pcm`: PCM-буфер с `sampleRate`, рантайм ресемплирует; `xrGetDeviceSampleRateFB`
  ([spec](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrHapticPcmVibrationFB.html)).
- `XR_FB_haptic_amplitude_envelope`: огибающая до 4000 сэмплов на заданную длительность
  ([spec](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrHapticAmplitudeEnvelopeVibrationFB.html)).

**Meta Haptics SDK** (`.haptic` из Haptics Studio, `HapticClipPlayer`) требует настройки проекта Meta XR и рекомендует
Unity OpenXR Plugin. Meta: «The Oculus XR Plugin is deprecated and scheduled for removal»
([get started](https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk-get-started/)).
На Quest 2 «users won't feel frequency changes». Пакетов `com.meta.*` в `manifest.json` нет [факт].

**Различима ли частота.** На Quest 2 — нет, по железу. На Quest 3 — да, но только через Meta Haptics SDK или PCM; в
нашем стеке — нет. **Проектировать только на амплитуде, длительности и ритме** — это работает одинаково на обоих
контроллерах.

**Пределы по длительности [гипотеза, подобрать в шлеме].**
- LRA Quest 2 нужны десятки миллисекунд на разгон и затухание.
- Импульсы короче ~30 мс слабые.
- Чтобы два импульса читались как два, пауза между ними — не меньше ~60–80 мс.

## 5. Рекомендации Meta и других

| Тема | Рекомендация | Источник |
|---|---|---|
| Мало и тонко | «Less is More: … should be subtle»; «Avoid haptic feedback that is too intense … can cause user fatigue» | [Unity guidelines](https://developers.meta.com/horizon/documentation/unity/unity-haptics-design-guidelines/), [best practices](https://developers.meta.com/horizon/design/haptics-best-practices/) |
| Паузы, утомление | «Use 'whitespace', short pauses where the skin can rest»; «avoid long, overlapping haptic effects» в динамичных играх | там же |
| Причинность и синхронность | «Ensure timely playback … clear causal connection»; синхронно со звуком и картинкой; «Do not just play haptic feedback if there is no corresponding visual or audio cue» | [best practices](https://developers.meta.com/horizon/design/haptics-best-practices/) |
| Кодирование смысла | интенсивность, ритм, текстура как переменные. Срочность — через «intensity, frequency, and rhythm». Хаптика годится для коротких ясных сигналов, не для сложной информации; память на ощупь короткая — опираться на узнавание | [Unity guidelines](https://developers.meta.com/horizon/documentation/unity/unity-haptics-design-guidelines/), [best practices](https://developers.meta.com/horizon/design/haptics-best-practices/) |
| Словарь платформы | Essential Pack: Hover, Press, Long Press, Select, Deselect, Object Hover, Grab, Release, Success, Warning, Error. «Object Hover — объект в досягаемости до хвата» — прямой аналог готового кармана | [haptics system](https://developers.meta.com/horizon/design/haptics-system/) |
| Приоритет и наложение | Meta Haptics SDK: priority 0–255 (меньше — важнее). Новый плеер «will interrupt a currently active source/player of the same or lower priority»; при равном приоритете побеждает последний | [Haptics SDK integrate](https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk-integrate/) |
| Доступность | «Make haptics optional … turn them off or mute»; «customize the intensity». XAG 110: обязательно отключение и регулировка силы «from off to maximum»; хаптика никогда не единственный канал информации | [Unity guidelines](https://developers.meta.com/horizon/documentation/unity/unity-haptics-design-guidelines/), [XAG 110](https://devdocs.xbox.com/build/game-principles/accessibility/xag-deep-dives/xag-110-haptic-feedback) |
| Отдача оружия | Конкретных чисел у Meta нет. Рекомендация — «audio-driven haptics» из звука выстрела (Haptics Studio), сейчас нам недоступно | [haptics overview](https://developers.meta.com/horizon/design/haptics-overview/), [GDC 2024](https://schedule.gdconf.com/session/haptics-a-playful-collaboration-between-audio-and-design/898952) |

Конкретных диапазонов амплитуды и длительности в найденных первоисточниках Meta **нет** [факт]. Числа в разделе 7 —
стартовые значения для подбора в шлеме.

## 6. Инвентарь проекта

Режим везде Mix (на Quest ≈ replace). Классы: **П** — положительный, **Ф** — физический, **О** — отрицательный.

| # | Место | Событие | Параметры (амплитуда × длит., кулдаун) | Класс | Проблемы и конфликты |
|---|---|---|---|---|---|
| 1 | `Interaction/PocketHaptics` | карман стал готов принять или отдать (по руке) | 0.5 × 0.3 с, один раз на вход (`PocketTap`) | П | сильнее задуманного «тихого»; обрывается любым следующим сигналом руки (Click хвата 0.8, Place 0.4); см. ниже |
| 2 | `Weapons/WeaponAttemptFeedback` | спуск при не готовом оружии (нет магазина, пустой) | 0.15 × 0.08 с, кулдаун 0.6 — **фактически выключено**: ассетов `WeaponFeedbackProfile` нет, у префабов `_commonProfile: 0`, у запасной реакции `AdditionalHapticEnabled = false` | О | отрицательный слабее отдачи и Click; рисунка нет |
| 3 | `Weapons/WeaponChamberingReminder` | спуск, нужен досыл | 0.15 × 0.08 с, кулдаун 0.6 | О | слишком слабо для «ошибки», путается с тиком; локальность — через `hand.Avatar.ControllerInput` (dummy у чужих) |
| 4 | `Weapons/AutomaticWeaponSlideFeedback` (в работе у другого агента) | затвор назад/вперёд | пусто: Click 1.0 (50 мс); заряжен: **Slide 1.0 × 0.5 с** (Fabarm 0.15) | Ф | Slide 1.0 полсекунды — длинно и сильно, перекрывает всё на руке |
| 5 | `Weapons/BarrelObstruction` | спуск при упёртом стволе | `RumbleFreqNormal` 0.8 × 0.2 с, без кулдауна | О | одиночный сильный импульс, неотличим от отдачи; частота не работает |
| 6 | `Player/WallPass/WallPassFeedback` | стена: Hint / Violating, обе руки | Hint 0.08 × 0.06 с / 1.5 с; Violating 0.35 × 0.15 с / 0.75 с | О (предупреждение) | периодический сигнал на обе руки обрывает отдачу и клики; Hint 0.08 на LRA, вероятно, неощутим [гипотеза] |
| 7 | `UI/HUD/WristDisplay` | уведомление часов (рука с часами) | 0.25 / 0.45 / 0.7 / 1.0 × 0.15 с (Critical 0.4 с) | П или О по смыслу | уведомление посреди стрельбы обрывает отдачу; амплитуда «High» 0.7 сильнее отрицательных |
| 8 | `Player/Ghost/GhostViewEffect` | своя гибель, обе руки | 0.8 × **3 с** | О (критический) | длинный сигнал, против рекомендации Meta; обрывается первым же вызовом |
| 9 | `Debug/DebugMode/DebugGestureInput` | жест отладки | 0.8 × 0.25 / 0.3 × 0.1 напрямую `InputDevice.SendHapticImpulse` всем контроллерам | — | обход UXR; в сервис или в исключения сторожа |
| 10 | `Weapons/Core/WeaponOutput` (`IWeaponOutput.Haptic`) | `WeaponHapticCue`: ActionRear, NotReady, Obstructed, Faulted | не подключено (этап D, `WeaponFeedbackExecutor`); машина шлёт только у автора (`AuthorHaptic`, `Deny`) | Ф / О / О / О | исполнитель этапа D должен звать сервис, а не `ControllerInput` |
| 11 | SDK `UxrFirearmWeapon` | выстрел | `ShotBig` 1.0, 0.25 с (10 × 25 мс) | Ф | у автоматов интервал 60–100 мс < 0.25 с — корутины выстрелов накладываются и перемежаются [гипотеза по коду] |
| 12 | SDK `UxrShotgunPump` | помпа | Click 1.0 / Slide 0.15 | Ф | — |
| 13 | SDK `UxrManipulationHapticFeedback` | хват, укладка; планшет — непрерывно | Click 0.8 / 0.4; планшет 0.05–0.15 каждые 0.1 с | П / Ф | Click хвата обрывает импульс кармана; непрерывная вибрация планшета |
| 14 | SDK `UxrGrabManager` | сорван дальний хват | Click 1.0 | О | зашито в коде |
| 15 | SDK `UxrPointerInputModule` | UI планшета | Click 0.2 / 0.6, перетаскивание | П | зашито в коде |

**Общие проблемы класса [факт по коду].**
- Писателей мотора 15. Ни один не знает о других, у каждого свои числа.
- Отрицательные сигналы (0.15) слабее физических (1.0) и не отличаются рисунком.
- На Quest «последний вызов побеждает».
- Громкости и выключателя для игрока нет.

**`PocketHaptics`: почему «не заметил».**
1. **[факт]** С 2026-09-29 (коммит `0298465b4`) сигнал — **одиночный** импульс 0.5 × 0.3 с при входе руки в готовую зону.
   Раньше была непрерывная вибрация 0.15, и она «раздражала и сливалась с отдачей». Пока рука в зоне — тишина.
2. **[гипотеза, высокая]** Перебивание. Готовность «отдать» наступает на границе досягаемости хвата. Игрок обычно жмёт
   grip через 100–200 мс, и Click хвата 0.8 × 50 мс (`UxrManipulationHapticFeedback` на магазине или гильзе) прерывает
   импульс кармана (OpenXR replace). Ощущается один короткий щелчок, неотличимый от хвата. При укладке то же делает
   Place 0.4.
3. **[гипотеза]** Импульс на одну руку во время движения легко пропустить. Причина — не амплитуда 0.5 (это заметно),
   а то, что сигнал разовый и совпадает с движением.
4. **[гипотеза, ложное срабатывание]** Прокси магазинного кармана хватаем при любом содержимом
   (`OnProxyGrabbableQuery`: `HasItems()`), но магазин выдаётся только к оружию во второй руке. Карман может
   «вибрировать готовым» и ничего не дать.

   **Проверка:** Verbose канала `Player` — строки `[PocketHaptics] … готов — вибрация`.
   - Строк нет — дело в готовности.
   - Строки есть, вибрации нет — перебивание или устройство.

## 7. Предлагаемая модель

### Классы и параметры по умолчанию (стартовые, подобрать в шлеме)

| Класс | Назначение | Рисунок по умолчанию | Приоритет |
|---|---|---|---|
| **Отрицательный** | действие не выполнено: спуск не готов, ствол упёрт, сбой, сорван хват, нарушение стены | **двойной импульс**: 0.8 × 50 мс, пауза 70 мс, 0.8 × 50 мс (≈170 мс); кулдаун 0.4 с на руку и сигнал. Критический (гибель, «вернитесь за N с»): тройной 1.0 × 60 / 80 мс, без длинного гула | 3 (высший) |
| **Физический** | следствие механики: отдача, ход затвора/помпы, удар магазина, хват/укладка предмета | один всплеск со спадом. Пистолет 0.7 → 0 за 80 мс; винтовка 0.9 → 0 за 100 мс; дробовик 1.0 → 0 за 180 мс; затвор 0.5 × 40 мс; хват 0.35 × 30 мс; укладка 0.25 × 30 мс. Слабая рука ×0.5 | 2 |
| **Положительный** | «можно»: карман готов, объект в досягаемости, UI | одиночный — 0.3 × 40 мс. **Непрерывный** — акцент 0.35 × 40 мс, затем тик 0.15 × 30 мс каждые 250 мс, пока контакт; через 3 с тик ослабевает до 0.1 | 1 |

Почему так.
- Отрицательный отличается **ритмом** (двойной), а не только силой: на LRA, кроме амплитуды, ритм — единственный
  надёжный признак, и Meta называет ритм носителем срочности.
- Непрерывный положительный — **тик**, а не гул. Гул 0.15 уже отвергнут пользователем, а тик не сливается с одиночным
  всплеском отдачи и даёт «белые паузы» коже.

### Приоритеты и смешивание

- Мотор каждой руки в каждый момент принадлежит **одному** сигналу. Суммы нет: она стирает рисунок.
- Победитель — высший приоритет. При равном побеждает последний (как в Meta Haptics SDK). Исключение — физический
  против физического: берётся максимум огибающих, чтобы отдача на автомате не рвалась.
- Разовый сигнал низшего класса, проигравший конкуренцию, **отбрасывается**, а не откладывается: запоздавшая вибрация
  теряет причинность.
- Непрерывный положительный **ставится на паузу** и возобновляется после окончания сигнала высшего класса.
- Сигналы на обе руки (стена, гибель) — два независимых запроса по рукам с одним приоритетом.

### Непрерывные сигналы

- Запуск — `HapticHandle Begin(id, hand, owner)`, остановка — `handle.End()`.
- Сервис сам завершает дескриптор, если `owner` выключен или уничтожен: висящей вибрации быть не может.
- Сервис сэмплирует огибающую каждый кадр. `SendHapticImpulse(амплитуда, остаток сегмента + 1 кадр)` шлётся только
  при смене амплитуды или истечении сегмента; нулевая амплитуда — `StopHaptics`.

### Каталог и API

- **`HapticCatalog` (ScriptableObject, один ассет).** Записи `HapticSignal`: `Id` (константы `HapticIds.PocketReady` и т. п.,
  тест сверяет с каталогом), `Class`, `Segments` (список «амплитуда, мс»; кривая не нужна — импульсный API
  дискретен), `Loop`, `LoopFrom`, `Cooldown`, `SecondaryHandGain`, опционально `ProgressDriven` (идея из пакета).
- **`HapticService`.** Единственный писатель `InputDevice` и обработчик перехвата SDK. API:
  - `Play(HapticId id, UxrGrabber hand, float gain = 1)`;
  - `Play(HapticId id, UxrHandSide side, float gain = 1)` — только для своего аватара;
  - `Begin(...)`;
  - `SetProgress(handle, t01)`;
  - `StopAll(side)`;
  - устройство за интерфейсом `IHapticDevice` (подмена в тестах).
- **Оружие (этап D).** `WeaponFeedbackExecutor.Haptic(WeaponHapticCue)` отображает cue в Id: ActionRear → Ф «затвор»;
  NotReady / Obstructed → О; Faulted → О критический.
- Отдача SDK приходит через перехват как Ф с силой по классу оружия.

### Правило «единственный владелец» и тест

- Правило для `AGENTS.md`: вибрация — только `HapticService.Play/Begin` с Id из каталога.
- Запрещены `SendHapticFeedback`, `SendGrabbableHapticFeedback`, `StopHapticFeedback`, `SendHapticImpulse`,
  `SendHapticBuffer` и `StopHaptics` вне сервиса.
- EditMode-сторож по образцу правила `GameLog`: регулярное выражение по `Assets/Scripts/**/*.cs` и
  `Assets/Editor/**/*.cs`, белый список — файл адаптера сервиса.
- Второй тест — перехват SDK: вызов `SendHapticFeedback` у контроллера доходит до фейкового `IHapticDevice` только
  через сервис.
- Третий (префабы): нет `UxrManipulationHapticFeedback` с `_continuousManipulationHaptics = 1` вне белого списка.
- По решению 2026-10-02 тесты пишутся после подтверждения механики в шлеме.

### Настройка для игрока (отклонено, п. 8.3)

- `Вибрация: вкл/выкл` и `Сила: 0–100 %` (XAG 110). Отдельный переключатель «Подсказки на ощупь» для положительного
  класса — открытый вопрос.
- Хранение — `PlayerPrefs` на устройстве: это настройка игрока, не отладочная.
- Применяется **одним множителем в сервисе**.
- Экран — через `/add-menu-screen`.
- Хаптика никогда не единственный канал: у кармана есть звук укладки и доставания, у отказа — сухой щелчок.

### Сеть

- Вибрация строго локальна, по сети не передаётся.
- Цель — рука `UxrAvatar.LocalAvatar` в режиме `Local`. Сервис сам игнорирует запросы с чужим `UxrGrabber`, поэтому
  обработчики синхронизируемых событий (они идут на каждой машине) безопасны.
- `StateEventAuthority.IsAuthorOfItem` как фильтр **не годится** [факт по коду `StateEventAuthority.cs:85`]: для
  предмета, который никто не держит, он возвращает `IsWorldAuthority` (true на хосте), а для аватара без владельца (боты на хосте) —
  true. Хост вибрировал бы за ботов и лежащие предметы.
- Авторская проверка остаётся нужна, чтобы не продублировать **событие**. Машина оружия уже шлёт вибрацию только
  у автора и не при replay. Фильтр «локальная рука» — вторая, независимая страховка.

## 8. Решения пользователя (2026-10-07)

1. **Карман** — непрерывный положительный сигнал всё время, пока рука в зоне готового кармана. Сила настраивается
   в каталоге, стартовое значение — 10 %. Комфортную силу подбирать в шлеме.
2. **Отдача** принадлежит владельцу архитектуры системы оружия (WeaponSystem): машина выдаёт физический сигнал,
   исполнитель передаёт его в сервис. Отдача SDK отключается.
3. **Настроек вибрации для игрока нет** — ни выключателя, ни силы, ни переключателя подсказок (уточнено 2026-10-07).
   Параметры сигналов — настройка проекта; подбор в шлеме — дебаг-экраном (дизайн, п. 8). Рекомендация XAG 110 сознательно
   не выполняется.
4. **Гибель** — максимальная сила, 1,5 с.
5. На Unity OpenXR Plugin **пока не переходим**. Проектируем на амплитуде и ритме.
6. **Отказ по темпу стрельбы** (S2 в `weapon-system-refactor-plan.md`) — отрицательный сигнал плюс отдельный
   искусственный звук отказа, без подсветки. Звук — `USER_INTERFACES/Errors/UI_Error_Subtle_Deep_stereo.wav`
   (0,21 с) из библиотеки `Docs/sound-library.md`. Импорт моно вместе с `.meta` — при подключении сигнала.
