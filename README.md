# Claw Machine

Полная актуальная документация по Unity-проекту `Claw Machine`.

## 1. Назначение проекта

`Claw Machine` — это WebGL-клиент мини-игры с автоматом-хватайкой, рассчитанный на запуск в Telegram Mini App и в браузере. Клиент отвечает за:

- визуализацию автомата, игрушек, движения клешни и интерфейса;
- получение Telegram Mini App контекста и авторизацию пользователя через backend;
- отправку телеметрии ввода во время попытки;
- запуск серверной попытки, получение финального результата и автоклейм награды;
- отображение факта выигрыша на стороне клиента только после ответа backend.

Ключевой принцип проекта: результат попытки считается на backend. Клиентская физика и захват нужны для визуального восприятия, но не являются источником истины по награде.

## 2. Текущее техническое состояние

| Параметр | Значение |
| --- | --- |
| Движок | Unity `6000.2.13f1` |
| Версия продукта | `0.1.0` |
| Product Name | `Claw Machine` |
| Company Name | `Papaya Dev` |
| Render Pipeline | URP |
| Основная целевая платформа | WebGL |
| WebGL Template | `PROJECT:TelegramMiniApp` |
| Основные сцены | `Bootstrap`, `Gameplay` |
| Основная backend-конфигурация | `Assets/Settings/ClawBackendSettings.asset` |
| Текущий backend base URL | `https://kcalo.ru/` |

Ключевые пакеты, реально влияющие на проект:

- `com.unity.render-pipelines.universal` `17.2.0`
- `com.unity.inputsystem` `1.14.2`
- `com.unity.ugui` `2.0.0`
- TextMesh Pro

## 3. Что реализовано в текущей версии

### Игровая часть

- перемещение клешни по двум осям;
- ручной запуск цикла кнопкой;
- вертикальное опускание и подъем клешни;
- открытие и закрытие пальцев;
- захват игрушки по контактам пальцев;
- ослабленный захват и случайный срыв при пограничных контактах;
- автоматический возврат клешни в точку сброса и затем в центр;
- визуальная анимация стика и кнопки управления;
- отображение имени пользователя Telegram в UI.

### Серверная интеграция

- Telegram auth через `initData`;
- dev auth fallback при отсутствии Mini App контекста;
- старт серверной попытки перед началом игрового цикла;
- периодическая отправка input-пакетов во время движения;
- `resolve` в конце цикла;
- `claim reward` при результате `win`;
- загрузка серверного spawn plan для наполнения автомата игрушками;
- post-win spawn одной игрушки по серверному полю `spawnOnWinToyId`.

### Платформенная интеграция

- запуск в Telegram Mini App через собственный WebGL template;
- адаптация темы Telegram;
- адаптация высоты viewport под Mini App;
- отключение вертикальных свайпов Telegram WebApp, если API доступен;
- JS-мост `window.UnityTelegramMiniApp` для доступа к `initData`, `startParam` и haptic feedback.

## 4. Общий пользовательский сценарий

1. Открывается сцена `Bootstrap`.
2. Применяются стартовые настройки качества для мобильных платформ.
3. Выполняется проверка интернет-соединения.
4. Выполняется авторизация через backend.
5. Загружается сцена `Gameplay`.
6. В `Gameplay` клиент запрашивает у backend spawn plan и заполняет автомат игрушками.
7. Игрок двигает клешню.
8. При нажатии кнопки клиент инициирует backend attempt.
9. Во время движения и цикла захвата клиент отправляет backend input telemetry.
10. После завершения цикла клиент вызывает `resolve`.
11. Если backend возвращает `win`, клиент проверяет `spawnOnWinToyId` в ответе `resolve`.
12. Если `spawnOnWinToyId` пришел непустым, клиент спавнит одну игрушку в верхней точке зоны `ToyMachineFiller` со случайными `x/z` внутри той же области.
13. Если backend возвращает `win`, клиент автоматически вызывает `claim`.
14. После завершения `resolve` управление снова разблокируется.

## 5. Архитектура клиента

## 5.1. Сцена Bootstrap

Сцена `Bootstrap` — это входная техническая сцена, которая последовательно запускает набор bootstrap-запросов (`BootstrapLoadQuery`).

Текущий порядок выполнения:

| Order | Query | Назначение |
| --- | --- | --- |
| `0` | `ApplyMobileQualityQuery` | Выставляет `targetFrameRate = 60` и `vSync = 0` на мобильных платформах |
| `1` | `InternetConnectionCheckQuery` | Проверяет доступ в интернет через `https://www.gstatic.com/generate_204` |
| `2` | `BackendTelegramAuthQuery` | Выполняет Telegram/dev auth и сохраняет access token |
| `3` | `LoadSceneQuery` | Загружает сцену `Gameplay` |

