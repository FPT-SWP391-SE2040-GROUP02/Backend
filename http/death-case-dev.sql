-- Công cụ hỗ trợ thử luồng Executor nộp hồ sơ chứng tử trên database DEV local (chạy bằng SSMS hoặc sqlcmd). KHÔNG dùng cho môi trường thật.
--
-- Backend CHƯA có API mời/chấp nhận Executor, nên script này dựng sẵn dữ liệu thử:
--   - đặt kế hoạch của kho sang ACTIVE (chưa có API kích hoạt kế hoạch);
--   - tạo một dòng Executor đã chấp nhận (Accepted) cho tài khoản Executor;
--   - đổi một tài khoản đã đăng ký thành Verifier (để hệ thống phân công khi Executor gửi hồ sơ).
--
-- Lưu ý: nếu chạy bằng sqlcmd phải thêm cờ -I (QUOTED_IDENTIFIER ON), vì bảng có index lọc; SSMS đã bật sẵn.
-- Cách dùng: thay 3 email bên dưới bằng tài khoản ĐÃ ĐĂNG KÝ và xác minh email (xem http/auth.http), rồi chạy cả file.
--   - Owner: tài khoản đã tạo kho (POST /api/v1/vaults).
--   - Executor: tài khoản KHÁC Owner. Đăng nhập tài khoản này rồi dùng http/death-cases.http.
--   - Verifier: tài khoản KHÁC Owner và Executor (hiện chưa có API duyệt; chỉ dùng để thử phân công).
-- Chạy lại được: hồ sơ chứng tử cũ của kho sẽ bị xóa (nội dung trang giấy trong App_Data/asset-content KHÔNG bị xóa,
-- hãy xóa thư mục đó thủ công nếu muốn dọn).

DECLARE @ownerEmail nvarchar(256) = N'<owner@email>';
DECLARE @executorEmail nvarchar(256) = N'<executor@email>';
DECLARE @verifierEmail nvarchar(256) = N'<verifier@email>';

DECLARE @ownerId uniqueidentifier = (SELECT Id FROM Users WHERE Email = LOWER(@ownerEmail));
DECLARE @executorId uniqueidentifier = (SELECT Id FROM Users WHERE Email = LOWER(@executorEmail));
DECLARE @verifierId uniqueidentifier = (SELECT Id FROM Users WHERE Email = LOWER(@verifierEmail));
DECLARE @vaultId uniqueidentifier = (SELECT Id FROM Vaults WHERE OwnerId = @ownerId);

IF @ownerId IS NULL OR @executorId IS NULL OR @verifierId IS NULL
    THROW 50001, 'Thieu tai khoan: kiem tra lai 3 email o dau file (phai da dang ky).', 1;
IF @vaultId IS NULL
    THROW 50002, 'Owner chua co kho: goi POST /api/v1/vaults truoc.', 1;
IF @ownerId = @executorId OR @ownerId = @verifierId OR @executorId = @verifierId
    THROW 50003, 'Owner, Executor va Verifier phai la 3 tai khoan khac nhau.', 1;

-- 1) Dọn hồ sơ chứng tử cũ của kho này (để chạy lại được)
UPDATE DeathCases SET PreviousCaseId = NULL WHERE VaultId = @vaultId;
DELETE f FROM DeathCaseFiles f JOIN DeathCaseVersions v ON v.Id = f.VersionId JOIN DeathCases c ON c.Id = v.CaseId WHERE c.VaultId = @vaultId;
DELETE fl FROM DeathCaseFlags fl JOIN DeathCases c ON c.Id = fl.CaseId WHERE c.VaultId = @vaultId;
DELETE n FROM DeathCaseNotices n JOIN DeathCases c ON c.Id = n.CaseId WHERE c.VaultId = @vaultId;
DELETE a FROM DeathCaseAssignments a JOIN DeathCases c ON c.Id = a.CaseId WHERE c.VaultId = @vaultId;
DELETE v FROM DeathCaseVersions v JOIN DeathCases c ON c.Id = v.CaseId WHERE c.VaultId = @vaultId;
DELETE FROM DeathCases WHERE VaultId = @vaultId;

