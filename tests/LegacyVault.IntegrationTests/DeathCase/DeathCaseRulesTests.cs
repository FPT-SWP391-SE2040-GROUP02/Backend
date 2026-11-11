using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Reference;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Luật thuần của hồ sơ chứng tử (không cần DB).</summary>
public sealed class DeathCaseRulesTests
{
    [Fact]
    public void Death05_OnlyRunningPlanCanOpenCase()
    {
        // Chuẩn bị
        PlanStatus[] running = { PlanStatus.Active, PlanStatus.CheckinOverdue, PlanStatus.AwaitingCheck };

        foreach (PlanStatus status in Enum.GetValues<PlanStatus>())
        {
            // Hành động
            bool canOpen = DeathCaseRules.CanOpen(status);

            // Kiểm tra
            bool expected = Array.IndexOf(running, status) >= 0;
            Assert.Equal(expected, canOpen);
        }
    }

    [Fact]
    public void Death05_DraftPlanCannotOpenCase()
    {
        // Chuẩn bị
        PlanStatus status = PlanStatus.Draft;

        // Hành động
        bool canOpen = DeathCaseRules.CanOpen(status);

        // Kiểm tra
        Assert.False(canOpen);
    }

    private static readonly DateOnly DeathDay = new DateOnly(2026, 10, 10);

    [Fact]
    public void Death01_ThreeCertificateFactsAndTheFileAreRequiredToSubmit()
    {
        // Chuẩn bị + Hành động: hồ sơ trống, hồ sơ đủ 3 thông tin nhưng chưa có tệp, hồ sơ đủ tất cả
        List<string> empty = DeathCaseRules.GetMissingRequirements(null, null, null, null, 0);
        List<string> noFile = DeathCaseRules.GetMissingRequirements(DeathDay, DeathDay, "79", "26743", 0);
        List<string> complete = DeathCaseRules.GetMissingRequirements(DeathDay, DeathDay, "79", "26743", 1);

        // Kiểm tra: đúng thứ tự DATE_OF_DEATH, ISSUED_ON, DEATH_REGISTRATION_PLACE, FILES
        Assert.Equal(
            new List<string> { "DATE_OF_DEATH", "ISSUED_ON", "DEATH_REGISTRATION_PLACE", "FILES" }, empty);
        Assert.Equal(new List<string> { "FILES" }, noFile);
        Assert.Empty(complete);
    }

    [Fact]
    public void Death01_RegistrationPlaceNeedsBothProvinceAndWard()
    {
        // Hành động: chỉ có tỉnh, chỉ có phường, tỉnh toàn khoảng trắng
        List<string> onlyProvince = DeathCaseRules.GetMissingRequirements(DeathDay, DeathDay, "79", null, 1);
        List<string> onlyWard = DeathCaseRules.GetMissingRequirements(DeathDay, DeathDay, null, "26743", 1);
        List<string> blankProvince = DeathCaseRules.GetMissingRequirements(DeathDay, DeathDay, "  ", "26743", 1);

        // Kiểm tra
        Assert.Equal(new List<string> { "DEATH_REGISTRATION_PLACE" }, onlyProvince);
        Assert.Equal(new List<string> { "DEATH_REGISTRATION_PLACE" }, onlyWard);
        Assert.Equal(new List<string> { "DEATH_REGISTRATION_PLACE" }, blankProvince);
    }

    [Fact]
    public void Death01_NewRequirementCodeIsAppendedAtTheEndOfAllCodes()
    {
        // Kiểm tra: mã mới nằm CUỐI mảng, mã FILES cũ đứng ngay trước
        string[] codes = DeathCaseRules.AllRequirementCodes;
        Assert.Equal("DEATH_REGISTRATION_PLACE", codes[codes.Length - 1]);
        Assert.Equal("FILES", codes[codes.Length - 2]);
    }

    [Fact]
    public void Death01_OnlyDeathCertificateFilesCountTowardsTheRequirement()
    {
        DeathCaseFile certificate = new DeathCaseFile();
        certificate.Kind = DeathCaseFileKind.DeathCertificate;
        DeathCaseFile notice = new DeathCaseFile();
        notice.Kind = DeathCaseFileKind.DeathNotice;

        // Giấy báo tử đứng một mình không đủ để nộp.
        Assert.Equal(0, DeathCaseRules.CountCertificateFiles(new List<DeathCaseFile> { notice }));
        Assert.Equal(1, DeathCaseRules.CountCertificateFiles(new List<DeathCaseFile> { notice, certificate }));
        Assert.Equal(new List<string> { "FILES" },
            DeathCaseRules.GetMissingRequirements(
                DeathDay, DeathDay, "79", "26743",
                DeathCaseRules.CountCertificateFiles(new List<DeathCaseFile> { notice })));
    }

