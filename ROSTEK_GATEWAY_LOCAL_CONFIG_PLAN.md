# KẾ HOẠCH XÂY DỰNG ROSTEK INDUSTRIAL GATEWAY

## 1. Mục đích tài liệu

Tài liệu này mô tả phương án xây dựng ứng dụng **Rostek Industrial Gateway** bằng C# và ASP.NET Core.

Tài liệu được viết để sử dụng làm đầu vào cho Codex khi:

- Phân tích yêu cầu.
- Lập kế hoạch triển khai.
- Tạo solution và cấu trúc project.
- Xây dựng từng module theo giai đoạn.
- Viết migration, service, Razor Pages và kiểm thử.
- Đánh giá mức độ hoàn thành theo tiêu chí nghiệm thu.

Phương án trong tài liệu này đơn giản hóa thiết kế MES Gateway ban đầu:

- Không quản lý cấu hình tập trung tại MES Server trong phiên bản đầu.
- Cấu hình được lưu local trên chính máy chạy Gateway.
- Gateway tự host website cấu hình trong mạng nội bộ.
- SQLite là nguồn dữ liệu cấu hình chính.
- JSON chỉ dùng cho import, export, backup và snapshot.
- Thiết kế hướng tới khoảng 50 máy công nghiệp trên một Gateway.

---

# 2. Bối cảnh hệ thống

Gateway được cài trên một máy tính công nghiệp hoặc máy tính Ubuntu/Windows trong mạng nhà máy.

Gateway phải có khả năng:

1. Host một website để kỹ thuật viên cấu hình hệ thống.
2. Quản lý khoảng 50 máy công nghiệp.
3. Hỗ trợ trước mắt:
   - OPC UA.
   - Modbus TCP.
4. Quản lý mapping tín hiệu của từng loại máy.
5. Áp dụng cấu hình mà không phải khởi động lại toàn bộ Gateway.
6. Chỉ restart kết nối của máy có cấu hình thay đổi.
7. Chạy độc lập khi không có kết nối Internet.
8. Sau này có thể gửi dữ liệu lên MES Server.

Sơ đồ triển khai:

```text
Máy vận hành/kỹ thuật
        │
        │ HTTP qua mạng LAN
        ▼
http://<gateway-ip>:8080
        │
        ▼
┌──────────────────────────────────────────┐
│ Rostek Industrial Gateway               │
│                                          │
│ ASP.NET Core                             │
│ ├── Razor Pages                          │
│ ├── Configuration Services              │
│ ├── Machine Runtime Manager              │
│ ├── OPC UA Collector                     │
│ ├── Modbus TCP Collector                 │
│ ├── SQLite config.db                     │
│ └── Log files                            │
└──────────────────────────────────────────┘
        │
        ├── OPC UA ───────> Máy Sumitomo
        └── Modbus TCP ───> PLC/Máy JSW
```

---

# 3. Mục tiêu phiên bản đầu

## 3.1. Mục tiêu chính

Phiên bản đầu tập trung vào **host cấu hình local**, chưa tập trung xây dựng đầy đủ pipeline telemetry.

Kết quả cần đạt:

- Chạy được một ASP.NET Core application.
- Truy cập được website từ máy khác trong LAN.
- Quản lý danh sách khoảng 50 máy.
- Quản lý nhóm máy.
- Quản lý template máy.
- Quản lý template tín hiệu.
- Cấu hình kết nối OPC UA.
- Cấu hình kết nối Modbus TCP.
- Cho phép override tín hiệu trên từng máy.
- Validate toàn bộ cấu hình.
- Apply cấu hình thành một version active.
- Lưu lịch sử version.
- Rollback về nội dung version trước.
- Export/import cấu hình JSON.
- Runtime đọc cấu hình active từ bộ nhớ.
- Có simulator hoặc fake runtime để kiểm thử Apply.

## 3.2. Chưa thuộc phạm vi phiên bản đầu

Các chức năng sau chưa bắt buộc:

- Tính OEE.
- Dashboard sản xuất hoàn chỉnh.
- Lưu lịch sử telemetry dung lượng lớn.
- Gửi telemetry thực tế lên MES.
- MQTT.
- Cloud management.
- Cập nhật phần mềm từ xa.
- High availability.
- Cluster nhiều Gateway.
- OPC UA Node Browser hoàn chỉnh.
- Quản lý certificate nâng cao.
- Phân quyền nhiều cấp phức tạp.
- Điều khiển ghi ngược xuống máy.
- Gọi OPC UA Method.
- Ghi Modbus Register từ web.

---

# 4. Quyết định kiến trúc

## 4.1. Công nghệ

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ | C# |
| Runtime | .NET 10 LTS |
| Application host | ASP.NET Core |
| Web UI | Razor Pages |
| CSS/UI | Bootstrap |
| API nội bộ | Minimal API hoặc API Controller |
| ORM | Entity Framework Core |
| Database cấu hình | SQLite |
| JSON | System.Text.Json |
| Background processing | BackgroundService |
| Logging | Microsoft.Extensions.Logging, có thể bổ sung Serilog |
| OPC UA | OPC Foundation UA .NET Standard |
| Modbus TCP | Thư viện Modbus được bọc sau interface riêng |
| Realtime UI | SignalR ở giai đoạn sau |
| Test | xUnit |
| Mock | NSubstitute hoặc Moq |

## 4.2. Nguyên tắc

1. SQLite là nguồn cấu hình chính.
2. File SQLite phải nằm trên ổ local của Gateway.
3. Không đặt SQLite trên SMB hoặc NFS.
4. Không dùng JSON làm database cấu hình chính.
5. Không query SQLite trong mỗi chu kỳ đọc thiết bị.
6. Runtime chỉ sử dụng cấu hình active đã được build trong RAM.
7. Cấu hình đang chỉnh sửa là draft.
8. Draft không ảnh hưởng tới runtime.
9. Chỉ Apply sau khi validation thành công.
10. Mỗi lần Apply tạo một version mới.
11. Version đã Apply là immutable snapshot.
12. Rollback tạo version mới từ nội dung version cũ.
13. Không restart toàn bộ Gateway khi sửa một máy.
14. Chỉ stop/start runtime của máy bị thay đổi.
15. Logic nghiệp vụ không đặt trực tiếp trong Razor PageModel.
16. OPC UA và Modbus phải được che sau interface thống nhất.
17. Không lưu password rõ trong JSON export hoặc log.
18. Telemetry database sau này phải tách khỏi config database.

## 4.3. Pattern kiến trúc

Ứng dụng được tổ chức theo mô hình:

```text
Modular Monolith
+ Clean Architecture principles
+ Ports and Adapters
+ DDD-lite
```

Ý nghĩa:

- **Modular Monolith:** toàn bộ Gateway được triển khai và chạy như một ứng dụng/process duy nhất, nhưng mã nguồn được chia thành các project/module có trách nhiệm rõ ràng.
- **Clean Architecture:** logic domain và application không phụ thuộc ASP.NET Core, EF Core, SQLite, OPC UA SDK hoặc thư viện Modbus.
- **Ports and Adapters:** Core khai báo các interface/port; Infrastructure và Runtime cung cấp implementation/adapter.
- **DDD-lite:** dùng entity, value object khi cần, quy tắc domain, repository abstraction và ngôn ngữ nghiệp vụ thống nhất; không triển khai full DDD, CQRS hoặc event sourcing nếu chưa có nhu cầu.

Ứng dụng này **không phải microservices**. Khi deploy chỉ cần một ứng dụng Gateway, một file SQLite cấu hình và các thư mục dữ liệu liên quan.

Quy tắc dependency quan trọng:

```text
Framework và adapter bên ngoài
        ↓
Application/Core abstractions
        ↓
Domain
```

Các project phía trong không được reference ngược ra project phía ngoài.

Ví dụ hợp lệ:

```text
Host           → Application
Infrastructure → Application + Domain
Runtime        → Domain + Contracts
Application    → Domain + Contracts
```

Ví dụ không hợp lệ:

```text
Domain → Infrastructure
Domain → Host
Application → Host
Runtime → Host
```

Mục tiêu của pattern này không phải tạo nhiều project, mà là bảo đảm:

