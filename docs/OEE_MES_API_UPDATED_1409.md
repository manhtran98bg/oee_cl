# Rostek Gateway OEE API Contract

Tài liệu này mô tả API tích hợp OEE giữa MES server và Rostek Gateway.

## 1. Tổng Quan

Có hai luồng HTTP:

```text
MES -> Gateway: gửi lệnh sản xuất/control
Gateway -> MES: gửi realtime OEE snapshot, đồng bộ các dữ liệu sản xuất theo giờ (hour), ngày (daily), lượt sản xuất (session), lệnh sản xuất (order), sản phẩm (product)
```

Endpoint dùng chung namespace:

```http
MES -> Gateway: POST /api/v1/gateway/oee/production-commands
Gateway -> MES: POST /api/v1/gateway/oee/realtime-snapshots
Gateway -> MES: POST /api/v1/gateway/oee/machine-state-events
Gateway -> MES: POST /api/v1/gateway/oee/production-metrics
```

Quy ước chung:

- JSON property dùng `snake_case`.
- Timestamp dùng Unix seconds.
- `schema_version` hiện tại là `1`.
- `gateway_id` là mã Gateway, ví dụ `GW-M16-01`.
- `items` luôn là array để hỗ trợ batch.

## 2. Control API: MES Gửi Lệnh Sản Xuất Xuống Gateway

MES gọi API này vào Gateway khi bắt đầu, tạm dừng hoặc dừng lệnh sản xuất.

```http
POST http://<gateway-host>:8080/api/v1/gateway/oee/production-commands
Content-Type: application/json
```

### 2.1 Start Một Lệnh

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750000,
  "items": [
    {
      "command_code": "CMD-20260910-0001",
      "machine_code": "M16-01",
      "action": "start",
      "order_id": "LSX-001",
      "products": [
        {
          "product_code": "SP-001",
          "mold_code": "KHUON-001",
          "cavity": 4,
          "cycle_time": 16.0,
          "target_qty": 10000
        }
      ]
    }
  ]
}
```

### 2.2 Start Nhiều Hơn 1 Lệnh Trên Cùng Một Máy

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750000,
  "items": [
    {
      "command_code": "CMD-20260910-0001",
      "machine_code": "M16-01",
      "action": "start",
      "order_id": "LSX-001",
      "products": [
        {
          "product_code": "SP-001",
          "mold_code": "KHUON-001",
          "cavity": 4,
          "cycle_time": 16.0,
          "target_qty": 10000
        }
      ]
    },
    {
      "command_code": "CMD-20260910-0002",
      "machine_code": "M16-01",
      "action": "start",
      "order_id": "LSX-002",
      "products": [
        {
          "product_code": "SP-002",
          "mold_code": "KHUON-002",
          "cavity": 2,
          "cycle_time": 12.0,
          "target_qty": 5000
        }
      ]
    }
  ]
}
```

### 2.3 Pause Một Lệnh

`pause` kết thúc session hiện tại. Gateway chốt session metric, đóng `production_period` với trạng thái `paused`, xoá session khỏi current production context và trả lại `session_id` vừa kết thúc. Khi MES gửi `start` lại cùng machine/order, Gateway tạo một session mới.

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750300,
  "items": [
    {
      "command_code": "CMD-20260910-0003",
      "machine_code": "M16-01",
      "action": "pause",
      "order_id": "LSX-001"
    }
  ]
}
```

### 2.4 Stop Một Lệnh

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750600,
  "items": [
    {
      "command_code": "CMD-20260910-0004",
      "machine_code": "M16-01",
      "action": "stop",
      "order_id": "LSX-001"
    }
  ]
}
```

### 2.5 Control Request Schema

| Field | Type | Required | Description |
|---|---:|---:|---|
| `schema_version` | integer | yes | Version payload. Hiện tại là `1`. |
| `gateway_id` | string | yes | Mã Gateway. |
| `created_at` | integer | yes | Unix seconds do MES gửi. Nếu `<= 0`, Gateway dùng thời gian hiện tại. |
| `items` | array | yes | Danh sách lệnh cần xử lý. |

Item schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `command_code` | string | yes | Mã lệnh/request từ MES để trace log. |
| `machine_code` | string | yes | Mã máy, phải tồn tại trong cấu hình Gateway. |
| `action` | string | yes | `start`, `pause`, hoặc `stop`. |
| `order_id` | string | yes | Mã lệnh sản xuất. |
| `products` | array | required for `start` | Danh sách sản phẩm của lệnh. |

