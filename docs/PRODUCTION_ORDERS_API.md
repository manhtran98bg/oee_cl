# Gateway đọc danh sách lệnh sản xuất từ MES

Gateway gọi API này khi cần lấy thông tin hiện tại của lệnh sản xuất. API dùng
cùng cấu trúc `products` và tên trường nghiệp vụ như API
[`production-commands`](OEE_MES_API_UPDATED_1409.md#2-control-api-mes-gửi-lệnh-sản-xuất-xuống-gateway).
Đây là API chỉ đọc. Gateway dùng `production_status` để biết trạng thái hiện tại
của lệnh. `action` và `command_code` thuộc luồng gửi lệnh điều khiển.

```http
GET /api/v1/gateway/oee/production-orders
Authorization: Bearer <MES_API_KEY>
Accept: application/json
```

Dùng API key Odoo như khi gửi dữ liệu OEE. Tài khoản sở hữu key cần quyền đọc
lệnh sản xuất (nhóm **Sản xuất / Chỉ xem**) và quyền đọc dữ liệu máy, khuôn,
sản phẩm liên quan. API áp dụng quyền truy cập của tài khoản đó. Host MES phải
trỏ đến đúng cơ sở dữ liệu Odoo. API hoạt động cả khi MES tạm dừng nhận webhook OEE.

## Tham số

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `page` | `1` | Số trang, là số nguyên từ 1. Mỗi trang tối đa 100 lệnh. |
| `production_status` | Bỏ qua để lấy tất cả | Mỗi lần gọi nhận một giá trị trong bảng dưới đây. |

| Giá trị `production_status` | Trạng thái sản xuất |
|---|---|
| `not_started` | Chưa thực hiện. Bao gồm lệnh nháp chưa có trạng thái sản xuất. |
| `producing` | Đang sản xuất |
| `paused` | Tạm dừng |
| `finished` | Kết thúc |

Ví dụ lấy trang đầu của các lệnh tạm dừng:

```bash
curl --get 'https://<mes-host>/api/v1/gateway/oee/production-orders' \
  --header "Authorization: Bearer ${MES_API_KEY}" \
  --header 'Accept: application/json' \
  --data-urlencode 'page=1' \
  --data-urlencode 'production_status=paused'
```

## Phản hồi

```json
{
  "schema_version": 1,
  "gateway_id": "GW-M16-01",
  "created_at": 1788750000,
  "pagination": {
    "page": 1,
    "page_size": 100,
    "total_items": 1,
    "total_pages": 1,
    "next_page": null
  },
  "items": [
    {
      "order_id": "LSX-001",
      "machine_code": "M16-01",
      "quantity_update_source": "oee",
      "production_status": "paused",
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

- `gateway_id` là mã Gateway trong cấu hình OI của MES, hoặc `null` nếu chưa cấu
  hình. `created_at` là thời điểm MES tạo phản hồi, tính theo Unix seconds.
- `quantity_update_source` nhận `oi` (thủ công) hoặc `oee` (tự động).
- `products` có một phần tử cho thành phẩm của lệnh. `cycle_time` tính bằng giây.
  Lệnh chưa đủ dữ liệu vẫn xuất hiện. Mã máy hoặc mã thành phẩm có thể là chuỗi
  rỗng, mã khuôn có thể là `null`, và cavity hoặc cycle time có thể bằng 0.
  `target_qty` chỉ có mặt khi số lượng kế hoạch là số nguyên không âm, như
  trong payload `start`.
- Kết quả sắp xếp theo ID lệnh giảm dần. Để lấy trang tiếp theo, truyền
  `pagination.next_page` vào `page` và giữ nguyên bộ lọc. Khi `next_page` là
  `null`, đã hết trang. Trang vượt phạm vi hoặc không có lệnh phù hợp trả
  HTTP 200 với `items: []`. `total_pages` bằng 0 nếu không có lệnh phù hợp.
- Mỗi lần gọi đọc dữ liệu hiện tại. Nếu MES thêm lệnh hoặc lệnh đổi trạng thái
  giữa hai lần gọi, danh sách theo trang có thể dịch chuyển. Gateway có thể
  dùng `order_id` để nhận diện lệnh đã đọc.

## Lỗi

| HTTP | `error` | Nguyên nhân |
|---|---|---|
| 400 | `invalid_page` | `page` không phải số nguyên dương. |
| 400 | `invalid_production_status` | Giá trị lọc ngoài bốn trạng thái trên. |
| 401 | `unauthorized` | Thiếu API key, key sai hoặc hết hạn. |
| 403 | `forbidden` | Tài khoản của key thiếu quyền đọc dữ liệu cần thiết. |
| 503 | `production_orders_unavailable` | Module lệnh sản xuất `cl_mrp` chưa được cài trên MES. |
