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

SQL Server Developer chạy Docker, chỉ bind localhost, dùng named volume và image digest đã chốt. API chạy trực tiếp bằng SDK. .env phục vụ Compose; API đọc user-secrets do script setup ghi. Không cài SQL Server service trên Windows.

UserSecretsId dành riêng cho starter. Các clone khác của chính starter trên cùng tài khoản Windows dùng chung ID; nếu muốn chạy nhiều clone với database khác nhau, cần ID/config riêng để không ghi đè connection string nhau.

## Các quyết định nghiệp vụ phải giữ theo SRS 3.14.0

- Một Owner có tối đa một Vault. Tạo Vault, thanh toán và kích hoạt kế hoạch là ba kết quả khác nhau.
- Owner/Executor/Beneficiary cần xét quan hệ theo đối tượng; một role toàn cục không đủ biểu diễn mọi quyền.
- eKYC không tự cấp grant; nhận tài sản cần đủ các bước xét, video và đồng ý nhận.
- Kế hoạch, quyền dịch vụ, chứng tử, claim và grant có trạng thái riêng.
- Mã hóa tài sản, audit, snapshot và chống xử lý lặp cần được thiết kế với use case thực, không tạo stub trả thành công.

Bộ khung không cố định schema nghiệp vụ. Chưa tạo BaseEntity chung hoặc InitialCreate để tránh khóa thiết kế dữ liệu quá sớm.
