# Hướng dẫn chạy Bài 2 trên Facebook Page thật

Tài liệu này chỉ hướng dẫn cách cấu hình và chạy dự án Bài 2 với Facebook Page thật. Phần dữ liệu test, kết quả kỳ vọng và lệnh kiểm chứng chi tiết nằm trong [Bai2-Test-Matrix.md](./Bai2-Test-Matrix.md).

## 1. Điều kiện cần trước khi chạy

Bạn cần chuẩn bị:

- Docker Desktop đang chạy.
- .NET SDK phù hợp với solution.
- `ngrok` hoặc công cụ tunnel HTTPS tương đương.
- Một Facebook Page thật mà bạn quản lý.
- Một Facebook App đã cấu hình Webhooks cho Page.
- Page Access Token còn hạn và có quyền thao tác với Page.
- Gemini API key nếu muốn Core Service phân loại intent/sentiment bằng AI thật.

## 2. Kiểm tra cấu hình bắt buộc

### 2.1 `webhook-service`

File: [appsettings.json](<d:\coding for Future\API\Page-API\webhook-service\WebhookService\appsettings.json>)

Kiểm tra các giá trị:

- `Facebook:VerifyToken`: dùng để Meta verify callback URL.
- `Facebook:AppSecret`: dùng để xác thực chữ ký webhook `X-Hub-Signature-256`.
- `Facebook:PageId`: Page ID thật.
- `Facebook:PageAccessToken`: Page Access Token còn hạn.

Nếu đổi Page Access Token, hãy restart `webhook-service`.

### 2.2 `backend-api`

File ưu tiên khi chạy local Development: [appsettings.Development.json](<d:\coding for Future\API\Page-API\backend-api\BackendApi\appsettings.Development.json>)

Kiểm tra các giá trị:

- `Facebook:PageAccessToken`: Page Access Token thật.
- `Facebook:PageId`: Page ID thật.
- `AdminAuth:ApiKey`: API key để test các API admin nếu cần.

Nếu `backend-api` không có token thật thì pipeline vẫn nhận webhook và xử lý Core Service, nhưng sẽ không reply/hide được comment trên Facebook.

### 2.3 `core-service`

File: [appsettings.Development.json](<d:\coding for Future\API\Page-API\core-service\CoreService\appsettings.Development.json>)

Kiểm tra:

- `Gemini:ApiKey`: Gemini API key thật.
- `Gemini:Model`: hiện dùng `gemini-2.5-flash`.
- `Kafka:RawEventsTopic`: `raw_events`.
- `Kafka:ReplyCommandsTopic`: `reply_commands`.

Nếu Gemini hết quota hoặc lỗi, Core Service sẽ log warning và fallback sang rule-based classification.

### 2.4 `retry-service`

File: [appsettings.Development.json](<d:\coding for Future\API\Page-API\retry-service\RetryService\appsettings.Development.json>)

Kiểm tra:

- `Kafka:SendFailedTopic`: `send_failed`.
- `Kafka:SendRetryTopic`: `send_retry`.
- `Kafka:DeadLetterTopic`: `dead_letter`.
- `Retry:MaxAttempts`: số lần retry tối đa.

## 3. Khởi động hạ tầng

Tại thư mục root repo, chạy:

```powershell
docker compose up -d
```

Mở các công cụ quan sát:

- Kafka UI: <http://localhost:8080>
- Prometheus: <http://localhost:9090>
- Alertmanager: <http://localhost:9093>
- Kafka exporter: <http://localhost:9308/metrics>

Kiểm tra Kafka topic:

```powershell
docker exec -it page-api-kafka-1 kafka-topics --bootstrap-server localhost:9092 --list
```

Cần thấy các topic chính:

- `raw_events`
- `reply_commands`
- `send_failed`
- `send_retry`
- `dead_letter`

## 4. Chạy 4 service

Mở 4 terminal riêng tại thư mục root repo.

### Terminal 1 - `backend-api`

```powershell
dotnet run --project "backend-api\BackendApi\BackendApi.csproj" --launch-profile http
```

Địa chỉ thường dùng:

- Swagger: <http://localhost:3000/swagger>

### Terminal 2 - `webhook-service`

```powershell
dotnet run --project "webhook-service\WebhookService\WebhookService.csproj" --launch-profile WebhookService
```

Địa chỉ local:

- Webhook endpoint: <http://localhost:3001/webhook>
- Health: <http://localhost:3001/health>

### Terminal 3 - `core-service`

```powershell
dotnet run --project "core-service\CoreService\CoreService.csproj" --launch-profile http
```

Địa chỉ local:

- Health: <http://localhost:3002/health>

### Terminal 4 - `retry-service`

```powershell
dotnet run --project "retry-service\RetryService\RetryService.csproj" --launch-profile RetryService
```

Địa chỉ local:

- Health: <http://localhost:3003/health>

## 5. Mở public URL cho webhook

Facebook không gọi được `localhost`, nên cần tunnel `webhook-service`:

```powershell
ngrok http 3001
```

