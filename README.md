# LegacyVault Backend — bộ khung khởi tạo

Cấu trúc giữ bốn project của nhóm: **Api, Application, Domain, Infrastructure**. Khi có chức năng, luồng xử lý là **Controller → Service → Repository → Database**.

**Docker chỉ chạy SQL Server. API chạy bằng Visual Studio hoặc .NET trên máy.** Chưa có auth, tài khoản demo hoặc bảng nghiệp vụ.

## 1. Chạy SQL bằng một lệnh

Cài [Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/), mở Docker và chờ Linux Engine sẵn sàng. Lấy source về, mở terminal tại thư mục chứa `docker-compose.yml`, chạy:

```powershell
docker compose up -d --wait
```

Docker tự tải image nếu cần, khởi động SQL Server và tạo database `LegacyVault` trống nếu chưa có. Lệnh đợi tới khi kết nối được database. **Không cần tạo .env, đặt mật khẩu hoặc chạy script setup.** Lần đầu tải image/khởi động có thể mất vài phút.

```powershell
docker compose ps
```

Service `sqlserver` báo `healthy` là database sẵn sàng. Mỗi thành viên có database local riêng dù dùng cùng thông tin kết nối mẫu.

## 2. Kết nối bằng SSMS

| Trường | Giá trị |
|---|---|
| Server name | `127.0.0.1,14331` |
| Authentication | SQL Server Authentication |
| Login | `sa` |
| Password | `LegacyVault12345!` |
| Database | `LegacyVault` |
| Encrypt | Bật |
| Trust server certificate | Bật cho môi trường local |

SSMS là công cụ tùy chọn; không cần cài SQL Server riêng trên Windows. Database chưa có bảng nghiệp vụ là đúng phạm vi bộ khung.

Mật khẩu này là **mẫu development công khai**, chỉ dùng trên máy local. SQL chỉ mở cổng tại `127.0.0.1`. Server triển khai phải dùng cấu hình/bí mật riêng.

## 3. Chạy API

Chỉ chạy SQL thì cần Docker. Để chạy/code API cần thêm [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) theo `global.json` (dòng **10.0.400**, chấp nhận patch mới hơn trong cùng feature band), Visual Studio hỗ trợ SDK này với workload ASP.NET hoặc VS Code/terminal. Git dùng để clone/pull/push.

**Visual Studio:** mở `LegacyVault.sln` → chọn `LegacyVault.Api` làm Startup Project → chọn profile **http** → F5 hoặc Ctrl+F5.

**Local đã đối soát Google ngày 15/10/2026:** chọn profile **google-dev** để dùng `LegacyVault_OfficialDev` trên SQL Windows local `localhost,1433` với Windows authentication. DB này đã áp đủ migrations; profile này không dùng database Docker của `http`. Xem [Google Auth local recovery](docs/GOOGLE-AUTH-LOCAL-RECOVERY.md) để biết cấu hình, bằng chứng và rollback. Chỉ chạy một host trên cổng 5000.

**Terminal:**

```powershell
dotnet run --project src/LegacyVault.Api --launch-profile http
```

.NET tự restore/build khi cần; lần đầu cần mạng để tải NuGet. Connection string đã có trong `src/LegacyVault.Api/appsettings.Development.json`, không phải nhập lại. Chỉ chạy API bằng một cách để tránh trùng cổng 5000.

| Địa chỉ | Kết quả |
|---|---|
| `http://localhost:5000/swagger` | Giao diện tài liệu API |
| `http://localhost:5000/openapi/v1.json` | OpenAPI JSON |
| `http://localhost:5000/health/live` | 200 Healthy khi API hoạt động |
| `http://localhost:5000/health/ready` | 200 Healthy khi kết nối được database; 503 khi không kết nối được |

Swagger/OpenAPI chỉ mở ở Development. Chưa có login/register trong Swagger là bình thường. Có thể mở `http/health.http` để gửi request hạ tầng.

## 4. Các lần chạy sau và cách dừng

Mở Docker Desktop, chạy lại `docker compose up -d --wait`, rồi chạy API. Dữ liệu nằm trong named volume `legacyvault-starter_sql-data`; không phải tạo lại password/database.

Dừng API bằng Stop của IDE hoặc Ctrl+C. Dừng SQL:

```powershell
docker compose stop
```

`docker compose down` cũng giữ named volume. **Không thêm -v nếu cần giữ dữ liệu.**

## 5. Cấu hình và chuyển từ bản setup cũ