Product schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `product_code` | string | yes | Mã sản phẩm. |
| `mold_code` | string/null | no | Mã khuôn. |
| `cavity` | number | yes | Số cavity/gain, phải lớn hơn `0`. |
| `cycle_time` | number | yes | Cycle time chuẩn, đơn vị giây, phải lớn hơn `0`. |
| `target_qty` | integer | no | Sản lượng kế hoạch của lệnh. |

### 2.6 Control Response

```json
{
  "accepted": true,
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750001,
  "accepted_count": 2,
  "rejected_count": 0,
  "items": [
    {
      "accepted": true,
      "machine_code": "M16-01",
      "command_code": "CMD-20260910-0001",
      "status": "active",
      "order_id": "LSX-001",
      "session_id": "M16-01-LSX-001-1788750000",
      "message": "Accepted"
    }
  ]
}
```

Response rules:

- Mỗi item được xử lý độc lập.
- `accepted = false` ở top-level nếu có ít nhất một item bị reject.
- `session_id` do Gateway tự sinh khi `start`.
- `pause` và `stop` tìm session hiện tại theo `machine_code + order_id`, chốt session metric và trả lại `session_id` vừa kết thúc.
- Sau `pause`, Gateway đóng history với trạng thái `paused` và xoá session khỏi `production_context`.
- Sau `stop`, Gateway đóng history với trạng thái `stopped` và xoá session khỏi `production_context`.
- `start` sau `pause` hoặc `stop` tạo một `session_id` mới.

## 3. Sync API: Gateway Gửi Realtime OEE Snapshot Lên MES

MES server cần mở API này để Gateway gửi snapshot realtime.

Gateway sẽ gọi:

```http
POST http://<mes-base-url>/api/v1/gateway/oee/realtime-snapshots
Authorization: Bearer <token>
Content-Type: application/json
```

`<mes-base-url>` lấy từ cấu hình Gateway:

```json
{
  "MesSync": {
    "Enabled": true,
    "BaseUrl": "http://mes-server:8070",
    "BearerToken": "<token>",
    "SyncIntervalMs": 5000,
    "RequireProductionContext": true
  }
}
```

### 3.1 Realtime Snapshot Payload

```json
{
  "schema_version": 2,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750000,
  "items": [
    {
      "machine_code": "M16-01",
      "order_id": "LSX-001",
      "session_id": "M16-01-LSX-001-1788750000",
      "product_code": "SP-001",
      "mold_code": "KHUON-001",
      "machine_state": "run",
      "actual_qty": 125,
      "total_qty": 205,
      "planned_qty": 144,
      "availability": 83.333333,
      "performance": 86.805556,
      "quality": 96,
      "oee": 69.444445,
      "extra": {}
    }
  ]
}
```

Máy enabled nhưng chưa có production order vẫn được gửi để MES dashboard luôn hiển thị đầy đủ thiết bị:

```json
{
  "schema_version": 2,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750000,
  "items": [
    {
      "machine_code": "M16-02",
      "order_id": null,
      "session_id": null,
      "product_code": null,
      "mold_code": null,
      "machine_state": "stop",
      "actual_qty": 0,
      "total_qty": 0,
      "planned_qty": 0,
      "availability": 0,
      "performance": 0,
      "quality": 0,
      "oee": 0,
      "extra": {}
    }
  ]
}
```

### 3.2 Sync Payload Schema

| Field | Type | Required | Description |
|---|---:|---:|---|
| `schema_version` | integer | yes | Version realtime payload. Hiện tại là `2`. |
| `gateway_id` | string | yes | Mã Gateway gửi dữ liệu. |
| `created_at` | integer | yes | Unix seconds, thời điểm Gateway build payload. |
| `items` | array | yes | Danh sách realtime snapshot. |

Item schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `machine_code` | string | yes | Mã máy. |
| `order_id` | string/null | no | Mã lệnh sản xuất; `null` khi máy chưa có production context. |
| `session_id` | string/null | no | Mã lượt sản xuất; `null` khi máy chưa có production context. |
| `product_code` | string/null | no | Mã sản phẩm chính; `null` khi máy chưa có production context. |
| `mold_code` | string/null | no | Mã khuôn. |
| `machine_state` | string | yes | `run`, `stop`, `error`, `disconnect`. |
| `actual_qty` | integer | yes | Sản lượng hiện tại của session: `(shot_ok_delta + shot_ng_delta) × cavity`. |
| `total_qty` | integer | yes | Tổng `actual_qty` của order, gồm các session đã hoàn thành và session hiện tại. |
| `planned_qty` | number | yes | Sản lượng theo kế hoạch của session. |
| `availability` | number | yes | A, phần trăm `0..100`. |
| `performance` | number | yes | P, phần trăm `0..100`. |
| `quality` | number | yes | Q, phần trăm `0..100`. |
| `oee` | number | yes | OEE, phần trăm `0..100`. |
| `extra` | object | yes | Hiện tại Gateway gửi `{}`. |