- UI không chứa logic kết nối thiết bị.
- Domain không phụ thuộc database.
- Runtime không phụ thuộc Razor Pages.
- Thay SQLite hoặc SDK giao thức không buộc phải viết lại nghiệp vụ cấu hình.
- Một máy lỗi không kéo theo toàn bộ ứng dụng.

---

# 5. Kiến trúc ứng dụng

```text
┌────────────────────────────────────────────────────┐
│                 ASP.NET Core Host                  │
│                                                    │
│  ┌──────────────────────────────────────────────┐  │
│  │ Web UI - Razor Pages                        │  │
│  │                                              │  │
│  │ Dashboard cấu hình                           │  │
│  │ Machine Groups                               │  │
│  │ Machine Templates                            │  │
│  │ Machines                                     │  │
│  │ Connections                                  │  │
│  │ Signal Mapping                               │  │
│  │ Validation                                   │  │
│  │ Apply / Rollback                             │  │
│  │ Import / Export                              │  │
│  └──────────────────────────────────────────────┘  │
│                        │                           │
│  ┌──────────────────────────────────────────────┐  │
│  │ Application Services                       │  │
│  │                                              │  │
│  │ MachineConfigurationService                  │  │
│  │ TemplateService                              │  │
│  │ ConfigurationBuilder                         │  │
│  │ ConfigurationValidator                       │  │
│  │ ConfigurationApplyService                    │  │
│  │ ConfigurationImportExportService             │  │
│  └──────────────────────────────────────────────┘  │
│                        │                           │
│  ┌──────────────────────────────────────────────┐  │
│  │ SQLite config.db                            │  │
│  └──────────────────────────────────────────────┘  │
│                        │                           │
│  ┌──────────────────────────────────────────────┐  │
│  │ Runtime Configuration Manager              │  │
│  │                                              │  │
│  │ Active immutable configuration in RAM        │  │
│  │ Configuration diff                           │  │
│  │ Selective machine reload                     │  │
│  └──────────────────────────────────────────────┘  │
│                        │                           │
│  ┌──────────────────────────────────────────────┐  │
│  │ Machine Runtime Manager                    │  │
│  │                                              │  │
│  │ OPC UA runtime                               │  │
│  │ Modbus runtime                               │  │
│  │ Reconnect                                    │  │
│  │ Status                                       │  │
│  └──────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────┘
```

---

# 6. Cấu trúc solution

## 6.1. Mục đích chia project

Solution là tập hợp các project cùng tạo thành một ứng dụng Gateway.

Mỗi project được build thành một assembly riêng. `Rostek.Gateway.Host` là executable/composition root; các project còn lại chủ yếu tạo DLL và không tự chạy độc lập.

Việc chia project nhằm dùng compiler để kiểm soát dependency, không chỉ để tổ chức thư mục.

```text
Rostek.Gateway.sln

src/
├── Rostek.Gateway.Host
│   ├── Pages/
│   ├── Api/
│   ├── BackgroundServices/
│   ├── Authorization/
│   ├── Program.cs
│   ├── appsettings.json
│   └── wwwroot/
│
├── Rostek.Gateway.Application
│   ├── MachineGroups/
│   ├── MachineTemplates/
│   ├── Machines/
│   ├── Configurations/
│   ├── Validation/
│   ├── ImportExport/
│   └── Audit/
│
├── Rostek.Gateway.Domain
│   ├── Entities/
│   ├── Enums/
│   ├── ValueObjects/
│   ├── Events/
│   └── Exceptions/
│
├── Rostek.Gateway.Infrastructure
│   ├── Persistence/
│   ├── Repositories/
│   ├── Serialization/
│   ├── Backup/
│   ├── Security/
│   └── Logging/
│
├── Rostek.Gateway.Runtime
│   ├── Configuration/
│   ├── Machines/
│   ├── OpcUa/
│   ├── Modbus/
│   └── Diagnostics/
│
└── Rostek.Gateway.Contracts
    ├── Configuration/
    ├── Machines/
    └── Diagnostics/

tests/
├── Rostek.Gateway.UnitTests
├── Rostek.Gateway.IntegrationTests
└── Rostek.Gateway.ArchitectureTests
```

Đây là cấu trúc target cho dự án. Với repository rất nhỏ có thể tạm gộp `Domain` và `Application` thành `Core`, nhưng Codex không được tự gộp hoặc đổi cấu trúc nếu repository đã có các project trên.

## 6.2. `Rostek.Gateway.Host`

Đây là project chạy chính và là **composition root**.

Chứa:

- ASP.NET Core host và Kestrel.
- Razor Pages.
- API endpoint.
- Authentication và authorization.
- Middleware.
- Health checks.
- Static files.
- Đăng ký dependency injection.
- Đăng ký hosted service để khởi động runtime supervisor.

Được phép:

- Nhận HTTP request.
- Model binding.
- Kiểm tra `ModelState`.
- Gọi application service.
- Trả HTML, JSON, redirect hoặc `ProblemDetails`.
- Cấu hình các implementation ở `Program.cs`.

Không được:

- Truy cập trực tiếp OPC UA session trong PageModel.
- Chứa logic merge template và override.
- Thực hiện transaction nghiệp vụ dài trong PageModel.
- Query `GatewayDbContext` trực tiếp từ Razor Page nếu đã có application service.
- Tự quyết định máy nào phải restart sau Apply.

Ví dụ luồng đúng:

```text
Razor Page/API
      ↓
Application Service
      ↓
Repository/Runtime abstraction
```

## 6.3. `Rostek.Gateway.Application`

Đây là lớp **use case và điều phối nghiệp vụ**.

Chứa các hành động mà hệ thống cung cấp:

- Tạo, sửa, clone và vô hiệu hóa máy.
- Quản lý group/template/signal.
- Build effective configuration.
- Validate draft.
- Apply configuration.
- Tạo version.
- Rollback.
- Import/export.
- Ghi audit.
- Tính configuration diff ở cấp nghiệp vụ.

Application được phép:

- Phụ thuộc `Domain`.
- Phụ thuộc `Contracts`.
- Khai báo các interface/port cần Infrastructure hoặc Runtime triển khai.
- Điều phối transaction thông qua abstraction.

Application không được:

- Reference ASP.NET Core.
- Reference EF Core/SQLite trực tiếp.
- Reference OPC Foundation SDK.
- Reference thư viện Modbus cụ thể.
- Biết chi tiết HTML hoặc CSS.

Ví dụ port do Application khai báo:

```csharp
public interface IMachineRepository
{
    Task<Machine?> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(
        CancellationToken cancellationToken);
}
```

## 6.4. `Rostek.Gateway.Domain`

Đây là lớp chứa **khái niệm và quy tắc cốt lõi** của Gateway.

Chứa:

- Entity: `Machine`, `MachineGroup`, `MachineTemplate`, `TemplateSignal`, `MachineConnection`, `ConfigurationVersion`.
- Enum: protocol, status, data type.
- Value object ở nơi có giá trị: `MachineCode`, `SignalCode`, checksum.
- Domain exception.
- Quy tắc luôn đúng bất kể UI/database/protocol SDK nào được sử dụng.

Ví dụ quy tắc domain:

- Mã máy không được rỗng.
- Signal bắt buộc không được disable.
- Version active không được sửa nội dung.
- Protocol phải thuộc tập giá trị hỗ trợ.
- Rollback không chỉnh sửa version cũ.

Domain không được reference:

- ASP.NET Core.
- EF Core.
- SQLite.
- OPC UA SDK.
- Modbus SDK.
- `Host`, `Infrastructure` hoặc `Runtime`.

Không phải mọi validation đều đặt trong Domain. Quy tắc cần query toàn database, ví dụ machine code duy nhất, thường nằm trong Application validator/repository workflow.

## 6.5. `Rostek.Gateway.Infrastructure`

Đây là các adapter kỹ thuật phục vụ lưu trữ và hệ thống bên ngoài không thuộc machine runtime.

Chứa:

- `GatewayDbContext`.
- EF Core entity configuration.
- Migration.
- SQLite initialization và PRAGMA.
- Repository implementation.
- JSON serializer cho import/export/snapshot.
- Backup SQLite.
- Secret storage.
- Audit persistence.
- File system service.

