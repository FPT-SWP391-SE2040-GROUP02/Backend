-- Tài khoản Admin có sẵn cho database DEV local (chạy bằng SSMS hoặc sqlcmd). KHÔNG dùng cho môi trường thật.
--
-- Tài khoản dùng chung của nhóm khi thử local:
--   Email:    admin@legacyvault.test
--   Mật khẩu: Admin@12345
--
-- Dùng cho khu Admin (/admin) và Công cụ thử nghiệm (/admin?view=dev-tools).
-- Không dùng tài khoản này làm Owner, Executor hay người nhận của kho đang thử (luật xung đột lợi ích).
--
-- Cách dùng (mỗi máy làm một lần, sau khi Docker SQL healthy và đã chạy migration):
--   1) Chạy Backend, đăng ký tài khoản trên đúng email và mật khẩu ở trên:
--      - Frontend: http://localhost:5173/register (họ tên tùy ý), hoặc
--      - Swagger: POST /api/v1/auth/register với body
--        { "email": "admin@legacyvault.test", "password": "Admin@12345", "confirmPassword": "Admin@12345", "fullName": "Quan tri vien" }
--      Phải đăng ký qua API để mật khẩu được băm đúng; SQL không tự tạo mật khẩu được.
--   2) Chạy khối dưới trong SSMS (database LegacyVault): nâng role Admin và đánh dấu đã xác minh email.
--   3) Đăng nhập lại bằng tài khoản này để token mang role mới.

UPDATE Users
SET Role = 'Admin', Status = 'Active', EmailVerifiedAt = COALESCE(EmailVerifiedAt, SYSDATETIMEOFFSET())
WHERE Email = 'admin@legacyvault.test';

-- Kiểm tra: phải thấy một dòng Role = Admin, Status = Active, EmailVerifiedAt có giá trị.
SELECT Email, Role, Status, EmailVerifiedAt FROM Users WHERE Email = 'admin@legacyvault.test';

-- Trả một tài khoản về người dùng thường khi cần:
-- UPDATE Users SET Role = 'User' WHERE Email = 'email-can-tra-ve';
