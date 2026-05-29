# Bai 2 E2E Guide

## 1. Start Infrastructure

```powershell
docker compose up -d
```

Check:

- Kafka UI: <http://localhost:8080>
- Prometheus: <http://localhost:9090>
- Alertmanager: <http://localhost:9093>
- Kafka exporter: <http://localhost:9308/metrics>

## 2. Run Services

Use four terminals:

```powershell
dotnet run --project "backend-api\BackendApi\BackendApi.csproj" --launch-profile http
dotnet run --project "webhook-service\WebhookService\WebhookService.csproj" --launch-profile WebhookService
dotnet run --project "core-service\CoreService\CoreService.csproj" --launch-profile http
dotnet run --project "retry-service\RetryService\RetryService.csproj" --launch-profile RetryService
```

Expected ports:

- backend-api: <http://localhost:3000>
- webhook-service: <http://localhost:3001>
- core-service: <http://localhost:3002/health>
- retry-service: <http://localhost:3003/health>

## 3. Facebook Webhook Setup

Facebook cannot call localhost. Expose webhook-service with ngrok or an equivalent HTTPS tunnel:

```powershell
ngrok http 3001
```

In Meta App Webhooks:

- Callback URL: `https://<your-ngrok-domain>/webhook`
- Verify token: value from `webhook-service/WebhookService/appsettings.json` -> `Facebook:VerifyToken`
- Subscribe Page field: `feed`

## 4. Verify Each Comment

After posting a real comment on the Page:

1. Webhook logs should show normalized and published event count.
2. Kafka UI topic `raw_events` should receive the raw event.
3. Core Service logs should show event processing.
4. PostgreSQL should contain the event state:

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, intent, sentiment, status, reason from event_states order by updated_at desc limit 10;"
```

5. Kafka UI topic `reply_commands` should contain Core decisions.
6. Backend logs should show reply/hide/manual review command processing.
7. Failed Facebook calls should produce `send_failed`.
8. Retry Service should move retries to `send_retry`, then exhausted messages to `dead_letter`.

## 5. DLQ Alert Test

Produce a message to `dead_letter` and wait up to one minute:

```powershell
docker exec -i page-api-kafka-1 kafka-console-producer --bootstrap-server localhost:9092 --topic dead_letter
```

Then paste a JSON message and press Ctrl+C:

```json
{"command_id":"demo","event_id":"demo","retry_count":3,"final_error":"demo","payload":{}}
```

Prometheus alert `DeadLetterQueueReceived` should become active. Alertmanager will attempt Slack delivery after `alertmanager/alertmanager.yml` has a real Slack webhook URL.