Если любой обязательный шаг завершается ошибкой, загрузка останавливается и причина выводится в текст загрузочного экрана.

## 5.2. Сцена Gameplay

Сцена `Gameplay` содержит всю игровую логику и основные runtime-системы:

| Система | Роль |
| --- | --- |
| `InputController` | Читает ввод с экранного джойстика и кнопки, плюс desktop fallback (`WASD` / `Space`) |
| `MovementController` | Двигает клешню по двум направляющим и управляет авто-переездом в drop point и центр |
| `ClawController` | Ведет полный игровой цикл: open, drop, close, rise, release, return |
| `FingerContactSensor` | Считывает физические контакты пальцев с игрушками |
| `ControlsVisualController` | Визуально анимирует стик и кнопку |
| `ToyMachineFiller` | Запрашивает spawn plan у backend, заполняет автомат игрушками и выполняет post-win spawn |
| `ClawBackendApiClient` | Выполняет HTTP-запросы к backend |
| `ClawBackendAttemptClient` | Управляет жизненным циклом серверной попытки |
| `TelegramDisplayNameText` | Показывает имя текущего Telegram-пользователя |

## 5.3. Игровой цикл клешни

Текущий цикл в `ClawController` выглядит так:

1. В состоянии `Idle` игрок свободно перемещает клешню.
2. По нажатию кнопки управление блокируется.
3. До старта анимации клиент обязан успешно вызвать `TryStartAttemptAsync()`.
4. Пальцы раскрываются.
5. Клешня опускается вниз.
6. На старте закрытия отправляется отметка `closeStartMs`.
7. Во время закрытия анализируются контакты пальцев с rigidbody игрушек.
8. Если условия захвата выполняются, игрушка прикрепляется к клешне.
9. На подъеме может произойти ослабленный срыв игрушки.
10. На середине подъема запускается автоматический переезд в точку сброса.
11. В точке сброса клешня разжимается.
12. После короткой паузы каретка автоматически возвращается в центр.
13. В конце цикла вызывается backend `resolve`.
14. Пока `resolve` не завершится, управление остается заблокированным.

Важно: в текущей конфигурации `ClawBackendAttemptClient` работает с `_allowOfflineFallback = false`. Это означает, что без успешного backend attempt цикл игры не стартует.

## 5.4. Модель захвата игрушки

Текущая реализация использует гибридную схему:

- каждый палец клешни имеет `FingerContactSensor`;
- сенсор поддерживает trigger/collision и overlap fallback;
- `ClawController` ищет доминирующее тело, к которому одновременно прикасается несколько пальцев;
- по умолчанию для захвата требуется контакт как минимум `3` пальцев;
- разрешен "пограничный" lucky grab на один палец меньше с вероятностью `0.35`;
- после захвата игрушка переводится в слой `ClawToy`;
- в момент сброса победившая игрушка переводится в слой `WinnedToy`.

Дополнительно включен weak grip slip:

- если захват был слабым и контакт пришелся на край объекта;
- игрушка может сорваться на определенном прогрессе подъема;
- это добавляет вариативность визуальному поведению захвата.

## 5.5. Серверная логика попытки

Класс `ClawBackendAttemptClient` делает следующее:

- стартует серверную попытку через `/v1/attempts/start`;
- хранит `attemptId` и `attemptToken`;
- семплирует движение игрока каждые `0.04` секунды;
- отправляет батчи инпутов примерно каждые `0.12` секунды;
- по завершении цикла вызывает `/v1/attempts/{attemptId}/resolve`;
- читает из `resolve` необязательное поле `spawnOnWinToyId`;
- при непустом `spawnOnWinToyId` инициирует спавн одной игрушки через `ToyMachineFiller`;
- при результате `win` автоматически вызывает `/v1/rewards/claim`.

Отправляемые данные клиента:

- `pressTimeMs`
- `closeStartMs`
- `localGrabObserved`
- массив input-пакетов с `seq`, `clientTimeMs`, `moveX`, `moveY`

Финальный `win/lose` определяется сервером.

Для post-win spawn клиент использует отдельный контракт:

- `spawnOnWinToyId` не выводится из `reward.code`, а приходит отдельным полем;
- на backend это поле выбирается из общего массива `spawnOnWinToys` по весам;
- если поле отсутствует или пустое, дополнительный spawn не происходит;
- если поле заполнено, `ToyMachineFiller` спавнит одну игрушку на верхней границе своей зоны по `y`;
- `x/z` выбираются случайно внутри той же зоны с учетом L-shape фильтра;
- для такого спавна используется runtime random, даже если стартовое наполнение автомата идет с фиксированным seed.

