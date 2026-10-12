# LegacyVault Backend — bộ khung khởi tạo

Repository dành cho nhóm bắt đầu phát triển BE theo SRS 3.14.0. Cấu trúc giữ bốn project giống BE hiện tại: **Api → Application → Domain**, Infrastructure triển khai database/repository. Luồng request khi có chức năng là **Controller → Service → Repository → Database**.

**Docker chỉ chạy SQL Server. API chạy trên máy bằng Visual Studio hoặc `dotnet run`.** Bộ khung chưa có đăng nhập, tài khoản demo, bảng nghiệp vụ, Vault, thanh toán hoặc eKYC.

## 1. Công cụ cần cài trên mỗi máy

- Windows và PowerShell 5.1 trở lên (khuyến nghị PowerShell 7).
- [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) theo `global.json`: dòng **10.0.400**, cho phép patch mới hơn trong cùng feature band.
- [Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/), chạy **Linux containers**. Hoàn tất WSL 2 theo hướng dẫn của Docker nếu máy yêu cầu.
- [Git](https://git-scm.com/downloads).
- Visual Studio có hỗ trợ SDK .NET 10 của project và workload ASP.NET; hoặc VS Code + terminal. Nếu IDE cũ không hỗ trợ SDK thì dùng terminal hoặc cập nhật IDE.
- SSMS là công cụ tùy chọn để xem database, **không cần cài SQL Server riêng trên Windows**.

Kiểm tra:

```powershell
dotnet --version
docker version
git --version
```

`docker version` cần có cả Client và Server. Nếu báo không có Docker Engine, mở Docker Desktop và chờ sẵn sàng.

## 2. Setup lần đầu sau khi clone

Clone repo của nhóm bằng URL GitHub thực tế, sau đó mở PowerShell tại thư mục chứa `LegacyVault.sln`:

```powershell
.\scripts\setup-local.ps1
```

Nếu PowerShell chặn chạy script, chỉ bỏ chặn trong tiến trình này, không đổi policy toàn máy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-local.ps1
```

Script thực hiện:

1. Kiểm tra SDK và Docker Engine.
2. Tạo `.env` từ `.env.example` nếu chưa có.
3. Tạo mật khẩu dev ngẫu nhiên khi `SQL_PASSWORD` còn là `CHANGE_ME`.
4. Restore NuGet và local tool `dotnet-ef`.
5. Khởi động SQL Server, chờ health check thành công.
6. Tạo database `LegacyVault` trống nếu chưa có; giữ nguyên database đã tồn tại.
7. Lưu connection string vào user-secrets của API, không in mật khẩu ra terminal.
8. Build solution.

Script **không cài tự động .NET/Docker/Git**, không xóa database và không chạy migration nghiệp vụ. Các công cụ ở mục 1 cần được cài trước. Mỗi thành viên có database và password local riêng; không dùng chung database qua mạng.

### Muốn tự chọn password trước lần setup đầu

```powershell
Copy-Item .env.example .env
```

Mở `.env`, sửa `SQL_PASSWORD`, sau đó chạy script. Ví dụ password development dễ nhớ đã thống nhất cho máy setup ban đầu là `LegacyVault12345!`; các máy mới có thể dùng mật khẩu được script sinh tự động. Không dùng mật khẩu ví dụ này cho server triển khai.

SQL Server không chấp nhận `12345` lúc khởi tạo: cần 8–128 ký tự và ít nhất ba nhóm chữ hoa/chữ thường/số/ký hiệu. Script hỗ trợ các ký tự nêu trong `.env.example` để tránh lỗi escape khi nối cấu hình.

## 3. Chạy API

### Bằng Visual Studio

1. Mở `LegacyVault.sln`.
2. Chuột phải `LegacyVault.Api` → **Set as Startup Project**.
3. Chọn profile **http**.
4. Bấm F5 hoặc Ctrl+F5. Swagger mở ở `http://localhost:5000/swagger`.

### Bằng terminal

```powershell
dotnet run --project src/LegacyVault.Api --launch-profile http
```

Chỉ chọn một cách để tránh chạy hai API trùng cổng 5000. Swagger/OpenAPI chỉ mở ở Development. Swagger báo chưa có operation nghiệp vụ là bình thường: bộ khung chỉ có health endpoint hạ tầng.

| Địa chỉ | Kết quả |
|---|---|
| `http://localhost:5000/swagger` | Giao diện tài liệu API |
| `http://localhost:5000/openapi/v1.json` | OpenAPI JSON |
| `http://localhost:5000/health/live` | 200 `Healthy` khi API đang hoạt động |
| `http://localhost:5000/health/ready` | 200 `Healthy` khi kết nối được SQL; 503 `Unhealthy` khi SQL không sẵn sàng |

Có thể mở `http/health.http` bằng Visual Studio hoặc REST Client để gửi request. Test nhanh bằng PowerShell:

```powershell
Invoke-RestMethod http://localhost:5000/health/live
Invoke-RestMethod http://localhost:5000/health/ready
```

## 4. Mỗi ngày làm việc

1. Mở Docker Desktop.
2. Pull code mới.
3. Tại thư mục repo, chạy:

```powershell
docker compose up -d --wait
dotnet run --project src/LegacyVault.Api --launch-profile http
```

Không cần setup lại mỗi ngày. Khi pull package mới chạy `dotnet restore`; khi nhóm thay phiên bản tool chạy `dotnet tool restore`. Khi sửa `.env`, đọc mục 6 rồi chạy lại setup để cập nhật cấu hình API.

Dừng API bằng nút Stop của IDE hoặc Ctrl+C. Dừng database:

```powershell
docker compose stop
```

Volume `sql-data` giữ dữ liệu qua lần dừng/chạy. `docker compose down` cũng giữ named volume. **Không dùng `docker compose down -v` trừ khi chủ động muốn xóa toàn bộ database local.**

## 5. Kết nối bằng SSMS

| Trường | Giá trị mặc định |
|---|---|
| Server name | `127.0.0.1,14331` |
| Authentication | SQL Server Authentication |
| Login | `sa` |
| Password | Giá trị `SQL_PASSWORD` trong `.env` trên máy bạn |
| Database | `LegacyVault` |
| Encrypt | Bật |
| Trust server certificate | Bật cho môi trường development này |

Máy setup ban đầu dùng password `LegacyVault12345!`. Database ở bộ khung chưa có bảng nghiệp vụ là đúng thiết kế.

Cổng **14331** dành cho bộ khung mới, tránh cổng 14330 của project BE cũ. Compose chỉ bind SQL vào `127.0.0.1`; database này dành cho máy local. Container/volume nằm trong Compose project `legacyvault-starter`; không dùng volume của project cũ. Không đặt `container_name` cố định để giảm xung đột giữa checkout.

## 6. Hiểu `.env`, appsettings và user-secrets

| Nơi cấu hình | Nội dung | Commit Git? |
|---|---|---|
| `.env.example` | Tên biến, giá trị mẫu và hướng dẫn | Có |
| `.env` | Password, cổng và tên database local cho Docker | Không |
| `appsettings.json` | Logging, cấu hình chung không bí mật | Có |
| `appsettings.Development.json` | Origin FE dev, mặc định `http://localhost:5173` | Có |
| User-secrets | `ConnectionStrings:Default` cho API chạy local | Không; lưu ngoài repo |

ASP.NET Core **không tự đọc `.env`** theo mặc định. Script đọc file này rồi ghi connection string vào user-secrets. `UserSecretsId` của starter khác bản auth cũ để không ghi đè cấu hình của project cũ. User-secrets giúp tách bí mật khỏi Git, không phải kho bí mật được mã hóa cho production.

Mỗi dòng `.env` theo dạng đơn giản `KEY=value`, không có dấu nháy hoặc comment cuối dòng. Không đặt cùng tên biến `SQL_PORT`, `SQL_DATABASE`, `SQL_PASSWORD`, `COMPOSE_PROJECT_NAME` trong shell: script sẽ báo lỗi để tránh Docker và API dùng cấu hình khác nhau. `ConnectionStrings__Default` trong biến môi trường cũng có thể ghi đè user-secrets; bỏ biến này nếu đang trỏ nhầm database.

Nếu đổi cổng trong `.env`, chạy lại setup. Nếu đổi password khi volume SQL **đã tồn tại**, chỉ sửa `.env` không đổi password bên trong SQL Server. Hãy đổi password của login trong SQL trước rồi đồng bộ `.env` và chạy lại setup; nếu chỉ nhập nhầm `.env`, khôi phục password đúng. Không xóa volume để sửa lỗi kết nối khi cần giữ dữ liệu.

## 7. Cấu trúc code và cách thêm chức năng

```text
src/
├── LegacyVault.Api/
│   ├── Controllers/       # Nhận HTTP request, gọi service, trả status/response
│   ├── Config/            # Cấu hình HTTP, CORS, OpenAPI
│   ├── Exceptions/        # Xử lý lỗi HTTP tập trung
│   └── Program.cs         # Lắp ráp ứng dụng và middleware
├── LegacyVault.Application/
│   ├── Dtos/Requests/     # Dữ liệu đầu vào của use case
│   ├── Dtos/Responses/    # Dữ liệu trả ra
│   ├── Services/          # Interface và class xử lý nghiệp vụ
│   ├── Repositories/      # Interface để service yêu cầu đọc/ghi dữ liệu
│   ├── Exceptions/        # Lỗi nghiệp vụ khi bắt đầu triển khai
│   └── ApplicationConfig.cs
├── LegacyVault.Domain/
│   ├── Entities/          # Đối tượng nghiệp vụ
│   └── Enums/             # Trạng thái/giá trị nghiệp vụ
└── LegacyVault.Infrastructure/
    ├── Data/AppDbContext.cs
    ├── Data/Configurations/  # EF mapping theo entity
    ├── Data/Migrations/      # Lịch sử thay đổi schema
    ├── Repositories/         # Triển khai truy vấn EF Core
    ├── Health/               # Kiểm tra kết nối database
    └── InfrastructureConfig.cs
```

Thư mục trống dùng `.gitkeep` để Git lưu được. Khi có code thật có thể bỏ `.gitkeep`.

Ví dụ khi nhóm bắt đầu feature Vault (các file sau **chưa được tạo**):

| Việc | File / vị trí |
|---|---|
| Entity | `Domain/Entities/Vault.cs` |
| EF mapping | `Infrastructure/Data/Configurations/VaultConfiguration.cs` |
| DbSet | Bổ sung vào `Infrastructure/Data/AppDbContext.cs` |
| Hợp đồng truy vấn | `Application/Repositories/IVaultRepository.cs` |
| Truy vấn EF | `Infrastructure/Repositories/VaultRepository.cs` |
| Request / response | `Application/Dtos/Requests/CreateVaultRequest.cs`, `Dtos/Responses/VaultResponse.cs` |
| Nghiệp vụ | `Application/Services/IVaultService.cs`, `VaultService.cs` |
| HTTP | `Api/Controllers/VaultsController.cs` |
| Đăng ký service | `ApplicationConfig.cs`: `AddScoped<IVaultService, VaultService>()` |
| Đăng ký repository | `InfrastructureConfig.cs`: `AddScoped<IVaultRepository, VaultRepository>()` |

Controller chỉ điều phối HTTP; service kiểm tra nghiệp vụ/quyền; repository thực hiện truy vấn. Không dùng DbContext trực tiếp trong controller. Application không tham chiếu EF Core; Domain không tham chiếu project/framework hạ tầng. Hướng dẫn thêm trong `docs/architecture.md`.

## 8. Migration khi bắt đầu có entity

**Không chạy `InitialCreate` cho bộ khung trống.** Người thêm entity/mapping tạo migration cùng feature:

```powershell
dotnet tool restore
dotnet ef migrations add AddVaults --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api --output-dir Data/Migrations -- --environment Development
dotnet ef database update --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api -- --environment Development
```

Commit migration và model snapshot cùng entity/mapping. Các thành viên khác pull code rồi chạy `database update`, **không tạo lại cùng migration**. Môi trường Development được truyền rõ để EF đọc user-secrets khi khởi tạo API. Script tạo database không thay thế EF migration. Khi triển khai server cần quy trình migration riêng; bộ khung không tự migrate mỗi lần API khởi động.

## 9. Kiểm thử

```powershell
dotnet test LegacyVault.sln
```

Test HTTP kiểm tra liveness vẫn chạy khi SQL lỗi, readiness trả 503, OpenAPI truy cập được và route không tồn tại trả ProblemDetails. Test SQL thật chỉ chạy khi có `LEGACYVAULT_TEST_CONNECTION`; mặc định hiện **Skipped**, không giả vờ là đã pass.

Để chạy test SQL thật trên máy đã setup, lấy connection string từ user-secrets mà không in ra terminal:

```powershell
$secretJson = (dotnet user-secrets list --project src/LegacyVault.Api --json | Where-Object { $_ -notmatch '^//' }) -join "`n"
$localSecrets = $secretJson | ConvertFrom-Json
$env:LEGACYVAULT_TEST_CONNECTION = $localSecrets.'ConnectionStrings:Default'
try {
    dotnet test LegacyVault.sln
} finally {
    Remove-Item Env:LEGACYVAULT_TEST_CONNECTION -ErrorAction SilentlyContinue
    $localSecrets = $null
    $secretJson = $null
}
```

GitHub Actions khởi động SQL riêng và chạy cả hai test. Workflow chưa thể được xác nhận trên GitHub cho đến khi repository được push; chạy local thành công không thay thế kết quả CI remote. Không build Docker image API vì API chưa được container hóa.

## 10. Lỗi thường gặp

| Lỗi | Cách xử lý |
|---|---|
| Không tìm thấy SDK phù hợp | Cài SDK theo `global.json`; IDE phải hỗ trợ SDK này |
| Không kết nối Docker Engine | Mở Docker Desktop, đợi Linux Engine chạy |
| SQL không healthy | Xem `docker compose logs --tail 50 sqlserver`; kiểm tra password, RAM và volume cũ |
| Cổng SQL bị chiếm | Đổi `SQL_PORT` trong `.env`, chạy lại setup |
| API báo thiếu connection string | Chạy setup, chọn profile `http`/Development |
| `/health/ready` trả 503 | Kiểm tra SQL đang chạy, tên DB, password và user-secrets có cùng `.env` |
| API báo cổng 5000 đang dùng | Dừng bản API khác hoặc đổi cổng profile và cập nhật URL test |
| Swagger không có login/register | Đúng: bộ khung chưa có nghiệp vụ auth |
| `database update` báo thiếu cấu hình | Thêm `-- --environment Development` như mục 8 |

## 11. Đưa lên GitHub và bắt đầu làm việc

Bộ khung đã chuẩn bị `.gitignore`, PR template và `.github/workflows/ci.yml`. Chưa có remote GitHub tự động. Tạo repository trống trong tài khoản/organization của nhóm, sau đó thực hiện trong thư mục starter:

```powershell
git init -b main
git add .
git status
git diff --cached --stat
git commit -m "chore: bootstrap backend environment"
# Thay URL dưới đây bằng repository thật của nhóm:
git remote add origin https://github.com/YOUR-ORG/legacyvault-be.git
git push -u origin main
```

Kiểm tra `.env`, `bin`, `obj` không xuất hiện trong phần staged trước commit. Không copy thư mục `.git`, `.vs` hoặc database của project cũ sang đây. Sau khi có repo, mỗi chức năng dùng nhánh `feature/...` và PR theo `CONTRIBUTING.md`.

## 12. Trạng thái chức năng

Đã có: cấu trúc bốn project, DI, SQL Server/EF Core, CORS, ProblemDetails, Swagger, health check, script setup và cấu hình CI.

Chưa có: Auth, tài khoản demo, bảng nghiệp vụ, Vault/Assets, mã hóa tài sản, Google, eKYC, thanh toán, điểm danh, chứng tử, video/bàn giao, Admin/audit/jobs. Xem `docs/backlog.md` để bắt đầu các feature theo SRS 3.14.0. Health endpoint chạy được chỉ xác nhận môi trường, không có nghĩa đã hoàn thành SRS.