    [Fact]
    public void Death01_FileKindOnlyAcceptsKnownValues()
    {
        Assert.True(DeathCaseFileKind.IsValid("DEATH_CERTIFICATE"));
        Assert.True(DeathCaseFileKind.IsValid("DEATH_NOTICE"));
        Assert.False(DeathCaseFileKind.IsValid("OTHER"));
        Assert.False(DeathCaseFileKind.IsValid(null));
    }

    // "Hôm nay" theo giờ Việt Nam của mốc này là 15/10/2026 (UTC 02:00 = VN 09:00).
    private static readonly DateTimeOffset UtcNow = new DateTimeOffset(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new DateOnly(2026, 10, 15);

    // Kế hoạch kích hoạt lúc 12/10/2026 08:00 giờ Việt Nam.
    private static readonly DateTimeOffset ActivatedAt = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.FromHours(7));

    private static string ValidationMessage(AppException error, string field)
    {
        Dictionary<string, string[]> errors = (Dictionary<string, string[]>)error.Extensions["errors"]!;
        return errors[field][0];
    }

    private static AppException ExpectError(
        DateOnly? dateOfDeath, DateOnly? issuedOn, string? provinceCode, string? wardCode, DateTimeOffset? activatedAt)
    {
        AdministrativeUnitCatalog catalog = new AdministrativeUnitCatalog();
        AppException error = Assert.Throws<AppException>(() => DeathCaseRules.ValidateCertificateInfo(
            dateOfDeath, issuedOn, provinceCode, wardCode, activatedAt, UtcNow, catalog));
        Assert.Equal(ErrorCode.ValidationError, error.Code);
        return error;
    }

    private static void ExpectValid(
        DateOnly? dateOfDeath, DateOnly? issuedOn, string? provinceCode, string? wardCode, DateTimeOffset? activatedAt)
    {
        AdministrativeUnitCatalog catalog = new AdministrativeUnitCatalog();
        DeathCaseRules.ValidateCertificateInfo(dateOfDeath, issuedOn, provinceCode, wardCode, activatedAt, UtcNow, catalog);
    }

    [Fact]
    public void Death01_DeathDateAfterTodayIsRejectedButTodayIsAllowed()
    {
        // Hành động
        AppException tomorrow = ExpectError(Today.AddDays(1), null, null, null, null);

        // Kiểm tra: ngày mất = hôm nay hợp lệ (ranh giới), ngày mai thì lỗi đúng trường và đúng chữ
        Assert.Equal("Ngày mất không được sau hôm nay.", ValidationMessage(tomorrow, "dateOfDeath"));
        ExpectValid(Today, null, null, null, null);
    }

    [Fact]
    public void Death01_DeathDateBeforeActivationDayIsRejectedButTheSameDayIsAllowed()
    {
        // Hành động: kích hoạt ngày 12/10 (giờ Việt Nam)
        AppException beforeActivation = ExpectError(new DateOnly(2026, 10, 11), null, null, null, ActivatedAt);

        // Kiểm tra
        Assert.Equal("Ngày mất phải sau ngày Chủ kho kích hoạt kế hoạch (12/10/2026).",
            ValidationMessage(beforeActivation, "dateOfDeath"));
        ExpectValid(new DateOnly(2026, 10, 12), null, null, null, ActivatedAt);
        ExpectValid(new DateOnly(2026, 10, 13), null, null, null, ActivatedAt);
        // Kế hoạch chưa có ngày kích hoạt (dữ liệu thử) thì bỏ qua luật này.
        ExpectValid(new DateOnly(2026, 1, 1), null, null, null, null);
    }

    [Fact]
    public void Death01_ActivationDayIsTakenInVietnamTime()
    {
        // Chuẩn bị: 11/10/2026 18:00 UTC = 12/10/2026 01:00 giờ Việt Nam, nên ngày kích hoạt là 12/10
        DateTimeOffset activatedLateUtc = new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);

