# Quantity Update Source API

## Mục đích

API này đồng bộ nguồn cập nhật sản lượng của một lệnh sản xuất từ MES/Odoo xuống
OEE Gateway.

Nguồn cập nhật sản lượng có hai giá trị:

- `oi`: OI ghi nhận sản lượng thủ công. Gateway dừng hoặc bỏ qua việc đếm sản
  lượng tự động cho lệnh.
- `oee`: Gateway tự động đếm sản lượng và đồng bộ số liệu về MES.

## Khi nào MES gọi API

MES gọi API khi người dùng thay đổi trường `quantity_update_source` trên form
lệnh sản xuất Odoo. API này **không** được gọi khi người dùng đổi trạng thái
lệnh từ giao diện OI.

Luồng đổi trạng thái OI vẫn dùng endpoint `production-commands` hiện có. Mỗi
command `start`, `pause` hoặc `stop` chỉ được bổ sung field
`quantity_update_source` trong payload của command.

## Endpoint

```http
POST http://<gateway-host>:8080/api/v1/gateway/oee/production-quantity-sources
Content-Type: application/json
```

## Cách hoạt động

1. Người dùng đổi nguồn cập nhật sản lượng trên form lệnh sản xuất.
2. MES tạo một `event_code` duy nhất và gửi request tới Gateway.
3. Gateway kiểm tra mã máy, lệnh sản xuất, sản phẩm và giá trị nguồn.
4. Gateway trả response xác nhận hoặc từ chối event.
5. Chỉ khi Gateway trả `accepted: true` và trả đúng `event_code`, MES mới lưu
   thay đổi.
6. Nếu Gateway lỗi kết nối, timeout, trả HTTP không phải `2xx`, từ chối event,
   hoặc trả sai `event_code`, MES rollback thay đổi trên form.
7. MES lưu payload, HTTP status và response Gateway vào **Nhật ký OEE Gateway**.

## Request payload

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1792470000,
  "items": [
    {
      "event_code": "QUS-20261020-AB12CD34",
      "machine_code": "2-20",
      "order_id": "LSX-202609-00031",
      "product_code": "CL1814-PLS-0104",
      "quantity_update_source": "oee"
    }
  ]
}
```

### Top-level schema

| Field | Type | Required | Description |
|---|---|---:|---|
| `schema_version` | integer | Yes | Phiên bản schema. Hiện tại là `1`. |
| `gateway_id` | string | Yes | Mã Gateway nhận request. |
| `created_at` | integer | Yes | Unix timestamp, đơn vị giây, tại thời điểm MES tạo payload. |
| `items` | array | Yes | Danh sách event thay đổi nguồn. MES hiện gửi một item cho một lần thay đổi. |

### Item schema

| Field | Type | Required | Description |
|---|---|---:|---|
| `event_code` | string | Yes | Mã event duy nhất, ví dụ `QUS-20261020-AB12CD34`. Gateway dùng mã này để idempotent và phản hồi đúng event. |
| `machine_code` | string | Yes | Mã máy sản xuất. |
| `order_id` | string | Yes | Mã lệnh sản xuất. |
| `product_code` | string | Yes | Mã nội bộ thành phẩm của lệnh sản xuất. |
| `quantity_update_source` | string | Yes | Chỉ nhận `oi` hoặc `oee`. |

## Response

Gateway chỉ cần trả một object JSON cho event đã nhận.

### Thành công

```json
{
  "event_code": "QUS-20261020-AB12CD34",
  "accepted": true
}
```

### Từ chối

```json
{
  "event_code": "QUS-20261020-AB12CD34",
  "accepted": false,
  "error": {
    "code": "machine_not_found",
    "message": "machine_code 2-20 không tồn tại"
  }
}
```

### Response schema

| Field | Type | Required | Description |
|---|---|---:|---|
| `event_code` | string | Yes | Phải trùng `event_code` trong request. |
| `accepted` | boolean | Yes | `true` nếu Gateway áp dụng thay đổi; `false` nếu Gateway từ chối. |
| `error` | object | Khi `accepted` là `false` | Thông tin lỗi. Không cần gửi khi thành công. |
| `error.code` | string | Khi từ chối | Mã lỗi để hệ thống xử lý, ví dụ `machine_not_found`, `order_not_found`, `product_not_found`, `invalid_quantity_update_source`. |
| `error.message` | string | Khi từ chối | Mô tả lỗi để hiển thị và đối soát log. |

## Điều kiện MES chấp nhận response

MES chỉ lưu thay đổi khi đồng thời thỏa cả ba điều kiện:

1. Gateway trả HTTP `2xx`.
2. `accepted` là `true`.
3. `event_code` trong response trùng với request.

Mọi trường hợp khác được xem là thất bại và thay đổi `quantity_update_source`
không được lưu tại MES.

## Lưu ý cho Gateway

- Gateway nên xử lý idempotent theo `event_code`: nếu nhận lại cùng event, trả
  lại kết quả đã xử lý thay vì áp dụng thay đổi lần nữa.
- `error.message` nên nêu rõ nguyên nhân, ví dụ mã máy/lệnh/sản phẩm không tồn
  tại, để người dùng Odoo biết cách xử lý.
- Gateway có thể ghi nhận `gateway_id` và `created_at` để audit, nhưng các field
  này không ảnh hưởng điều kiện Odoo chấp nhận response.
