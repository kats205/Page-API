# Bài tập thực hành Lập trình API
## Hệ thống quản lý Facebook Page phân tán

**Học phần:** Lập trình API  
**Ngày:** 9 tháng 5 năm 2026

---

## 1. Tổng quan

Bài tập yêu cầu sinh viên thiết kế và triển khai một hệ thống phân tán kết nối với Facebook Graph API, xử lý sự kiện theo thời gian thực, truyền dữ liệu qua Kafka và phân tích cảm xúc bằng AI API.

**Mục tiêu cốt lõi:** Không chỉ gọi được API, mà còn hiểu cách nhiều service nhỏ phối hợp trong một hệ thống thực tế — thấy được luồng dữ liệu đi từ Facebook, qua các service xử lý, rồi quay trở lại hệ thống để phản hồi hoặc lưu trữ.

---

## 2. Kiến trúc hệ thống

### 2.1 Sơ đồ kiến trúc (text)

```
Facebook Page
    │ HTTP POST (comment / message)
    ▼
webhook-service (port 3001)
  - verify HMAC-SHA256
  - parse + normalize payload
    │ publish
    ▼
Kafka: raw_events
    │ consume
    ▼
core-service (port 3002)
  - AI: intent + sentiment
  - Automation rule engine
    │ publish
    ▼
Kafka: reply_commands
    │ consume
    ▼
backend-api (port 3000) ──────────────────► Facebook Graph API
  - check idempotency key                        (reply / hide / post)
  - gọi Graph API
    │ lỗi → publish
    ▼
Kafka: send_failed
    │ consume
    ▼
retry-service (port 3003)
  - exponential backoff: 1s × 2^retry_count
  - count < N  → publish send_retry → backend-api retry
  - count ≥ N  → publish dead_letter

Kafka: dead_letter
  - Prometheus theo dõi offset
  - Alertmanager → Slack / Email
```

### 2.2 Mô tả các service

#### Facebook Page
Khi có người dùng bình luận hoặc nhắn tin, Facebook gửi HTTP POST đến Webhook endpoint.

#### webhook-service (port 3001)
- Xác thực chữ ký HMAC-SHA256
- Parse payload JSON
- Normalize về schema chuẩn nội bộ (comment và message ra cùng một cấu trúc)
- Publish vào topic `raw_events`
- Trả 200 OK cho Facebook càng nhanh càng tốt

#### Kafka Broker — các topic

| Topic | Producer | Consumer |
|---|---|---|
| `raw_events` | webhook-service | core-service |
| `reply_commands` | core-service | backend-api |
| `send_retry` | retry-service | backend-api |
| `send_failed` | backend-api | retry-service |
| `dead_letter` | retry-service | _(không có consumer)_ |

#### core-service (port 3002)
Consume `raw_events`, xử lý 2 bước tuần tự:

1. **AI step:** Gọi LLM (OpenAI / Gemini / Claude / Grok) để:
   - Phân loại intent: hỏi giá, khiếu nại, spam, khen ngợi...
   - Phân tích sentiment: tích cực / trung tính / tiêu cực
2. **Automation step:** Rule engine quyết định: auto reply, ẩn bình luận, manual review, blacklist

Kết quả publish vào `reply_commands`.

#### backend-api (port 3000)
- Service duy nhất được gọi Facebook Graph API
- Consume `reply_commands` và `send_retry`
- Kiểm tra idempotency key trong Database trước khi gửi
- Thành công → lưu key
- Thất bại → publish `send_failed`
- Expose REST API cho dashboard quản trị

#### Database
Lưu idempotency key của từng `command_id` đã xử lý, đảm bảo mỗi reply chỉ gửi đúng một lần.

#### retry-service (port 3003)
- Consume `send_failed`
- Đọc `retry_count`, tính thời gian chờ: `1s × 2^retry_count`
- `count < N` → publish `send_retry`
- `count ≥ N` → publish `dead_letter`, dừng retry

#### Dead Letter Queue (topic `dead_letter`)
- Không phải service, chỉ là Kafka topic
- Prometheus theo dõi offset; khi tăng → Alertmanager → Slack/Email
- Admin xem qua Kafka UI (Kafdrop / Redpanda Console)

### 2.3 Quy ước port

| Service | Port | Vai trò |
|---|---|---|
| backend-api | 3000 | REST API, gọi Facebook Graph API |
| webhook-service | 3001 | Nhận webhook Facebook |
| core-service | 3002 | AI, sentiment, automation |
| retry-service | 3003 | Retry, DLQ |

### 2.4 Nguyên tắc giao tiếp

- **Mọi giao tiếp nội bộ qua Kafka** — các service không gọi nhau trực tiếp bằng HTTP
- **Chỉ backend-api gọi Facebook Graph API**
- **Idempotency bắt buộc** — mọi consumer xử lý an toàn khi nhận cùng một message nhiều lần
- **Retry có giới hạn** — tối đa N lần với exponential backoff, vượt ngưỡng → dead_letter