Infrastructure thường implement các interface được khai báo tại Application.

Ví dụ:

```text
Application port:
IMachineRepository

Infrastructure adapter:
EfCoreMachineRepository
```

Infrastructure không được reference `Host` hoặc chứa Razor Pages.

## 6.6. `Rostek.Gateway.Runtime`

Đây là adapter/layer đặc thù cho **quá trình vận hành thiết bị công nghiệp dài hạn**.

Chứa:

- Active immutable configuration trong RAM.
- Runtime configuration provider.
- Machine runtime manager.
- Machine runtime factory.
- OPC UA client wrapper.
- OPC UA session/subscription/reconnect.
- Modbus client wrapper và polling plan.
- Connection concurrency limiter.
- Runtime status và diagnostics.
- Selective reload.

Lý do tách Runtime khỏi Infrastructure:

- Phần này có vòng đời dài và stateful.
- Quản lý đồng thời khoảng 50 kết nối.
- Có reconnect, cancellation, throttling và shutdown.
- Sau này có thể lớn hơn phần persistence.
- Không nên biến Infrastructure thành nơi chứa mọi adapter.

Runtime không được:

- Reference `Host`.
- Dùng Razor PageModel.
- Query SQLite trong mỗi chu kỳ đọc.
- Serialize trực tiếp EF entity thành runtime configuration.
- Tự chỉnh sửa draft configuration.

## 6.7. `Rostek.Gateway.Contracts`

Chứa các schema/model được truyền qua ranh giới:

- API request/response ổn định.
- JSON import/export.
- Configuration snapshot.
- Machine status DTO.
- Gateway diagnostics DTO.
- Payload gửi MES trong giai đoạn sau.

Contracts chỉ nên chứa dữ liệu và enum/schema cần chia sẻ; không chứa nghiệp vụ hoặc dependency framework.

Không trả trực tiếp EF/domain entity ra API hoặc file export.

## 6.8. Các project test

### `Rostek.Gateway.UnitTests`

Test không cần database hoặc mạng thật:

- Domain rule.
- Template/override merge.
- Validation.
- Configuration diff.
- Checksum.
- Runtime decision.

### `Rostek.Gateway.IntegrationTests`

Test với SQLite thật ở file tạm:

- EF migration.
- Constraint/index.
- Repository.
- Apply transaction.
- Import/export.
- Razor/API ở mức integration khi cần.

Không dùng EF Core InMemory thay cho SQLite trong các test cần kiểm tra hành vi database.

### `Rostek.Gateway.ArchitectureTests`

Kiểm tra dependency:

- Domain không reference EF Core/ASP.NET Core.
- Application không reference Host.
- Runtime không reference Host.
- Host PageModel không reference OPC UA SDK.
- Không có circular project reference.

Project này có thể tạo sau, nhưng các rule phải được tuân thủ từ đầu.

## 6.9. Chiều dependency

```text
Host
├── Application
├── Infrastructure
├── Runtime
└── Contracts

Application
├── Domain
└── Contracts

Infrastructure
├── Application
└── Domain

Runtime
├── Domain
└── Contracts

Domain
└── không reference project nội bộ nào

Contracts
└── không reference project nội bộ nào
```

`Host` biết toàn bộ implementation để lắp dependency injection. Đây là nơi duy nhất nên biết đồng thời Application, Infrastructure và Runtime.

Không cho phép circular dependency.

## 6.10. Phân biệt Domain và các loại Model

Từ `Model` là khái niệm rộng. Không dùng một class `MachineModel` cho toàn bộ UI, database, API và runtime.

### Domain Entity

Biểu diễn đối tượng nghiệp vụ có định danh và vòng đời:

```text
Machine
MachineTemplate
TemplateSignal
ConfigurationVersion
```

Có thể chứa hành vi và bảo vệ invariant.

### Input Model

Nhận dữ liệu chưa tin cậy từ form/API:

```text
MachineCreateInput
MachineEditInput
OpcUaConnectionInput
```

Được phép có public setter và DataAnnotations. Không được runtime sử dụng.

### View Model

Chuẩn bị dữ liệu cho một trang:

```text
MachineEditViewModel
MachineListViewModel
ConfigurationPageViewModel
```

Có thể kết hợp dữ liệu SQLite, runtime status và danh sách lựa chọn.

### Contract/DTO

Dùng để truyền qua API hoặc JSON:

```text
MachineStatusDto
ConfigurationExportDocument
MachineConfigurationDto
```

Không chứa domain behavior và không lộ secret.

### Persistence Model

Với phiên bản đầu, EF Core có thể map trực tiếp Domain Entity để giảm code.

Chỉ tách persistence row/model riêng nếu:

- Schema database khác đáng kể domain.
- Cần thay đổi persistence độc lập.
- Mapping trực tiếp làm domain phụ thuộc framework.
- Có legacy database.

Không tạo persistence model riêng một cách máy móc.

### Runtime Model

Là cấu hình immutable, đầy đủ mà collector sử dụng:

```text
RuntimeConfiguration
EffectiveMachineConfiguration
EffectiveConnectionConfiguration
EffectiveSignalConfiguration
```

Runtime model được build từ Domain/Persistence data sau Apply và được giữ trong RAM. Nó không chứa draft, DataAnnotations, navigation property hoặc secret dạng rõ.

Luồng model:

```text
HTML/API
   ↓
Input Model
   ↓
Application Service
   ↓
Domain Entity
   ↓
EF Core/SQLite
   ↓ Apply
Runtime Model trong RAM
   ↓
OPC UA/Modbus Runtime
   ↓
Status DTO
   ↓
Web/API
```

## 6.11. Quy tắc đặt code

| Loại code | Project |
|---|---|
| Razor Page, API endpoint, middleware | Host |
| Use case, validation, apply, rollback | Application |
| Entity, invariant, enum, value object | Domain |
| DbContext, migration, repository implementation | Infrastructure |
| OPC UA/Modbus, runtime cache, supervisor | Runtime |
| API DTO, snapshot/import/export schema | Contracts |
| Domain/configuration logic test | UnitTests |
| SQLite/API integration test | IntegrationTests |

Quy tắc dễ nhớ:

```text
Giao diện và HTTP       → Host
Use case                → Application
Nghiệp vụ cốt lõi       → Domain
Database và file        → Infrastructure
Kết nối máy đang chạy   → Runtime
Dữ liệu truyền qua biên → Contracts
```

---

# 7. Mô hình cấu hình cho 50 máy

## 7.1. Lý do cần template

Không nên nhập lại toàn bộ signal cho từng máy.

Ví dụ:

```text
30 máy Sumitomo OPC UA
20 máy JSW Modbus TCP
10 tín hiệu chuẩn/máy
```

Nếu cấu hình độc lập:

```text
50 × 10 = 500 cấu hình tín hiệu
```

Nếu dùng template:

```text
Template SUMITOMO-OPCUA
└── 10 TemplateSignals

Template JSW-MODBUS
└── 10 TemplateSignals

50 Machines
50 Connections
Một số Signal Overrides
```

Template giúp:

- Giảm nhập lặp.
- Tránh sai khác giữa các máy cùng loại.
- Sửa đồng loạt.
- Clone máy nhanh.
- Import danh sách 50 máy dễ hơn.
- Validation đơn giản hơn.

## 7.2. Effective configuration

Cấu hình cuối cùng của một máy được build từ:

```text
Machine
   +
MachineTemplate
   +
TemplateSignals
   +
MachineSignalOverrides
   =
EffectiveMachineConfiguration
```

---

# 8. Database SQLite

Tên file:

```text
config.db
```

Đường dẫn khuyến nghị:

## Windows

```text
C:\ProgramData\Rostek\Gateway\data\config.db
```

## Linux

```text
/var/lib/rostek-gateway/config.db
```

Không lưu database trong thư mục chứa executable.

## 8.1. Bảng `MachineGroups`

```text
MachineGroups
├── Id                 TEXT/GUID PK
├── Code               TEXT UNIQUE NOT NULL
├── Name               TEXT NOT NULL
├── Description        TEXT NULL
├── DisplayOrder       INTEGER NOT NULL
├── CreatedAtUtc       TEXT NOT NULL
└── UpdatedAtUtc       TEXT NOT NULL
```

