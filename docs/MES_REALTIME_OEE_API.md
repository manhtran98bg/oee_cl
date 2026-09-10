# MES Realtime OEE API

Tài liệu này mô tả contract API giữa MES server và Rostek Gateway cho luồng OEE realtime.

## Namespace

```http
MES -> Gateway: POST /api/v1/gateway/oee/production-commands
Gateway -> MES: POST /api/v1/gateway/oee/realtime-snapshots
```

Payload dùng `snake_case`, Unix seconds, `schema_version = 1`.

## 1. MES Gửi Lệnh Sản Xuất Xuống Gateway

```http
POST /api/v1/gateway/oee/production-commands
Content-Type: application/json
```

Request:

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750000,
  "items": [
    {
      "command_code": "CMD-20260909-0001",
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

Response:

```json
{
  "accepted": true,
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750001,
  "accepted_count": 1,
  "rejected_count": 0,
  "items": [
    {
      "accepted": true,
      "machine_code": "M16-01",
      "command_code": "CMD-20260909-0001",
      "status": "active",
      "order_id": "LSX-001",
      "session_id": "M16-01-LSX-001-1788750000",
      "message": "Accepted"
    }
  ]
}
```

Rules:

- `items` là array bắt buộc; một request có thể chứa nhiều lệnh.
- Một item lỗi không làm fail cả batch.
- `action` nhận `start`, `pause`, `stop`.
- `start` cần `command_code`, `machine_code`, `order_id`, `products`.
- `pause`/`stop` cần `command_code`, `machine_code`, `order_id`.
- `session_id` do Gateway tự sinh khi `start`.
- Một máy có thể chạy nhiều `order_id` cùng lúc.
- Không cho có hai active sessions cùng `machine_code + order_id`.
- Nếu `start` lại cùng `machine_code + order_id` đang active/pause, Gateway reuse `session_id` hiện tại và update `products`.
- Nếu session cũ đã `stopped`, `start` mới tạo `session_id` mới.
- Sau `stop`, session bị xoá khỏi `production_context`; history của session vẫn nằm trong `production_period`.

## 2. Gateway Gửi Realtime Snapshot Lên MES

```http
POST /api/v1/gateway/oee/realtime-snapshots
Authorization: Bearer <token>
Content-Type: application/json
```

Gateway gửi theo chu kỳ cấu hình, mặc định 5 giây/lần. Mỗi `item` tương ứng một active/pause session/order.

Payload:

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

Item fields:

| Field | Type | Required | Description |
|---|---:|---:|---|
| `machine_code` | string | yes | Mã máy trong Gateway/MES. |
| `order_id` | string | yes | Mã lệnh sản xuất. |
| `session_id` | string | yes | Mã lượt sản xuất do Gateway sinh. |
| `product_code` | string | yes | Mã sản phẩm chính của order/session. |
| `mold_code` | string/null | no | Mã khuôn. |
| `machine_state` | string | yes | `run`, `stop`, `error`, `disconnect`. |
| `actual_qty` | integer | yes | Sản lượng thực tế từ đầu session, tính từ counter PLC hiện tại trừ baseline session. |
| `planned_qty` | number | yes | Sản lượng kế hoạch từ đầu session. |
| `availability` | number | yes | A, phần trăm `0..100`. |
| `performance` | number | yes | P, phần trăm `0..100`. |
| `quality` | number | yes | Q, phần trăm `0..100`. |
| `oee` | number | yes | OEE, phần trăm `0..100`. |
| `extra` | object | yes | Hiện tại Gateway gửi `{}`. |

Gateway coi request là thành công khi MES trả HTTP status `2xx`.

Response đề xuất:

```json
{
  "accepted": true,
  "message": "OK"
}
```
