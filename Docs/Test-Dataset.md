# Bộ dữ liệu test Bài 2 và Bài 3

Tài liệu này là kịch bản test end-to-end cho Facebook Page thật. Mục tiêu là dùng 3 tài khoản người dùng khác nhau để kiểm hết luồng Bài 2 và Bài 3 mà không làm các trạng thái như spam/blacklist ảnh hưởng lẫn nhau.

## 1. Quy ước tài khoản

| Tài khoản | Vai trò | Dùng để test | Lưu ý |
|---|---|---|---|
| User A | Khách hàng bình thường | Reply tự động: hỏi giá, tích cực, trung tính, tiêu cực | Không dùng để spam, để tránh bị blacklist |
| User B | Người spam | Spam link, spam keyword, spam lặp, blacklist | Sau khi bị blacklist thì mọi comment thường sẽ không được reply |
| User C | Người test biên | Unknown/manual review và rate limit | Dùng comment nhanh nhiều lần trong 1 phút |
| Page | Chính Page `Khanh Education` | Page tự reply/comment | Webhook phải bỏ qua để tránh vòng lặp |

Nếu đã test trước đó và user bị blacklist, hãy reset dữ liệu demo theo `actor_id` của user đó trước khi chạy lại. Xem lệnh ở mục `5.1`.

## 2. Luồng topic cần quan sát

| Bước | Producer | Kafka topic / DB | Consumer / nơi xử lý | Mục đích |
|---|---|---|---|---|
| Webhook nhận comment | `webhook-service` | `raw_events` | `core-service` | Chứng minh Facebook event đã vào pipeline |
| Core quyết định action | `core-service` | `reply_commands` | `backend-api` | Chứng minh AI/rule đã tạo command |
| Backend gửi Facebook lỗi | `backend-api` | `send_failed` + `failed_commands` | `retry-service` | Chứng minh lỗi không bị mất |
| Retry còn lượt | `retry-service` | `send_retry` | `backend-api` | Chứng minh retry backoff |
| Retry hết lượt | `retry-service` | `dead_letter` | Prometheus/Alertmanager | Chứng minh DLQ và alert |
| Kết quả xử lý | Các service | PostgreSQL tables | Người kiểm thử | `event_states`, `processed_commands`, `manual_review_items`, `actor_spam_events`, `blacklisted_actors` |

## 3. Bảng comment test theo tài khoản

Chạy theo đúng thứ tự dưới đây. Không dùng User B cho các case reply bình thường sau khi đã bắt đầu test spam.

### 3.1 User A - reply automation

| STT | Bài | Comment | Kỳ vọng trong DB | Kafka/Action | Kết quả Facebook |
|---:|---|---|---|---|---|
| A1 | Bài 2 | `Shop ơi giá bao nhiêu?` | `intent=ask_price`, `sentiment=neutral`, `reason=price_or_info_question` | `reply_commands`, action `reply` | Page reply: `Cảm ơn bạn đã quan tâm. Khanh Education sẽ gửi thông tin chi tiết cho bạn sớm.` |
| A2 | Bài 2/3 | `Bài viết hay quá` | `intent=praise`, `sentiment=positive`, `reason=positive_engagement` hoặc `ai_positive_engagement` | `reply_commands`, action `reply` | Page reply: `Cảm ơn bạn đã ủng hộ Khanh Education.` |
| A3 | Bài 3 | `Dịch vụ rất tốt, mình sẽ quay lại` | `intent=praise`, `sentiment=positive` | `reply_commands`, action `reply` | Page reply: `Cảm ơn bạn đã ủng hộ Khanh Education.` |
| A4 | Bài 3 | `Sản phẩm tạm ổn` | `intent=neutral_feedback`, `sentiment=neutral`, `reason=neutral_feedback_acknowledged` hoặc `ai_neutral_feedback_acknowledged` | `reply_commands`, action `reply` | Page reply: `Cảm ơn bạn đã chia sẻ. Bên mình đã ghi nhận ý kiến của bạn.` |
| A5 | Bài 2/3 | `Mình chưa nhận được hàng` | `intent=complaint`, `sentiment=negative`, `reason=complaint_apology_and_review` hoặc `ai_apology_and_review`; có `manual_review_items` | `reply_commands`, action `reply` | Page reply: `Rất xin lỗi vì trải nghiệm chưa tốt. Bên mình sẽ kiểm tra ngay.` |
| A6 | Bài 3 | `Trải nghiệm quá tệ` | `intent=complaint`, `sentiment=negative`; có `manual_review_items` | `reply_commands`, action `reply` | Page reply xin lỗi giống A5 |