Ví dụ:

```text
MOLDING-A
MOLDING-B
PACKING
UTILITY
```

## 8.2. Bảng `MachineTemplates`

```text
MachineTemplates
├── Id                         TEXT/GUID PK
├── Code                       TEXT UNIQUE NOT NULL
├── Name                       TEXT NOT NULL
├── Protocol                   TEXT NOT NULL
├── Manufacturer               TEXT NULL
├── Model                      TEXT NULL
├── DefaultPollingIntervalMs   INTEGER NOT NULL
├── Description                TEXT NULL
├── Enabled                    INTEGER NOT NULL
├── CreatedAtUtc               TEXT NOT NULL
└── UpdatedAtUtc               TEXT NOT NULL
```

Protocol:

```text
OPCUA
MODBUS_TCP
```

## 8.3. Bảng `Machines`

```text
Machines
├── Id                 TEXT/GUID PK
├── Code               TEXT UNIQUE NOT NULL
├── Name               TEXT NOT NULL
├── GroupId            TEXT NULL FK
├── TemplateId         TEXT NOT NULL FK
├── Enabled            INTEGER NOT NULL
├── DisplayOrder       INTEGER NOT NULL
├── Description        TEXT NULL
├── CreatedAtUtc       TEXT NOT NULL
└── UpdatedAtUtc       TEXT NOT NULL
```

Runtime status không lưu trực tiếp trong bảng này. Runtime status nằm trong memory và chỉ lưu snapshot chẩn đoán nếu cần.

## 8.4. Bảng `MachineConnections`

Mỗi máy chỉ có một connection active.

```text
MachineConnections
├── Id                   TEXT/GUID PK
├── MachineId            TEXT UNIQUE NOT NULL FK
├── Protocol             TEXT NOT NULL
├── Host                 TEXT NULL
├── Port                 INTEGER NULL
├── EndpointUrl          TEXT NULL
├── UnitId               INTEGER NULL
├── SecurityMode         TEXT NULL
├── SecurityPolicy       TEXT NULL
├── AuthenticationMode   TEXT NULL
├── CredentialReference  TEXT NULL
├── ConnectTimeoutMs     INTEGER NOT NULL
├── RequestTimeoutMs     INTEGER NOT NULL
├── RetryCount           INTEGER NOT NULL
├── PollingIntervalMs    INTEGER NULL
├── OptionsJson          TEXT NULL
├── CreatedAtUtc         TEXT NOT NULL
└── UpdatedAtUtc         TEXT NOT NULL
```

Quy tắc:

- OPC UA cần `EndpointUrl`.
- Modbus TCP cần `Host`, `Port`, `UnitId`.
- Không lưu password vào `OptionsJson`.
- `CredentialReference` chỉ là mã tham chiếu.

## 8.5. Bảng `TemplateSignals`

```text
TemplateSignals
├── Id                   TEXT/GUID PK
├── TemplateId           TEXT NOT NULL FK
├── SignalCode           TEXT NOT NULL
├── DisplayName          TEXT NOT NULL
├── SourceAddress        TEXT NOT NULL
├── DataType             TEXT NOT NULL
├── AccessMode           TEXT NOT NULL
├── SamplingIntervalMs   INTEGER NULL
├── ScalingFactor        REAL NOT NULL
├── ScalingOffset        REAL NOT NULL
├── Required             INTEGER NOT NULL
├── Enabled              INTEGER NOT NULL
├── ValueMappingJson     TEXT NULL
├── OptionsJson          TEXT NULL
├── DisplayOrder         INTEGER NOT NULL
├── CreatedAtUtc         TEXT NOT NULL
└── UpdatedAtUtc         TEXT NOT NULL
```

Unique index:

```text
TemplateId + SignalCode
```

Signal chuẩn ban đầu:

```text
machine_state
connection_state
good_shot_count
ng_shot_count
mold_open_count
cycle_time_ms
fault_code
run_time_seconds
stop_time_seconds
error_time_seconds
```

## 8.6. Bảng `MachineSignalOverrides`

Chỉ lưu khác biệt so với template.

```text
MachineSignalOverrides
├── Id                   TEXT/GUID PK
├── MachineId            TEXT NOT NULL FK
├── TemplateSignalId     TEXT NOT NULL FK
├── SourceAddress        TEXT NULL
├── DataType             TEXT NULL
├── SamplingIntervalMs   INTEGER NULL
├── ScalingFactor        REAL NULL
├── ScalingOffset        REAL NULL
├── Enabled              INTEGER NULL
├── ValueMappingJson     TEXT NULL
├── OptionsJson          TEXT NULL
├── CreatedAtUtc         TEXT NOT NULL
└── UpdatedAtUtc         TEXT NOT NULL
```

Unique index:

```text
MachineId + TemplateSignalId
```

Giá trị `NULL` nghĩa là dùng giá trị từ template.

## 8.7. Bảng `ConfigurationVersions`

```text
ConfigurationVersions
├── Id              TEXT/GUID PK
├── Version         INTEGER UNIQUE NOT NULL
├── Status          TEXT NOT NULL
├── SnapshotJson    TEXT NOT NULL
├── Checksum        TEXT NOT NULL
├── Description     TEXT NULL
├── CreatedBy       TEXT NULL
├── CreatedAtUtc    TEXT NOT NULL
└── AppliedAtUtc    TEXT NULL
```

Status:

```text
ACTIVE
SUPERSEDED
FAILED
```

Không cần lưu DRAFT vào bảng này. Draft chính là dữ liệu hiện tại trong các bảng cấu hình.

## 8.8. Bảng `AuditLogs`

```text
AuditLogs
├── Id              TEXT/GUID PK
├── UserName        TEXT NULL
├── Action          TEXT NOT NULL
├── EntityType      TEXT NOT NULL
├── EntityId        TEXT NULL
├── OldValueJson    TEXT NULL
├── NewValueJson    TEXT NULL
├── CreatedAtUtc    TEXT NOT NULL
└── CorrelationId   TEXT NULL
```

Audit tối thiểu:

- Create machine.
- Update machine.
- Disable machine.
- Change endpoint.
- Change signal mapping.
- Import configuration.
- Apply configuration.
- Rollback configuration.

---

# 9. SQLite configuration

Khi khởi tạo database:

```sql
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
PRAGMA synchronous = NORMAL;
```

Nguyên tắc transaction:

- Transaction phải ngắn.
- Không gọi OPC UA trong transaction.
- Không gọi Modbus trong transaction.
- Không chờ HTTP trong transaction.
- Không giữ transaction trong thời gian người dùng chỉnh form.
- Apply snapshot và cập nhật trạng thái phải atomic.
- Backup database trước migration schema.

Index tối thiểu:

```text
Machines.Code
Machines.GroupId
Machines.TemplateId
MachineTemplates.Code
TemplateSignals.TemplateId
TemplateSignals.TemplateId + SignalCode
MachineSignalOverrides.MachineId
ConfigurationVersions.Version
AuditLogs.CreatedAtUtc
```

---

# 10. Các model runtime

## 10.1. Runtime configuration

```csharp
public sealed record RuntimeConfiguration(
    long Version,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyDictionary<string, EffectiveMachineConfiguration> Machines);
```

## 10.2. Effective machine configuration

```csharp
public sealed record EffectiveMachineConfiguration(
    Guid MachineId,
    string MachineCode,
    string MachineName,
    string Protocol,
    bool Enabled,
    EffectiveConnectionConfiguration Connection,
    IReadOnlyList<EffectiveSignalConfiguration> Signals);
```

## 10.3. Effective signal configuration

```csharp
public sealed record EffectiveSignalConfiguration(
    string SignalCode,
    string SourceAddress,
    string DataType,
    int? SamplingIntervalMs,
    double ScalingFactor,
    double ScalingOffset,
    bool Required,
    bool Enabled,
    IReadOnlyDictionary<string, string>? ValueMapping,
    IReadOnlyDictionary<string, object>? Options);
```

Các record runtime phải immutable.

---

# 11. Configuration services

## 11.1. `IConfigurationBuilder`

