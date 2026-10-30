using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Pins;

/// <summary>
/// Mã PIN kho qua HTTP trên SQL Server thật: đặt PIN, thao tác bị chặn khi thiếu hoặc sai PIN, khóa, quên PIN.
/// </summary>
/// <remarks>
/// [SqlFact] chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION. Factory bật <see cref="ApiFactory.EnforceVaultPin"/>
/// để dùng bộ kiểm PIN thật (các test khác chạy với bộ kiểm cho qua hết). Logic từng nhánh đã có test trong RAM ở
/// <see cref="VaultPinServiceTests"/>; lớp này kiểm phần nối: route, header, mã HTTP và migration.
/// </remarks>
public sealed class VaultPinFlowTests : IAsyncLifetime
{
    private const string GoodPin = "482913";
    private const string OtherPin = "715284";
    private const string WrongPin = "999000";
    private const string Password = "Matkhau-cua-toi-1";

    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.EnforceVaultPin = true;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    [SqlFact]
    public async Task FirstAssetIsFree_SecondAssetNeedsAPin()
    {
        (HttpClient client, Guid vaultId) = await NewVaultAsync();
        await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản đầu");

        HttpResponseMessage blocked = await client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/assets/text", new { name = "Tài sản hai", content = "nội dung" });
        await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "VAULT_PIN_NOT_SET");