---

## 3. Chuẩn message giữa các service

Mỗi nhóm tự định nghĩa schema JSON, cần đảm bảo:

- Có `schema_version` để mở rộng về sau
- Các field định danh rõ ràng: `event_id`, `command_id`, `comment_id`, `page_id`
- Timestamp dùng ISO 8601: `2026-04-26T09:30:00Z`
- Field retry: `retry_count`, `last_error`, `next_retry_at`
- Phân biệt payload gốc Facebook với payload đã normalize

### Ví dụ JSON

**`raw_events`**
```json
{
  "schema_version": 1,
  "event_id": "evt_001",
  "event_type": "comment_created",
  "source": "facebook",
  "page_id": "123456789",
  "post_id": "post_001",
  "comment_id": "cmt_001",
  "user_id": "user_001",
  "message": "Shop oi gia bao nhieu?",
  "created_at": "2026-04-26T09:30:00Z"
}
```

**`reply_commands`**
```json
{
  "schema_version": 1,
  "command_id": "cmd_001",
  "event_id": "evt_001",
  "action": "reply",
  "target": {
    "page_id": "123456789",
    "comment_id": "cmt_001"
  },
  "reply_text": "Da shop da gui thong tin chi tiet qua inbox.",
  "intent": "ask_price",
  "sentiment": "neutral",
  "created_at": "2026-04-26T09:31:00Z"
}
```

**`send_failed` / `send_retry`**
```json
{
  "schema_version": 1,
  "command_id": "cmd_001",
  "event_id": "evt_001",
  "retry_count": 1,
  "last_error": "Facebook API timeout",
  "next_retry_at": "2026-04-26T09:31:05Z",
  "payload": {
    "action": "reply",
    "reply_text": "Da shop da gui thong tin chi tiet qua inbox."
  }
}
```

**`dead_letter`**
```json
{
  "schema_version": 1,
  "command_id": "cmd_001",
  "event_id": "evt_001",
  "retry_count": 3,
  "failed_at": "2026-04-26T09:33:00Z",
  "final_error": "Facebook API timeout after maximum retries",
  "original_topic": "send_failed",
  "payload": {
    "action": "reply",
    "target": { "page_id": "123456789", "comment_id": "cmt_001" },
    "reply_text": "Da shop da gui thong tin chi tiet qua inbox."
  }
}
```

---

## 4. Các bước sinh viên cần thực hiện

1. Tạo Facebook App, Facebook Page, cấu hình quyền Graph API và Webhooks
2. Xây dựng Backend API làm lớp proxy (frontend không gọi trực tiếp Facebook)
3. Cài đặt Webhook endpoint: verify webhook, HMAC-SHA256, đăng ký sự kiện comment
4. Thiết kế Kafka topic, producer, consumer
5. Xây dựng Core Service: AI intent/sentiment + automation rule engine
6. Xây dựng cơ chế lỗi hoàn chỉnh: Retry + DLQ + Prometheus + Alertmanager
7. Đảm bảo idempotency toàn pipeline
8. Kiểm thử luồng end-to-end: comment mới → webhook → Kafka → xử lý → phản hồi
9. Kiểm thử kịch bản lỗi: gửi Facebook thất bại, retry hết lần, DLQ alert

---

## 5. Bài 1 — Tích hợp Facebook API và xây dựng Backend

### Mục tiêu
Tích hợp Facebook Graph API, xây dựng backend trung gian.

### Yêu cầu

- Tạo Facebook Page và App, lấy Page Access Token
- Cài đặt các API:
  - `GET /posts`
  - `POST /post`
  - `GET /comments`
- Backend đóng vai trò proxy (frontend không gọi trực tiếp Facebook)
- Xác thực và phân quyền cho dashboard quản trị
- Chuẩn hóa response API, mã lỗi, thông báo lỗi
- Ghi log đầy đủ cho mọi request gửi đến Facebook
- Xử lý lỗi Facebook API có thể giám sát và khôi phục

### Kết quả mong đợi
- API hoạt động thành công
- Có thể tạo bài viết qua backend
- API có xử lý lỗi, log, response chuẩn hóa

---

## 6. Bài 2 — Xử lý thời gian thực với Webhook và Kafka

### Mục tiêu
Xây dựng hệ thống hướng sự kiện, xử lý theo thời gian thực.

### Yêu cầu

**Webhook + Kafka:**
- webhook-service (port 3001): nhận webhook, xác thực, parse, publish `raw_events`
- Normalize comment và message về cùng schema
- Đăng ký sự kiện bình luận từ Facebook

