-- Công cụ hỗ trợ thử luồng chứng tử (Executor nộp hồ sơ, Verifier xét/duyệt) trên database DEV local (chạy bằng SSMS hoặc sqlcmd).
-- KHÔNG dùng cho môi trường thật.
--
-- Backend CHƯA có API mời/chấp nhận Executor, nên script này dựng sẵn dữ liệu thử:
--   - dọn hồ sơ chứng tử cũ VÀ dữ liệu bàn giao cũ của kho (để chạy lại được sau khi đã duyệt);
--   - đặt kế hoạch của kho sang ACTIVE (chưa có API kích hoạt kế hoạch);
--   - tạo một dòng Executor đã chấp nhận (Accepted) cho tài khoản Executor;
--   - đổi một tài khoản đã đăng ký thành Verifier (để hệ thống phân công khi Executor gửi hồ sơ).
--
-- THỨ TỰ THỬ DUYỆT (http/death-cases.http rồi http/verifier-death-cases.http):
--   1. Owner chuẩn bị TRƯỚC khi Executor gửi hồ sơ: kho trả phí, có ít nhất một gói READY (tài sản + người nhận được chỉ định).
--      Gửi hồ sơ lần đầu khóa cấu hình kho (VAULT_NOT_EDITABLE / PLAN_LOCKED) nên không thêm gói sau đó được.
--      Duyệt mà không có gói READY nào → 409 HANDOVER_NOT_ALLOWED (NOTHING_TO_HAND_OVER).
--   2. Chạy script này, Executor mở/khai/gửi hồ sơ.
--   3. Hồ sơ được giao cho MỘT Verifier theo xoay vòng. Có nhiều tài khoản Verifier thì xem bảng DeathCaseAssignments
--      (dòng EndedAt = NULL, lệnh bên dưới) để biết ĐÚNG tài khoản được giao; tài khoản khác nhận 404.
--      Vai trò Verifier được service đọc từ DB nên đổi Role xong không cần đăng nhập lại để gọi API; FE vẫn đọc claim trong
--      token nên cần đăng nhập lại nếu FE phân trang theo vai trò.
--   4. Dùng lệnh "Cho phép duyệt" ở cuối file để coi như Owner đã nhận thư và hết 72 giờ phản đối.
--
-- Lưu ý: nếu chạy bằng sqlcmd phải thêm cờ -I (QUOTED_IDENTIFIER ON), vì bảng có index lọc; SSMS đã bật sẵn.
-- Cách dùng: thay 3 email bên dưới bằng tài khoản ĐÃ ĐĂNG KÝ và xác minh email (xem http/auth.http), rồi chạy cả file.
--   - Owner: tài khoản đã tạo kho (POST /api/v1/vaults).
--   - Executor: tài khoản KHÁC Owner. Đăng nhập tài khoản này rồi dùng http/death-cases.http.
--   - Verifier: tài khoản KHÁC Owner và Executor; đăng nhập tài khoản được giao rồi dùng http/verifier-death-cases.http.
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
-- Dữ liệu bàn giao do duyệt tạo ra (lượt, snapshot, quyền tải) cũng phải xóa, nếu không kế hoạch vẫn còn lượt cũ.
DELETE l FROM HandoverAccessLogs l
    JOIN HandoverGrants g ON g.Id = l.GrantId
    JOIN HandoverTurns t ON t.Id = g.TurnId
    WHERE t.VaultId = @vaultId;
DELETE g FROM HandoverGrants g JOIN HandoverTurns t ON t.Id = g.TurnId WHERE t.VaultId = @vaultId;
DELETE i FROM HandoverSnapshotItems i JOIN HandoverTurns t ON t.Id = i.TurnId WHERE t.VaultId = @vaultId;
DELETE FROM HandoverTurns WHERE VaultId = @vaultId;
UPDATE DeathCases SET PreviousCaseId = NULL WHERE VaultId = @vaultId;
DELETE d FROM DeathCaseDecisions d JOIN DeathCases c ON c.Id = d.CaseId WHERE c.VaultId = @vaultId;
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
-- Xem quyết định của Verifier (yêu cầu bổ sung, từ chối, duyệt) và xem ai đang được giao (EndedAt = NULL):
--   SELECT d.Kind, d.VersionNo, d.Reason, d.RequestedParts, d.DecidedAt FROM DeathCaseDecisions d JOIN DeathCases c ON c.Id = d.CaseId WHERE c.VaultId = @vaultId ORDER BY d.DecidedAt;
--   SELECT u.Email, a.AssignedAt, a.DueAt, a.EndedAt FROM DeathCaseAssignments a JOIN DeathCases c ON c.Id = a.CaseId JOIN Users u ON u.Id = a.VerifierUserId WHERE c.VaultId = @vaultId ORDER BY a.AssignedAt;
-- Cho phép duyệt: coi như Owner đã nhận thư cách đây 80 giờ (DEATH-08, DEATH-09). Chạy SAU khi Executor gửi hồ sơ.
-- Không đổi DeathCases.UpdatedAt nên etag không đổi. Hai lệnh UPDATE ở mục "Kéo lùi mốc giao thư" phía trên là bản từng phần của lệnh này.
--   UPDATE n SET Status = 'Delivered', DeliveredAt = DATEADD(HOUR, -80, SYSDATETIMEOFFSET()), AttemptCount = CASE WHEN n.AttemptCount = 0 THEN 1 ELSE n.AttemptCount END FROM DeathCaseNotices n JOIN DeathCases c ON c.Id = n.CaseId WHERE c.VaultId = @vaultId AND n.VersionNo = c.NotifiedVersionNo;
--   UPDATE DeathCases SET ObjectionStartsAt = DATEADD(HOUR, -80, SYSDATETIMEOFFSET()), ObjectionEndsAt = DATEADD(HOUR, -8, SYSDATETIMEOFFSET()) WHERE VaultId = @vaultId AND NotifiedVersionNo IS NOT NULL;
-- Verifier yêu cầu bổ sung / từ chối / duyệt đã có API (http/verifier-death-cases.http); không cần giả lập trạng thái bằng SQL.
-- Sau khi TỪ CHỐI, kế hoạch về trạng thái trước khi gửi nhưng job điểm danh có thể tính lại trong 60 giây (NFR-03).
-- Sau khi DUYỆT, kế hoạch sang HANDOVER_IN_PROGRESS và có lượt bàn giao; muốn thử lại từ đầu thì chạy lại cả file này (dọn lượt cũ).
--   SELECT p.Status FROM HandoverPlans p WHERE p.VaultId = @vaultId;
--   SELECT t.Status, t.ContainerName, t.InvitedAt FROM HandoverTurns t WHERE t.VaultId = @vaultId;
-- Giả lập Owner có điểm danh sau ngày mất để thử cờ DEATH-10 (làm TRƯỚC khi Executor gửi hồ sơ; kho cần có ít nhất một kỳ điểm danh):
--   UPDATE CheckInPeriods SET CheckedInAt = SYSDATETIMEOFFSET(), ClosedAt = SYSDATETIMEOFFSET() WHERE ClosedAt IS NULL AND PlanId IN (SELECT Id FROM HandoverPlans WHERE VaultId = @vaultId);
