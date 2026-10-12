# Backlog theo SRS 3.14.0

Căn cứ: LegacyVault-SRS-v3.14.0.md, bản chính thức ngày 08/10/2026. Nhóm đặt bản SRS được phép chia sẻ trong tài liệu repo hoặc liên kết nguồn tài liệu chung khi tạo GitHub. Bảng dưới đây là nhóm việc, không thay tiêu chí nghiệm thu của SRS.

| Nhóm việc | Nội dung | Trạng thái |
|---|---|---|
| Môi trường | Bốn project, SQL Docker, DI, Swagger, health, setup script, cấu hình CI | Có trong starter; CI remote cần chạy sau push |
| Auth | Đăng ký, xác minh email, login/logout, session, refresh, Google/link, giới hạn phiên | Chưa làm |
| Quyền | Role staff, quan hệ Owner/Executor/Beneficiary, guard theo object và phân công | Chưa làm |
| F1 | Vault, gói bàn giao, chỉ định, quota, mời Executor, kích hoạt kế hoạch | Chưa làm |
| Assets | Phiên bản nội dung, storage private, envelope encryption, upload/download guard | Chưa làm |
| eKYC | Consent, phiên/purpose/evidence, callback nội bộ, tích hợp prototype và đo chất lượng | Chưa làm |
| P | SePay, đơn/giao dịch, chống webhook lặp, quyền dịch vụ/ân hạn | Chưa làm |
| F2 | Điểm danh, chứng tử, phân công, snapshot, Owner báo còn sống | Chưa làm |
| F3 | Lời mời, claim, lịch/video, xác minh, đồng ý/từ chối, grant và hạn tải | Chưa làm |
| F4 | Admin, quản lý sự cố, hỗ trợ kỹ thuật, metadata vận hành | Chưa làm |
| Dùng chung | Audit, thông báo, jobs bền vững, retention/cleanup, backup/restore | Chưa làm |

PR đầu tiên sau starter có thể là auth. Dùng code auth hiện tại làm tài liệu tham khảo; cần kiểm tra và bổ sung yêu cầu SRS, không copy seeder mật khẩu/log hoặc coi auth đã nghiệm thu.

Mỗi feature cần nêu mã yêu cầu SRS, endpoint, dữ liệu/migration, quyền, nhánh lỗi, xử lý lặp/xung đột và bằng chứng kiểm thử phù hợp. Không đưa API giả trả đã thanh toán/đã xác minh vào bộ khung.