| File | Vai trò |
|---|---|
| `docker-compose.yml` | SQL, cổng 14331, password mẫu, volume, tự tạo database và health check |
| `appsettings.Development.json` | Connection string khớp Docker, origin FE localhost:5173 |
| `appsettings.json` | Logging/cấu hình chung; không chứa password server triển khai |
| `launchSettings.json` | Profile http, môi trường Development, API cổng 5000 |
| `global.json` | SDK nhóm dùng |
| `Directory.Packages.props` | Phiên bản NuGet tập trung |

Compose dùng tên project `legacyvault-starter` để không phụ thuộc tên thư mục clone. Cổng 14331 tránh SQL 14330 của project cũ. Không chạy hai checkout với cùng project/cổng cùng lúc.

Nếu từng chạy script setup bản trước:

- Máy đang dùng `LegacyVault12345!` chỉ cần chạy lại Compose; giữ nguyên volume/dữ liệu.
- Đã bỏ UserSecretsId nên API không tự nạp user-secrets cũ; không cần xóa kho secrets trên máy.
- `.env` cũ không còn cung cấp port/password. Nếu có `COMPOSE_PROJECT_NAME` khác `legacyvault-starter`, kiểm tra project/volume đang dùng trước khi đổi.
- Biến môi trường `ConnectionStrings__Default` có thể ghi đè appsettings; bỏ biến nếu đang trỏ nhầm database.
- Volume đã có password tự sinh khác sẽ **không tự đổi password trong SQL**. Dùng kết nối/password cũ để đổi login sa, hoặc giữ password cũ đồng bộ trong Compose và appsettings.Development.json. Không xóa volume khi cần giữ dữ liệu.

Cần **Docker Compose 2.30 trở lên** để hỗ trợ `post_start` tự tạo database. Docker Desktop mới đã kèm Compose; nếu báo không hỗ trợ hãy cập nhật Docker Desktop. Hook có retry chờ SQL; không tạo bảng nghiệp vụ và không chạy migration.

## 6. Cấu trúc code

```text
src/
├── LegacyVault.Api/
│   ├── Controllers/          # Nhận request, gọi service, trả HTTP
│   ├── Config/               # HTTP, CORS, OpenAPI
│   ├── Exceptions/           # Xử lý lỗi HTTP tập trung
│   ├── Security/             # Cookie, HTTP context security
│   └── Program.cs            # DI, middleware, endpoint
├── LegacyVault.Application/
│   ├── Dtos/Requests/        # Dữ liệu đầu vào
│   ├── Dtos/Responses/       # Dữ liệu trả ra
│   ├── Services/             # Interface/class nghiệp vụ
│   ├── Repositories/         # Interface đọc/ghi dữ liệu
│   ├── Notifications/        # Interface thông báo, gửi email
│   ├── Exceptions/           # Lỗi nghiệp vụ
│   └── ApplicationConfig.cs
├── LegacyVault.Domain/
│   ├── Entities/             # Đối tượng nghiệp vụ
│   └── Enums/                # Trạng thái, giá trị nghiệp vụ
└── LegacyVault.Infrastructure/
    ├── Data/AppDbContext.cs
    ├── Data/Configurations/  # EF mapping theo entity
    ├── Data/Migrations/      # Lịch sử schema
    ├── Repositories/         # Truy vấn EF Core
    ├── Security/             # Hash mật khẩu Argon2, JWT token
    ├── Notifications/        # Triển khai email dev, app URL provider
    ├── Health/               # Kiểm tra SQL
    └── InfrastructureConfig.cs
```

Thư mục trống dùng `.gitkeep` để Git lưu được. Khi có code thật có thể bỏ file đó.

Ví dụ thêm Vault (các file minh họa chưa có):

| Phần việc | Nơi đặt |
|---|---|
| VaultsController.cs | Api/Controllers |
| IVaultService.cs, VaultService.cs | Application/Services |
| CreateVaultRequest.cs, VaultResponse.cs | Application/Dtos/Requests và Responses |
| IVaultRepository.cs | Application/Repositories |
| Vault.cs | Domain/Entities |
| VaultRepository.cs | Infrastructure/Repositories |
| VaultConfiguration.cs | Infrastructure/Data/Configurations |
| DbSet<Vault> | AppDbContext |
| Đăng ký service/repository | ApplicationConfig / InfrastructureConfig |

Controller điều phối HTTP; service kiểm tra nghiệp vụ/quyền; repository truy vấn dữ liệu. Application không tham chiếu EF Core; Domain không phụ thuộc API/database. Có thể chia feature lớn cho nhiều người theo [CONTRIBUTING.md](CONTRIBUTING.md). Xem thêm [kiến trúc](docs/architecture.md).

## 7. Migration khi bắt đầu có entity

