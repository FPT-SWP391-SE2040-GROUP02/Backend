-- Công cụ hỗ trợ thử điểm danh trên database DEV local (chạy bằng SSMS hoặc sqlcmd). KHÔNG dùng cho môi trường thật.
--
-- Tài khoản thử đã tạo sẵn trên máy này (chỉ để thử local):
--   Email:    checkin.tester@test.local
--   Mật khẩu: Dms-vk5Xb3bfb9f01aA!
--
-- Lưu ý: nếu chạy bằng sqlcmd phải thêm cờ -I (QUOTED_IDENTIFIER ON), vì bảng có index lọc; SSMS đã bật sẵn.
-- Cách dùng: thay <vaultId> bằng id kho (GET /api/v1/vaults/me) rồi chạy từng khối.

-- 1) Đặt kế hoạch sang ACTIVE (backend chưa có API kích hoạt kế hoạch)
UPDATE HandoverPlans SET Status = 'Active' WHERE VaultId = '<vaultId>';

-- 2) Kéo lùi hạn của kỳ đang mở để thấy trạng thái quá hạn mà không chờ 30 ngày (job nền quét mỗi 60 giây)
UPDATE CheckInPeriods SET DueAt = DATEADD(HOUR, -1, SYSDATETIMEOFFSET())
WHERE ClosedAt IS NULL AND PlanId IN (SELECT Id FROM HandoverPlans WHERE VaultId = '<vaultId>');

-- 3) Kéo lùi cả hạn chờ để thấy trạng thái AWAITING_CHECK
UPDATE CheckInPeriods SET DueAt = DATEADD(DAY, -10, SYSDATETIMEOFFSET()), GraceEndsAt = DATEADD(HOUR, -1, SYSDATETIMEOFFSET())
WHERE ClosedAt IS NULL AND PlanId IN (SELECT Id FROM HandoverPlans WHERE VaultId = '<vaultId>');

-- 4) Xem kỳ và các lời nhắc đã ghi
SELECT p.Sequence, p.StartedAt, p.DueAt, p.GraceEndsAt, p.CheckedInAt, p.OverdueAt, p.AwaitingCheckAt FROM CheckInPeriods p
JOIN HandoverPlans h ON h.Id = p.PlanId WHERE h.VaultId = '<vaultId>' ORDER BY p.Sequence;
SELECT r.Kind, r.Status, r.SentAt FROM CheckInReminders r JOIN CheckInPeriods p ON p.Id = r.PeriodId
JOIN HandoverPlans h ON h.Id = p.PlanId WHERE h.VaultId = '<vaultId>';
