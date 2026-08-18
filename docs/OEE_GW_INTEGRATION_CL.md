# Rostek Gateway Integration Cho OEE CLX4

Tài liệu này mô tả cách hệ thống OEE đọc dữ liệu sample từ PostgreSQL cache db của Rostek Gateway.

## 1. Mục Đích

Rostek Gateway dùng:

- SQLite để lưu cấu hình local của gateway.
- PostgreSQL để lưu dữ liệu sample device cho hệ thống OEE đọc.

Hệ thống OEE chỉ đọc PostgreSQL. Không đọc hoặc ghi vào SQLite config DB.

## 2. Thông Tin Kết Nối

Thông tin thực tế sẽ được cấp theo môi trường triển khai:

```text
Host: <postgres-host-or-ip>
Port: 5432
Database: oee_cl
Username: reader
Password: rostek2019
SSL mode: disable
```

Ví dụ connection string:

```text
Host=192.168.1.20;Port=5432;Database=oee_cl;Username=reader;Password=rostek2019;
```

User `reader` là user readonly. OEE không insert, update hoặc delete dữ liệu trong database oee_cl.

## 3. Bảng Dữ Liệu

Bảng chính:

```sql
device_samples
```

Schema:

```sql
create table device_samples (
    id bigserial primary key,
    gateway_id text not null,
    machine_code text not null,
    sampled_at_utc timestamptz not null,
    created_at_utc timestamptz not null default now(),

    machine_state smallint null,
    shot_ok_delta integer null,
    shot_ng_delta integer null,
    cycle_time_ms integer null,
    run_time_delta integer null,
    stop_time_delta integer null,
    error_time_delta integer null
);
```

## 4. Ý Nghĩa Dữ Liệu

Mỗi row là dữ liệu của 1 máy tại 1 mốc sample. Chu kỳ mặc định là 5 giây.

Các cột định danh:

| Column | Ý nghĩa |
|---|---|
| `id` | ID tăng dần, dùng làm checkpoint đọc dữ liệu |
| `gateway_id` | Mã gateway ghi dữ liệu |
| `machine_code` | Mã máy trong Gateway |
| `sampled_at_utc` | Thời điểm Gateway lấy snapshot dữ liệu máy |
| `created_at_utc` | Thời điểm row được ghi vào PostgreSQL |

Các cột giá trị:

| Column | Ý nghĩa |
|---|---|
| `machine_state` | Trạng thái máy tại mốc sample |
| `shot_ok_delta` | Số shot OK tăng thêm trong chu kỳ |
| `shot_ng_delta` | Số shot NG tăng thêm trong chu kỳ |
| `cycle_time_ms` | Cycle time hiện tại, đơn vị millisecond |
| `run_time_delta` | Run time tăng thêm trong chu kỳ, đơn vị second |
| `stop_time_delta` | Stop time tăng thêm trong chu kỳ, đơn vị second |
| `error_time_delta` | Error time tăng thêm trong chu kỳ, đơn vị second |

Các cột `*_delta*` là delta đã được Gateway tính từ counter tích lũy của PLC. OEE không cần tự trừ counter PLC.

`machine_state` và `cycle_time_ms` là giá trị hiện tại tại mốc sample, không phải delta.

## 5. Quy Ước `null`

`null` nghĩa là Gateway không có dữ liệu hợp lệ cho field đó tại mốc sample.

Một số nguyên nhân thường gặp:

- Signal chưa được cấu hình.
- PLC không trả giá trị.
- Value không convert được sang kiểu số.
- Counter PLC bị reset hoặc giảm so với mốc trước.
- Gateway mới restart và chưa có baseline trước đó.

OEE không nên tự động coi `null` là `0` nếu chưa có rule nghiệp vụ rõ ràng.

## 6. Cách Đọc Dữ Liệu Không Bị Trùng

OEE tự lưu checkpoint `last_processed_id` trong database của OEE.

Query đọc batch:

```sql
select
    id,
    gateway_id,
    machine_code,
    sampled_at_utc,
    machine_state,
    shot_ok_delta,
    shot_ng_delta,
    cycle_time_ms,
    run_time_delta,
    stop_time_delta,
    error_time_delta
from device_samples
where id > @last_processed_id
order by id
limit 10000;
```