```csharp
public interface IConfigurationBuilder
{
    Task<RuntimeConfiguration> BuildDraftAsync(
        CancellationToken cancellationToken);

    Task<RuntimeConfiguration> BuildVersionAsync(
        long version,
        CancellationToken cancellationToken);
}
```

Trách nhiệm:

- Load dữ liệu relational.
- Áp dụng template.
- Áp dụng override.
- Parse JSON option.
- Tạo effective configuration.
- Không kết nối thiết bị.

## 11.2. `IConfigurationValidator`

```csharp
public interface IConfigurationValidator
{
    Task<ConfigurationValidationResult> ValidateAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken);
}
```

Validation phải trả kết quả theo từng máy và từng signal.

## 11.3. `IConfigurationApplyService`

```csharp
public interface IConfigurationApplyService
{
    Task<ConfigurationApplyResult> ApplyDraftAsync(
        string? description,
        string? userName,
        CancellationToken cancellationToken);

    Task<ConfigurationApplyResult> RollbackAsync(
        long sourceVersion,
        string? description,
        string? userName,
        CancellationToken cancellationToken);
}
```

## 11.4. `IRuntimeConfigurationProvider`

```csharp
public interface IRuntimeConfigurationProvider
{
    RuntimeConfiguration Current { get; }

    Task ReplaceAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken);
}
```

Provider phải thread-safe.

Có thể sử dụng:

- `Volatile.Read`.
- `Interlocked.Exchange`.
- Immutable records.
- Không cho collector thay đổi trực tiếp configuration object.

---

# 12. Validation rules

## 12.1. Validation chung

- Machine code bắt buộc.
- Machine code duy nhất, không phân biệt hoa thường.
- Template code duy nhất.
- Group code duy nhất.
- Máy phải tham chiếu template tồn tại.
- Protocol của connection phải khớp protocol của template.
- Máy enabled phải có connection hợp lệ.
- Signal code không được trùng trong cùng template.
- Signal required không được disable bằng override.
- Source address của signal enabled không được rỗng.
- Polling interval phải lớn hơn hoặc bằng ngưỡng cấu hình.
- Timeout phải lớn hơn 0.
- Port nằm trong khoảng 1–65535.
- Scaling factor không được bằng NaN hoặc Infinity.
- JSON option phải parse được.

## 12.2. OPC UA validation

- Endpoint phải là URI hợp lệ.
- Scheme phải là `opc.tcp`.
- Security mode phải thuộc danh sách hỗ trợ.
- Authentication mode phải thuộc danh sách hỗ trợ.
- NodeId/source address không được rỗng.
- Không yêu cầu kết nối OPC UA thật trong server-side validation bản đầu.

## 12.3. Modbus validation

- Host phải là IP hoặc hostname hợp lệ.
- Port mặc định 502 nếu không khai báo.
- Unit ID nằm trong khoảng do thư viện hỗ trợ.
- Register address không âm.
- Function code phải thuộc danh sách cho phép.
- Data type phải có số register tương ứng hợp lệ.
- Word order và byte order phải thuộc enum cho phép.

## 12.4. Validation result

```csharp
public sealed record ConfigurationValidationResult(
    bool IsValid,
    IReadOnlyList<ConfigurationValidationIssue> Issues);

public sealed record ConfigurationValidationIssue(
    string Severity,
    string Code,
    string Message,
    string? MachineCode,
    string? SignalCode,
    string? Field);
```

Severity:

```text
ERROR
WARNING
INFO
```

Chỉ `ERROR` ngăn Apply.

---

# 13. Luồng Save Draft và Apply

## 13.1. Save Draft

```text
Người dùng sửa form
        ↓
PageModel nhận InputModel
        ↓
Application Service validate field
        ↓
Lưu các bảng cấu hình
        ↓
Commit transaction
        ↓
Ghi AuditLog
        ↓
Không tác động runtime
```

## 13.2. Apply

```text
Người dùng nhấn Apply
        ↓
Build draft effective configuration
        ↓
Validate toàn hệ thống
        ├── Có ERROR
        │      ↓
        │  Không Apply
        │  Trả danh sách lỗi
        │
        └── Hợp lệ
               ↓
        Serialize snapshot JSON
               ↓
        Tính SHA-256 checksum
               ↓
        Tạo ConfigurationVersion mới
               ↓
        So sánh với active version
               ↓
        RuntimeConfigurationProvider.Replace
               ↓
        MachineRuntimeManager.ApplyConfiguration
               ↓
        ACTIVE version mới
        SUPERSEDED version cũ
```

Nếu runtime apply thất bại:

- Không xóa version cũ.
- Giữ runtime configuration cũ nếu có thể.
- Đánh dấu version mới là `FAILED`.
- Ghi lỗi chi tiết.
- Ghi AuditLog.
- Không restart toàn bộ ứng dụng.

---

# 14. Configuration diff

Cần tính thay đổi giữa active và draft.

Các loại thay đổi:

```text
UNCHANGED
ADDED
REMOVED
ENABLED
DISABLED
CONNECTION_CHANGED
SIGNALS_CHANGED
TEMPLATE_CHANGED
```

Kết quả:

```csharp
public sealed record MachineConfigurationChange(
    string MachineCode,
    MachineConfigurationChangeType ChangeType,
    EffectiveMachineConfiguration? Previous,
    EffectiveMachineConfiguration? Current);
```

Quy tắc runtime:

| Thay đổi | Hành động |
|---|---|
| UNCHANGED | Không làm gì |
| ADDED | Tạo runtime mới và start |
| REMOVED | Stop và dispose runtime |
| ENABLED | Start runtime |
| DISABLED | Stop runtime |
| CONNECTION_CHANGED | Stop, replace config, start |
| SIGNALS_CHANGED | Rebuild subscription/polling plan |
| TEMPLATE_CHANGED | Stop, replace config, start |

---

# 15. Machine Runtime Manager

Không tạo cứng 50 `BackgroundService`.

Chỉ tạo một hosted service supervisor:

```text
GatewayRuntimeHostedService
        ↓
MachineRuntimeManager
        ├── Runtime M16-01
        ├── Runtime M16-02
        ├── ...
        └── Runtime M16-50
```

Interface:

```csharp
public interface IMachineRuntimeManager
{
    Task StartAsync(CancellationToken cancellationToken);

    Task ApplyConfigurationAsync(
        RuntimeConfiguration previous,
        RuntimeConfiguration current,
        CancellationToken cancellationToken);

    IReadOnlyCollection<MachineRuntimeStatus> GetStatuses();
}
```

Machine runtime:

```csharp
public interface IMachineRuntime : IAsyncDisposable
{
    string MachineCode { get; }

    MachineRuntimeStatus Status { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task ApplyConfigurationAsync(
        EffectiveMachineConfiguration configuration,
        CancellationToken cancellationToken);
}
```

Factory:

```csharp
public interface IMachineRuntimeFactory
{
    IMachineRuntime Create(
        EffectiveMachineConfiguration configuration);
}
```

Factory chọn runtime theo protocol:

```text
OPCUA       → OpcUaMachineRuntime
MODBUS_TCP  → ModbusMachineRuntime
```

---

# 16. Quản lý kết nối 50 máy

Không kết nối đồng thời toàn bộ 50 máy khi startup.

Thiết lập đề xuất:

```text
MaxInitialConcurrentConnections = 5
MaxReconnectConcurrentConnections = 5
ConnectionStartJitterMs = 500–2000
```

Reconnect backoff:

```text
1 s
2 s
5 s
10 s
30 s
60 s
```

Phải có jitter để tránh tất cả máy reconnect cùng lúc.

Runtime status:

```text
DISABLED
STOPPED
STARTING
CONNECTING
CONNECTED
DEGRADED
RECONNECTING
FAULTED
STOPPING
```

Trạng thái runtime chỉ nằm trong memory. UI lấy qua service/API.

---

# 17. Web UI

## 17.1. Trang Dashboard

Route:

```text
/
```

Hiển thị:

- Tổng số máy.
- Số máy enabled.
- Số máy connected.
- Số máy disconnected.
- Số máy faulted.
- Active configuration version.
- Thời điểm Apply gần nhất.
- Số draft changes chưa Apply.

