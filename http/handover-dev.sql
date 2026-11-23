-- Công cụ hỗ trợ thử giai đoạn nhận tài sản trên database DEV local (chạy bằng SSMS hoặc sqlcmd). KHÔNG dùng cho môi trường thật.
--
-- Backend CHƯA có endpoint "mở bàn giao" (luồng xác nhận qua đời F2B do người khác làm). Script này làm đúng việc
-- IHandoverStarter.StartAsync làm: đặt kế hoạch sang HandoverInProgress, tạo mỗi người nhận có tài sản được gán một lượt mời,
-- ghim snapshot tài sản của người đó, và đặt token cố định để bạn mở link thử.
--
-- Điều kiện: kho đã gán ít nhất một tài sản cho một người nhận. Tạo bằng web hoặc file *.http khác.
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

-- 3) Đánh số các người nhận có tài sản được gán: số thứ tự N tạo ra token dev-token-N
SELECT a.BeneficiaryId,
       ROW_NUMBER() OVER (ORDER BY MIN(a.CreatedAt), a.BeneficiaryId) AS N
INTO #DevRecipients
FROM AssetAssignments a
JOIN Assets s ON s.Id = a.AssetId
WHERE s.VaultId = @vaultId
GROUP BY a.BeneficiaryId;

-- 4) Mỗi người nhận một lượt mời. Hash token = SHA-256 hex chữ thường của chuỗi token, đúng như ITokenService.
INSERT INTO HandoverTurns
    (Id, VaultId, PlanId, ContainerName, BeneficiaryId, Status,
     InvitationTokenHash, InvitedAt, ResponseDeadline, IdentityBypassed, CreatedAt)
SELECT NEWID(), @vaultId, p.Id, N'Biên bản bàn giao', x.BeneficiaryId, 'Invited',
       LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', 'dev-token-' + CONVERT(varchar(10), x.N)), 2)),
       SYSDATETIMEOFFSET(), DATEADD(DAY, 5, SYSDATETIMEOFFSET()), 0, SYSDATETIMEOFFSET()
FROM #DevRecipients x
JOIN HandoverPlans p ON p.VaultId = @vaultId;

-- 5) Ghim snapshot: từng tài sản được gán cho người nhận của lượt
INSERT INTO HandoverSnapshotItems
    (Id, TurnId, AssetId, Name, Kind, FileName, ContentType, SizeBytes, Position)
SELECT NEWID(), t.Id, s.Id, s.Name, s.Kind, s.FileName, s.ContentType, s.SizeBytes,
       ROW_NUMBER() OVER (PARTITION BY t.Id ORDER BY a.CreatedAt, a.Id) - 1
FROM HandoverTurns t
JOIN AssetAssignments a ON a.BeneficiaryId = t.BeneficiaryId
JOIN Assets s ON s.Id = a.AssetId AND s.VaultId = @vaultId
WHERE t.VaultId = @vaultId;

-- 6) In các link để mở thử
SELECT t.ContainerName,
       '/receive?token=dev-token-' + CONVERT(varchar(10), x.N) AS Link,
       t.Status, t.ResponseDeadline
FROM #DevRecipients x
JOIN HandoverTurns t ON t.BeneficiaryId = x.BeneficiaryId AND t.VaultId = @vaultId
ORDER BY x.N;

DROP TABLE #DevRecipients;

-- Các lệnh hay dùng khi thử (bỏ comment để chạy):
-- Kéo lùi hạn quyết định của mọi lượt READY_TO_ACCEPT để thử "hết hạn quyết định" (job quét mỗi 60 giây):
--   UPDATE HandoverTurns SET DecisionDeadline = DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()) WHERE VaultId = @vaultId AND Status = 'ReadyToAccept';
-- Kéo lùi hạn quyền xem 30 ngày để thử "grant hết hạn":
--   UPDATE g SET ExpiresAt = DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()) FROM HandoverGrants g JOIN HandoverTurns t ON t.Id = g.TurnId WHERE t.VaultId = @vaultId;
-- Xem nhật ký tải (chỉ có id và kết quả, không có tên tệp):
--   SELECT l.At, l.Outcome FROM HandoverAccessLogs l JOIN HandoverGrants g ON g.Id = l.GrantId JOIN HandoverTurns t ON t.Id = g.TurnId WHERE t.VaultId = @vaultId ORDER BY l.At;
