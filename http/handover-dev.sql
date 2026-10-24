-- Công cụ hỗ trợ thử giai đoạn nhận tài sản trên database DEV local (chạy bằng SSMS hoặc sqlcmd). KHÔNG dùng cho môi trường thật.
--
-- Backend CHƯA có endpoint "mở bàn giao" (luồng xác nhận qua đời F2B do người khác làm). Script này làm đúng việc
-- IHandoverStarter.StartAsync làm: đặt kế hoạch sang HandoverInProgress, tạo mỗi chỉ định của gói READY một lượt mời,
-- ghim snapshot tài sản, và đặt token cố định để bạn mở link thử.
--
-- Điều kiện: kho đã có ít nhất một gói READY (có tài sản + người nhận được chỉ định). Tạo bằng web hoặc file *.http khác.
-- Lưu ý: nếu chạy bằng sqlcmd phải thêm cờ -I (QUOTED_IDENTIFIER ON), vì bảng có index lọc; SSMS đã bật sẵn.
-- Cách dùng: thay <vaultId> bằng id kho (GET /api/v1/vaults/me) rồi chạy cả file. Chạy lại được: dữ liệu bàn giao cũ của kho sẽ bị xóa.
--
-- Sau khi chạy: đăng nhập bằng một tài khoản KHÁC chủ kho (đã xác minh email) rồi mở
--   http://localhost:5173/receive?token=dev-token-1   (người nhận thứ hai: dev-token-2, ...)
-- Cần API chạy ở môi trường Development (Handover:RequireIdentityVerification=false trong appsettings.Development.json).

DECLARE @vaultId uniqueidentifier = '<vaultId>';

-- 1) Dọn dữ liệu bàn giao cũ của kho này (để chạy lại được)
DELETE l FROM HandoverAccessLogs l
    JOIN HandoverGrants g ON g.Id = l.GrantId
    JOIN HandoverTurns t ON t.Id = g.TurnId
    WHERE t.VaultId = @vaultId;
DELETE g FROM HandoverGrants g JOIN HandoverTurns t ON t.Id = g.TurnId WHERE t.VaultId = @vaultId;
DELETE i FROM HandoverSnapshotItems i JOIN HandoverTurns t ON t.Id = i.TurnId WHERE t.VaultId = @vaultId;
DELETE FROM HandoverTurns WHERE VaultId = @vaultId;

-- 2) Đặt kế hoạch sang đang bàn giao
UPDATE HandoverPlans SET Status = 'HandoverInProgress', UpdatedAt = SYSDATETIMEOFFSET() WHERE VaultId = @vaultId;

-- 3) Đánh số các chỉ định của gói READY: số thứ tự N tạo ra token dev-token-N
SELECT d.Id AS DesignationId,
       ROW_NUMBER() OVER (ORDER BY d.CreatedAt, d.Id) AS N
INTO #DevDesignations
FROM Designations d
JOIN Containers c ON c.Id = d.ContainerId
WHERE c.VaultId = @vaultId AND c.Status = 'Ready';

-- 4) Mỗi chỉ định một lượt mời. Hash token = SHA-256 hex chữ thường của chuỗi token, đúng như ITokenService.
INSERT INTO HandoverTurns
    (Id, VaultId, PlanId, ContainerId, ContainerName, DesignationId, BeneficiaryId, Status,
     InvitationTokenHash, InvitedAt, ResponseDeadline, IdentityBypassed, CreatedAt)
SELECT NEWID(), c.VaultId, p.Id, c.Id, c.Name, d.Id, d.BeneficiaryId, 'Invited',
       LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', 'dev-token-' + CONVERT(varchar(10), x.N)), 2)),
       SYSDATETIMEOFFSET(), DATEADD(DAY, 5, SYSDATETIMEOFFSET()), 0, SYSDATETIMEOFFSET()
FROM #DevDesignations x
JOIN Designations d ON d.Id = x.DesignationId
JOIN Containers c ON c.Id = d.ContainerId
JOIN HandoverPlans p ON p.VaultId = c.VaultId;

-- 5) Ghim snapshot: từng tài sản trong gói
INSERT INTO HandoverSnapshotItems
    (Id, TurnId, AssetId, Name, Kind, FileName, ContentType, SizeBytes, Position)
SELECT NEWID(), t.Id, a.Id, a.Name, a.Kind, a.FileName, a.ContentType, a.SizeBytes,
       ROW_NUMBER() OVER (PARTITION BY t.Id ORDER BY ca.AddedAt, ca.Id) - 1
FROM HandoverTurns t
JOIN ContainerAssets ca ON ca.ContainerId = t.ContainerId
JOIN Assets a ON a.Id = ca.AssetId
WHERE t.VaultId = @vaultId;

-- 6) In các link để mở thử
SELECT t.ContainerName,
       '/receive?token=dev-token-' + CONVERT(varchar(10), x.N) AS Link,
       t.Status, t.ResponseDeadline
FROM #DevDesignations x
JOIN HandoverTurns t ON t.DesignationId = x.DesignationId
ORDER BY x.N;

DROP TABLE #DevDesignations;

-- Các lệnh hay dùng khi thử (bỏ comment để chạy):
-- Kéo lùi hạn quyết định của mọi lượt READY_TO_ACCEPT để thử "hết hạn quyết định" (job quét mỗi 60 giây):
--   UPDATE HandoverTurns SET DecisionDeadline = DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()) WHERE VaultId = @vaultId AND Status = 'ReadyToAccept';
-- Kéo lùi hạn quyền xem 30 ngày để thử "grant hết hạn":
--   UPDATE g SET ExpiresAt = DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()) FROM HandoverGrants g JOIN HandoverTurns t ON t.Id = g.TurnId WHERE t.VaultId = @vaultId;
-- Xem nhật ký tải (chỉ có id và kết quả, không có tên tệp):
--   SELECT l.At, l.Outcome FROM HandoverAccessLogs l JOIN HandoverGrants g ON g.Id = l.GrantId JOIN HandoverTurns t ON t.Id = g.TurnId WHERE t.VaultId = @vaultId ORDER BY l.At;