Quy tắc hiển thị:

- Gateway gửi một item cho mỗi active session nếu máy đang có lệnh sản xuất.
- Gateway gửi đúng một machine-level item với `order_id = null` nếu máy enabled nhưng chưa có lệnh.
- Máy enabled chưa có dữ liệu runtime được gửi với `machine_state = "disconnect"`.
- MES dùng `machine_code` để upsert thiết bị và hiển thị trạng thái `Chưa có lệnh` khi `order_id = null`.

MES trả HTTP `2xx` là thành công.

Response đề xuất:

```json
{
  "accepted": true,
  "message": "OK"
}
```

## 4. Sync API: Gateway Gửi Machine State Event Lên MES

MES server cần mở API này để Gateway gửi các block trạng thái máy phục vụ chart timeline chạy/dừng/lỗi.

Gateway sẽ gọi:

```http
POST http://<mes-base-url>/api/v1/gateway/oee/machine-state-events
Authorization: Bearer <token>
Content-Type: application/json
```

### 4.1 Machine State Event Payload

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750060,
  "items": [
    {
      "event_id": "GW-M16-01:M16-01:M16-01-LSX-001-1788750000:1788750000:run",
      "machine_code": "M16-01",
      "order_id": "LSX-001",
      "session_id": "M16-01-LSX-001-1788750000",
      "state": "run",
      "start_at": 1788750000,
      "end_at": 1788750010,
      "duration_sec": 10,
      "is_open": false
    },
    {
      "event_id": "GW-M16-01:M16-01:M16-01-LSX-001-1788750000:1788750010:stop",
      "machine_code": "M16-01",
      "order_id": "LSX-001",
      "session_id": "M16-01-LSX-001-1788750000",
      "state": "stop",
      "start_at": 1788750010,
      "end_at": 1788750060,
      "duration_sec": 50,
      "is_open": true
    }
  ]
}
```

### 4.2 Machine State Event Schema

Top-level schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `schema_version` | integer | yes | Version payload. Hiện tại là `1`. |
| `gateway_id` | string | yes | Mã Gateway gửi dữ liệu. |
| `created_at` | integer | yes | Unix seconds, thời điểm Gateway build payload. |
| `items` | array | yes | Danh sách machine state event cần sync. |

Item schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `event_id` | string | yes | Khóa idempotent để MES upsert, tránh insert trùng. |
| `machine_code` | string | yes | Mã máy. |
| `order_id` | string | yes | Mã lệnh sản xuất. |
| `session_id` | string | yes | Mã lượt sản xuất do Gateway sinh. |
| `state` | string | yes | `run`, `stop`, `error`, `disconnect`. |
| `start_at` | integer | yes | Unix seconds, thời điểm block trạng thái bắt đầu. |
| `end_at` | integer | yes | Unix seconds, thời điểm block trạng thái kết thúc hoặc thời điểm cập nhật mới nhất nếu `is_open = true`. |
| `duration_sec` | integer | yes | Số giây của block trạng thái, bằng `end_at - start_at`. |
| `is_open` | boolean | yes | `true` nếu event là trạng thái hiện tại còn đang diễn ra, `false` nếu event đã đóng. |

MES nên xử lý idempotent bằng cách upsert theo `event_id`.

Nếu Gateway sync mỗi 60 giây, `items` phải chứa tất cả event đã tạo hoặc thay đổi trong chu kỳ đó. Ví dụ trong 60 giây máy `stop` hai lần, `error` một lần rồi quay lại `run`, payload sẽ gồm đầy đủ các block `run/stop/run/error/run`; không chỉ gửi trạng thái cuối cùng.

MES trả HTTP `2xx` là thành công.

## 5. Sync API: Gateway Gửi Production Metrics Bucket Lên MES

MES server cần mở API này để Gateway gửi dữ liệu thống kê sản xuất theo bucket thời gian hoặc theo phạm vi sản xuất.

Gateway sẽ gọi:

```http
POST http://<mes-base-url>/api/v1/gateway/oee/production-metrics
Authorization: Bearer <token>
Content-Type: application/json
```

API này dùng chung cho các loại bucket:

```text
hour
day
period
order
```

Không tạo endpoint riêng cho từng bucket.

### 5.1 Production Metrics Payload

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788753605,
  "items": [
    {
      "metric_id": "GW-M16-01:hour:M16-01:LSX-001:SESSION-001:1788750000",
      "bucket_type": "hour",
      "bucket_start": 1788750000,
      "bucket_end": 1788753600,
      "machine_code": "M16-01",
      "order_id": "LSX-001",
      "session_id": "SESSION-001",
      "product_code": "SP-001",
      "mold_code": "KHUON-001",
      "machine_state": "run",
      "actual_qty": 125,
      "total_qty": 205,
      "planned_qty": 144,
      "run_time": 1500,
      "stop_time": 240,
      "error_time": 60,
      "production_time": 1800,
      "availability": 83.333333,
      "performance": 86.805556,
      "quality": 96,
      "oee": 69.444445,
      "is_final": false,
      "extra": {}
    }
  ]
}
```

