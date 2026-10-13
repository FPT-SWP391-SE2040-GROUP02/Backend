# Quy ước làm việc nhóm

Xem [Hướng dẫn Git và quy trình code chức năng](docs/HUONG-DAN-GIT.md) để thực hiện từng bước.

1. Cập nhật `develop` trước khi bắt đầu; tạo nhánh ngắn `feature/auth`, `feature/vaults` hoặc `fix/...` từ `develop`. Mở PR về `develop`; khi nhóm chốt bản ổn định mới mở PR từ `develop` vào `main`.
2. Phân chia công việc linh hoạt theo độ lớn của feature: chức năng nhỏ có thể giao một người làm trọn; chức năng lớn có thể chia nhiều người theo phần nghiệp vụ hoặc theo tầng controller/service/repository/entity. Trước khi làm, thống nhất phạm vi từng người, DTO/interface dùng chung và một người phụ trách tích hợp để bảo đảm các phần hoạt động cùng nhau.
3. Dùng SDK/package/tool đã chốt; cập nhật version tập trung, không sửa lệch từng csproj.
4. Route nghiệp vụ dùng `/api/v1/...`; request/response dùng DTO, không trả trực tiếp entity EF.
5. Service đăng ký tại ApplicationConfig; repository/tích hợp tại InfrastructureConfig.
6. CancellationToken truyền từ controller đến thao tác async trong service/repository.
7. Quyền đối tượng, trạng thái và quy tắc SRS phải kiểm tra ở server, không dựa vào FE ẩn nút.
8. Thay đổi schema phải kèm migration/model snapshot; thống nhất thứ tự merge migration khi cùng sửa model. Không sửa migration đã được nhóm áp dụng; thêm migration mới.
9. Không commit secrets thật, `.env`, connection string của server triển khai, token, giấy tờ hoặc dữ liệu thật. Mật khẩu SQL development công khai trong Compose và appsettings.Development.json là cấu hình mẫu dùng riêng trên máy local của nhóm. Không log password/payload tài sản.
10. Trước PR chạy build và kiểm thử liên quan. Cập nhật OpenAPI/README khi thay contract hoặc cấu hình. Không viết test rỗng chỉ để làm CI xanh.
11. Một người khác review PR trước merge. Cấu hình branch protection trên GitHub sau khi tạo repo để yêu cầu review/CI.

## Khi nhiều người cùng làm một chức năng

- Chia thành các đầu việc cụ thể, có người phụ trách và kết quả bàn giao rõ ràng. Có thể chia theo nghiệp vụ (đăng ký, đăng nhập, khôi phục mật khẩu) hoặc theo tầng khi phù hợp tiến độ và năng lực của nhóm.
- Thống nhất trước route, request/response DTO, chữ ký method của service/repository, lỗi trả về và thay đổi dữ liệu. Khi cần đổi phần dùng chung, báo cho người đang phụ thuộc vào phần đó.
- Mỗi đầu việc dùng nhánh riêng, ví dụ `feature/auth-data`, `feature/auth-service`, `feature/auth-api`; ghi rõ PR nào phụ thuộc PR nào và thứ tự merge. Các nhánh thông thường tạo từ `develop`; nếu cần code của PR chưa merge, thống nhất cách phối hợp trước, không tự copy code qua lại.
- Hạn chế cùng sửa một file; phối hợp thứ tự thay đổi `AppDbContext`, migration, cấu hình DI và DTO/interface chung. Phân công một người tạo migration cho cùng một thay đổi schema để tránh trùng lặp.
- Người phụ trách tích hợp phối hợp với các thành viên để kiểm tra toàn bộ luồng sau khi ghép code. Từng phần build được chưa đủ để kết luận chức năng hoàn thành; không merge phần chưa build được hoặc endpoint giả trả thành công chỉ để chờ phần còn lại.

Bộ khung chưa chọn contract auth. Khi đưa phần auth cũ vào phải thống nhất FE/cookie/CSRF, xử lý thu hồi phiên, xác minh email và giới hạn phiên theo SRS; không coi enum role hoặc JWT đơn thuần là đã có đủ phân quyền.
