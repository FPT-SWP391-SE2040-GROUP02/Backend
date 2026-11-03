using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.ServiceConfig;
using LegacyVault.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>
/// R2 đọc tài khoản, bucket và khóa từ cấu hình lúc chạy: đổi trên trang Admin thì thao tác kế tiếp dùng giá trị mới mà không
/// cần khởi động lại. Kiểm bằng cách bắt yêu cầu HTTP thật mà SDK S3 gửi đi (địa chỉ, đường dẫn, chữ ký). Không cần DB.
/// </summary>
public sealed class R2ConfigFromWebTests
{
    private const string SecretKey = "r2-secret-LEAK-7777";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public Func<HttpResponseMessage> Responder { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Responder());
        }
    }

    private sealed class StubAwsHttpClientFactory : HttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        public StubAwsHttpClientFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            return new HttpClient(handler, false);
        }

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig)
        {
            return false;
        }

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig)
        {
            return false;
        }

        public override string GetConfigUniqueString(IClientConfig clientConfig)
        {
            return "stub-r2";
        }
    }

    private sealed class TestableObjectClient : S3R2ObjectClient
    {
        private readonly HttpMessageHandler handler;

        public TestableObjectClient(FakeConfigResolver resolver, HttpMessageHandler handler)
            : base(SmtpTestSenders.ScopesFor(resolver), new NoDefaults(), NullLogger<S3R2ObjectClient>.Instance)
        {
            this.handler = handler;
        }

        protected override AmazonS3Config BuildConfig(R2StorageSettings settings)
        {
            AmazonS3Config config = base.BuildConfig(settings);
            config.HttpClientFactory = new StubAwsHttpClientFactory(handler);
            return config;
        }
    }

    private sealed class TestableTester : R2ConnectionTester
    {
        private readonly HttpMessageHandler handler;

        public TestableTester(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        protected override AmazonS3Config BuildConfig(R2StorageSettings settings)
        {
            AmazonS3Config config = base.BuildConfig(settings);
            config.HttpClientFactory = new StubAwsHttpClientFactory(handler);
            return config;
        }
    }

    private sealed class NoDefaults : ISystemConfigDefaults
    {
        public ResolvedConfig? ReadPrimary(string serviceKey)
        {
            return null;
        }
    }

    private static ResolvedConfig Config(string accountId, string bucket, string accessKeyId, string secret)
    {
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["accountId"] = accountId;
        settings["bucketName"] = bucket;
        Dictionary<string, string> secrets = new Dictionary<string, string>();
        secrets["accessKeyId"] = accessKeyId;
        secrets["secretAccessKey"] = secret;
        return FakeConfigResolver.Config(settings, secrets);
    }

    private static MemoryStream Content()
    {
        return new MemoryStream(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task Put_UsesAccountBucketAndKeyIdFromConfig()
    {
        FakeConfigResolver resolver = new FakeConfigResolver();
        resolver.Slots[SystemConfigSlot.Primary] = Config("acc1", "bucket-one", "AKIAONE", SecretKey);
        CapturingHandler handler = new CapturingHandler();
        TestableObjectClient client = new TestableObjectClient(resolver, handler);

        await client.PutAsync("object-1", Content(), CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("acc1.r2.cloudflarestorage.com", request.RequestUri!.Host);
        Assert.Equal("/bucket-one/object-1", request.RequestUri.AbsolutePath);
        Assert.Contains("Credential=AKIAONE/", string.Join(" ", request.Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task ChangedConfig_IsUsedByTheNextCall_WithoutRestart()
    {
        FakeConfigResolver resolver = new FakeConfigResolver();
        resolver.Slots[SystemConfigSlot.Primary] = Config("acc1", "bucket-one", "AKIAONE", SecretKey);
        CapturingHandler handler = new CapturingHandler();
        TestableObjectClient client = new TestableObjectClient(resolver, handler);
        await client.PutAsync("object-1", Content(), CancellationToken.None);

        resolver.Slots[SystemConfigSlot.Primary] = Config("acc2", "bucket-two", "AKIATWO", SecretKey + "-2");
        await client.PutAsync("object-2", Content(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("acc2.r2.cloudflarestorage.com", handler.Requests[1].RequestUri!.Host);
        Assert.Equal("/bucket-two/object-2", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Contains("Credential=AKIATWO/", string.Join(" ", handler.Requests[1].Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task SameConfig_ReusesTheClient_AndNewKeyOnlyChangesTheSignature()
    {
        FakeConfigResolver resolver = new FakeConfigResolver();
        resolver.Slots[SystemConfigSlot.Primary] = Config("acc1", "bucket-one", "AKIAONE", SecretKey);
        CapturingHandler handler = new CapturingHandler();
        TestableObjectClient client = new TestableObjectClient(resolver, handler);

        await client.PutAsync("a", Content(), CancellationToken.None);
        resolver.Slots[SystemConfigSlot.Primary] = Config("acc1", "bucket-one", "AKIAONE", "rotated-secret");
        await client.PutAsync("b", Content(), CancellationToken.None);

        string first = string.Join(" ", handler.Requests[0].Headers.GetValues("Authorization"));
        string second = string.Join(" ", handler.Requests[1].Headers.GetValues("Authorization"));
        Assert.Contains("Credential=AKIAONE/", second);
        Assert.NotEqual(first.Substring(first.IndexOf("Signature=", StringComparison.Ordinal)),
            second.Substring(second.IndexOf("Signature=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NotConfigured_ThrowsStorageUnavailable_WithoutHttp()
    {
        FakeConfigResolver resolver = new FakeConfigResolver();
        resolver.Slots[SystemConfigSlot.Primary] = Config("", "", "", "");
        CapturingHandler handler = new CapturingHandler();
        TestableObjectClient client = new TestableObjectClient(resolver, handler);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.PutAsync("object-1", Content(), CancellationToken.None));

        Assert.Equal(ErrorCode.StorageUnavailable, error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task R2Error_StillBecomesStorageUnavailable_WithoutLeakingTheKey()
    {
        FakeConfigResolver resolver = new FakeConfigResolver();
        resolver.Slots[SystemConfigSlot.Primary] = Config("acc1", "bucket-one", "AKIAONE", SecretKey);
        CapturingHandler handler = new CapturingHandler();
        handler.Responder = () => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<Error><Code>AccessDenied</Code><Message>denied</Message></Error>", Encoding.UTF8, "application/xml"),
        };
        TestableObjectClient client = new TestableObjectClient(resolver, handler);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.PutAsync("object-1", Content(), CancellationToken.None));

        Assert.Equal(ErrorCode.StorageUnavailable, error.Code);
        Assert.DoesNotContain(SecretKey, error.Message);
    }

    [Fact]
    public async Task Tester_Success_ReportsOk()
    {
        CapturingHandler handler = new CapturingHandler();
        handler.Responder = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"><Name>bucket-one</Name><KeyCount>0</KeyCount><MaxKeys>1</MaxKeys><IsTruncated>false</IsTruncated></ListBucketResult>",
                Encoding.UTF8, "application/xml"),
        };
        TestableTester tester = new TestableTester(handler);

        TestSystemConfigResponse result = await tester.TestAsync(Config("acc1", "bucket-one", "AKIAONE", SecretKey), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
        Assert.Equal("acc1.r2.cloudflarestorage.com", Assert.Single(handler.Requests).RequestUri!.Host);
    }

    [Theory]
    [InlineData("InvalidAccessKeyId", HttpStatusCode.Forbidden)]
    [InlineData("SignatureDoesNotMatch", HttpStatusCode.Forbidden)]
    [InlineData("NoSuchBucket", HttpStatusCode.NotFound)]
    [InlineData("AccessDenied", HttpStatusCode.Forbidden)]
    public async Task Tester_ReportsTheS3ErrorCode_WithoutTheKey(string code, HttpStatusCode status)
    {
        CapturingHandler handler = new CapturingHandler();
        handler.Responder = () => new HttpResponseMessage(status)
        {
            Content = new StringContent(
                "<Error><Code>" + code + "</Code><Message>" + SecretKey + "</Message></Error>", Encoding.UTF8, "application/xml"),
        };
        TestableTester tester = new TestableTester(handler);

        TestSystemConfigResponse result = await tester.TestAsync(Config("acc1", "bucket-one", "AKIAONE", SecretKey), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public async Task Tester_Unreachable_ReportsNetwork()
    {
        CapturingHandler handler = new CapturingHandler();
        handler.Responder = () => throw new HttpRequestException("down");
        TestableTester tester = new TestableTester(handler);

        TestSystemConfigResponse result = await tester.TestAsync(Config("acc1", "bucket-one", "AKIAONE", SecretKey), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("NETWORK", result.ErrorCode);
    }
}
