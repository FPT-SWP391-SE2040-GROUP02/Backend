# Kiến trúc bộ khung

## Quyết định

Giữ cách tổ chức giống BE hiện tại: bốn project, controller/service/repository, một backend và một SQL Server. Không CQRS/MediatR, microservices hoặc GenericRepository khi chưa có nhu cầu cụ thể.

```text
Api             -> Application, Infrastructure
Application     -> Domain
Infrastructure  -> Application, Domain
Domain          -> không có project dependency
```

Luồng gọi khi thêm feature:

```text
HTTP -> Controller -> Service -> IRepository -> Repository -> AppDbContext -> SQL Server
```

Application định nghĩa interface repository; Infrastructure triển khai. Api là nơi lắp ráp dependency injection. Cách tổ chức này không có nghĩa mọi lời gọi đều phải tạo thêm một abstraction: chỉ thêm file thực sự cần cho feature.

## Trách nhiệm

| Tầng | Làm gì | Tránh |
|---|---|---|
| Api | Route, model binding, HTTP status, response DTO, auth middleware khi có auth | Viết truy vấn EF hoặc luật nghiệp vụ trong controller |
| Application | Use case, kiểm tra quyền/trạng thái, transaction boundary được thiết kế cho use case | Tham chiếu HttpContext hoặc EF Core trực tiếp |
| Domain | Entity, enum và quy tắc domain khi cần | Phụ thuộc API/SQL/provider |
| Infrastructure | EF mapping, repository, provider bên ngoài | Quyết định quyền bàn giao chỉ dựa vào kết quả provider |

## Code có sẵn

- ApiConfig/CorsConfig: cấu hình HTTP, OpenAPI và origin FE. Chưa bật credentials vì chưa có auth cookie.
- GlobalExceptionHandler: lỗi 500 có traceId; không trả exception/message nội bộ ra client.
- AppDbContext: chưa có DbSet/bảng. InfrastructureConfig kiểm tra connection string lúc khởi động.
- DatabaseHealthCheck: kết nối database thật; readiness 503 khi thất bại, liveness độc lập database.
- Program: Development có Swagger; môi trường khác có HSTS/HTTPS redirection. Đây là cấu hình nền, chưa phải cấu hình triển khai production hoàn chỉnh.

## Môi trường

SQL Server Developer chạy Docker, chỉ bind localhost cổng 14331, dùng named volume và image digest đã chốt. Chạy `docker compose up -d --wait` để SQL khởi động và Compose tự tạo database LegacyVault nếu chưa có. Hook `post_start` có retry chờ SQL; health check xác nhận kết nối được đúng database LegacyVault. Không cài SQL Server service trên Windows.

API chạy trực tiếp bằng SDK, đọc connection string mẫu từ appsettings.Development.json. Cấu hình local dùng password công khai `LegacyVault12345!`; không cần `.env`, user-secrets hoặc script setup. UserSecretsId cũ được bỏ khỏi csproj để secret từ lần setup trước không ghi đè cấu hình mẫu. Không xóa kho user-secrets cũ trên máy.

Compose cần phiên bản 2.30 trở lên để hỗ trợ post_start. Giữ nguyên tên Compose project `legacyvault-starter` và volume `sql-data` để lần cập nhật này không tạo nhầm database khác. Khi đổi password cho volume đã có dữ liệu phải cập nhật login SQL trước; sửa cấu hình không tự đổi password bên trong database. Môi trường triển khai phải cấp connection string/bí mật riêng; không dùng password development này.

## Các quyết định nghiệp vụ phải giữ theo SRS 3.14.0

- Một Owner có tối đa một Vault. Tạo Vault, thanh toán và kích hoạt kế hoạch là ba kết quả khác nhau.
- Owner/Executor/Beneficiary cần xét quan hệ theo đối tượng; một role toàn cục không đủ biểu diễn mọi quyền.
- eKYC không tự cấp grant; nhận tài sản cần đủ các bước xét, video và đồng ý nhận.
- Kế hoạch, quyền dịch vụ, chứng tử, claim và grant có trạng thái riêng.
- Mã hóa tài sản, audit, snapshot và chống xử lý lặp cần được thiết kế với use case thực, không tạo stub trả thành công.

Bộ khung không cố định schema nghiệp vụ. Chưa tạo BaseEntity chung hoặc InitialCreate để tránh khóa thiết kế dữ liệu quá sớm.