-- 2) Đặt kế hoạch sang ACTIVE (DEATH-05: chỉ mở hồ sơ khi kế hoạch đang chạy)
UPDATE HandoverPlans SET Status = 'Active', UpdatedAt = SYSDATETIMEOFFSET() WHERE VaultId = @vaultId;

-- 3) Executor đã chấp nhận (Primary). Hash token là giá trị ngẫu nhiên, không dùng để mời thật.
DELETE FROM Executors WHERE VaultId = @vaultId AND UserId = @executorId;
INSERT INTO Executors
    (Id, VaultId, UserId, InviteeEmail, Role, Status, InvitationTokenHash, IsBeneficiary, InvitedAt, ExpiresAt, AcceptedAt)
VALUES
    (NEWID(), @vaultId, @executorId, LOWER(@executorEmail), 'Primary', 'Accepted',
     LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(36), NEWID())), 2)),
     0, DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, 6, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET());

-- 4) Verifier (ASSIGN-01: hồ sơ được giao cho Verifier Active, không phải Owner/Executor của kho)
UPDATE Users SET Role = 'Verifier' WHERE Id = @verifierId;

SELECT @vaultId AS VaultId, @ownerEmail AS Owner, @executorEmail AS Executor, @verifierEmail AS Verifier;

-- Các lệnh hay dùng khi thử (bỏ comment để chạy). Thay @vaultId bằng id kho ở kết quả trên:
-- Xem hồ sơ, thông báo Owner, cờ và phân công:
--   SELECT c.Status, c.StatusBeforeCase, c.CurrentVersionNo, c.ReviewVersionNo, c.NotifiedVersionNo, c.ObjectionStartsAt, c.ObjectionEndsAt FROM DeathCases c WHERE c.VaultId = @vaultId;
--   SELECT n.VersionNo, n.Status, n.AttemptCount, n.DeliveredAt FROM DeathCaseNotices n JOIN DeathCases c ON c.Id = n.CaseId WHERE c.VaultId = @vaultId;
--   SELECT f.Kind, f.Detail, f.ReviewedAt FROM DeathCaseFlags f JOIN DeathCases c ON c.Id = f.CaseId WHERE c.VaultId = @vaultId;
--   SELECT a.VerifierUserId, a.AssignedAt, a.DueAt FROM DeathCaseAssignments a JOIN DeathCases c ON c.Id = a.CaseId WHERE c.VaultId = @vaultId AND a.EndedAt IS NULL;
-- Job gửi thông báo Owner quét mỗi 60 giây (DeathCase:NoticePollSeconds); chưa cấu hình email thì chỉ ghi log "[THÔNG BÁO HỒ SƠ CHỨNG TỬ]"
-- và coi là đã giao, nên DeliveredAt được đặt và đồng hồ 72 giờ bắt đầu chạy.
-- Kéo lùi mốc giao thư để thử "hết 72 giờ phản đối" (DEATH-08):
--   UPDATE n SET DeliveredAt = DATEADD(HOUR, -73, SYSDATETIMEOFFSET()) FROM DeathCaseNotices n JOIN DeathCases c ON c.Id = n.CaseId WHERE c.VaultId = @vaultId AND n.Status = 'Delivered';
--   UPDATE DeathCases SET ObjectionStartsAt = DATEADD(HOUR, -73, SYSDATETIMEOFFSET()), ObjectionEndsAt = DATEADD(HOUR, -1, SYSDATETIMEOFFSET()) WHERE VaultId = @vaultId AND ObjectionStartsAt IS NOT NULL;
-- Giả lập "Verifier yêu cầu bổ sung" để thử tạo phiên bản mới (nhánh Verifier chưa có API):
--   UPDATE DeathCases SET Status = 'SupplementRequired' WHERE VaultId = @vaultId;
-- Giả lập Owner có điểm danh sau ngày mất để thử cờ DEATH-10 (làm TRƯỚC khi Executor gửi hồ sơ; kho cần có ít nhất một kỳ điểm danh):
--   UPDATE CheckInPeriods SET CheckedInAt = SYSDATETIMEOFFSET(), ClosedAt = SYSDATETIMEOFFSET() WHERE ClosedAt IS NULL AND PlanId IN (SELECT Id FROM HandoverPlans WHERE VaultId = @vaultId);