Lấy URL HTTPS mà ngrok cấp, ví dụ:

```text
https://abcxyz.ngrok-free.app
```

Callback URL dùng trong Meta sẽ là:

```text
https://abcxyz.ngrok-free.app/webhook
```

Bạn có thể mở giao diện ngrok để xem request gửi vào:

```text
http://127.0.0.1:4040
```

## 6. Cấu hình Meta Webhooks

Trong Meta for Developers:

1. Vào Facebook App của dự án.
2. Mở `Webhooks`.
3. Chọn object `Page`.
4. Add hoặc Edit callback URL.
5. Nhập Callback URL là URL ngrok dạng `https://<ngrok-domain>/webhook`.
6. Nhập Verify Token đúng với `Facebook:VerifyToken` trong `webhook-service`.
7. Subscribe field `feed`.
8. Đảm bảo Page thật đã subscribe app.

Nếu chỉ bật field `feed` trong màn hình Webhooks nhưng Page chưa subscribe app, Facebook sẽ không gửi comment thật về webhook.

Có thể subscribe Page bằng Graph API:

```powershell
$token="PAGE_ACCESS_TOKEN_THAT"
$pageId="PAGE_ID_THAT"
Invoke-RestMethod -Method Post -Uri "https://graph.facebook.com/v19.0/$pageId/subscribed_apps" -Body @{
  subscribed_fields="feed"
  access_token=$token
}
```

Kiểm tra lại Page đã subscribe:

```powershell
Invoke-RestMethod -Method Get -Uri "https://graph.facebook.com/v19.0/$pageId/subscribed_apps?access_token=$token"
```

Kết quả đúng phải có app của bạn và `subscribed_fields` chứa `feed`.

## 7. Luồng chạy sau khi đã cấu hình xong

Khi có comment thật trên Page:

1. Facebook gửi `POST /webhook` vào ngrok.
2. `webhook-service` xác thực chữ ký, normalize event và publish vào `raw_events`.
3. `core-service` consume `raw_events`, phân loại spam/intent/sentiment và publish command vào `reply_commands`.
4. `backend-api` consume command và gọi Facebook Graph API để reply/hide/manual review.
5. Nếu gọi Facebook lỗi, `backend-api` publish `send_failed`.
6. `retry-service` xử lý retry qua `send_retry`, quá số lần thì vào `dead_letter`.
7. Prometheus và Alertmanager theo dõi `dead_letter` để gửi cảnh báo Discord.

## 8. Lỗi thường gặp khi chạy

### Không thấy request mới trong ngrok

Nguyên nhân thường gặp:

- Page chưa subscribe app.
- Callback URL trong Meta đang là URL ngrok cũ.
- App chưa subscribe field `feed`.
- Comment được tạo trước khi Page subscribe app.

### Webhook nhận request nhưng không publish Kafka

Nguyên nhân thường gặp:

- Payload là test event của Meta, thường có `entry.id = "0"` và `item = "status"`.
- Comment do chính Page tạo nên bị normalizer bỏ qua để tránh vòng lặp reply.
- Page ID trong payload không khớp `Facebook:PageId`.

### Có `raw_events` nhưng không có command

Nguyên nhân thường gặp:

- `core-service` chưa chạy.
- Kafka hoặc PostgreSQL chưa sẵn sàng.
- Event trùng `event_id` nên bị idempotency bỏ qua.

### Có command nhưng không reply/hide trên Facebook

Nguyên nhân thường gặp:

- `backend-api` chưa chạy.
- Page Access Token trong `backend-api` hết hạn hoặc thiếu quyền.
- Comment ID không còn tồn tại.
- Facebook API trả lỗi và message bị chuyển sang `send_failed`.

## 9. Tài liệu test và kiểm chứng

Sau khi hệ thống chạy ổn, dùng file sau để test từng case và kiểm chứng kết quả:

- [Bai2-Test-Matrix.md](./Bai2-Test-Matrix.md)
- [Comment-Test-Matrix.md](./Comment-Test-Matrix.md)
- [Bai3-E2E-Guide.md](./Bai3-E2E-Guide.md)

File đó chứa:

- Bộ comment mẫu.
- Kết quả kỳ vọng.
- Action kỳ vọng.
- Lệnh kiểm tra PostgreSQL, Kafka, retry, DLQ và alert Discord.

## 10. Ghi chú riêng cho Bài 3

Bài 3 dùng lại pipeline Bài 2 nhưng chấm riêng phần AI sentiment và automation:

1. Positive comment được reply cảm ơn.
2. Neutral feedback được reply ghi nhận.
3. Negative comment được reply xin lỗi và đưa vào manual review.
4. Spam comment được hide và đưa vào manual review.
5. Retry, circuit breaker, idempotency, DLQ và alert được chứng minh bằng các lệnh trong [Bai3-E2E-Guide.md](./Bai3-E2E-Guide.md).

Alertmanager trong repo hiện gửi Discord thay vì Slack. Khi trình bày, cần nói rõ đây là lựa chọn demo vận hành theo kênh đã cấu hình.