## 5.6. Telegram Mini App интеграция

В проекте есть статический контейнер `TelegramMiniAppSession`, который:

- парсит `tgWebAppData` из URL query или fragment;
- извлекает из `initData` данные пользователя;
- вычисляет отображаемое имя пользователя;
- хранит признаки Mini App запуска;
- дает доступ к `start_param`, `chat_type`, `platform` и другим метаданным.

При работе внутри Telegram клиент дополнительно отправляет в backend заголовки:

- `X-Client-Source: telegram-miniapp`
- `X-Telegram-MiniApp: 1`
- `X-Telegram-User-Id`
- `X-Telegram-Display-Name`
- `X-Telegram-Username`
- `X-Telegram-Language`
- `X-Telegram-Chat-Type`
- `X-Telegram-Start-Param`
- `X-Telegram-Platform`
- `X-Telegram-Init-Source`

## 6. Backend API, который использует клиент

В клиенте зашиты вызовы следующих endpoint'ов:

| Метод | Endpoint | Назначение |
| --- | --- | --- |
| `POST` | `/v1/auth/telegram` | Авторизация по Telegram `initData` |
| `POST` | `/v1/auth/dev` | Dev auth fallback для Editor / локальной разработки |
| `POST` | `/v1/machines/{machineId}/spawn-plan` | Получение набора игрушек для наполнения автомата |
| `POST` | `/v1/attempts/start` | Старт попытки |
| `POST` | `/v1/attempts/{attemptId}/inputs` | Отправка input telemetry |
| `POST` | `/v1/attempts/{attemptId}/resolve` | Финализация попытки |
| `POST` | `/v1/rewards/claim` | Подтверждение награды после `win` |

Правила интеграции, которые зафиксированы в коде:

- `configVersion` должен быть `v1-default`;
- на `inputs` и `resolve` используется `X-Attempt-Token`;
- для `start`, `resolve`, `claim` генерируется `Idempotency-Key`;
- access token хранится в `PlayerPrefs` по ключу `backend_access_token`;
- `resolve` может вернуть необязательное поле `spawnOnWinToyId` для post-win spawn;
- post-win spawn должен опираться именно на `spawnOnWinToyId`, а не на `reward.code`;
- токен считается валидным только если это JWT с достаточным остатком TTL.

## 7. Конфигурация проекта

## 7.1. Основные конфигурационные файлы

| Файл | Назначение |
| --- | --- |
| `Assets/Settings/ClawBackendSettings.asset` | основная backend-конфигурация, реально привязана в сценах |
| `Assets/Resources/ToyCatalog.asset` | каталог игрушек для автомата |
| `Assets/Settings/Mobile_RPAsset.asset` | URP asset для mobile/WebGL профиля |
| `Assets/Settings/PC_RPAsset.asset` | URP asset для desktop-профиля |
| `ProjectSettings/EditorBuildSettings.asset` | список сцен сборки |
| `ProjectSettings/QualitySettings.asset` | quality profiles `Mobile` и `PC` |
| `ProjectSettings/ProjectSettings.asset` | product metadata и WebGL template |

Важно: в репозитории также присутствует `Assets/Resources/ClawBackendSettings.asset`, но текущие сцены используют именно `Assets/Settings/ClawBackendSettings.asset`. Если используются обе копии, их нужно держать синхронными.

## 7.2. Текущие backend-настройки

Текущее значение в asset:

| Поле | Значение |
| --- | --- |
| `BaseUrl` | `https://kcalo.ru/` |
| `TelegramAuthPath` | `/v1/auth/telegram` |
| `DevAuthPath` | `/v1/auth/dev` |
| `AccessTokenPlayerPrefsKey` | `backend_access_token` |
| `RequestTimeoutSeconds` | `10` |
| `MinTokenRemainingSeconds` | `15` |

## 7.3. Текущие игровые настройки

| Параметр | Значение |
| --- | --- |
| `machineId` | `main` |
| `configVersion` | `v1-default` |
| `input sample interval` | `0.04` сек |
| `input flush interval` | `0.12` сек |
| `max packets per flush` | `12` |
| `auto claim reward` | включен |
| `offline fallback for attempts` | выключен |
| `lock controls until backend resolve` | включен |

## 7.4. Quality profiles

В проекте настроены два профиля качества:

| Профиль | Использование |
| --- | --- |
| `Mobile` | Android, iPhone, WebGL |
| `PC` | Standalone |