### 5.2 Production Metrics Schema

Top-level schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `schema_version` | integer | yes | Version payload. Hiện tại là `1`. |
| `gateway_id` | string | yes | Mã Gateway gửi dữ liệu. |
| `created_at` | integer | yes | Unix seconds, thời điểm Gateway build payload. |
| `items` | array | yes | Danh sách production metrics cần sync. |

Item schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `metric_id` | string | yes | Khóa idempotent để MES upsert, tránh insert trùng khi Gateway retry hoặc restart. |
| `bucket_type` | string | yes | `hour`, `day`, `period`, hoặc `order`. |
| `bucket_start` | integer | yes | Unix seconds, thời điểm bắt đầu bucket/session/order scope. |
| `bucket_end` | integer | yes | Unix seconds, thời điểm kết thúc bucket hoặc thời điểm tính gần nhất nếu bucket chưa chốt. |
| `machine_code` | string | yes | Mã máy. |
| `order_id` | string | yes | Mã lệnh sản xuất. |
| `session_id` | string/null | no | Mã lượt sản xuất do Gateway sinh. Với `order`, field này có thể `null` nếu metric gom nhiều session. |
| `product_code` | string | yes | Mã sản phẩm chính. |
| `mold_code` | string/null | no | Mã khuôn. |
| `machine_state` | string | yes | `run`, `stop`, `error`, `disconnect`, là trạng thái tại thời điểm build metric. |
| `actual_qty` | integer | yes | Sản lượng trong phạm vi metric, tính bằng `(shot_ok_delta + shot_ng_delta) × cavity`. Với `hour/day` là sản lượng phát sinh trong bucket; với `session` là sản lượng của session; với `order` là tổng sản lượng hiện tại của order. |
| `total_qty` | integer | yes | Tổng sản lượng hiện tại của order tại `bucket_end`. |
| `planned_qty` | number | yes | Sản lượng kế hoạch trong phạm vi metric. |
| `target_qty` | integer | yes | Sản lượng mục tiêu của order. |
| `run_time` | integer | yes | Tổng thời gian chạy trong phạm vi metric, đơn vị giây. |
| `stop_time` | integer | yes | Tổng thời gian dừng trong phạm vi metric, đơn vị giây. |
| `error_time` | integer | yes | Tổng thời gian lỗi trong phạm vi metric, đơn vị giây. |
| `production_time` | integer | yes | Tổng thời gian sản xuất trong phạm vi metric, đơn vị giây. |
| `availability` | number | yes | A, phần trăm `0..100`. |
| `performance` | number | yes | P, phần trăm `0..100`. |
| `quality` | number | yes | Q, phần trăm `0..100`. |
| `oee` | number | yes | OEE, phần trăm `0..100`. |
| `is_final` | boolean | yes | `false` nếu metric còn có thể update, `true` nếu bucket/session/order đã chốt. |
| `extra` | object | yes | Object mở rộng. Hiện tại Gateway gửi `{}`. |

### 5.3 Production Metrics Rules

- `hour` update mỗi 1 phút nếu giờ đang chạy: `is_final = false`.
- Khi qua giờ mới, Gateway gửi bucket giờ cũ lần cuối: `is_final = true`.
- `day` update mỗi 5 phút nếu ngày đang chạy: `is_final = false`.
- Khi qua ngày mới, Gateway gửi bucket ngày cũ lần cuối: `is_final = true`.
- `session` chốt khi pause hoặc stop session: `is_final = true`.
- `order` là rolling summary: thường `is_final = false` cho tới khi sau này có command đóng order thật.
- MES nên xử lý idempotent bằng cách upsert theo `metric_id`.

MES trả HTTP `2xx` là thành công.