**Core Service pipeline:**
1. Phát hiện spam (link, nội dung lặp)
2. AI: intent + sentiment
3. Automation rule:
   - Spam nhẹ → ẩn ngay
   - Spam lặp 3 lần / 24h → blacklist nội bộ
   - Link độc hại → ẩn + manual review
   - Tái phạm nhiều lần → admin block thủ công

**Reliability:**
- Consumer chịu tải tăng đột biến, không mất dữ liệu
- Theo dõi trạng thái từng sự kiện: `received → processed → replied → failed`
- Rate limiting: 20 comment / 1 phút → `pending_review`

### Logic xử lý lỗi

- **Rate limiting:** tạm dừng AI và automation, chuyển sang `pending_review`
- **Retry:** tối đa N lần, exponential backoff (lần 1: 1s, lần 2: 2s, lần 3: 4s)
- **Circuit breaker:** 10 lỗi liên tiếp → ngắt gọi tạm thời
- **Idempotency:** `command_id` đã tồn tại trong DB → bỏ qua
- **DLQ + alert:** offset tăng → Prometheus → Alertmanager → Slack

### Kết quả mong đợi
- Sự kiện truyền qua Kafka thành công
- Dữ liệu lưu trữ và phân loại
- Hệ thống thể hiện rõ retry, circuit breaker, idempotent
- Message thất bại → DLQ → alert

---

## 7. Bài 3 — Phân tích cảm xúc bằng AI và tự động hóa

### Mục tiêu
Tích hợp AI, xây dựng hệ thống tự động hóa phản hồi.

### Yêu cầu phân tích cảm xúc

| Sentiment | Ví dụ | Hành động |
|---|---|---|
| Tích cực | "Dịch vụ rất tốt, mình sẽ quay lại" | Cảm ơn người dùng |
| Trung tính | "Sản phẩm tạm ổn" | Phản hồi thông thường |
| Tiêu cực | "Trải nghiệm quá tệ" | Xin lỗi người dùng |
| Spam | Link quảng cáo lặp | Ẩn bình luận |

### 4 cơ chế bắt buộc (có chấm điểm)

#### Retry với exponential backoff
- Chiến lược thử lại rõ ràng, giới hạn số lần
- Phân biệt lỗi tạm thời (timeout → retry) với lỗi không khôi phục (invalid token → không retry)

#### Circuit breaker
- Điều kiện đóng/mở rõ ràng
- Ví dụ: 5 lỗi liên tiếp → mở mạch, chờ 30s, thử lại ở `half-open`

#### Idempotent consumer
- `command_id` đã có trong DB → bỏ qua, không gửi reply lần 2
- Áp dụng cho mọi consumer trong pipeline

#### Dead Letter Queue + alert vận hành
- Message vào `dead_letter` → Prometheus phát hiện qua offset tăng
- Alertmanager gửi Slack trong vòng dưới 1 phút

### Kết quả mong đợi
- Bình luận được phân loại theo cảm xúc
- Hệ thống tạo phản hồi tự động
- Luồng lỗi: `retry → dead letter → cảnh báo` hoạt động đúng

---

## Phụ lục A — Cài đặt môi trường với Docker

### A.1 Cài đặt trước
- Docker
- Docker Compose plugin

### A.2 Cấu trúc thư mục

```
fb_api/
├── docker-compose.yml
├── prometheus/
│   ├── prometheus.yml
│   └── alert.rules.yml
├── alertmanager/
│   └── alertmanager.yml
└── services/
    ├── backend-api/
    ├── webhook-service/
    ├── core-service/
    └── retry-service/
```

### A.3 docker-compose.yml

```yaml
services:
  zookeeper:
    image: confluentinc/cp-zookeeper:7.6.1
    container_name: fb_api-zookeeper
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
      ZOOKEEPER_TICK_TIME: 2000
    ports:
      - "2181:2181"

  kafka:
    image: confluentinc/cp-kafka:7.6.1
    container_name: fb_api-kafka
    depends_on: [zookeeper]
    ports:
      - "9092:9092"
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://localhost:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1

  kafka-ui:
    image: provectuslabs/kafka-ui:latest
    container_name: fb_api-kafka-ui
    depends_on: [kafka]
    ports:
      - "8080:8080"
    environment:
      KAFKA_CLUSTERS_0_NAME: fb_api-local
      KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS: kafka:9092
      KAFKA_CLUSTERS_0_ZOOKEEPER: zookeeper:2181

  kafka-exporter:
    image: danielqsj/kafka-exporter:latest
    container_name: fb_api-kafka-exporter
    depends_on: [kafka]
    ports:
      - "9308:9308"
    command:
      - "--kafka.server=kafka:9092"

  prometheus:
    image: prom/prometheus:latest
    container_name: fb_api-prometheus
    depends_on: [kafka-exporter]
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus/prometheus.yml:/etc/prometheus/prometheus.yml
      - ./prometheus/alert.rules.yml:/etc/prometheus/alert.rules.yml
      - prometheus_data:/prometheus
    command:
      - "--config.file=/etc/prometheus/prometheus.yml"
      - "--storage.tsdb.path=/prometheus"

  alertmanager:
    image: prom/alertmanager:latest
    container_name: fb_api-alertmanager
    depends_on: [prometheus]
    ports:
      - "9093:9093"
    volumes:
      - ./alertmanager/alertmanager.yml:/etc/alertmanager/alertmanager.yml
    command:
      - "--config.file=/etc/alertmanager/alertmanager.yml"

  postgres:
    image: postgres:16
    container_name: fb_api-postgres
    environment:
      POSTGRES_DB: fb_api_db
      POSTGRES_USER: fb_api_user
      POSTGRES_PASSWORD: fb_api_password
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data

volumes:
  postgres_data:
  prometheus_data:
```

