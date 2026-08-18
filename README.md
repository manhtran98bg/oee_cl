# Rostek Industrial Gateway

Rostek Industrial Gateway là ứng dụng gateway cấu hình local cho các máy công nghiệp trong nhà máy. Mục tiêu của phiên bản hiện tại là quản lý cấu hình máy, template signal, apply cấu hình vào runtime, đọc dữ liệu thử từ thiết bị và hiển thị trạng thái vận hành trên dashboard.

## Công nghệ chính

- C# / .NET 10
- ASP.NET Core Razor Pages
- EF Core + SQLite cho cấu hình local
- Modular Monolith theo Clean Architecture, Ports and Adapters và DDD-lite
- Runtime đọc máy chạy trong RAM bằng immutable active configuration
- Modbus TCP và OPC UA runtime thử nghiệm

## Kiến trúc tổng quan

Solution được chia thành các project chính:

- `Rostek.Gateway.Host`: web host Razor Pages, API, background services và composition root.
- `Rostek.Gateway.Application`: application services, validation, dashboard, apply/version/rollback logic.
- `Rostek.Gateway.Domain`: entity và enum nghiệp vụ cấu hình gateway.
- `Rostek.Gateway.Infrastructure`: EF Core, SQLite persistence, migration và repository implementation.
- `Rostek.Gateway.Runtime`: runtime đọc máy, machine manager, Modbus TCP, OPC UA, value store trong RAM.
- `Rostek.Gateway.Contracts`: DTO và runtime contracts dùng để giữ dependency direction rõ ràng.

Chiều dependency chính:

```text
Host -> Application
Infrastructure -> Application / Domain / Contracts
Runtime -> Domain / Contracts
Application -> Domain / Contracts
```

Business logic không đặt trong Razor PageModel. PageModel chỉ nhận input, gọi Application service và trả view/result.

## Dữ liệu cấu hình

SQLite là nguồn cấu hình local chính. Cấu hình gồm:

- Machine Group
- Machine Template
- Template Signal
- Machine
- Machine Connection
- Signal Override
- Configuration Version
- Audit Log

Ứng dụng hỗ trợ draft, validate, apply, version history và rollback. Khi apply thành công, runtime nhận active configuration mới trong RAM và chỉ reload những máy bị thay đổi.

Đường dẫn dữ liệu mặc định:

```text
Windows: C:\Users\<user>\.gateway\data\config.db
Linux: /home/<user>/.gateway/data/config.db
```

## Runtime hiện tại

Runtime hiện có:

- Fake machine runtime để kiểm thử apply/reload.
- Modbus TCP runtime dùng NModbus, có request timeout, retry và reconnect backoff.
- OPC UA runtime thử nghiệm, hỗ trợ anonymous/no-security endpoint.
- Dashboard hiển thị trạng thái máy, trạng thái kết nối và các signal value mới nhất đang đọc được.

Dashboard đọc latest values từ RAM, không query trực tiếp database cho dữ liệu runtime realtime.

## Chạy ứng dụng

Trên Windows trong workspace hiện tại:

```powershell
cd E:\Rostek\CuuLong
$env:DOTNET_ROOT=(Resolve-Path .\.dotnet).Path
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
.\.dotnet\dotnet run --project src\Rostek.Gateway.Host --urls http://0.0.0.0:8080
```

Mở trình duyệt:

```text
http://localhost:8080
```

Dừng app bằng `Ctrl+C` trong terminal đang chạy app.

## Build và test

```powershell
.\.dotnet\dotnet build Rostek.Gateway.sln
.\.dotnet\dotnet test Rostek.Gateway.sln --no-build
```

## Ghi chú vận hành

- SQLite chỉ dùng local, không đặt file database trên network share.
- Secret không được lưu trong snapshot JSON.
- PostgreSQL remote cho dữ liệu sample/history OEE đang là hướng mở rộng tiếp theo, chưa thay thế SQLite config.
- OPC UA security nâng cao, Modbus RTU, telemetry pipeline và OEE aggregation chưa phải phạm vi hoàn chỉnh của phiên bản hiện tại.