Docker chỉ tạo database trống. **Không tạo InitialCreate khi chưa có entity.** Người thêm entity/mapping tạo migration:

```powershell
dotnet tool restore
dotnet ef migrations add AddVaults --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api --output-dir Data/Migrations -- --environment Development
dotnet ef database update --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api -- --environment Development
```

Commit migration/model snapshot cùng code. Người khác pull rồi chạy `database update`, không tạo lại cùng migration. Tham số Development giúp EF đọc connection string có sẵn. API không tự migrate khi khởi động; server triển khai cần quy trình migration riêng.

## 8. Kiểm thử

```powershell
dotnet test LegacyVault.sln
```

Test HTTP kiểm tra liveness khi SQL lỗi, readiness 503, OpenAPI và lỗi 404. Test SQL thật chỉ chạy khi có `LEGACYVAULT_TEST_CONNECTION`; chưa đặt thì hiển thị Skipped.

Chỉ khi muốn chạy thêm test SQL thật, sau khi Docker healthy:

```powershell
$devSettings = Get-Content src/LegacyVault.Api/appsettings.Development.json -Raw | ConvertFrom-Json
$env:LEGACYVAULT_TEST_CONNECTION = $devSettings.ConnectionStrings.Default
try {
    dotnet test LegacyVault.sln
} finally {
    Remove-Item Env:LEGACYVAULT_TEST_CONNECTION -ErrorAction SilentlyContinue
}
```

Biến trên chỉ phục vụ kiểm thử tự động, không bắt buộc để chạy SQL/API. GitHub Actions chạy cùng Compose và cả hai test; workflow mới cần được push để xác nhận trên GitHub.

## 9. Lỗi thường gặp

| Lỗi | Cách xử lý |
|---|---|
| Không kết nối Docker Engine | Mở Docker Desktop, đợi Linux Engine sẵn sàng |
| Compose không hiểu post_start | Cập nhật Docker Desktop/Compose 2.30 trở lên |
| SQL không healthy | Xem `docker compose logs --tail 50 sqlserver`; kiểm tra RAM và password volume cũ |
| Cổng 14331 bị chiếm | Dừng SQL khác chiếm cổng hoặc đổi cổng Compose và connection string API cùng nhau |
| SSMS lỗi chứng chỉ | Bật Trust server certificate cho kết nối dev |
| API thiếu SDK | Cài SDK theo global.json, dùng IDE hỗ trợ SDK |
| API thiếu connection string | Chọn profile http/Development |
| Health ready trả 503 | Chờ Docker healthy, kiểm tra password/database/cấu hình ghi đè |
| Cổng API 5000 bị chiếm | Dừng API cũ hoặc đổi profile và URL test |
| Swagger chưa có login/register | Bộ khung chưa triển khai auth |

## 10. Git và trạng thái chức năng

