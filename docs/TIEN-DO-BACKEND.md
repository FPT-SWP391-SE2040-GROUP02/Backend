# Tiến độ backend (cập nhật 16/10/2026)

Nhánh `feature/dot1-port` (từ `origin/develop` tại PR #18). Kế hoạch tổng: `docs/KE-HOACH-CAC-CHUC-NANG-CON-LAI.md`. API: `docs/api-contract.md`.
Test: 683 test tích hợp qua trên SQL Server (Docker). Chưa push.

## Đã xong trong nhánh này

### Đợt 1 — chuyển phần đã viết sẵn lên nền `develop`
| Việc | Nội dung | Migration |
|---|---|---|
| F2C Owner báo còn sống (mục 6.14) | Báo → chặn ngay hồ sơ/kế hoạch/grant; selfie; Verifier kết luận (còn sống / bác / chưa đủ căn cứ) | `AddDeathCaseObjections` |
| Đúng đắn dữ liệu (mục 9a) | Giữ chỗ dung lượng 15 phút dưới khóa kho; chống trùng CCCD bằng chỉ mục băm; email chưa cấu hình không bị coi là đã giao | `AddStorageReservationsAndCitizenIdHash` |
| Hạn bổ sung và DEATH-13 (mục 9b) | Yêu cầu bổ sung có hạn 7 ngày + job ghi quá hạn; cờ `IDENTITY_MISMATCH` | `AddSupplementDueAndOverdue` |
| Lưu và xóa dữ liệu (mục 10) | Đồng hồ 30 ngày theo lượt, tạm dừng/chạy tiếp, dọn dữ liệu, đóng kho | `AddRetention` |
| Executor (PLAN-SETUP-API.md) | Thu hồi; đổi Executor chính khi kế hoạch đang chạy; job hết hạn 7 ngày và nhắc giờ 48 | `AddExecutorReplacement` |

Đã sửa trong lúc chuyển: lỗi thật trong giữ chỗ dung lượng (hai phép đọc không đồng thời có thể bỏ sót một lần tải vừa chốt → đảo thứ tự đọc).

### Đợt 2 — hold, sự cố, Admin (mục 6.15)
- Bảng `Holds`, `Incidents`, `IncidentEvents` (migration `AddHoldsAndIncidents`).
- API `/api/v1/admin/*`: dashboard, tài khoản (đổi vai trò/trạng thái, không tự đổi mình, luôn còn một Admin), sự cố (giao/ghi chú/xem lại/đóng), hold (đặt/xem lại/gỡ), giao lại Verifier có lý do.
- Hold chặn: duyệt chứng tử, nhận lượt, đồng ý, liệt kê/tải, kích hoạt, dọn dữ liệu.
- Sự cố tự tạo: không có Verifier, quá hạn điểm danh (giao Executor; không có Executor thì "bị kẹt" do Admin phụ trách), Owner báo còn sống. Job nhắc 48 giờ, báo Admin 72 giờ.
- Nhật ký (`AuditLogs`) cho thao tác Admin và F2C.

## Còn lại (chưa làm)
- **Đợt 3 LV-02:** phiên eKYC theo mục đích, ghi đồng ý, danh tính đã gắn, mẫu khuôn mặt, thử thách camera. *Chặn bởi quyết định: dịch vụ eKYC Python có trả mẫu khuôn mặt (vector) không.*
- **Đợt 4 LV-09 (không video):** claim có trạng thái, eKYC người nhận, Executor kết luận đúng người, hạn claim 30 ngày và giữ dữ liệu 7 ngày khi xem xét. *Chặn bởi quyết định: bỏ video có được chấp nhận.*
- **Đợt 5 LV-14:** outbox thông báo, gửi lại lời mời F3, tính hạn từ lúc thông báo được giao, giới hạn tần suất.
- **Đợt 7:** quên mật khẩu và `/me/login-methods` (LV-12), sửa tài sản và phiên bản, bản ghi JSON, xuất PDF (LV-10), xác thực lại khi ân hạn.
- **Frontend** (không trong repo này).
- **SePay (LV-04):** ngoài phạm vi theo yêu cầu.
- Hold phạm vi `DESIGNATION`/`CLAIM`/`GRANT`, `DISPUTE`; `DeathCaseAssignmentJob` chưa giao lại hồ sơ `SUSPENDED`; thu hồi phiên đăng nhập khi khóa tài khoản.

## Lưu ý khi chạy
- Áp migration: `dotnet ef database update` (7 migration mới).
- Khóa chỉ mục CCCD: đặt `AssetEncryption:BlindIndexKey` (base64 32 byte) khi deploy thật, **không đổi** khi xoay KEK.
- Job nền mới và công tắc: `DeathCase:SupplementJobEnabled`, `Executor:InvitationJobEnabled`, `Retention:Enabled`, `Incident:JobEnabled` (mặc định bật).
- Lỗi conflict thường gặp khi `git pull`: `ErrorCode.cs` và bảng lỗi trong `api-contract.md` — giữ cả hai bên.