В `Bootstrap` дополнительно принудительно ставится `targetFrameRate = 60` для мобильных платформ.

## 7.5. Слои проекта

Используемые кастомные layers:

| Layer | Назначение |
| --- | --- |
| `Toy` | обычные игрушки в автомате |
| `ClawToy` | игрушка, удерживаемая клешней |
| `Barrier` | ограничители / геометрия взаимодействия |
| `WinnedToy` | игрушка после сброса как выигранная |

## 8. Каталог контента и игрушек

Каталог игрушек хранится в `Assets/Resources/ToyCatalog.asset`.

Текущие идентификаторы игрушек:

| toyId | Prefab | Scale |
| --- | --- | --- |
| `bow_tie` | `Toy (Bow Tie)` | `1.5` |
| `button` | `Toy (Button)` | `1.5` |
| `sword` | `Toy (Sword)` | `1.5` |
| `pacifier` | `Toy (Pacifier)` | `0.4` |
| `heart` | `Toy (Heart)` | `0.8` |
| `bear` | `Toy (Bear)` | `0.8` |
| `potion` | `Toy (Potion)` | `0.8` |
| `cake` | `Toy (Cake)` | `0.8` |

Наполнение автомата работает так:

- backend возвращает список `toyId`;
- `ToyMachineFiller` сопоставляет их с каталогом;
- каждая игрушка инстанцируется в выделенной зоне спавна;
- для зоны используется L-образная область с исключенным одним кварталом;
- при необходимости объекту добавляются collider и rigidbody.

Если backend не вернул spawn plan или вернул пустой список, автомат не заполняется.

Post-win spawn работает отдельно:

- backend возвращает в `resolve` необязательное поле `spawnOnWinToyId`;
- не каждая награда обязана иметь это поле;
- если поле заполнено, клиент спавнит ровно одну игрушку;
- спавн идет в самой высокой точке зоны по `y`, но со случайными `x/z`;
- ограничения зоны полностью берутся из настроек `ToyMachineFiller`.

## 9. Структура репозитория

```text
Assets/
  Art/                    Вспомогательные арт-ресурсы
  Fonts/                  Шрифты
  Joystick Pack/          Мобильный joystick asset
  Models/                 3D-модели игрушек
  Prefabs/                Prefab'ы игрушек
  Resources/              ToyCatalog и резервные runtime-assets
  Scenes/                 Bootstrap и Gameplay
  Scripts/
    Bootstraps/           Система стартовых запросов загрузки
    ClawMachine/          Игровая логика автомата
    ClawMachine/Backend/  HTTP/API и lifecycle серверной попытки
    ClawMachine/Editor/   Кастомный editor для ToyMachineFiller
  Settings/               URP, Input System, backend settings
  SimpleToon/             Шейдерные и визуальные материалы
  Textures/               Текстуры
  WebGLTemplates/         Telegram Mini App шаблон WebGL
Packages/
ProjectSettings/
Claw-Machine-Build/       Готовые WebGL-артефакты сборки
```

Ключевые скрипты:

| Файл | Роль |
| --- | --- |
| `Assets/Scripts/Bootstraps/EntryBootstrap.cs` | запуск bootstrap-цепочки |
| `Assets/Scripts/Bootstraps/Queries/BackendTelegramAuthQuery.cs` | авторизация через Telegram/dev backend auth |
| `Assets/Scripts/ClawMachine/ClawController.cs` | логика полного цикла клешни |
| `Assets/Scripts/ClawMachine/MovementController.cs` | движение по рельсам |
| `Assets/Scripts/ClawMachine/InputController.cs` | ввод пользователя |
| `Assets/Scripts/ClawMachine/ToyMachineFiller.cs` | наполнение автомата игрушками |
| `Assets/Scripts/ClawMachine/Backend/ClawBackendApiClient.cs` | транспортный слой backend API |
| `Assets/Scripts/ClawMachine/Backend/ClawBackendAttemptClient.cs` | attempt lifecycle |
| `Assets/Scripts/ClawMachine/Backend/TelegramMiniAppSession.cs` | хранение Mini App сессии |

## 10. Управление

Текущее управление в клиенте:

| Платформа | Действие |
| --- | --- |
| Touch / Mobile | экранный joystick — движение |
| Touch / Mobile | экранная кнопка — запуск клешни |
| Desktop / Editor | `W`, `A`, `S`, `D` — движение |
| Desktop / Editor | `Space` — запуск клешни |

Desktop-ввод используется как fallback для локальной отладки и тестирования в редакторе / браузере.

## 11. Telegram WebGL template

Шаблон `Assets/WebGLTemplates/TelegramMiniApp/index.html` делает следующее:

- подключает `https://telegram.org/js/telegram-web-app.js`;
- вызывает `tg.ready()` и `tg.expand()`;
- применяет Telegram theme colors через CSS variables;
- обновляет viewport height при событиях Telegram и resize окна;
- показывает кастомный progress bar загрузки Unity;
- публикует объект `window.UnityTelegramMiniApp`.

Доступные JS-методы, которые уже экспонированы в шаблоне:

| Метод | Назначение |
| --- | --- |
| `getInitData()` | вернуть raw `tg.initData` |
| `getStartParam()` | вернуть `start_param` |
| `hapticImpact(style)` | вызвать Telegram haptic feedback |

## 12. Сборка и запуск

## 12.1. Требования

- установлен Unity Editor версии `6000.2.13f1`;
- доступен backend с совместимым API;
- для Mini App запуска нужен Telegram WebApp-контекст;
- для локальной разработки без Telegram backend должен поддерживать `/v1/auth/dev`.

## 12.2. Локальный запуск в Unity Editor

1. Открыть проект в Unity `6000.2.13f1`.
2. Проверить `Assets/Settings/ClawBackendSettings.asset`.
3. Убедиться, что backend доступен по указанному `BaseUrl`.
4. Запустить сцену `Bootstrap` или просто нажать Play с текущими Build Settings.
5. При запуске без Telegram будет использован dev auth fallback, если он разрешен на backend.

## 12.3. Сборка WebGL

В проекте нет отдельного build-скрипта или CI-конвейера. Сборка выполняется вручную из Unity Editor.

Текущие build settings:

| Порядок | Сцена |
| --- | --- |
| `0` | `Assets/Scenes/Bootstrap.unity` |
| `1` | `Assets/Scenes/Gameplay.unity` |

Для релизной сборки нужно:

1. Собрать проект под `WebGL`.
2. Убедиться, что используется шаблон `TelegramMiniApp`.
3. Проверить доступность backend и корректный `BaseUrl`.
4. Разместить WebGL build на HTTPS-хостинге.
5. Открывать игру через Telegram Mini App или напрямую в браузере для dev-проверок.

## 12.4. Готовые build-артефакты в репозитории

В корне репозитория присутствует каталог `Claw-Machine-Build/`, содержащий готовые WebGL-файлы:

- `index.html`
- `Build/*.loader.js`
- `Build/*.framework.js.unityweb`
- `Build/*.data.unityweb`
- `Build/*.wasm.unityweb`

Это артефакты сборки, а не исходный код клиента.

## 13. Редакторские инструменты

Для `ToyMachineFiller` реализован custom inspector:

- показывает количество валидных записей в каталоге игрушек;
- содержит кнопку `Заполнить автомат`;
- содержит кнопку `Очистить заполнение`.

Важно: runtime refill зависит от backend spawn plan. Если backend недоступен, наполнение автомата не произойдет.

## 14. Ограничения и особенности текущей реализации

- backend является обязательной частью боевого сценария;
- результат попытки не вычисляется локально;
- без успешного `attempt start` игрок не сможет начать цикл;
- без успешного `spawn-plan` автомат останется пустым;
- без согласованного `spawnOnWinToyId` post-win spawn не произойдет даже при `win`;
- в репозитории нет автоматических тестов, asmdef-модулей и CI-конвейера;
- документирован только Unity-клиент; backend поставляется как отдельный сервис;
- часть клиентских решений ориентирована именно на Telegram Mini App и WebGL.

## 15. Что важно для сопровождения

При изменении проекта в первую очередь нужно проверять согласованность следующих сущностей:

- `machineId` в сцене и backend-конфигурации;
- `configVersion` клиента и backend;
- `ToyCatalog.asset` и серверного spawn plan;
- `ToyCatalog.asset` и backend-поле `spawnOnWinToys`;
- `ClawBackendSettings.asset` и реального адреса backend;
- WebGL template и требований Telegram Mini App;
- поведение блокировки ввода до завершения `resolve`.

## 16. Краткий итог

На текущий момент `Claw Machine` — это Unity WebGL-клиент автомата-хватайки с авторизацией через Telegram Mini App, серверным управлением экономикой и результатом попытки, загрузкой контента автомата с backend и ручной сборкой WebGL через кастомный Telegram-шаблон.

Если проект передается другой команде, минимально необходимый набор для поддержки включает:

- Unity-проект из этого репозитория;
- отдельный совместимый backend;
- актуальные значения `BaseUrl`, `machineId` и `configVersion`;
- понимание, что итог выигрыша принадлежит backend, а не клиентской физике.