Trong giai đoạn host cấu hình, runtime status có thể là simulated hoặc `NOT_STARTED`.

## 17.2. Machine Groups

Route:

```text
/MachineGroups
/MachineGroups/Create
/MachineGroups/Edit/{id}
```

Chức năng:

- Danh sách.
- Thêm.
- Sửa.
- Sắp xếp.
- Không cho xóa group đang được dùng, hoặc chuyển sang soft delete.

## 17.3. Machine Templates

Route:

```text
/MachineTemplates
/MachineTemplates/Create
/MachineTemplates/Edit/{id}
/MachineTemplates/{id}/Signals
```

Chức năng:

- Khai báo protocol.
- Khai báo manufacturer/model.
- Polling interval mặc định.
- Danh sách template signals.
- Clone template.
- Validate template.

## 17.4. Machines

Route:

```text
/Machines
/Machines/Create
/Machines/Edit/{id}
/Machines/Clone/{id}
```

Danh sách cần:

- Search theo code/name.
- Filter group.
- Filter template.
- Filter protocol.
- Filter enabled.
- Filter runtime status.
- Pagination.
- Bulk enable/disable.
- Bulk assign group.
- Export selected.

Cột:

```text
Code
Name
Group
Template
Protocol
Endpoint/IP
Enabled
Runtime status
Draft status
```

## 17.5. Machine edit

Tab:

```text
General
Connection
Signal Overrides
Effective Configuration
Diagnostics
```

General:

- Code.
- Name.
- Group.
- Template.
- Enabled.
- Description.

Connection OPC UA:

- Endpoint URL.
- Security mode.
- Security policy.
- Authentication mode.
- Credential reference.
- Timeout.

Connection Modbus:

- Host.
- Port.
- Unit ID.
- Timeout.
- Retry.
- Polling interval.
- Protocol options.

Signal Overrides:

- Signal code.
- Template address.
- Override address.
- Template data type.
- Override data type.
- Enabled.
- Reset override.

Effective Configuration:

- Chỉ đọc.
- Hiển thị cấu hình sau khi merge template và override.
- Cho xem JSON.

Diagnostics bản đầu:

- Validate machine.
- Fake test connection hoặc placeholder.
- Hiển thị runtime status nếu runtime đã có.

## 17.6. Configuration page

Route:

```text
/Configuration
/Configuration/Validate
/Configuration/History
/Configuration/Version/{version}
```

Chức năng:

- Hiển thị draft changes.
- Validate toàn hệ thống.
- Preview effective JSON.
- Apply.
- Version history.
- Compare versions.
- Rollback.
- Export version.

## 17.7. Import/Export

Route:

```text
/Configuration/Import
/Configuration/Export
```

Import hỗ trợ:

- JSON full configuration.
- CSV machine list.

Trước import:

- Parse.
- Validate schema.
- Hiển thị preview.
- Hiển thị create/update/conflict.
- Chỉ commit sau khi người dùng xác nhận.

---

# 18. Input model và ViewModel

Không bind trực tiếp Entity vào Razor Page.

Ví dụ:

```csharp
public sealed class MachineEditInput
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public Guid? GroupId { get; set; }

    [Required]
    public Guid TemplateId { get; set; }

    public bool Enabled { get; set; }

    public string? Description { get; set; }
}
```

PageModel chỉ điều phối:

```csharp
public async Task<IActionResult> OnPostAsync()
{
    if (!ModelState.IsValid)
    {
        await LoadOptionsAsync();
        return Page();
    }

    await _machineService.SaveAsync(Input, User.Identity?.Name, HttpContext.RequestAborted);

    return RedirectToPage("Index");
}
```

Không đặt logic merge template, validation toàn hệ thống hoặc runtime restart trong PageModel.

---

# 19. JSON snapshot

Snapshot JSON phải độc lập với database schema.

Ví dụ rút gọn:

```json
{
  "schemaVersion": 1,
  "configVersion": 12,
  "appliedAtUtc": "2026-07-27T03:30:00Z",
  "machines": [
    {
      "machineId": "f3bd381e-f841-457a-9f5e-39c654452b63",
      "machineCode": "M16-01",
      "machineName": "Sumitomo 01",
      "protocol": "OPCUA",
      "enabled": true,
      "connection": {
        "endpointUrl": "opc.tcp://172.28.79.21:4840",
        "securityMode": "NONE",
        "securityPolicy": "None",
        "authenticationMode": "ANONYMOUS",
        "connectTimeoutMs": 3000,
        "requestTimeoutMs": 3000
      },
      "signals": [
        {
          "signalCode": "machine_state",
          "sourceAddress": "ns=2;s=Machine.State",
          "dataType": "INT16",
          "samplingIntervalMs": 1000,
          "scalingFactor": 1,
          "scalingOffset": 0,
          "required": true,
          "enabled": true,
          "valueMapping": {
            "0": "STOPPED",
            "1": "RUNNING",
            "2": "FAULT"
          }
        }
      ]
    }
  ]
}
```

Snapshot không chứa:

- Password.
- Private key.
- Access token.
- Secret dạng rõ.

---

# 20. appsettings.json

Chỉ lưu bootstrap của app:

```json
{
  "Gateway": {
    "GatewayId": "GW-M16-01",
    "DataDirectory": "data",
    "BackupDirectory": "backups",
    "ExportDirectory": "exports"
  },
  "ConnectionStrings": {
    "ConfigDatabase": "Data Source=data/config.db"
  },
  "Runtime": {
    "MaxInitialConcurrentConnections": 5,
    "MaxReconnectConcurrentConnections": 5,
    "MinimumPollingIntervalMs": 200,
    "ShutdownTimeoutSeconds": 30
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:8080"
      }
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Không lưu danh sách 50 máy trong `appsettings.json`.

---

# 21. API nội bộ

API phục vụ UI hoặc tích hợp sau này.

```text
GET  /api/v1/status
GET  /api/v1/machines
GET  /api/v1/machines/{machineCode}
GET  /api/v1/machines/{machineCode}/runtime-status
POST /api/v1/configuration/validate
POST /api/v1/configuration/apply
GET  /api/v1/configuration/active
GET  /api/v1/configuration/versions
GET  /api/v1/configuration/versions/{version}
POST /api/v1/configuration/versions/{version}/rollback
GET  /api/v1/configuration/export
POST /api/v1/configuration/import/preview
POST /api/v1/configuration/import/commit
```

Không cần public API phức tạp trong version đầu.

Các write API phải:

- Validate request.
- Ghi audit.
- Trả `ProblemDetails` khi lỗi.
- Dùng cancellation token.
- Không để exception nội bộ lộ ra client.

---

# 22. Authentication và bảo mật

Phiên bản đầu tối thiểu:

- Website chỉ bind vào mạng LAN.
- Có tài khoản Administrator.
- Có thể có Viewer.
- Administrator mới được Save/Apply/Import/Rollback.
- Viewer chỉ được xem.
- Không ghi password vào log.
- Không xuất secret trong JSON.
- Chống CSRF cho form.
- Cookie secure khi chạy HTTPS.
- Có giới hạn kích thước file import.
- Validate tên file.
- Không cho người dùng truyền đường dẫn file tùy ý.
- Audit thao tác cấu hình.
- API write phải có authorization.

Vai trò:

```text
Administrator
Viewer
```

---

# 23. Logging và diagnostics

Log cần structured:

```csharp
_logger.LogInformation(
    "Applied configuration version {Version} with {MachineCount} machines",
    version,
    machineCount);
