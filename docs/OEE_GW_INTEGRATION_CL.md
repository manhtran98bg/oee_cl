# Rostek Gateway Integration Cho MES/OEE CLX4

Tài liệu này mô tả luồng tích hợp hiện tại giữa Rostek Gateway và MES/OEE CLX4.

## 1. Mục Đích

Rostek Gateway dùng:

- SQLite `config.db` để lưu cấu hình local: group, template, machine, signal, version.
- SQLite `oee.db` để lưu production context và raw PLC interval local.
- Runtime RAM để giữ latest signal values.
- HTTP API để nhận production command từ MES và gửi realtime OEE snapshot lên MES.
- Unix seconds cho các mốc thời gian OEE.

PostgreSQL không còn là contract chính cho hệ thống OEE bên ngoài đọc trực tiếp.

## 2. Luồng Dữ Liệu

```text
MES production command
-> Gateway API
-> production_context trong oee.db
-> ProductionContextCache trong RAM

Modbus/OPC UA device
-> Gateway Runtime
-> MachineValueStore trong RAM
-> plc_raw_interval trong oee.db
-> Realtime OEE snapshot builder
-> HTTP MES realtime snapshot API
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
    "SyncIntervalMs": 5000,
    "RequireProductionContext": false
  }
}
```

`RequireProductionContext=false` là chế độ test: Gateway tự tạo context test nếu chưa nhận command thật từ MES. Production nên đổi thành `true`.

Production có thể override bằng environment variable:

```text
MesSync__Enabled=true
MesSync__BaseUrl=http://mes-server:8070
MesSync__BearerToken=<token>
MesSync__RequireProductionContext=true
```

## 4. Production Command MES -> Gateway

Endpoint chính:

```http
POST /api/v1/gateway/oee/production-commands
```

Payload:

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

`action` hỗ trợ:

```text
start
pause
stop
```

Gateway tự sinh `session_id` khi nhận `start`. Một máy có thể có nhiều order active cùng lúc, nhưng không có hai active sessions cùng `machine_code + order_id`.

`production_context` chỉ lưu các session hiện hành `active` hoặc `pause`. Khi MES gửi `stop`, Gateway đóng dòng history trong `production_period`, sau đó xoá session đó khỏi `production_context`.

## 5. Signal Code Cần Cấu Hình

Gateway map signal theo `SignalCode`, không phân biệt hoa thường:

| SignalCode | Ý nghĩa |
|---|---|
| `MACHINE_STATE` | Trạng thái máy hiện tại |
| `SHOT_OK_COUNT` | Counter OK tích lũy từ PLC |
| `SHOT_NG_COUNT` | Counter NG tích lũy từ PLC |
| `CYCLE_TIME_MS` | Cycle time hiện tại, millisecond |
| `RUN_TIME_TOTAL` | Runtime tích lũy, đơn vị giây |
| `STOP_TIME_TOTAL` | Stop time tích lũy, đơn vị giây |
| `ERROR_TIME_TOTAL` | Error time tích lũy, đơn vị giây |

Gateway lưu raw interval theo mốc cấu hình, mặc định 5 giây. Realtime snapshot tính từ baseline được lưu trong `production_context` của từng `session_id`.

## 6. Realtime Snapshot Gateway -> MES

Endpoint MES cần mở:

```http
POST /api/v1/gateway/oee/realtime-snapshots
```

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

Mỗi item là snapshot realtime cho một session/order đang active hoặc pause.

## 7. API Master Data Máy Từ MES CL

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

`code` nên trùng với `machine_code` Gateway đang cấu hình.
