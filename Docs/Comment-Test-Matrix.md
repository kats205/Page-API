# Bài 2 - Bảng dữ liệu test và lệnh kiểm chứng

File này dùng để kiểm tra nhanh luồng comment thật trên Facebook Page sau khi hệ thống đã chạy đủ `webhook-service`, `core-service`, `backend-api`, `retry-service`, Kafka và PostgreSQL.

## 1. Bảng dữ liệu test

| STT | Mục tiêu test | Comment mẫu | Kỳ vọng trong `event_states` | Action kỳ vọng | Kết quả trên Facebook | Lệnh kiểm chứng nhanh |
|---:|---|---|---|---|---|---|
| 1 | Hỏi giá | `Shop ơi giá bao nhiêu?` | `intent=ask_price`, `sentiment=neutral`, `status=replied`, `reason=price_or_info_question` | `reply` | Page reply: `Cam on ban da quan tam. Khanh Education se gui thong tin chi tiet cho ban som.` | Xem mục `2.1` |
| 2 | Khiếu nại | `Mình chưa nhận được hàng` | `intent=complaint`, `sentiment=negative`, `status=pending_review`, `reason=complaint_or_support_needed` | `manual_review` | Không tự reply, không hide | Xem mục `2.2` |
| 3 | Khen bài viết | `Bài viết hay quá` | `intent=praise`, `sentiment=positive`, `status=replied`, `reason=positive_engagement` | `reply` | Page reply: `Cam on ban da ung ho Khanh Education.` | Xem mục `2.3` |
| 4 | Spam có link | `Xem ngay ưu đãi tại http://spam-example.test` | `intent=spam`, `sentiment=negative`, `reason=link_or_scam_detected` | `hide_and_review` | Comment bị hide và đưa vào review | Xem mục `2.4` |
| 5 | Spam keyword không link | `Nhận quà tại telegram abc` | `intent=spam`, `sentiment=negative`, `reason=spam_keyword_detected` | `hide_and_review` | Comment bị hide và đưa vào review | Xem mục `2.4` |
| 6 | Spam lặp 3 lần | Cùng user comment spam 3 lần trong 24h | Có bản ghi spam; sau lần đủ ngưỡng có actor trong `blacklisted_actors` | `hide_and_review` | Các comment spam bị hide/review | Xem mục `2.5` |
| 7 | User đã blacklist | Sau khi user bị blacklist, comment: `Shop tư vấn giúp` | `intent=blacklisted_actor`, `status=processed`, `reason=actor_is_blacklisted` | `none` | Không auto reply | Xem mục `2.6` |
| 8 | Rate limit | Một user gửi từ 20 comment/phút trở lên | `intent=rate_limited`, `status=pending_review`, `reason=actor_rate_limit_exceeded` | `manual_review` | Không auto reply/hide | Xem mục `2.7` |
| 9 | Nội dung không rõ | `abc xyz` | `intent=unknown`, `status=pending_review`, `reason=fallback_manual_review` hoặc `ai_unknown_manual_review` | `manual_review` | Không auto reply | Xem mục `2.8` |
| 10 | Page tự reply | Reply do chính `Khanh Education` tạo | Bị chặn ở webhook normalizer, không tạo event mới | Không có command | Không còn vòng lặp reply | Xem mục `2.9` |
| 11 | Retry / DLQ | Tạo lỗi Facebook API hoặc đẩy message test vào `dead_letter` | Có `failed_commands` nếu lỗi qua backend; có message ở `dead_letter` nếu retry hết | `send_failed -> send_retry -> dead_letter` | Alert Discord khi DLQ tăng | Xem mục `2.10` |

## 2. Lệnh kiểm chứng

### 2.1 Hỏi giá

Tác dụng: kiểm tra comment hỏi giá đã được Core Service phân loại thành `ask_price` và Backend API đã xử lý command `reply`.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, message, intent, sentiment, status, reason from event_states where message ilike '%giá%' or message ilike '%gia%' order by updated_at desc limit 5;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select command_id, action, event_id, status, processed_at from processed_commands where action='reply' order by processed_at desc limit 10;"
```

### 2.2 Khiếu nại

Tác dụng: kiểm tra comment khiếu nại được đưa vào trạng thái chờ admin review thay vì tự động reply.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, message, intent, sentiment, status, reason from event_states where message ilike '%chưa nhận%' or message ilike '%chua nhan%' order by updated_at desc limit 5;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select id, event_id, actor_id, reason, created_at from manual_review_items order by created_at desc limit 10;"
```

### 2.3 Khen bài viết

Tác dụng: kiểm tra comment tích cực được nhận diện là `praise` và bot chỉ reply một lần.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, message, intent, sentiment, status, reason from event_states where message ilike '%hay quá%' or message ilike '%hay qua%' order by updated_at desc limit 5;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select command_id, action, event_id, status, processed_at from processed_commands where action='reply' order by processed_at desc limit 10;"
```

### 2.4 Spam có link hoặc spam keyword

Tác dụng: kiểm tra comment spam bị đánh dấu, ghi lịch sử spam và đưa vào hàng chờ review.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, message, intent, sentiment, status, reason from event_states where message ilike '%http%' or message ilike '%www.%' or message ilike '%telegram%' or message ilike '%nhận quà%' or message ilike '%nhan qua%' order by updated_at desc limit 10;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select actor_id, event_id, reason, created_at from actor_spam_events order by created_at desc limit 10;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select id, event_id, actor_id, reason, created_at from manual_review_items order by created_at desc limit 10;"
```

### 2.5 Blacklist sau spam 3 lần

