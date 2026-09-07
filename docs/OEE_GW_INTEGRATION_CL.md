# Rostek Gateway Integration Cho MES/OEE CLX4

Tài liệu này mô tả hướng tích hợp hiện tại: Gateway đọc máy, tự chuẩn bị dữ liệu OEE, lưu outbox local bằng SQLite và sync lên MES server qua HTTP.

## 1. Mục Đích

Rostek Gateway dùng:

- SQLite để lưu cấu hình local.
- SQLite để lưu production context và MES sync outbox.
- Runtime RAM để giữ latest signal values.
- SQLite để lưu raw checkpoint `PlcRawIntervals` theo mốc 5 giây.
- HTTP API để gửi OEE metrics lên MES.
- Timestamp của production context, raw interval và outbox dùng Unix seconds.

PostgreSQL Gateway không còn là contract chính cho hệ thống OEE bên ngoài đọc trực tiếp.

## 2. Luồng Dữ Liệu

```text
Modbus/OPC UA device
-> Gateway Runtime
-> MachineValueStore trong RAM
-> PlcRawIntervals trong SQLite
-> OEE metric builder
-> SQLite MES sync outbox
-> HTTP MES API
-> MES/OEE dashboard
```

Runtime chỉ đọc device và cập nhật RAM. Runtime không gọi HTTP và không biết MES server.

## 3. Cấu Hình MES Sync

File cấu hình ngoài:

```text
~/.gateway/appsettings.json
```

Section:

```json
{
  "MesSync": {
    "Enabled": false,
    "BaseUrl": "",
    "BearerToken": "",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "BatchSize": 100,
    "SyncIntervalMs": 5000
  }
}
```

`BearerToken` không commit vào repository. Production có thể override bằng environment variable:

```text
MesSync__Enabled=true
MesSync__BaseUrl=http://mes-server:8070
MesSync__BearerToken=<token>
```

## 4. Production Command Từ MES Xuống Gateway

MES gọi endpoint:

```http
POST /api/v1/mes/production-commands
```

Payload dùng snake_case:

```json
{
  "machine_code": "M16-01",
  "command_code": "CMD-20260907-001",
  "action": "start",
  "occurred_at_unix_seconds": 1788746400,
  "production_order_code": "MO-001",
  "operator_code": "OP-01",
  "reason_code": null,
  "note": null
}
```

`action` hỗ trợ V1:

```text
start
pause
stop
```

Gateway lưu trạng thái này vào SQLite `ProductionContexts`. Dữ liệu này dùng để đóng gói metric theo máy/lệnh sản xuất.

Nếu không gửi `occurred_at_unix_seconds`, Gateway tự dùng thời điểm hiện tại theo Unix seconds.

## 5. Signal Code Cần Cấu Hình

Gateway map signal theo `SignalCode`, không phân biệt hoa thường:

| SignalCode | Ý nghĩa |
|---|---|
| `MACHINE_STATE` | Trạng thái máy hiện tại |
| `SHOT_OK_COUNT` | Counter OK tích lũy từ PLC |
| `SHOT_NG_COUNT` | Counter NG tích lũy từ PLC |
| `CYCLE_TIME_MS` | Cycle time hiện tại, millisecond |
| `RUN_TIME_TOTAL` | Runtime tích lũy |
| `STOP_TIME_TOTAL` | Stop time tích lũy |
| `ERROR_TIME_TOTAL` | Error time tích lũy |

Gateway lưu raw interval vào SQLite rồi tính delta từ raw trước đó. Sau khi app restart, nếu còn raw interval trước đó trong SQLite thì gateway vẫn có thể tính tiếp metric.

## 6. HTTP Sync Lên MES

V1 sync endpoint:

```http
POST /secondly-production/sync
```

Header:

```http
Authorization: Bearer <token>
Content-Type: application/json
Accept: application/json
```

Body:

```json
{
  "data": [
    {
      "mode": "Started",
      "machine": "M16-01",
      "version": "1",
      "order_id": "MO-001",
      "tag": "CMD-20260907-001",
      "total": 10,
      "ng_qty": 1,
      "run_time": 5,
      "error_time": 0,
      "stop_time": 0,
      "prod_time": 5,
      "A": 1,
      "P": 1,
      "Q": 0.9,
      "cycle": 0.5,
      "OEE": 0.9,
      "product_id": null,
      "start_at": 1788746400,
      "end_at": 1788746405,
      "updated_at": 1788746405
    }
  ]
}
```

`A/P/Q/OEE` hiện là khung V1 và có thể thay đổi khi chốt công thức OEE chính thức.

Các field thời gian trong payload sync (`start_at`, `end_at`, `updated_at`) là Unix seconds.

## 7. Outbox Và Retry

Gateway luôn enqueue payload vào SQLite trước.

Nếu MES server lỗi hoặc mất mạng:

- message vẫn nằm trong outbox;
- Gateway retry theo `MesSync.RetryCount`;
- lỗi không làm sập runtime đọc máy.

Diagnostic:

```http
GET /api/v1/mes-sync/status
```

## 8. API Master Data Máy Từ MES CL

MES CL cần cung cấp API readonly để Gateway hoặc OEE dashboard lấy master data máy đúc:

```http
GET /api/v1/equipment/molding-machines
```

Response đề xuất:

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

| Field | Required | Ý nghĩa |
|---|---:|---|
| `code` | Yes | Mã số quản lý thiết bị |
| `name` | Yes | Tên thiết bị |
| `model` | No | Model máy |
| `serial` | No | Số sê-ri |
| `manufacturer` | No | Nhà cung cấp |
| `location` | No | Vị trí lắp đặt |

`code` nên trùng với `machine_code` Gateway đang cấu hình.