```

Không nối chuỗi log bằng tay.

Nhóm log:

```text
Application
Configuration
Runtime
OpcUa
Modbus
Audit
ImportExport
```

Các trường thường dùng:

```text
CorrelationId
MachineCode
ConfigVersion
Protocol
Endpoint
Operation
DurationMs
ErrorCode
```

File log đề xuất:

```text
logs/app-yyyyMMdd.log
logs/runtime-yyyyMMdd.log
logs/audit-yyyyMMdd.log
```

Health endpoints:

```text
/health/live
/health/ready
```

`live` chỉ kiểm tra process đang chạy.

`ready` kiểm tra:

- Database mở được.
- Active configuration load được.
- Runtime supervisor đã khởi tạo.

Không đánh dấu toàn hệ thống not-ready chỉ vì một máy trong 50 máy bị mất kết nối.

---

# 24. Backup và recovery

## 24.1. Backup

Tạo backup:

- Trước migration.
- Trước import.
- Trước rollback.
- Theo lịch mỗi ngày nếu cần.

Thư mục:

```text
backups/
├── config-20260727-033000.db
└── config-v12.json
```

Không copy file SQLite một cách tùy tiện trong lúc transaction đang chạy.

Ưu tiên sử dụng SQLite backup API hoặc dừng write ngắn trong lúc backup.

## 24.2. Recovery

Khi startup:

1. Mở database.
2. Chạy integrity check mức phù hợp.
3. Kiểm tra migration.
4. Load active version.
5. Nếu active snapshot lỗi:
   - Thử version active trước đó.
   - Ghi critical log.
   - Không tự động ghi đè database.
6. Nếu chưa có active version:
   - Chạy với runtime rỗng.
   - Cho phép người dùng cấu hình qua web.

---

# 25. Import CSV 50 máy

CSV tối thiểu:

```csv
machineCode,name,groupCode,templateCode,host,port,unitId,endpointUrl,enabled
M16-01,Sumitomo 01,MOLDING-A,SUMITOMO-OPCUA,,,opc.tcp://172.28.79.21:4840,true
M16-02,Sumitomo 02,MOLDING-A,SUMITOMO-OPCUA,,,opc.tcp://172.28.79.22:4840,true
M16-31,JSW 01,MOLDING-B,JSW-MODBUS,172.28.79.51,502,2,,true
```

Import phải:

- Không commit ngay.
- Tạo preview.
- Báo code trùng.
- Báo template không tồn tại.
- Báo group không tồn tại.
- Cho phép lựa chọn:
  - Create missing.
  - Update existing.
  - Skip conflicts.
- Commit trong một transaction.

---

# 26. Testing

## 26.1. Unit tests

Bắt buộc:

- Merge template và override.
- Override null dùng giá trị template.
- Required signal không được disable.
- Machine code duplicate.
- Protocol mismatch.
- OPC UA endpoint validation.
- Modbus address validation.
- Configuration diff.
- Checksum ổn định.
- Rollback tạo version mới.
- Secret không xuất ra JSON.
- Apply failure giữ active config cũ.

## 26.2. Integration tests

Sử dụng temporary SQLite database.

Bắt buộc:

- Migration tạo được database.
- CRUD group/template/machine.
- Save connection.
- Save signal override.
- Apply tạo version.
- Chỉ một version active.
- Rollback tạo version mới.
- Import preview không thay đổi database.
- Import commit atomic.
- Web/API trả validation errors đúng format.

## 26.3. Runtime tests

Dùng fake machine runtime:

```csharp
public sealed class FakeMachineRuntime : IMachineRuntime
```

Test:

- Add machine tạo runtime.
- Disable machine dừng runtime.
- Connection changed restart đúng máy.
- Unchanged không restart.
- Một runtime lỗi không làm dừng runtime khác.
- Startup giới hạn concurrency.
- Reconnect có backoff.

## 26.4. Acceptance test 50 máy

Tạo seed:

```text
30 OPC UA machines
20 Modbus TCP machines
10 signals/machine
```

Kiểm tra:

- Trang machine list tải được.
- Filter và pagination hoạt động.
- Apply cấu hình thành công.
- Snapshot chứa đủ 50 máy.
- Thay đổi một máy chỉ restart một fake runtime.
- Import 50 máy atomic.
- Export rồi import lại không mất dữ liệu.
- Runtime cache không query database trong vòng đọc giả lập.

---

# 27. Performance targets

Đây là mục tiêu kỹ thuật ban đầu, không phải benchmark cứng:

- Quản lý tối thiểu 50 máy.
- Quản lý tối thiểu 5.000 signal cấu hình.
- Machine list có pagination.
- Validate 50 máy trong thời gian phù hợp cho thao tác web.
- Apply không khóa UI quá lâu.
- Không giữ transaction trong khi runtime restart.
- Runtime active config đọc trong memory.
- Mỗi request có cancellation token.
- Startup không kết nối quá 5 máy đồng thời.
- Một máy lỗi không ảnh hưởng 49 máy còn lại.

---

# 28. Deployment

## 28.1. Windows

```text
C:\Program Files\Rostek\Gateway\
└── Rostek.Gateway.exe

C:\ProgramData\Rostek\Gateway\
├── data\
│   └── config.db
├── backups\
├── exports\
├── certificates\
└── logs\
```

Chạy dưới Windows Service.

## 28.2. Linux

```text
/opt/rostek-gateway/
/etc/rostek-gateway/
/var/lib/rostek-gateway/
├── config.db
├── backups/
├── exports/
└── certificates/