Tác dụng: kiểm tra user spam lặp lại đủ ngưỡng đã được đưa vào blacklist nội bộ.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select actor_id, reason, created_at from blacklisted_actors order by created_at desc limit 10;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select actor_id, count(*) as spam_count from actor_spam_events where created_at >= now() - interval '24 hours' group by actor_id order by spam_count desc;"
```

### 2.6 User đã blacklist

Tác dụng: kiểm tra actor đã blacklist không còn được auto reply dù comment mới có nội dung bình thường.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, user_id, message, intent, status, reason, updated_at from event_states where reason='actor_is_blacklisted' order by updated_at desc limit 10;"
```

### 2.7 Rate limit

Tác dụng: kiểm tra user gửi quá nhiều comment trong 1 phút bị chuyển sang `pending_review`.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, user_id, message, intent, status, reason, updated_at from event_states where reason='actor_rate_limit_exceeded' order by updated_at desc limit 10;"
```

### 2.8 Nội dung không rõ

Tác dụng: kiểm tra nội dung không rõ ý định không bị auto reply, mà được chuyển sang manual review.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, message, intent, sentiment, status, reason from event_states where reason in ('fallback_manual_review','ai_unknown_manual_review') order by updated_at desc limit 10;"
```

### 2.9 Kiểm tra không còn vòng lặp Page tự reply

Tác dụng: kiểm tra hệ thống không còn tạo chuỗi reply liên tiếp do webhook nhận lại comment do chính Page tạo.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select message, intent, status, reason, updated_at from event_states order by updated_at desc limit 30;"
```


Kỳ vọng: không thấy chuỗi nhiều event mới liên tiếp chỉ chứa nội dung reply của Page như `Cam on ban da ung ho Khanh Education.`

### 2.10 Retry và dead letter

Kiểm tra lỗi Facebook API:

Tác dụng: kiểm tra Backend API có ghi nhận lỗi gọi Facebook Graph API và tạo dữ liệu cho Retry Service.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select command_id, event_id, retry_count, last_error, status, next_retry_at, created_at from failed_commands order by created_at desc limit 10;"
```

Kiểm tra topic `dead_letter`:

Tác dụng: kiểm tra các message đã retry hết số lần tối đa có được chuyển vào DLQ hay không.

```powershell
docker exec -it page-api-kafka-1 kafka-console-consumer --bootstrap-server localhost:9092 --topic dead_letter --from-beginning --max-messages 10
```

Đẩy thử một message vào `dead_letter` để test alert:

Tác dụng: tạo dữ liệu lỗi giả để kiểm tra Prometheus, Alertmanager và Discord alert mà không cần chờ lỗi thật.

```powershell
docker exec -i page-api-kafka-1 kafka-console-producer --bootstrap-server localhost:9092 --topic dead_letter
```

Sau đó paste:

```json
{"command_id":"demo","event_id":"demo","retry_count":3,"final_error":"demo","payload":{}}
```

Rồi nhấn `Ctrl+C`, sau đó kiểm tra Prometheus, Alertmanager và Discord.

## 3. Lệnh xem tổng quan nhanh

Tác dụng: xem số lượng bản ghi ở các bảng chính để biết pipeline đã tạo dữ liệu ở những bước nào.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select count(*) as event_states from event_states; select count(*) as processed_commands from processed_commands; select count(*) as manual_review_items from manual_review_items; select count(*) as actor_spam_events from actor_spam_events; select count(*) as blacklisted_actors from blacklisted_actors; select count(*) as failed_commands from failed_commands;"
```

Tác dụng: xem 20 event mới nhất để đối chiếu nhanh message, intent, sentiment, status và reason.

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, message, intent, sentiment, status, reason, updated_at from event_states order by updated_at desc limit 20;"
```

## 4. Bài 3 - sentiment automation

Các case này dùng để chứng minh riêng phần Bài 3: AI/sentiment không chỉ phân loại mà còn chọn hành động tự động.

| STT | Mục tiêu test | Comment mẫu | Kỳ vọng trong `event_states` | Action kỳ vọng | Kết quả trên Facebook |
|---:|---|---|---|---|---|
| 12 | Tích cực | `Dịch vụ rất tốt, mình sẽ quay lại` | `intent=praise`, `sentiment=positive`, `reason=positive_engagement` hoặc `ai_positive_engagement` | `reply` | Page reply cảm ơn |
| 13 | Trung tính | `Sản phẩm tạm ổn` | `intent=neutral_feedback`, `sentiment=neutral`, `reason=neutral_feedback_acknowledged` hoặc `ai_neutral_feedback_acknowledged` | `reply` | Page reply ghi nhận ý kiến |
| 14 | Tiêu cực | `Trải nghiệm quá tệ` | `intent=complaint`, `sentiment=negative`, `reason=complaint_apology_and_review` hoặc `ai_apology_and_review` | `reply` và review nội bộ | Page reply xin lỗi, đồng thời có `manual_review_items` |
| 15 | Spam | `Quảng cáo lặp lại http://spam-example.test` | `intent=spam`, `sentiment=negative` | `hide_and_review` | Comment bị hide và có `manual_review_items` |

Lệnh kiểm chứng chung:

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select event_id, message, intent, sentiment, status, reason, updated_at from event_states order by updated_at desc limit 20;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select id, event_id, actor_id, reason, created_at from manual_review_items order by created_at desc limit 20;"
```

```powershell
docker exec -it page-api-postgres-1 psql -U postgres -d pageapi -c "select command_id, action, event_id, status, processed_at from processed_commands order by processed_at desc limit 20;"
```
