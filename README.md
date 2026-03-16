# Claw-Machine

Unity клиент для клеш-машины с серверной (authoritative) экономикой и финальным расчетом исхода попытки.

## Backend Overview

Backend проект (NestJS) расположен отдельно:

`/Users/papaya/CodeProjects/Claw-Machine-Backend`

Ключевой принцип:

- сервер решает `win/lose/void`, выдает награду и ведет античит/аудит;
- клиент отправляет только ввод и telemetry, локальная физика остается визуальной.

## Быстрый старт Backend

```bash
cd /Users/papaya/CodeProjects/Claw-Machine-Backend
npm install
npm run start:dev
```

Локальный API base URL:

`http://localhost:3000`

## Переменные окружения Backend

| Переменная | По умолчанию | Назначение |
| --- | --- | --- |
| `PORT` | `3000` | HTTP порт NestJS |
| `DATABASE_URL` | - | URL PostgreSQL для SQL-скриптов |
| `REDIS_URL` | - | Резерв под rate-limit/очереди |
| `JWT_SECRET` | - | Подпись access token |
| `JWT_TTL_SEC` | `21600` | TTL access token |
| `TELEGRAM_BOT_TOKEN` | - | Проверка Telegram `initData` |
| `TELEGRAM_INIT_DATA_TTL_SEC` | `120` | TTL Telegram auth payload |
| `DEV_AUTH_ENABLED` | `true` | Разрешить dev auth без Telegram |
| `DEV_AUTH_USER_PREFIX` | `dev` | Префикс dev user id |
| `ATTEMPT_TOKEN_SECRET` | - | Подпись attempt token |
| `ATTEMPT_TTL_SEC` | `300` | TTL attempt token |
| `INPUT_RATE_LIMIT_PER_SEC` | `30` | Ограничение частоты input пакетов |
| `AUDIT_LOG_ENABLED` | `true` | Включение audit событий |
| `DEFAULT_TICKETS` | `5` | Стартовые тикеты пользователя |

## API v1 (кратко)

### Auth

- `POST /v1/auth/telegram`
  - body: `{ "initData": "..." }`
  - response: `accessToken`, `expiresInSec`
- `POST /v1/auth/dev` (локальная разработка без Telegram)
  - body: `{ "devUserId": "editor-user-1" }`
  - response: `accessToken`, `expiresInSec`

### Attempt lifecycle

- `POST /v1/attempts/start`
  - headers: `Authorization: Bearer ...`, `Idempotency-Key`
  - body: `machineId`, `clientBuild`, `configVersion`
  - response: `attemptId`, `attemptToken`, `inputWindowMs`, `economySnapshot`
- `POST /v1/attempts/{attemptId}/inputs`
  - headers: `Authorization`, `X-Attempt-Token`
  - body: `packets[]` c `seq`, `clientTimeMs`, `moveX`, `moveY`
  - response: `acceptedSeqUpTo`, `warnings`
- `POST /v1/attempts/{attemptId}/resolve`
  - headers: `Authorization`, `X-Attempt-Token`, `Idempotency-Key`
  - body: `clientSummary` (`pressTimeMs`, `closeStartMs`, `localGrabObserved`, optional `contactHints`)
  - response: `result`, `reward?`, `riskScore`

### Reward claim

- `POST /v1/rewards/claim`
  - headers: `Authorization`, `Idempotency-Key`
  - body: `{ "attemptId": "..." }`
  - response: `granted` / `already_granted` / `pending` / `failed`

## Важные правила интеграции с Unity

- Используй `configVersion: "v1-default"` (не `"v1"`).
- На `inputs` и `resolve` всегда передавай `X-Attempt-Token` из `start`.
- Для `start`, `resolve`, `claim` всегда передавай `Idempotency-Key`.
- `claim` вызывается только при `result=win`.
- Если access token протух, нужно переаутентифицироваться до старта попытки.

## Сквозной сценарий попытки

1. Аутентификация (`/v1/auth/telegram` или `/v1/auth/dev`).
2. Старт (`/v1/attempts/start`), получить `attemptId` и `attemptToken`.
3. Во время движения отправлять батчи input пакетов (`/inputs`).
4. В конце цикла вызвать `/resolve`.
5. Если `resolve.result=win`, вызвать `/v1/rewards/claim`.
6. Показ награды в клиенте делать только по ответу backend.

## Минимальные curl-примеры

```bash
# 1) Dev auth
curl -sS -X POST http://localhost:3000/v1/auth/dev \
  -H 'Content-Type: application/json' \
  -d '{"devUserId":"editor-user-1"}'
```

```bash
# 2) Start attempt
curl -sS -X POST http://localhost:3000/v1/attempts/start \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Idempotency-Key: $(uuidgen | tr '[:upper:]' '[:lower:]')" \
  -H 'Content-Type: application/json' \
  -d '{"machineId":"main","clientBuild":"dev","configVersion":"v1-default"}'
```

```bash
# 3) Send inputs
curl -sS -X POST "http://localhost:3000/v1/attempts/$ATTEMPT_ID/inputs" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "X-Attempt-Token: $ATTEMPT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"packets":[{"seq":1,"clientTimeMs":1773520900000,"moveX":0.2,"moveY":0.8}]}'
```

```bash
# 4) Resolve
curl -sS -X POST "http://localhost:3000/v1/attempts/$ATTEMPT_ID/resolve" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "X-Attempt-Token: $ATTEMPT_TOKEN" \
  -H "Idempotency-Key: $(uuidgen | tr '[:upper:]' '[:lower:]')" \
  -H 'Content-Type: application/json' \
  -d '{"clientSummary":{"pressTimeMs":1773520900268,"closeStartMs":1773520902701,"localGrabObserved":true,"contactHints":[]}}'
```

```bash
# 5) Claim (only if result=win)
curl -sS -X POST http://localhost:3000/v1/rewards/claim \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Idempotency-Key: $(uuidgen | tr '[:upper:]' '[:lower:]')" \
  -H 'Content-Type: application/json' \
  -d "{\"attemptId\":\"$ATTEMPT_ID\"}"
```

## Где смотреть полную спецификацию

- Интеграция Unity + backend: `docs/backend-client-integration.md`
- Backend OpenAPI: `/Users/papaya/CodeProjects/Claw-Machine-Backend/docs/openapi-v1.yaml`
- Backend README: `/Users/papaya/CodeProjects/Claw-Machine-Backend/README.md`