/var/log/rostek-gateway/
```

Chạy dưới systemd.

## 28.3. Networking

Mặc định:

```text
http://0.0.0.0:8080
```

Truy cập:

```text
http://<gateway-ip>:8080
```

Production nên:

- Cấu hình firewall chỉ cho VLAN/LAN quản trị.
- Dùng HTTPS nếu hạ tầng cho phép.
- Không expose trực tiếp ra Internet.

---

# 29. Roadmap triển khai

## Phase 0 — Khởi tạo solution

### Công việc

- Tạo solution và project.
- Thiết lập dependency.
- Thiết lập nullable.
- Bật warnings as errors cho project chính nếu phù hợp.
- Thiết lập format/analyzer.
- Tạo basic ASP.NET Core Razor Pages app.
- Tạo health endpoint.
- Tạo cấu hình data directory.

### Hoàn thành khi

- Solution build thành công.
- Host chạy được.
- Truy cập trang chủ từ LAN.
- Health endpoint trả OK.
- Tests chạy được.

## Phase 1 — Persistence foundation

### Công việc

- Tạo entities.
- Tạo DbContext.
- Tạo EF Core configurations.
- Tạo migration đầu tiên.
- Áp dụng SQLite PRAGMA.
- Tạo repository hoặc application services.
- Tạo seed Administrator tùy phương án authentication.

### Hoàn thành khi

- Database tự tạo/migrate.
- Foreign key hoạt động.
- Unique constraints hoạt động.
- Integration test với SQLite thật pass.

## Phase 2 — Machine groups và templates

### Công việc

- CRUD machine groups.
- CRUD machine templates.
- CRUD template signals.
- Clone template.
- Validate template.

### Hoàn thành khi

- Tạo được template OPC UA.
- Tạo được template Modbus.
- Signal code không trùng.
- UI hiển thị validation rõ ràng.

## Phase 3 — Machines và connections

### Công việc

- CRUD machines.
- Machine list filter/pagination.
- Clone machine.
- OPC UA connection form.
- Modbus connection form.
- Enable/disable.
- Bulk actions cơ bản.

### Hoàn thành khi

- Tạo được 50 máy.
- Không nhập lặp toàn bộ signals.
- Protocol-specific fields hoạt động đúng.
- Không lưu entity trực tiếp từ form.

## Phase 4 — Signal overrides

### Công việc

- Trang signal overrides.
- Reset override.
- Effective configuration preview.
- Merge template và override.
- Unit tests đầy đủ.

### Hoàn thành khi

- Một máy có thể đổi NodeId/register riêng.
- Các máy khác vẫn dùng template.
- Effective configuration đúng.

## Phase 5 — Validation và versioning

### Công việc

- Configuration builder.
- Validator.
- Validation UI.
- JSON snapshot.
- SHA-256 checksum.
- Configuration versions.
- Apply.
- History.
- Rollback.

### Hoàn thành khi

- Draft không tác động runtime.
- Apply chỉ thành công khi không có ERROR.
- Mỗi Apply tăng version.
- Rollback tạo version mới.
- Version đã tạo không bị sửa.

## Phase 6 — Runtime configuration manager

### Công việc

- Runtime configuration provider.
- Configuration diff.
- Fake machine runtime.
- Machine runtime manager.
- Selective reload.
- Startup concurrency limiter.
- Runtime status API.

### Hoàn thành khi

- Thay đổi một máy chỉ restart runtime đó.
- Một runtime lỗi không làm sập host.
- Active config sử dụng từ RAM.
- Runtime không query DB trong fake read loop.

## Phase 7 — Import/export

### Công việc

- Export active JSON.
- Export draft JSON.
- Import JSON preview.
- Import JSON commit.
- Import CSV machine list.
- Backup trước import.
- Audit.

### Hoàn thành khi

- Export/import round-trip không mất dữ liệu.
- Import lỗi không thay đổi database.
- Import 50 máy trong một transaction.

## Phase 8 — OPC UA runtime

### Công việc

- OPC UA client wrapper.
- Session manager.
- Subscription.
- Monitored items.
- Reconnect.
- Quality mapping.
- Test connection.
- Read current values.

### Hoàn thành khi

- Kết nối được máy OPC UA thử nghiệm.
- Mỗi máy có runtime độc lập.
- Thay config một máy rebuild subscription máy đó.

## Phase 9 — Modbus runtime

### Công việc

- Modbus client wrapper.
- Polling plan.
- Register grouping.
- Data decoding.
- Retry.
- Reconnect.
- Test connection.
- Read current values.

### Hoàn thành khi

- Đọc được PLC thử nghiệm.
- Mapping register đúng.
- Một PLC timeout không block các máy khác.

## Phase 10 — Telemetry pipeline

Sau khi phần host cấu hình ổn định:

- Standard data model.
- `System.Threading.Channels`.
- Normalizer.
- Local outbox database riêng.
- Batch publisher.
- MES API.
- Retry/idempotency.
- Heartbeat.

---

# 30. Definition of Done

Một chức năng được coi là hoàn thành khi:

- Code build không lỗi.
- Có validation.
- Có error handling.
- Có logging phù hợp.
- Có cancellation token nếu có I/O.
- Không để business logic trong PageModel.
- Có unit test hoặc integration test tương ứng.
- Không làm lộ secret.
- Không phá version active đang chạy.
- Migration có thể áp dụng trên database mới.
- UI có thông báo thành công/thất bại rõ ràng.
- Không để một máy lỗi ảnh hưởng toàn bộ Gateway.

---

# 31. Coding conventions

- Bật nullable reference types.
- Dùng `async` cho I/O.
- Không dùng `.Result` hoặc `.Wait()`.
- Không dùng `async void`, trừ event handler bắt buộc.
- Dùng `CancellationToken`.
- Dùng `DateTimeOffset` và UTC.
- Dùng enum nội bộ, serialize thành string.
- Dùng `record` cho immutable runtime DTO.
- Dùng entity riêng cho persistence.
- Dùng input model riêng cho UI.
- Dùng structured logging.
- Không catch `Exception` rồi bỏ qua.
- Không log password/token/private key.
- Mỗi class có một trách nhiệm chính.
- Ưu tiên code đơn giản hơn pattern quá nặng.
- Không áp dụng CQRS/MediatR nếu chưa có nhu cầu rõ ràng.
- Không tạo generic repository chỉ để bọc toàn bộ EF Core.
- Không dùng AutoMapper nếu mapping ít và cần rõ ràng.
- Có thể mapping thủ công trong application services.

---

# 32. Yêu cầu dành cho Codex khi lập plan

Khi dùng tài liệu này với Codex, yêu cầu Codex:

1. Kiểm tra repository hiện tại trước khi tạo file mới.
2. Không xóa hoặc viết lại mã hiện có nếu chưa cần thiết.
3. Lập plan theo từng phase nhỏ.
4. Mỗi phase phải có:
   - File sẽ tạo/sửa.
   - Dependency cần thêm.
   - Migration cần tạo.
   - Test cần viết.
   - Acceptance criteria.
5. Không triển khai OPC UA/Modbus trước khi hoàn thành:
   - Persistence.
   - Machine/template configuration.
   - Validation.
   - Versioning.
   - Runtime configuration manager.
6. Ưu tiên host cấu hình trước.
7. Không xây telemetry database trong `config.db`.
8. Không dùng JSON thay thế cho SQLite.
9. Không để PageModel truy cập trực tiếp OPC UA hoặc Modbus.
10. Không reload toàn bộ Gateway khi một máy đổi cấu hình.
11. Sau mỗi phase:
    - Build solution.
    - Run tests.
    - Báo file đã thay đổi.
    - Báo vấn đề còn tồn tại.
12. Nếu phát hiện yêu cầu chưa rõ:
    - Chọn giải pháp đơn giản nhất phù hợp tài liệu.
    - Ghi assumption rõ trong plan.
    - Không mở rộng scope ngoài tài liệu.

---

# 33. Prompt gợi ý cho Codex Plan

Có thể sử dụng prompt sau:

```text
Đọc tài liệu ROSTEK_GATEWAY_LOCAL_CONFIG_PLAN.md và toàn bộ repository hiện tại.

Hãy lập kế hoạch triển khai chi tiết cho phiên bản đầu của Rostek Industrial Gateway, tập trung vào host web cấu hình local cho khoảng 50 máy.

Yêu cầu:
- Dùng C#, .NET 10, ASP.NET Core Razor Pages, EF Core và SQLite.
- Kiến trúc là Modular Monolith áp dụng Clean Architecture principles, Ports and Adapters và DDD-lite.
- Giữ chiều dependency: Host → Application; Infrastructure → Application/Domain; Runtime → Domain/Contracts.
- SQLite là nguồn cấu hình chính.
- Dùng template + machine + signal override.
- Có draft, validate, apply, version history và rollback.
- Runtime dùng immutable configuration trong RAM.
- Chỉ reload máy bị thay đổi.
- Chưa triển khai telemetry, OPC UA và Modbus thật cho đến khi nền tảng cấu hình hoàn thành.
- Trước mắt dùng fake machine runtime để kiểm thử Apply.
- Không đặt business logic trong Razor PageModel.
- Không lưu secret trong snapshot JSON.
- Không tạo kiến trúc phức tạp hơn mức cần thiết.

Plan phải chia thành các phase có thể thực hiện tuần tự.

Với mỗi phase, hãy nêu:
1. Mục tiêu.
2. File/project sẽ tạo hoặc sửa.
3. Entity, service và interface cần xây.
4. Migration cần tạo.
5. Razor Pages/API cần xây.
6. Unit test và integration test.
7. Acceptance criteria.
8. Rủi ro và dependency.

Không bắt đầu viết code cho đến khi hoàn thành plan và kiểm tra sự phù hợp với cấu trúc repository hiện tại.
```

---

# 34. Kết luận

Pattern kiến trúc: Modular Monolith, áp dụng Clean Architecture principles, Ports and Adapters và DDD-lite.

Phương án chốt:

```text
ASP.NET Core Gateway Host
├── Razor Pages host cấu hình
├── EF Core
├── SQLite config.db
├── Machine groups
├── Machine templates
├── Template signals
├── Machine signal overrides
├── Draft configuration
├── Validate
├── Apply
├── Version history
├── Rollback
├── Import/export JSON
├── Runtime configuration cache
└── Machine runtime supervisor
```

Các nguyên tắc quan trọng nhất:

1. **SQLite là nguồn cấu hình chính.**
2. **JSON chỉ dùng import, export, backup và snapshot.**
3. **Dùng template để quản lý khoảng 50 máy.**
4. **Draft không tác động runtime.**
5. **Chỉ Apply sau validation.**
6. **Runtime dùng cấu hình immutable trong RAM.**
7. **Chỉ reload máy bị thay đổi.**
8. **Tách config database khỏi telemetry/outbox database.**
9. **Ưu tiên hoàn thiện host cấu hình trước khi viết collector thật.**
10. **Giữ thiết kế đơn giản nhưng có đường mở rộng lên MES Gateway đầy đủ.**

---

# 35. Tài liệu tham khảo

- Thiết kế MES Gateway PoC ban đầu:  
  https://github.com/manhtran98bg/rostek-gw-cl/blob/main/MES_Gateway_PoC_Design.md

- ASP.NET Core hosted services:  
  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services

- EF Core SQLite provider:  
  https://learn.microsoft.com/en-us/ef/core/providers/sqlite/

- .NET support policy:  
  https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