### 3.2 User B - spam, hide, blacklist

| STT | Bài | Comment | Kỳ vọng trong DB | Kafka/Action | Kết quả Facebook |
|---:|---|---|---|---|---|
| B1 | Bài 2/3 | `Xem ngay ưu đãi tại http://spam-example.test` | `intent=spam`, `sentiment=negative`, `reason=link_or_scam_detected`; có `actor_spam_events` và `manual_review_items` | `reply_commands`, action `hide_and_review` | Comment bị hide |
| B2 | Bài 2/3 | `Nhận quà tại telegram abc` | `intent=spam`, `sentiment=negative`, `reason=spam_keyword_detected`; có `actor_spam_events` và `manual_review_items` | `reply_commands`, action `hide_and_review` | Comment bị hide |
| B3 | Bài 2 | `Quảng cáo lặp lại http://spam-example.test` | `intent=spam`; spam count tăng | `reply_commands`, action `hide_and_review` | Comment bị hide |
| B4 | Bài 2 | `Quảng cáo lặp lại http://spam-example.test` | Đủ ngưỡng spam 3 lần/24h thì có `blacklisted_actors` | `reply_commands`, action `hide_and_review` | Comment bị hide, user vào blacklist |
| B5 | Bài 2 | `Shop tư vấn giúp` | `intent=blacklisted_actor`, `reason=actor_is_blacklisted` | Không publish command mới hoặc action `none` | Không auto reply |

Ghi chú: Sau B4, User B đã bị blacklist. Đây là trạng thái đúng, không phải lỗi không reply.

### 3.3 User C - unknown và rate limit

| STT | Bài | Comment | Kỳ vọng trong DB | Kafka/Action | Kết quả Facebook |
|---:|---|---|---|---|---|
| C1 | Bài 2 | `abc xyz` | `intent=unknown`, `sentiment=neutral`, `reason=fallback_manual_review` hoặc `ai_unknown_manual_review`; có `manual_review_items` | `reply_commands`, action `manual_review` | Không auto reply |
| C2 | Bài 2 | Gửi thật nhanh 21 comment trong 1 phút: `rate test 01` đến `rate test 21` | Comment thứ 21 hoặc sau đó có `intent=rate_limited`, `reason=actor_rate_limit_exceeded` | `reply_commands`, action `manual_review` | Không auto reply/hide |

Rate limit hiện được cấu hình `RateLimitPerMinute=20`. Vì vậy cần vượt 20 comment/phút bằng cùng một user.

### 3.4 Page - chặn vòng lặp Page tự reply

| STT | Bài | Cách test | Kỳ vọng trong DB/Kafka | Kết quả Facebook |
|---:|---|---|---|---|
| P1 | Bài 2 | Đăng comment bằng chính Page, ví dụ `Cảm ơn bạn đã ủng hộ Khanh Education.` | Webhook normalizer bỏ qua event do Page tạo; không có `event_states` mới tương ứng, không có `reply_commands` | Không tạo vòng lặp auto reply |

Trong giao diện Facebook, comment do Page thường có nhãn `Author`. Case này phải không được xử lý.

## 4. Retry, send_retry, dead_letter và alert

Các case dưới đây không cần 3 tài khoản Facebook. Chúng dùng Kafka để chứng minh luồng lỗi.