Repo chính thức: [FPT-SWP391-SE2040-GROUP02/Backend](https://github.com/FPT-SWP391-SE2040-GROUP02/Backend).

Đọc [Hướng dẫn Git](docs/HUONG-DAN-GIT.md): tạo nhánh từ develop → code → kiểm tra → commit → push → PR về develop. Nhóm chốt bản ổn định mới mở PR về main. Không chạy lại git init sau khi clone.

Đã có: bốn project, DI, SQL Docker tự tạo database, EF Core, CORS, ProblemDetails, Swagger, health check và cấu hình CI.

Đã có thêm: đăng ký/xác minh email, đăng nhập email/mật khẩu (JWT + refresh cookie), đăng nhập Google; tạo/xem/sửa kho; tạo/xem tài sản mã hóa (tệp, chữ); gói: tạo/xem/đổi tên/lưu trữ, thêm/gỡ tài sản trong gói; điểm danh định kỳ (deadman switch) với job nền chuyển trạng thái và nhắc hạn — xem docs/api-contract.md mục 6. Email xác minh tài khoản và lời nhắc điểm danh gửi thật qua Gmail (MailKit) khi có cấu hình `Email:Smtp` — xem mục 11; chưa cấu hình thì chỉ ghi log. Lưu trữ gói READY cuối cùng (kế hoạch về nháp) cũng dừng lịch điểm danh.

Nội dung tài sản được **mã hóa envelope** trước khi ghi đĩa: AES-256-GCM, mỗi phiên bản một DEK riêng, DEK được bọc bằng KEK trong mục `AssetEncryption` của cấu hình (không nằm trong DB). `appsettings.Development.json` có sẵn KEK mẫu `dev-local` — **chỉ dùng local**, giống mật khẩu SQL mẫu. Server thật đặt KEK qua biến môi trường `AssetEncryption__ActiveKeyId` và `AssetEncryption__Keys__<id>`; thiếu/sai KEK thì API không khởi động. Đổi KEK: thêm khóa mới, chuyển `ActiveKeyId`, **giữ khóa cũ** để mở các phiên bản đã lưu. Mất KEK = mất toàn bộ nội dung đã lưu.

Database dev đã chạy các migration cũ (`InitialSchema`, `AddAssetVersionEncryption`, đã bỏ khi gộp với develop) thì xóa database rồi tạo lại theo migration hiện tại:

```powershell
dotnet ef database drop --force --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api -- --environment Development
dotnet ef database update --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api -- --environment Development
```

Nhớ xóa luôn thư mục `src/LegacyVault.Api/App_Data/asset-content` vì nội dung trong đó không còn bản ghi tương ứng.

Đoạn mô tả môi trường tối thiểu ở trên là checkpoint cũ, không phản ánh toàn bộ API hiện tại. Executor invite/resend/respond và kích hoạt kế hoạch nối điểm danh đã triển khai ngày 15/10/2026; xem [contract và giới hạn nghiệm thu](docs/PLAN-SETUP-API.md) cùng [API contract](docs/api-contract.md). Chạy được môi trường không đồng nghĩa hoàn thành SRS.

## 11. Gửi email thật bằng Gmail (MailKit SMTP)

Dịch vụ email dùng chung cho email xác minh tài khoản và lời nhắc điểm danh. **Chưa cấu hình thì hệ thống chỉ ghi log, không gửi email** (dev và test vẫn chạy bình thường).

1. Tài khoản Gmail dùng để gửi cần bật **Xác minh 2 bước**, rồi tạo **Mật khẩu ứng dụng** tại <https://myaccount.google.com/apppasswords> (16 ký tự). Gmail không cho dùng mật khẩu đăng nhập thường.
2. Đặt bí mật **bằng user-secrets** (chỉ nằm trên máy bạn, không vào Git):

```powershell
dotnet user-secrets set "Email:Smtp:UserName" "ten-ban@gmail.com" --project src/LegacyVault.Api
dotnet user-secrets set "Email:Smtp:Password" "mat-khau-ung-dung-16-ky-tu" --project src/LegacyVault.Api
```

3. Khởi động lại API. Thử bằng cách đăng ký một tài khoản bằng email thật: email xác minh sẽ tới hộp thư.

Server thật đặt biến môi trường `Email__Smtp__UserName` và `Email__Smtp__Password` (không viết vào `appsettings.json`, không commit). Các giá trị không bí mật (`Host`, `Port`, `SecureSocket`, `FromName`, `FromAddress`) nằm ở mục `Email:Smtp` của `appsettings.json`. Gmail chỉ cho gửi từ chính tài khoản đã đăng nhập (hoặc bí danh đã xác minh), nên để trống `FromAddress`. Cấu hình sai (cổng, `SecureSocket`, địa chỉ gửi) thì API không khởi động. Gmail giới hạn khoảng 500 thư/ngày cho tài khoản thường.


## 12. Lưu nội dung tài sản trên Cloudflare R2

Mặc định (`AssetStorage:Provider` = `Local`) nội dung đã mã hóa nằm trên đĩa server, ở thư mục `AssetStorage:RootPath`. Muốn lưu lên bucket R2 thì đổi `Provider` thành `R2`. R2 chỉ giữ dữ liệu đã mã hóa; KEK vẫn nằm ở server, nên không có KEK thì không đọc được nội dung (SEC-02, SEC-11).

1. Tạo bucket **private** (không bật truy cập công khai) và một API token R2 có quyền đọc/ghi object trên bucket đó.
2. Đặt cấu hình **bằng user-secrets**, không viết vào `appsettings*.json`:

```powershell
dotnet user-secrets set "AssetStorage:Provider" "R2" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:AccountId" "<account-id>" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:BucketName" "<ten-bucket>" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:AccessKeyId" "<access-key-id>" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:SecretAccessKey" "<secret-access-key>" --project src/LegacyVault.Api
```

3. Khởi động lại API. Thiếu một trong bốn giá trị `R2` thì API không khởi động.

Server thật đặt biến môi trường `AssetStorage__Provider`, `AssetStorage__R2__AccountId`... Đổi `Provider` không chuyển dữ liệu cũ: tài sản đã lưu trên đĩa sẽ không đọc được khi chạy với R2, và ngược lại. Test tự động luôn ép `Provider` = `Local`, nên `dotnet test` không ghi vào bucket thật dù máy có user-secrets R2.
