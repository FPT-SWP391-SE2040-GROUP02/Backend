-- CHỈ DB local, không chạy trên môi trường thật.
-- SETUP-01, EKYC-01: dữ liệu giả lập để thử kích hoạt, không phải kết quả thanh toán hay eKYC thật.
-- Chọn đúng database local của API trong SSMS; điền hai Id từ kho và tài khoản Owner của bạn.
-- Nếu dùng sqlcmd, thêm -I (QUOTED_IDENTIFIER ON) vì database có chỉ mục lọc.
-- Không đổi trạng thái HandoverPlans bằng SQL; kích hoạt qua http/plan-setup.http.
-- Thử thanh toán: dùng POST /billing/orders/{id}/dev-pay trong http/billing.http thay vì UPDATE tay VaultEntitlements;
-- dev-pay cấp quyền qua đúng nghiệp vụ thanh toán (chỉ Development). Script này chỉ dùng khi cần nhảy thẳng sang
-- XS còn hạn + eKYC mà không đi qua đơn thanh toán.

SET QUOTED_IDENTIFIER ON;

DECLARE @VaultId uniqueidentifier = NULL; -- Điền Id kho cần thử.
DECLARE @OwnerId uniqueidentifier = NULL; -- Điền Id Owner của kho đó.
DECLARE @CitizenIdLast4 varchar(4) = ''; -- Điền 4 số cuối khi thử luồng chứng tử; không ghi số thật vào file để commit.

-- DEATH-13: 4 số cuối phải khớp thông tin Owner sẽ khai trên chứng tử để tránh cờ IDENTITY_MISMATCH.
-- Để trống vẫn thử điều kiện eKYC của màn kích hoạt được; cần điền trước khi thử đối chiếu chứng tử.
IF @VaultId IS NULL OR @OwnerId IS NULL
BEGIN
    PRINT N'Điền @VaultId và @OwnerId trước khi chạy.';
    RETURN;
END;

-- SETUP-01: quyền dịch vụ XS còn hạn một năm, 200 MiB và hai suất người nhận.
UPDATE dbo.VaultEntitlements
SET Tier = N'Xs',
    Status = N'PaidActive',
    StorageQuotaBytes = 209715200,
    IncludedBeneficiarySlots = 2,
    ServiceExpiresAt = DATEADD(DAY, 365, SYSDATETIMEOFFSET())
WHERE VaultId = @VaultId;

-- EKYC-01 / AC-46: chỉ lượt đăng ký Owner mới được dùng cho điều kiện kích hoạt.
-- Các cột và kiểu đã đối chiếu AppDbContextModelSnapshot.cs, AddEkycConsentAndMatch, AddEkycLiveness.
-- Mỗi lần chạy tạo một lượt Passed mới; chỉ chạy khi chủ động chuẩn bị lại dữ liệu thử.
INSERT INTO dbo.EkycVerifications
(
    Id, UserId, Status, Purpose, IdentityMatch, BackSideChecked,
    LivenessPassed, SpoofCheckPassed, CitizenIdLast4, CreatedAt, CompletedAt
)
VALUES
(
    NEWID(), @OwnerId, N'Passed', N'OwnerEnrollment', N'NotChecked', 0,
    1, 1, @CitizenIdLast4, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
);
