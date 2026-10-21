namespace LegacyVault.Application.Services;

/// <summary>Luật tuổi của người nhận, dùng chung cho BeneficiaryService và BeneficiaryMappings.</summary>
public static class BeneficiaryRules
{
    /// <summary>Mốc 18 tuổi, chỉ dùng tính isMinor để hiển thị (đã bỏ luật xác nhận SETUP-19).</summary>
    public const int AdultAge = 18;

    /// <summary>
    /// Tuổi tròn tại ngày today. Kiểm "chưa tới sinh nhật năm nay" bằng dob.AddYears(age) > today
    /// để người sinh 29/02 được tính đúng ở năm không nhuận (DateOnly.AddYears đưa về 28/02).
    /// </summary>
    public static int GetAge(DateOnly dateOfBirth, DateOnly today)
    {
        int age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.AddYears(age) > today)
        {
            age = age - 1;
        }
        return age;
    }

    public static bool IsMinor(DateOnly dateOfBirth, DateOnly today)
    {
        return GetAge(dateOfBirth, today) < AdultAge;
    }
}