        // Hành động + Kiểm tra
        ExpectError(new DateOnly(2026, 10, 11), null, null, null, activatedLateUtc);
        ExpectValid(new DateOnly(2026, 10, 12), null, null, null, activatedLateUtc);
    }

    [Fact]
    public void Death01_TodayIsTakenInVietnamTime()
    {
        // Chuẩn bị: 14/10/2026 20:00 UTC = 15/10/2026 03:00 giờ Việt Nam
        DateTimeOffset lateUtc = new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);
        AdministrativeUnitCatalog catalog = new AdministrativeUnitCatalog();

        // Hành động + Kiểm tra: ngày 15/10 vẫn là "hôm nay", ngày 16/10 là tương lai
        DeathCaseRules.ValidateCertificateInfo(new DateOnly(2026, 10, 15), null, null, null, null, lateUtc, catalog);
        Assert.Throws<AppException>(() => DeathCaseRules.ValidateCertificateInfo(
            new DateOnly(2026, 10, 16), null, null, null, null, lateUtc, catalog));
    }

    [Fact]
    public void Death01_RegistrationDateBeforeDeathDateIsRejectedButTheSameDayIsAllowed()
    {
        // Hành động
        AppException beforeDeath = ExpectError(Today.AddDays(-2), Today.AddDays(-3), null, null, null);

        // Kiểm tra
        Assert.Equal("Ngày đăng ký khai tử phải từ ngày mất trở đi.", ValidationMessage(beforeDeath, "issuedOn"));
        ExpectValid(Today.AddDays(-2), Today.AddDays(-2), null, null, null);
        ExpectValid(Today.AddDays(-2), Today.AddDays(-1), null, null, null);
    }

    [Fact]
    public void Death01_RegistrationDateAfterTodayIsRejectedButTodayIsAllowed()
    {
        // Hành động
        AppException afterToday = ExpectError(Today.AddDays(-2), Today.AddDays(1), null, null, null);

        // Kiểm tra
        Assert.Equal("Ngày đăng ký khai tử không được sau hôm nay.", ValidationMessage(afterToday, "issuedOn"));
        ExpectValid(Today.AddDays(-2), Today, null, null, null);
    }

    [Fact]
    public void Death01_UnknownProvinceIsRejected()
    {
        // Hành động
        AppException error = ExpectError(null, null, "99", null, null);

        // Kiểm tra
        Assert.Equal("Tỉnh/thành phố không hợp lệ.", ValidationMessage(error, "deathRegistrationProvinceCode"));
        ExpectValid(null, null, "79", null, null);
    }

    [Fact]
    public void Death01_WardOfAnotherProvinceIsRejected()
    {
        // Chuẩn bị + Hành động: 00004 là Phường Ba Đình của Hà Nội (01), không thuộc TP.HCM (79)
        AppException wrongProvince = ExpectError(null, null, "79", "00004", null);
        AppException unknownWard = ExpectError(null, null, "79", "99999", null);
        AppException wardWithoutProvince = ExpectError(null, null, null, "26743", null);

        // Kiểm tra
        string expected = "Phường/xã không thuộc tỉnh/thành phố đã chọn.";
        Assert.Equal(expected, ValidationMessage(wrongProvince, "deathRegistrationWardCode"));
        Assert.Equal(expected, ValidationMessage(unknownWard, "deathRegistrationWardCode"));
        Assert.Equal(expected, ValidationMessage(wardWithoutProvince, "deathRegistrationWardCode"));
        ExpectValid(null, null, "79", "26743", null);
    }

    [Fact]
    public void Death01_FieldsNotFilledYetAreSkipped()
    {
        // Bản nháp chưa nhập gì thì không có gì để kiểm
        ExpectValid(null, null, null, null, ActivatedAt);
    }

    [Fact]
    public void Death10_CheckInAfterDeathDayRaisesAWarningButSameDayDoesNot()
    {
        // Chuẩn bị: ngày mất 10/10. Điểm danh lúc 11/10 08:00 giờ Việt Nam là SAU ngày mất; cùng ngày thì không.
        DateOnly death = new DateOnly(2026, 10, 10);
        DateTimeOffset afterDeath = new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.FromHours(7));
        DateTimeOffset sameDay = new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.FromHours(7));

        // Hành động + Kiểm tra
        Assert.True(DeathCaseRules.IsDeathBeforeLastCheckIn(death, afterDeath));
        Assert.False(DeathCaseRules.IsDeathBeforeLastCheckIn(death, sameDay));
        Assert.False(DeathCaseRules.IsDeathBeforeLastCheckIn(death, null));
        Assert.False(DeathCaseRules.IsDeathBeforeLastCheckIn(null, afterDeath));
    }

    [Fact]
    public void Death10_CheckInDayIsTakenInVietnamTime()
    {
        // Chuẩn bị: 10/10 17:30 UTC = 11/10 00:30 giờ Việt Nam, đã sang ngày sau ngày mất 10/10
        DateOnly death = new DateOnly(2026, 10, 10);
        DateTimeOffset lastCheckIn = new DateTimeOffset(2026, 10, 10, 17, 30, 0, TimeSpan.Zero);

        // Hành động + Kiểm tra
        Assert.True(DeathCaseRules.IsDeathBeforeLastCheckIn(death, lastCheckIn));
    }

    [Fact]
    public void Death10_RegistrationLaterThanFifteenDaysRaisesAWarning()
    {
        // Chuẩn bị
        DateOnly death = new DateOnly(2026, 10, 1);

        // Hành động + Kiểm tra: đúng 15 ngày chưa cảnh báo, 16 ngày thì cảnh báo
        Assert.False(DeathCaseRules.IsLateRegistration(death, death.AddDays(15)));
        Assert.True(DeathCaseRules.IsLateRegistration(death, death.AddDays(16)));
        Assert.False(DeathCaseRules.IsLateRegistration(death, death));
        Assert.False(DeathCaseRules.IsLateRegistration(null, death));
        Assert.False(DeathCaseRules.IsLateRegistration(death, null));
    }

    [Fact]
    public void Death10_WarningsListHasTheMessageForEachCondition()
    {
        // Chuẩn bị: ngày mất 01/10, đăng ký 20/10 (trễ 19 ngày), điểm danh cuối 05/10 giờ Việt Nam
        DateOnly death = new DateOnly(2026, 10, 1);
        DateOnly issued = new DateOnly(2026, 10, 20);
        DateTimeOffset lastCheckIn = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(7));

        // Hành động
        List<VerifierWarningResponse> both = DeathCaseReviewRules.BuildWarnings(death, issued, lastCheckIn);
        List<VerifierWarningResponse> none = DeathCaseReviewRules.BuildWarnings(death, death.AddDays(2), null);

        // Kiểm tra
        Assert.Equal(2, both.Count);
        Assert.Equal("DEATH_BEFORE_LAST_CHECKIN", both[0].Code);
        Assert.Equal("Ngày mất trước lần điểm danh cuối (05/10/2026). Kiểm tra kỹ giấy trước khi duyệt.", both[0].Message);
        Assert.Equal("LATE_REGISTRATION", both[1].Code);
        Assert.Empty(none);
    }

    [Fact]
    public void Death11_ChangingOwnerIdentityOrDeathDateIsCritical()
    {
        // Chuẩn bị
        DateOnly death = new DateOnly(2026, 10, 1);

        // Hành động
        bool nameChanged = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn Bình", "012345678901", death);
        bool citizenIdChanged = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn An", "012345678902", death);
        bool deathDateChanged = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn An", "012345678901", death.AddDays(-1));
        bool onlySpacingAndCase = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "  nguyễn   văn AN ", "012345678901", death);

        // Kiểm tra
        Assert.True(nameChanged);
        Assert.True(citizenIdChanged);
        Assert.True(deathDateChanged);
        Assert.False(onlySpacingAndCase);
    }

    [Fact]
    public void Death11_ChangingCertificatePlaceIsNotCritical()
    {
        // Chuẩn bị: số giấy, nơi cấp, ngày cấp không có trong tham số so sánh trọng yếu
        DateOnly death = new DateOnly(2026, 10, 1);

        // Hành động
        bool critical = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn An", "012345678901", death);

        // Kiểm tra
        Assert.False(critical);
    }

    [Fact]
    public void Death08_ObjectionEndsSeventyTwoHoursAfterDelivery()
    {
        // Chuẩn bị
        DateTimeOffset deliveredAt = new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero);

        // Hành động
        DateTimeOffset endsAt = DeathCaseRules.ObjectionEndsAt(deliveredAt);

        // Kiểm tra
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 8, 30, 0, TimeSpan.Zero), endsAt);
    }
}