| STT | Bài | Cách test | Topic cần thấy | Kỳ vọng |
|---:|---|---|---|---|
| R1 | Bài 3 | Chạy automated test realtime | Không cần Kafka live | Test `Retry planner uses 1s 2s 4s and then dead letter` pass |
| R2 | Bài 3 | Đẩy message vào `send_failed` với `retry_count=2` | `send_failed -> send_retry` | Retry Service chờ 4 giây rồi publish `send_retry` với `retry_count=3` |
| R3 | Bài 3 | Đẩy message vào `send_failed` với `retry_count=3` | `send_failed -> dead_letter` | Retry Service publish `dead_letter` |
| R4 | Bài 3 | Đẩy message trực tiếp vào `dead_letter` | `dead_letter` | Prometheus alert `DeadLetterQueueReceived`, Alertmanager gửi Discord |

### 4.0 Kiểm tra monitoring trước khi test alert

Trước khi chạy case R4, phải chắc chắn Kafka Exporter đang chạy. Nếu exporter bị `Exited`, Prometheus không có metric Kafka nên alert sẽ luôn `Inactive` dù topic `dead_letter` đã có message.

```powershell
docker compose ps -a kafka-exporter
```

Cần thấy `kafka-exporter` ở trạng thái `Up`. Nếu thấy `Exited`, chạy lại:

```powershell
docker compose up -d kafka-exporter
```

Sau đó mở Prometheus `Status -> Targets`, target `kafka` phải là `UP`. Có thể kiểm tra nhanh bằng API:

```powershell
Invoke-RestMethod -Uri "http://localhost:9090/api/v1/query?query=kafka_topic_partition_current_offset%7Btopic%3D%22dead_letter%22%7D" | ConvertTo-Json -Depth 6
```

Kết quả cần có series cho topic `dead_letter`. Nếu `result` rỗng thì chưa test alert, hãy sửa Kafka Exporter trước.

### 4.1 Automated retry test

```powershell
dotnet run --no-restore --project "realtime-tests\PageApi.Realtime.Tests\PageApi.Realtime.Tests.csproj"
```

Cần thấy:

```text
PASS Retry planner uses 1s 2s 4s and then dead letter
```

### 4.2 Đẩy message vào `send_failed` để test `send_retry`

```powershell
docker exec -i page-api-kafka-1 kafka-console-producer --bootstrap-server localhost:9092 --topic send_failed
```

Paste:

```json
{"schema_version":1,"command_id":"dataset-retry-demo","event_id":"dataset-retry-demo","retry_count":2,"last_error":"demo transient facebook error","next_retry_at":null,"payload":{"schema_version":1,"command_id":"dataset-retry-demo","event_id":"dataset-retry-demo","action":"reply","target":{"page_id":"YOUR_PAGE_ID","comment_id":"demo-comment-id","user_id":"demo-user"},"reply_text":"demo","intent":"complaint","sentiment":"negative","retry_count":2}}
```

Kiểm tra `send_retry`:

```powershell
docker exec -it page-api-kafka-1 kafka-console-consumer --bootstrap-server localhost:9092 --topic send_retry --from-beginning --max-messages 5
```

### 4.3 Đẩy message vào `send_failed` để test DLQ từ Retry Service

```powershell
docker exec -i page-api-kafka-1 kafka-console-producer --bootstrap-server localhost:9092 --topic send_failed
```

Paste:

```json
{"schema_version":1,"command_id":"dataset-dlq-demo","event_id":"dataset-dlq-demo","retry_count":3,"last_error":"demo exhausted retry","next_retry_at":null,"payload":{"schema_version":1,"command_id":"dataset-dlq-demo","event_id":"dataset-dlq-demo","action":"reply","target":{"page_id":"YOUR_PAGE_ID","comment_id":"demo-comment-id","user_id":"demo-user"},"reply_text":"demo","intent":"complaint","sentiment":"negative","retry_count":3}}
```

Kiểm tra `dead_letter`:

```powershell
docker exec -it page-api-kafka-1 kafka-console-consumer --bootstrap-server localhost:9092 --topic dead_letter --from-beginning --max-messages 10
```

### 4.4 Đẩy trực tiếp vào `dead_letter` để test alert

Cách nhanh, ít lỗi thao tác:

```powershell
docker exec page-api-kafka-1 /bin/sh -c 'echo dataset-alert-demo-001 | kafka-console-producer --bootstrap-server localhost:9092 --topic dead_letter'
```

Hoặc dùng producer interactive:

```powershell
docker exec -i page-api-kafka-1 kafka-console-producer --bootstrap-server localhost:9092 --topic dead_letter
```

Paste:

```json
{"command_id":"dataset-alert-demo","event_id":"dataset-alert-demo","retry_count":3,"final_error":"demo","payload":{}}
```

Đợi khoảng 15-30 giây, sau đó kiểm tra:

- Prometheus: alert `DeadLetterQueueReceived`.
- Alertmanager: alert đang firing.
- Discord: nhận thông báo.

Nếu Prometheus vẫn `Inactive`, kiểm tra expression trực tiếp:

```powershell
Invoke-RestMethod -Uri "http://localhost:9090/api/v1/query?query=increase(kafka_topic_partition_current_offset%7Btopic%3D%22dead_letter%22%7D%5B1m%5D)%20%3E%200" | ConvertTo-Json -Depth 6
```

Nếu expression có kết quả nhưng Discord chưa hiện, kiểm tra metric gửi notification của Alertmanager:

```powershell
$metrics=(Invoke-WebRequest -Uri "http://localhost:9093/metrics" -UseBasicParsing).Content
$metrics -split "`n" | Where-Object { $_ -match '^alertmanager_notifications_(total|failed_total).*discord' }
```

## 5. Lệnh kiểm chứng PostgreSQL

### 5.1 Reset trạng thái cho một user demo

Chỉ dùng khi muốn chạy lại test từ đầu cho một tài khoản đã bị blacklist. Thay `ACTOR_ID` bằng `user_id` lấy từ `event_states`.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "delete from blacklisted_actors where actor_id='ACTOR_ID'; delete from actor_spam_events where actor_id='ACTOR_ID'; delete from actor_activity where actor_id='ACTOR_ID';"
```

### 5.2 Xem 30 event mới nhất

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, user_id, message, intent, sentiment, status, reason, updated_at from event_states order by updated_at desc limit 30;"
```

### 5.3 Xem command đã xử lý

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select command_id, action, event_id, status, processed_at from processed_commands order by processed_at desc limit 30;"
```

### 5.4 Xem review queue

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select id, event_id, actor_id, reason, created_at from manual_review_items order by created_at desc limit 30;"
```

### 5.5 Xem spam và blacklist

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select actor_id, event_id, reason, created_at from actor_spam_events order by created_at desc limit 30; select actor_id, reason, created_at from blacklisted_actors order by created_at desc limit 30;"
```

### 5.6 Xem failed commands

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select command_id, event_id, retry_count, last_error, status, next_retry_at, created_at from failed_commands order by created_at desc limit 20;"
```

## 6. Checklist chụp bằng chứng

| Nhóm bằng chứng | Cần chụp |
|---|---|
| Facebook UI | Reply của Page cho User A; spam của User B bị hide; Page-authored comment không tạo loop |
| PostgreSQL | `event_states`, `processed_commands`, `manual_review_items`, `actor_spam_events`, `blacklisted_actors`, `failed_commands` nếu có lỗi thật |
| Kafka UI | Topics `raw_events`, `reply_commands`, `send_failed`, `send_retry`, `dead_letter` |
| Monitoring | Prometheus alert `DeadLetterQueueReceived`, Alertmanager firing alert, Discord notification |
| Tests tự động | Output realtime tests và Backend API tests pass |

## 7. Lưu ý tránh hiểu nhầm

- Không dùng một tài khoản đã bị spam/blacklist để test positive/negative reply, vì user blacklist sẽ nhận `action=none`.
- Negative comment phải được đăng bằng user thật, không phải Page. Comment do Page tạo sẽ bị bỏ qua.
- Direct publish vào `dead_letter` chỉ chứng minh alert. Muốn chứng minh retry chain thì dùng automated test hoặc đẩy message vào `send_failed`.
- Nếu vừa đổi code reply text, cần restart `core-service` trước khi test live.
