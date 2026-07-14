# Внешний API DeepBrain v1

## Назначение

Версионированный HTTP/SSE API позволяет наблюдать DeepBrain с телефона, домашнего ПК или будущего шлюза робота/IoT. Версия конверта событий: `deepbrain.external.v1`.

Первый уровень API намеренно не содержит произвольного управления исполнительными механизмами. Он публикует состояние, внешние выводы и наблюдения Cortex, а также позволяет явно запросить LLM-наблюдение. MQTT, Home Assistant и ROS2 подключаются позднее через `IExternalOutputAdapter`, получая тот же конверт событий.

## Маршруты

| Метод | Маршрут | Назначение |
|---|---|---|
| `GET` | `/api/v1/health` | Проверка доступности, без авторизации |
| `GET` | `/api/v1/capabilities` | Версии контрактов и возможности |
| `GET` | `/api/v1/state` | Последний `LifeStateDto` |
| `GET` | `/api/v1/outputs/latest` | Последний внешний вывод |
| `GET` | `/api/v1/system/status` | ML, память, Cortex и API |
| `GET` | `/api/v1/llm/status` | Состояние Cortex |
| `GET` | `/api/v1/llm/last` | Последнее принятое наблюдение |
| `POST` | `/api/v1/llm/observe` | Асинхронно запросить наблюдение |
| `GET` | `/api/v1/events` | Поток SSE: `state`, `output`, `cortex.insight` |

## Безопасная конфигурация

По умолчанию API отключён и привязан к loopback:

```json
"externalApi": {
  "enable": true,
  "prefix": "http://127.0.0.1:8787/",
  "requireToken": false,
  "tokenEnvironmentVariable": "DEEPBRAIN_API_TOKEN",
  "statePublishEveryTicks": 5,
  "allowedOrigins": [],
  "subscriberBufferCapacity": 128
}
```

Для доступа с другого устройства используйте VPN, например Tailscale/WireGuard, либо адрес локального интерфейса. Любая не-loopback или wildcard-привязка требует bearer token независимо от `requireToken`:

```bash
export DEEPBRAIN_API_TOKEN="$(openssl rand -hex 32)"
```

```json
"prefix": "http://+:8787/",
"requireToken": true,
"allowedOrigins": ["https://panel.example.lan"]
```

Не публикуйте порт 8787 напрямую в интернет. TLS и контроль удалённого доступа должны завершаться на VPN или доверенном reverse proxy.

## Примеры

```bash
curl http://127.0.0.1:8787/api/v1/health
curl -H "Authorization: Bearer $DEEPBRAIN_API_TOKEN" \
  http://SERVER:8787/api/v1/system/status | jq
curl -N -H "Authorization: Bearer $DEEPBRAIN_API_TOKEN" \
  http://SERVER:8787/api/v1/events
curl -X POST -H "Authorization: Bearer $DEEPBRAIN_API_TOKEN" \
  http://SERVER:8787/api/v1/llm/observe
```

Команда Host `api.status` показывает фактическую привязку и необходимость токена. Изменение адреса API требует перезапуска Host.
