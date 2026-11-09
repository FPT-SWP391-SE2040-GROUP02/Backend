using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;

namespace LegacyVault.Application.Ekyc;

/// <summary>
/// Dịch vụ eKYC giả dùng khi Admin bật "tạm ngừng eKYC" bằng công cụ thử nghiệm (chỉ có ở Development/Staging).
/// Mọi bước đều thành công: giấy tờ đọc ra đúng người được chỉ định (người nhận) hoặc một danh tính cố định theo tài khoản
/// (chủ kho), liveness và so khớp khuôn mặt đều đạt. Phần lưu trạng thái phía sau vẫn chạy như thật.
/// </summary>
/// <remarks>Không có ảnh nào được phân tích. Không bao giờ dùng ở môi trường thật; việc bật tắt chỉ có ở công cụ thử nghiệm.</remarks>
public sealed class SimulatedEkycClient : IEkycClient
{
    public const string TokenPrefix = "paused-";
    private const int EmbeddingSize = 128;

    private readonly BeneficiaryPersonalData? expected;
    private readonly Guid userId;

    public SimulatedEkycClient(BeneficiaryPersonalData? expected, Guid userId)
    {
        this.expected = expected;
        this.userId = userId;
    }

    public Task<EkycDocumentReading> ReadFrontAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        EkycDocumentReading reading = new EkycDocumentReading();
        if (expected != null)
        {
            reading.IdNumber = expected.CitizenId;
            reading.FullName = expected.FullName;
            reading.DateOfBirth = expected.DateOfBirth.ToString("yyyy-MM-dd");
        }
        else
        {
            // Chủ kho đăng ký: số CCCD 12 số lấy từ mã tài khoản nên mỗi tài khoản một số khác nhau.
            reading.IdNumber = BuildCitizenId(userId);
            reading.FullName = "NGUYEN VAN THU NGHIEM";
            reading.DateOfBirth = "1990-01-01";
        }
        reading.ResidenceAddress = "Địa chỉ thử nghiệm (eKYC đang tạm ngừng)";
        reading.DocumentToken = TokenPrefix + Guid.NewGuid().ToString("N");
        return Task.FromResult(reading);
    }

    public Task<EkycBackReading> ReadBackAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        // Số rỗng nghĩa là "không đọc được"; luật đối chiếu mặt sau bỏ qua khi rỗng.
        return Task.FromResult(new EkycBackReading { ResidenceAddress = "Địa chỉ thử nghiệm" });
    }

    public Task<EkycSpoofCheck> CheckSpoofAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(new EkycSpoofCheck { Passed = true, LiveScore = 1.0 });
    }

    public Task<EkycFaceComparison> CompareFaceAsync(
        string documentToken, byte[] selfie, string fileName, string contentType, double threshold, CancellationToken ct)
    {
        return Task.FromResult(new EkycFaceComparison { Score = 1.0, Matched = true });
    }

    public Task<EkycLivenessState> StartLivenessAsync(CancellationToken ct)
    {
        return Task.FromResult(PassedState(TokenPrefix + Guid.NewGuid().ToString("N")));
    }

    public Task<EkycLivenessState> SendLivenessFrameAsync(
        string sessionId, byte[] frame, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(PassedState(sessionId));
    }

    public Task<byte[]> GetLivenessSelfieAsync(string sessionId, CancellationToken ct)
    {
        return Task.FromResult(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
    }

    public Task DeleteLivenessAsync(string sessionId, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(string documentToken, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<EkycFaceEmbedding> GetFaceEmbeddingAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        // Mọi vector giả giống hệt nhau nên so với nhau luôn ra độ giống 1.
        float[] values = new float[EmbeddingSize];
        float value = 1f / MathF.Sqrt(EmbeddingSize);
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = value;
        }
        return Task.FromResult(new EkycFaceEmbedding { ModelName = "paused", Values = values });
    }

    private static EkycLivenessState PassedState(string sessionId)
    {
        return new EkycLivenessState
        {
            SessionId = sessionId,
            ExpiresInSeconds = 300,
            Calibrated = true,
            ChallengeCompleted = true,
            PadPassed = true,
            Passed = true,
            Prompt = "eKYC đang tạm ngừng: mặc định đạt.",
        };
    }

    private static string BuildCitizenId(Guid id)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("paused-ekyc:" + id.ToString("N")));
        StringBuilder digits = new StringBuilder("079");
        for (int i = 0; digits.Length < 12; i++)
        {
            digits.Append((hash[i] % 10).ToString());
        }
        return digits.ToString();
    }
}
