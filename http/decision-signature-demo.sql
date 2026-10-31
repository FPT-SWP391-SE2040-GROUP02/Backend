-- DEMO chữ ký số quyết định của Verifier (DEATH-14) trên database DEV local (SSMS hoặc sqlcmd -I).
-- CẢNH BÁO: CHỈ dùng trên DB dev để demo phát hiện sửa dữ liệu. KHÔNG chạy trên môi trường thật.
--
-- Cách dùng (cùng http/verifier-death-cases.http):
--   1. Verifier duyệt hoặc từ chối hồ sơ (bước 6a/6b) rồi gọi bước 7 (decision-signature) → status = VALID.
--   2. Điền @CaseId, để @Action = 'TAMPER', chạy cả file (sửa lý do của quyết định cuối)
--      → gọi lại bước 7 hoặc GET chi tiết (decisionSignatureStatus) → INVALID.
--   3. Đổi @Action = 'RESTORE', chạy lại cả file (trả lại lý do cũ) → gọi lại → VALID.
-- Script chỉ thêm/bỏ một đuôi đánh dấu vào lý do nên trả lại đúng giá trị cũ; không đụng chữ ký hay khóa.

DECLARE @CaseId uniqueidentifier = '00000000-0000-0000-0000-000000000000';
DECLARE @Action varchar(16) = 'TAMPER'; -- 'TAMPER' = sửa lý do, 'RESTORE' = trả lại
DECLARE @Marker nvarchar(32) = N' [DA SUA DEMO]';

IF @Action = 'TAMPER'
BEGIN
    -- Giả lập kẻ sửa DB đổi lý do sau khi ký (chỉ quyết định cuối; chạy lặp không thêm đuôi lần nữa).
    UPDATE DeathCaseDecisions
    SET Reason = Reason + @Marker
    WHERE CaseId = @CaseId
      AND Kind IN ('Approved', 'Rejected')
      AND RIGHT(Reason, LEN(@Marker)) <> @Marker
      AND LEN(Reason) + LEN(@Marker) <= 1000;
END
ELSE IF @Action = 'RESTORE'
BEGIN
    -- Bỏ đuôi đánh dấu, lý do về đúng giá trị đã ký.
    UPDATE DeathCaseDecisions
    SET Reason = LEFT(Reason, LEN(Reason) - LEN(@Marker))
    WHERE CaseId = @CaseId
      AND Kind IN ('Approved', 'Rejected')
      AND RIGHT(Reason, LEN(@Marker)) = @Marker;
END

-- Xem quyết định cuối sau khi chạy (chỉ duyệt/từ chối mới được ký số).
SELECT d.Id, d.Kind, d.VersionNo, d.Reason, d.DecidedAt, d.SigningKeyId,
       CASE WHEN d.Signature IS NULL THEN 0 ELSE DATALENGTH(d.Signature) END AS SignatureBytes
FROM DeathCaseDecisions d
WHERE d.CaseId = @CaseId AND d.Kind IN ('Approved', 'Rejected');