Sau khi xử lý thành công batch, OEE lưu lại `max(id)` đã xử lý.

Gateway không lưu checkpoint cho OEE và không biết OEE đã đọc tới đâu.

## 7. Query Theo Máy Và Khoảng Thời Gian

Khi cần xem dữ liệu theo máy:

```sql
select
    id,
    gateway_id,
    machine_code,
    sampled_at_utc,
    machine_state,
    shot_ok_delta,
    shot_ng_delta,
    cycle_time_ms,
    run_time_delta,
    stop_time_delta,
    error_time_delta
from device_samples
where machine_code = @machine_code
  and sampled_at_utc >= @from_utc
  and sampled_at_utc < @to_utc
order by sampled_at_utc;
```

## 8. Index

Gateway tạo các index chính:

```sql
create index ix_device_samples_sampled_at
    on device_samples (sampled_at_utc);

create index ix_device_samples_machine_sampled_at
    on device_samples (machine_code, sampled_at_utc);

create index ix_device_samples_id
    on device_samples (id);
```

OEE nên ưu tiên đọc incremental bằng:

```sql
where id > @last_processed_id
order by id
```

## 9. Tần Suất Và Volume

Thiết kế hiện tại:

```text
Sample interval: 5 giây
Row format: 1 row / machine / sample
Số máy dự kiến: 50-60
Volume dự kiến: khoảng 864k - 1.04M row/ngày
```

## 10. Retention

Gateway có cơ chế xóa dữ liệu cũ theo cấu hình:

```text
History.RetentionDays = 90
```

Dữ liệu cũ hơn retention có thể bị xóa khỏi PostgreSQL Gateway.

Nếu OEE cần báo cáo dài hạn, OEE nên lưu dữ liệu đã xử lý vào database riêng của OEE.

## 11. Machine State

Team vận hành cần cung cấp mapping `machine_state` thực tế.

Ví dụ:

```text
0 = Unknown
1 = Running
2 = Stopped
3 = Error
```

OEE cần dùng đúng mapping này để tính Availability và các chỉ số OEE liên quan.


## 12. Contract Thay Đổi Trong Tương Lai

Gateway có thể thêm cột mới trong tương lai.

Nguyên tắc tương thích:

- Không rename hoặc xóa cột hiện tại nếu chưa thống nhất với team OEE.
- OEE nên select rõ column cần dùng, không phụ thuộc lâu dài vào `select *`.
- Nếu cần thêm metric mới, thêm cột mới hoặc bảng mới theo version contract riêng.

## 13. API Master Data Máy Từ MES CL

Ngoài dữ liệu sample trong PostgreSQL, hệ thống cần có API từ MES CL để lấy master data máy đúc.

Mục đích:

- Gateway hoặc OEE có thể đồng bộ danh sách máy.
- OEE có đủ thông tin hiển thị, báo cáo và mapping thiết bị.
- `machine_code` trong PostgreSQL có thể đối chiếu với mã thiết bị trong MES CL.

### 13.1. Endpoint Đề Xuất

MES CL cung cấp API readonly:

```http
GET /api/v1/equipment/molding-machines
```

Hoặc nếu MES CL đã có convention riêng, có thể dùng endpoint tương đương. Quan trọng là response phải có đủ field bên dưới.

### 13.2. Response Đề Xuất

```json
{
  "items": [
    {
      "code": "M16-01",
      "name": "May duc M16 so 01",
      "model": "JSW J450",
      "serial": "SN-2024-0001",
      "manufacturer": "JSW",
      "location": "Line A / Cell 01"
    }
  ]
}
```

### 13.3. Field Contract

| Field | Required | Ý nghĩa |
|---|---:|---|
| `code` | Yes | Mã số quản lý thiết bị |
| `name` | Yes | Tên thiết bị |
| `model` | No | Model máy |
| `serial` | No | Số sê-ri |
| `manufacturer` | No | Nhà cung cấp / hãng sản xuất |
| `location` | No | Vị trí lắp đặt |

Quy ước:

- `code` phải unique.
- `code` nên ổn định lâu dài, không đổi theo tên hiển thị.
- `code` trong API MES CL nên trùng với `machine_code` trong bảng `device_samples`.



