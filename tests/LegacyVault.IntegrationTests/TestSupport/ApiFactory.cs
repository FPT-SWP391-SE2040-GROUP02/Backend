using LegacyVault.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>
/// Chạy API thật trong bộ nhớ (WebApplicationFactory) trên một database SQL Server TẠM riêng
/// và một thư mục lưu nội dung tạm riêng. Không đụng database dev.
/// </summary>
/// <remarks>
/// Cách dùng: <see cref="CreateForTempDatabase"/> trong InitializeAsync → <see cref="MigrateAsync"/>;
/// trong DisposeAsync gọi <see cref="DropAsync"/> rồi DisposeAsync.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION";

    private readonly string connectionString;

    public ApiFactory(string connectionString, string storageRoot)
    {
        this.connectionString = connectionString;
        StorageRoot = storageRoot;
    }

    public string StorageRoot { get; }
    public Action<IServiceCollection>? ConfigureAdditionalServices { get; set; }

    /// <summary>
    /// Tạo factory trỏ tới database tên ngẫu nhiên trên server của LEGACYVAULT_TEST_CONNECTION.
    /// Chưa đặt biến → null (khi đó [SqlFact] cũng đã Skip test).
    /// </summary>
    public static ApiFactory? CreateForTempDatabase()
    {
        string? baseConnection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            return null;
        }

        SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(baseConnection);
        builder.InitialCatalog = $"LegacyVault_Test_{Guid.NewGuid():N}";
        string storageRoot = Path.Combine(Path.GetTempPath(), $"legacyvault-test-{Guid.NewGuid():N}");
        return new ApiFactory(builder.ConnectionString, storageRoot);
    }

    /// <summary>Tạo database tạm và áp toàn bộ migration.</summary>
    public async Task MigrateAsync()
    {
        using (IServiceScope scope = Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
    }

    /// <summary>Xóa database tạm và thư mục nội dung tạm.</summary>
    public async Task DropAsync()
    {
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var storagePath = Path.GetFullPath(StorageRoot);
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (!databaseName.StartsWith("LegacyVault_Test_", StringComparison.Ordinal)
            || !Guid.TryParseExact(databaseName["LegacyVault_Test_".Length..], "N", out _)
            || !string.Equals(Path.GetDirectoryName(storagePath), expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(storagePath).StartsWith("legacyvault-test-", StringComparison.Ordinal)
            || !Guid.TryParseExact(Path.GetFileName(storagePath)["legacyvault-test-".Length..], "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside the generated test database/directory.");
        using (IServiceScope scope = Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureDeletedAsync();
        }
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((context, configuration) =>
        {
            Dictionary<string, string?> settings = new Dictionary<string, string?>();
            settings["ConnectionStrings:Default"] = connectionString;
            settings["AssetStorage:RootPath"] = StorageRoot;
            // user-secrets của người chạy test có thể đặt Provider = R2: ép lưu trên đĩa để test KHÔNG BAO GIỜ ghi vào bucket thật.
            settings["AssetStorage:Provider"] = "Local";
            // Jwt riêng cho test (không phụ thuộc appsettings.Development.json); token tạo bằng ITokenService của chính factory.
            settings["Jwt:Issuer"] = "LegacyVault.Tests";
            settings["Jwt:Audience"] = "LegacyVault.Tests";
            settings["Jwt:SigningKey"] = "LegacyVault_TestOnly_SigningKey_AtLeast32Bytes!";
            settings["Jwt:AccessTokenMinutes"] = "15";
            // Tắt job nền điểm danh: test tự gọi ICheckInJob khi cần, tránh job chạy xen vào dữ liệu test.
            settings["CheckIn:Enabled"] = "false";
            settings["Handover:Job:Enabled"] = "false";
            settings["ExecutorInvitations:DeliveryEnabled"] = "false";
            settings["DeathCase:NoticeJobEnabled"] = "false";
            // Môi trường Development nạp user-secrets của người chạy test: ép trống thông tin Gmail để test
            // KHÔNG BAO GIỜ gửi thư thật (mặc định dùng bản chỉ ghi log).
            settings["Email:Smtp:UserName"] = "";
            settings["Email:Smtp:Password"] = "";
            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services => ConfigureAdditionalServices?.Invoke(services));
    }
}