        await SetPinAsync(client, vaultId);
        await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản hai");
    }

    [SqlFact]
    public async Task StatusReportsRequiredAndSet()
    {
        (HttpClient client, Guid vaultId) = await NewVaultAsync();
        await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản đầu");

        JsonElement before = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(PinUrl(vaultId)));
        Assert.False(before.GetProperty("isSet").GetBoolean());
        Assert.True(before.GetProperty("isRequired").GetBoolean());

        await SetPinAsync(client, vaultId);
        JsonElement after = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(PinUrl(vaultId)));
        Assert.True(after.GetProperty("isSet").GetBoolean());
        Assert.Equal(5, after.GetProperty("attemptsRemaining").GetInt32());
    }

    [SqlFact]
    public async Task ProtectedActions_NeedTheRightPinInTheHeader()
    {
        (HttpClient client, Guid vaultId) = await NewVaultAsync();
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản đầu");
        await SetPinAsync(client, vaultId);
        string contentUrl = $"/api/v1/vaults/{vaultId}/assets/{assetId}/content";

        // Thiếu header: không tính là một lần đoán.
        await ApiTestHelpers.AssertProblemAsync(await client.GetAsync(contentUrl), HttpStatusCode.Forbidden, "VAULT_PIN_REQUIRED");

        // Sai PIN: còn 4 lần.
        JsonElement wrong = await ApiTestHelpers.AssertProblemAsync(
            await SendWithPinAsync(client, HttpMethod.Get, contentUrl, WrongPin), HttpStatusCode.Forbidden, "INVALID_VAULT_PIN");
        Assert.Equal(4, wrong.GetProperty("attemptsRemaining").GetInt32());

        // Đúng PIN: tải được nội dung, rồi xóa được tài sản.
        HttpResponseMessage download = await SendWithPinAsync(client, HttpMethod.Get, contentUrl, GoodPin);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        HttpResponseMessage delete = await SendWithPinAsync(
            client, HttpMethod.Delete, $"/api/v1/vaults/{vaultId}/assets/{assetId}", GoodPin);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [SqlFact]
    public async Task FiveWrongPins_LockWithHttp423()
    {
        (HttpClient client, Guid vaultId) = await NewVaultAsync();
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản đầu");
        await SetPinAsync(client, vaultId);
        string contentUrl = $"/api/v1/vaults/{vaultId}/assets/{assetId}/content";

        for (int i = 0; i < 4; i++)
        {
            await ApiTestHelpers.AssertProblemAsync(
                await SendWithPinAsync(client, HttpMethod.Get, contentUrl, WrongPin), HttpStatusCode.Forbidden, "INVALID_VAULT_PIN");
        }
        JsonElement locked = await ApiTestHelpers.AssertProblemAsync(
            await SendWithPinAsync(client, HttpMethod.Get, contentUrl, WrongPin), (HttpStatusCode)423, "VAULT_PIN_LOCKED");
        Assert.True(locked.TryGetProperty("lockedUntil", out _));

        // Đang khóa thì PIN đúng cũng bị từ chối.
        await ApiTestHelpers.AssertProblemAsync(
            await SendWithPinAsync(client, HttpMethod.Get, contentUrl, GoodPin), (HttpStatusCode)423, "VAULT_PIN_LOCKED");
    }

    [SqlFact]
    public async Task ChangePin_NeedsTheCurrentPin()
    {
        (HttpClient client, Guid vaultId) = await NewVaultAsync();
        await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản đầu");
        await SetPinAsync(client, vaultId);

        HttpResponseMessage wrong = await client.PostAsJsonAsync(
            PinUrl(vaultId) + "/change", new { currentPin = WrongPin, newPin = OtherPin, confirmPin = OtherPin });
        await ApiTestHelpers.AssertProblemAsync(wrong, HttpStatusCode.Forbidden, "INVALID_VAULT_PIN");

        HttpResponseMessage ok = await client.PostAsJsonAsync(
            PinUrl(vaultId) + "/change", new { currentPin = GoodPin, newPin = OtherPin, confirmPin = OtherPin });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [SqlFact]
    public async Task ForgotPin_PasswordResetsAtOnceAndUnlocks()
    {
        string email = $"{Guid.NewGuid():N}@test.local";
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, email);
        await SetPasswordAsync(email);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản đầu");
        await SetPinAsync(client, vaultId);
        string contentUrl = $"/api/v1/vaults/{vaultId}/assets/{assetId}/content";

        // Quên PIN nên nhập sai tới khi bị khóa.
        for (int i = 0; i < 5; i++)
        {
            await SendWithPinAsync(client, HttpMethod.Get, contentUrl, WrongPin);
        }

        HttpResponseMessage wrongPassword = await client.PostAsJsonAsync(
            PinUrl(vaultId) + "/reset", new { password = "sai-mat-khau", newPin = OtherPin, confirmPin = OtherPin });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        HttpResponseMessage reset = await client.PostAsJsonAsync(
            PinUrl(vaultId) + "/reset", new { password = Password, newPin = OtherPin, confirmPin = OtherPin });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        HttpResponseMessage download = await SendWithPinAsync(client, HttpMethod.Get, contentUrl, OtherPin);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [SqlFact]
    public async Task ForgotPin_AccountWithoutPassword_ReturnsConflict()
    {
        (HttpClient client, Guid vaultId) = await NewVaultAsync();
        await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Tài sản đầu");
        await SetPinAsync(client, vaultId);

        HttpResponseMessage reset = await client.PostAsJsonAsync(
            PinUrl(vaultId) + "/reset", new { password = Password, newPin = OtherPin, confirmPin = OtherPin });

        await ApiTestHelpers.AssertProblemAsync(reset, HttpStatusCode.Conflict, "VAULT_PIN_RESET_NEEDS_PASSWORD");
    }

    [SqlFact]
    public async Task AnotherUsersVault_Returns404()
    {
        (HttpClient owner, Guid vaultId) = await NewVaultAsync();
        await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Tài sản đầu");
        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        HttpResponseMessage response = await stranger.GetAsync(PinUrl(vaultId));

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    private async Task<(HttpClient Client, Guid VaultId)> NewVaultAsync()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        return (client, vaultId);
    }

    private static string PinUrl(Guid vaultId)
    {
        return $"/api/v1/vaults/{vaultId}/pin";
    }

    private static async Task SetPinAsync(HttpClient client, Guid vaultId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            PinUrl(vaultId), new { pin = GoodPin, confirmPin = GoodPin });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendWithPinAsync(HttpClient client, HttpMethod method, string url, string pin)
    {
        HttpRequestMessage request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Vault-Pin", pin);
        return client.SendAsync(request);
    }

    /// <summary>User tạo bằng token trong test không có mật khẩu; đặt mật khẩu để thử luồng quên PIN.</summary>
    private async Task SetPasswordAsync(string email)
    {
        IPasswordHasher hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        string hash = hasher.Hash(Password);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, hash));
        });
    }
}
