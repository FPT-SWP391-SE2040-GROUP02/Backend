/*
  Seed 3 Người thực thi hệ thống MẪU (hồ sơ giả) có EMAIL NHẬN ĐƯỢC THƯ THẬT: dạng legacyvault8386+<tên>@gmail.com, thư về hộp thư hệ thống cho danh bạ GET /api/v1/system-executors.
  Chạy được nhiều lần (upsert theo Email). Chỉ dùng cho DB dev, cần đã áp migration
  20261114140829_AddSystemExecutorAndEncryptedKeyShard.

  Mật khẩu: sao chép PasswordHash từ một tài khoản Admin bất kỳ nên đăng nhập bằng cùng mật khẩu đó
  (không ghi mật khẩu hay hash vào script). Không có Admin thì script dừng.

  Chạy:
    sqlcmd -S localhost,1433 -d LegacyVault_OfficialDev -E -C -I -f 65001 -b -i Backend/docs/seed-deliverable-executors.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @hash NVARCHAR(256) = (SELECT TOP 1 PasswordHash FROM Users WHERE Role = N'Admin' ORDER BY CreatedAt);
IF @hash IS NULL
BEGIN
    THROW 50001, N'Không có tài khoản Admin để lấy PasswordHash mẫu.', 1;
END;

DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

DECLARE @seed TABLE (
    Email NVARCHAR(256), FullName NVARCHAR(200), Phone NVARCHAR(32),
    Title NVARCHAR(200), Org NVARCHAR(200), Card NVARCHAR(64), Cert NVARCHAR(64));

INSERT INTO @seed VALUES
 (N'legacyvault8386+executor1@gmail.com', N'Luật sư Trần Đình Trọng', N'0912345001',
  N'Luật sư thi hành di chúc & Giám sát di sản', N'Đoàn Luật sư Thành phố Hà Nội (mẫu)', N'LS-HN-DEMO-001', N'CCHN-LS-DEMO-8801'),
 (N'legacyvault8386+executor2@gmail.com', N'Luật sư Nguyễn Thu Hà', N'0912345002',
  N'Luật sư độc lập thi hành di chúc', N'Đoàn Luật sư Thành phố Hồ Chí Minh (mẫu)', N'LS-HCM-DEMO-002', N'CCHN-LS-DEMO-8802'),
 (N'legacyvault8386+executor3@gmail.com', N'Công chứng viên Lê Minh Khang', N'0912345003',
  N'Chuyên viên giám sát di sản độc lập', N'Văn phòng Công chứng Đà Nẵng (mẫu)', N'LS-DN-DEMO-003', N'CCHN-CC-DEMO-8803');

BEGIN TRANSACTION;

UPDATE u SET
    Role = N'Executor', Status = N'Active', EmailVerifiedAt = COALESCE(u.EmailVerifiedAt, @now),
    FullName = s.FullName, ExecutorRoleTitle = s.Title, LegalOrganization = s.Org,
    LawyerCardNumber = s.Card, PracticeCertificateNumber = s.Cert,
    LegalVerifiedAt = COALESCE(u.LegalVerifiedAt, @now), UpdatedAt = @now
FROM Users u JOIN @seed s ON s.Email = u.Email;

INSERT INTO Users (Id, Email, EmailVerifiedAt, PasswordHash, FullName, Phone, Role, Status, CreatedAt,
                   ExecutorRoleTitle, LegalOrganization, LawyerCardNumber, PracticeCertificateNumber, LegalVerifiedAt)
SELECT NEWID(), s.Email, @now, @hash, s.FullName, s.Phone, N'Executor', N'Active', @now,
       s.Title, s.Org, s.Card, s.Cert, @now
FROM @seed s
WHERE NOT EXISTS (SELECT 1 FROM Users u WHERE u.Email = s.Email);

COMMIT;

SELECT Email, FullName, Role, Status, ExecutorRoleTitle, LawyerCardNumber FROM Users WHERE Role = N'Executor' ORDER BY FullName;
