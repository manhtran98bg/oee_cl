# MES Realtime OEE API

Tài liệu này mô tả API mà MES server cần mở để Rostek Gateway gửi dữ liệu realtime OEE.

Gateway hiện gửi dữ liệu theo chu kỳ cấu hình, mặc định 5 giây/lần. Mỗi request có thể chứa nhiều máy trong field `items`.

## Endpoint

```http
POST /api/v1/gateway/oee/realtime-snapshots
Content-Type: application/json
Authorization: Bearer <token>
```

Gateway coi request là thành công khi MES trả HTTP status `2xx`.

## Request Body

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750000,
  "items": [
    {
      "_key": "9f91f8d6b0e2b2b5d8a6a7f0e8a4c4f8",
      "machine_code": "M16-01",
      "order_code": "LSX-001",
      "session_id": "SESSION-001",
      "product_code": "SP-001",
      "mold_code": "KHUON-001",
      "machine_state": "run",
      "good_qty": 120,
      "ng_qty": 5,
      "actual_qty": 125,
      "planned_qty": 144,
      "run_time": 1500,
      "stop_time": 240,
      "error_time": 60,
      "production_time": 1800,
      "availability": 83.333333,
      "performance": 86.805556,
      "quality": 96,
      "oee": 69.444445,
      "extra": {}
    }
  ]
}
```

## Field Schema

| Field | Type | Required | Description |
|---|---:|---:|---|
| `schema_version` | integer | yes | Version payload. Hiện tại là `1`. |
| `gateway_id` | string | yes | Mã Gateway gửi dữ liệu. |
| `created_at` | integer | yes | Unix timestamp seconds, thời điểm Gateway build payload. |
| `items` | array | yes | Danh sách realtime snapshot của các máy. |

## Item Schema

| Field | Type | Required | Description |
|---|---:|---:|---|
| `_key` | string | yes | Trace id dạng GUID hex 32 ký tự. Sinh mới mỗi lần gửi, không dùng làm durable idempotency key. |
| `machine_code` | string | yes | Mã máy trong MES/Gateway. |
| `order_code` | string | yes | Mã lệnh sản xuất hiện tại. |
| `session_id` | string | yes | Mã lượt sản xuất/session hiện tại. |
| `product_code` | string | yes | Mã sản phẩm đang chạy. |
| `mold_code` | string/null | no | Mã khuôn. Có thể `null`. |
| `machine_state` | string | yes | Trạng thái máy: `run`, `stop`, `error`, `disconnect`. |
| `good_qty` | integer | yes | Số lượng OK tính từ đầu session. |
| `ng_qty` | integer | yes | Số lượng NG tính từ đầu session. |
| `actual_qty` | integer | yes | `good_qty + ng_qty`. |
| `planned_qty` | number | yes | Sản lượng kế hoạch tại thời điểm gửi. |
| `run_time` | integer | yes | Tổng thời gian chạy từ đầu session, đơn vị giây. |
| `stop_time` | integer | yes | Tổng thời gian dừng từ đầu session, đơn vị giây. |
| `error_time` | integer | yes | Tổng thời gian lỗi từ đầu session, đơn vị giây. |
| `production_time` | integer | yes | Thời gian từ lúc bắt đầu session đến hiện tại, đơn vị giây. |
| `availability` | number | yes | A, phần trăm `0..100`. |
| `performance` | number | yes | P, phần trăm `0..100`. |
| `quality` | number | yes | Q, phần trăm `0..100`. |
| `oee` | number | yes | OEE, phần trăm `0..100`. |
| `extra` | object | yes | Object mở rộng. Hiện Gateway gửi `{}`. |


## Suggested Success Response

```json
{
  "accepted": true,
  "message": "OK"
}
```

## Suggested Error Response

```json
{
  "accepted": false,
  "message": "Invalid payload"
}
```

Gateway hiện chỉ cần HTTP status để xác định thành công/thất bại. Response body có thể dùng cho log/debug phía MES.
