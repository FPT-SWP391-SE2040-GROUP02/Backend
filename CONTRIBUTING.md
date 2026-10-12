# Quy ước làm việc nhóm

1. Pull `main` trước khi bắt đầu; tạo nhánh ngắn `feature/auth`, `feature/vaults` hoặc `fix/...`.
2. Mỗi người triển khai trọn feature qua controller/service/repository/entity, không chia cố định một người chỉ code một tầng.
3. Dùng SDK/package/tool đã chốt; cập nhật version tập trung, không sửa lệch từng csproj.
4. Route nghiệp vụ dùng `/api/v1/...`; request/response dùng DTO, không trả trực tiếp entity EF.
5. Service đăng ký tại ApplicationConfig; repository/tích hợp tại InfrastructureConfig.
6. CancellationToken truyền từ controller đến thao tác async trong service/repository.
7. Quyền đối tượng, trạng thái và quy tắc SRS phải kiểm tra ở server, không dựa vào FE ẩn nút.
8. Thay đổi schema phải kèm migration/model snapshot; thống nhất thứ tự merge migration khi cùng sửa model. Không sửa migration đã được nhóm áp dụng; thêm migration mới.
9. Không commit `.env`, connection string có password, token, giấy tờ hoặc dữ liệu thật. Không log password/payload tài sản.
10. Trước PR chạy build và kiểm thử liên quan. Cập nhật OpenAPI/README khi thay contract hoặc cấu hình. Không viết test rỗng chỉ để làm CI xanh.
11. Một người khác review PR trước merge. Cấu hình branch protection trên GitHub sau khi tạo repo để yêu cầu review/CI.

Bộ khung chưa chọn contract auth. Khi đưa phần auth cũ vào phải thống nhất FE/cookie/CSRF, xử lý thu hồi phiên, xác minh email và giới hạn phiên theo SRS; không coi enum role hoặc JWT đơn thuần là đã có đủ phân quyền.
