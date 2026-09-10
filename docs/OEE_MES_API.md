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
- `pause` và `stop` tìm session hiện tại theo `machine_code + order_id`.
- Sau `stop`, Gateway đóng history trong `production_period` và xoá session khỏi `production_context`.

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
  "schema_version": 1,
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

### 3.2 Sync Payload Schema

| Field | Type | Required | Description |
|---|---:|---:|---|
| `schema_version` | integer | yes | Version payload. Hiện tại là `1`. |
| `gateway_id` | string | yes | Mã Gateway gửi dữ liệu. |
| `created_at` | integer | yes | Unix seconds, thời điểm Gateway build payload. |
| `items` | array | yes | Danh sách realtime snapshot. |

Item schema:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `machine_code` | string | yes | Mã máy. |
| `order_id` | string | yes | Mã lệnh sản xuất. |
| `session_id` | string | yes | Mã lượt sản xuất do Gateway sinh. |
| `product_code` | string | yes | Mã sản phẩm chính. |
| `mold_code` | string/null | no | Mã khuôn. |
| `machine_state` | string | yes | `run`, `stop`, `error`, `disconnect`. |
| `actual_qty` | integer | yes | Sản lượng thực tế từ đầu session. |
| `planned_qty` | number | yes | Sản lượng kế hoạch từ đầu session. |
| `availability` | number | yes | A, phần trăm `0..100`. |
| `performance` | number | yes | P, phần trăm `0..100`. |
| `quality` | number | yes | Q, phần trăm `0..100`. |
| `oee` | number | yes | OEE, phần trăm `0..100`. |
| `extra` | object | yes | Hiện tại Gateway gửi `{}`. |

MES trả HTTP `2xx` là thành công.

Response đề xuất:

```json
{
  "accepted": true,
  "message": "OK"
}
```