using System.Net;
using System.Net.Http.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Admin;

/// <summary>Admin tạo tài khoản nhân sự: số điện thoại có nhập thì phải đúng định dạng số di động Việt Nam. Chạy trên SQL Server thật ([SqlFact]).</summary>
public sealed class AdminCreateUserPhoneTests : IAsyncLifetime
{
    private const string UsersUrl = "/api/v1/admin/users";

    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
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

    private async Task<HttpClient> AdminClientAsync()
    {
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        return client;
    }

    private static object Body(string email, string? phone)
    {
        return new
        {
            fullName = "Luật Sư Thử",
            email,
            role = "VERIFIER",
            phone
        };
    }

    [SqlFact]
    public async Task Create_WithWrongPhoneFormat_IsRejected()
    {
        HttpClient admin = await AdminClientAsync();
        string[] wrong = new string[] { "3232", "12345", "0212345678", "091234567", "abc0912345678", "+8491234" };
        int index = 0;
        foreach (string phone in wrong)
        {
            index = index + 1;
            HttpResponseMessage response = await admin.PostAsJsonAsync(UsersUrl, Body($"sai{index}@example.com", phone));
            await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
    }

    [SqlFact]
    public async Task Create_WithRightPhoneOrNoPhone_IsAccepted_AndPhoneIsNormalized()
    {
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage withPhone = await admin.PostAsJsonAsync(UsersUrl, Body("dung@example.com", "091 234-56.78"));
        Assert.Equal(HttpStatusCode.Created, withPhone.StatusCode);
        string? savedPhone = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            User saved = await db.Users.SingleAsync(u => u.Email == "dung@example.com");
            savedPhone = saved.Phone;
        });
        Assert.Equal("0912345678", savedPhone);

        HttpResponseMessage withoutPhone = await admin.PostAsJsonAsync(UsersUrl, Body("khongso@example.com", null));
        Assert.Equal(HttpStatusCode.Created, withoutPhone.StatusCode);
    }
}