### A.4 prometheus/prometheus.yml

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

alerting:
  alertmanagers:
    - static_configs:
        - targets:
            - alertmanager:9093

rule_files:
  - "alert.rules.yml"

scrape_configs:
  - job_name: "prometheus"
    static_configs:
      - targets: ["localhost:9090"]
  - job_name: "kafka"
    static_configs:
      - targets: ["kafka-exporter:9308"]
```

### A.5 prometheus/alert.rules.yml

```yaml
groups:
  - name: kafka_alerts
    rules:
      - alert: DeadLetterQueueReceived
        expr: >
          increase(
            kafka_topic_partition_current_offset{topic="dead_letter"}[1m]
          ) > 0
        for: 0m
        labels:
          severity: critical
        annotations:
          summary: "Co message moi vao Dead Letter Queue"
          description: >
            Topic dead_letter vua nhan them message.
            Kiem tra Kafka UI tai http://localhost:8080

      - alert: KafkaConsumerLagHigh
        expr: kafka_consumer_group_lag > 500
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "Consumer lag cao bat thuong"
          description: >
            Consumer group {{ $labels.consumergroup }}
            dang lag {{ $value }} message tren topic {{ $labels.topic }}.

      - alert: WebhookReceiverSilent
        expr: >
          increase(
            kafka_topic_partition_current_offset{topic="raw_events"}[5m]
          ) == 0
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Khong co event nao tu Facebook trong 10 phut"
          description: Kiem tra lai Facebook Webhook subscription.
```

### A.6 alertmanager/alertmanager.yml

```yaml
global:
  slack_api_url: "https://hooks.slack.com/services/YOUR/SLACK/WEBHOOK"

route:
  receiver: "slack-notifications"
  group_wait: 10s
  group_interval: 5m
  repeat_interval: 1h
  routes:
    - match:
        severity: critical
      receiver: "slack-notifications"
      group_wait: 0s

receivers:
  - name: "slack-notifications"
    slack_configs:
      - channel: "#fb-api-alerts"
        send_resolved: true
        title: >-
          [{{ .Status | toUpper }}] {{ .GroupLabels.alertname }}
        text: >-
          {{ range .Alerts }}
          {{ .Annotations.description }}
          {{ end }}
```

> **Lưu ý:** Thay `YOUR/SLACK/WEBHOOK` bằng Incoming Webhook URL thật từ Slack workspace.

### A.7 Các bước khởi động

```bash
# Khởi động tất cả container
docker compose up -d

# Kiểm tra container
docker compose ps

# Xem log nếu lỗi
docker compose logs <tên-container>
```

Sau khi khởi động:
- Kafka UI: http://localhost:8080
- Prometheus: http://localhost:9090
- Alertmanager: http://localhost:9093
- Kafka Exporter metrics: http://localhost:9308/metrics

### A.8 Tạo Kafka topics

```bash
for TOPIC in raw_events reply_commands send_failed send_retry dead_letter; do
  docker exec -it fb_api-kafka kafka-topics \
    --create --topic $TOPIC \
    --bootstrap-server localhost:9092 \
    --partitions 1 --replication-factor 1
done
```

### A.9 Khởi tạo PostgreSQL

```sql
CREATE TABLE idempotency_keys (
  command_id   VARCHAR(100) PRIMARY KEY,
  processed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  status       VARCHAR(20) NOT NULL
);

CREATE TABLE comments (
  id         SERIAL PRIMARY KEY,
  comment_id VARCHAR(100) UNIQUE NOT NULL,
  post_id    VARCHAR(100) NOT NULL,
  message    TEXT,
  intent     VARCHAR(50),
  sentiment  VARCHAR(20),
  status     VARCHAR(20) DEFAULT 'received',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

### A.10 Dừng môi trường

```bash
# Dừng container
docker compose down

# Dừng và xóa cả volume
docker compose down -v
```
