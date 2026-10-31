using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Ekyc;

/// <summary>
/// Gọi dịch vụ eKYC bằng HTTP (ekyc-prototype: /api/ocr, /api/anti-spoof, /api/face-match, /api/documents, /api/liveness).
/// </summary>
/// <remarks>
/// - Đọc JSON bằng JsonDocument theo tên trường của dịch vụ, không dựng DTO để tránh lệ thuộc cấu trúc nội bộ.
/// - Không log và không đưa vào thông báo lỗi nội dung ảnh, số giấy tờ hay họ tên.
/// - Thông báo 422/410 của dịch vụ là câu tiếng Việt hướng dẫn người dùng, được chuyển nguyên văn (cắt 300 ký tự).
/// </remarks>
public sealed class EkycHttpClient : IEkycClient
{
    private const int MaxDetailLength = 300;
    private const string UnavailableMessage = "Dịch vụ xác minh danh tính tạm thời không khả dụng. Vui lòng thử lại sau.";

    private readonly HttpClient httpClient;
    private readonly ILogger<EkycHttpClient>? logger;

    public EkycHttpClient(HttpClient httpClient, ILogger<EkycHttpClient>? logger = null)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<EkycDocumentReading> ReadFrontAsync(
        byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "front.jpg" : Path.GetFileName(fileName);
            safeFileName = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(safeFileName)).Replace("?", "x");
            if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName == ".jpg") safeFileName = "front.jpg";

            form.Add(CreateImageContent(image, contentType), "file", safeFileName);
            form.Add(new StringContent("front"), "side");

            JsonElement root = await PostAsync("api/ocr", form, ct);

            // /api/ocr trả chữ đọc được trong object "fields"; mã tham chiếu và cảnh báo nằm ở cấp gốc.
            JsonElement fields = root;
            if (root.TryGetProperty("fields", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object)
            {
                fields = nested;
            }

            EkycDocumentReading reading = new EkycDocumentReading();
            reading.IdNumber = ReadString(fields, "id_number");
            reading.FullName = ReadString(fields, "full_name");
            reading.DateOfBirth = ReadString(fields, "date_of_birth");
            reading.DateOfExpiry = ReadString(fields, "date_of_expiry");
            reading.ResidenceAddress = ReadString(fields, "residence_address");
            if (root.TryGetProperty("document_token", out JsonElement token)
                && token.ValueKind == JsonValueKind.String)
            {
                reading.DocumentToken = token.GetString();
            }
            if (root.TryGetProperty("warnings", out JsonElement warnings)
                && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement warning in warnings.EnumerateArray())
                {
                    if (warning.ValueKind == JsonValueKind.String)
                    {
                        reading.Warnings.Add(warning.GetString() ?? string.Empty);
                    }
                }
            }
            return reading;
        }
    }

    public async Task<EkycBackReading> ReadBackAsync(
        byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "back.jpg" : Path.GetFileName(fileName);
            safeFileName = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(safeFileName)).Replace("?", "x");
            if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName == ".jpg") safeFileName = "back.jpg";

            form.Add(CreateImageContent(image, contentType), "file", safeFileName);
            form.Add(new StringContent("back"), "side");

            JsonElement root = await PostAsync("api/ocr", form, ct);

            JsonElement fields = root;
            if (root.TryGetProperty("fields", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object)
            {
                fields = nested;
            }

            // Số căn cước từ MRZ để so với mặt trước. Nơi cư trú mặt sau chỉ được chuyển tiếp cho người dùng đối
            // chiếu trong phản hồi (thẻ mới in địa chỉ ở mặt sau); không lưu và không ghi log.
            EkycBackReading reading = new EkycBackReading();
            reading.IdNumber = ReadString(fields, "id_number");
            reading.ResidenceAddress = ReadString(fields, "residence_address");
            if (root.TryGetProperty("warnings", out JsonElement warnings)
                && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement warning in warnings.EnumerateArray())
                {
                    if (warning.ValueKind == JsonValueKind.String)
                    {
                        reading.Warnings.Add(warning.GetString() ?? string.Empty);
                    }
                }
            }
            return reading;
        }
    }

    public async Task<EkycSpoofCheck> CheckSpoofAsync(
        byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            form.Add(CreateImageContent(image, contentType), "file", fileName);

            JsonElement root = await PostAsync("api/anti-spoof", form, ct);

            EkycSpoofCheck result = new EkycSpoofCheck();
            result.Passed = ReadBoolean(root, "passed");
            result.LiveScore = ReadDouble(root, "live_score");
            return result;
        }
    }

    public async Task<EkycFaceComparison> CompareFaceAsync(
        string documentToken, byte[] selfie, string fileName, string contentType, double threshold, CancellationToken ct)
    {
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            form.Add(CreateImageContent(selfie, contentType), "selfie", fileName);
            form.Add(new StringContent(documentToken), "document_token");
            form.Add(new StringContent(threshold.ToString(CultureInfo.InvariantCulture)), "threshold");

            JsonElement root = await PostAsync("api/face-match", form, ct);

            EkycFaceComparison result = new EkycFaceComparison();
            result.Score = ReadDouble(root, "score");
            result.Matched = ReadBoolean(root, "matched");
            if (root.TryGetProperty("reference_crop", out JsonElement refCrop) && refCrop.ValueKind == JsonValueKind.String)
            {
                result.ReferenceCrop = refCrop.GetString();
            }
            if (root.TryGetProperty("selfie_crop", out JsonElement selfCrop) && selfCrop.ValueKind == JsonValueKind.String)
            {
                result.SelfieCrop = selfCrop.GetString();
            }
            return result;
        }
    }

    public async Task<EkycFaceEmbedding> GetFaceEmbeddingAsync(
        byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            form.Add(CreateImageContent(image, contentType), "selfie", fileName);

            JsonElement root = await PostAsync("api/face-embedding", form, ct);

            // Chỉ nhận đúng vector 128 số thực hữu hạn; sai dạng thì coi như dịch vụ trả phản hồi lạ.
            if (!root.TryGetProperty("embedding", out JsonElement array) || array.ValueKind != JsonValueKind.Array
                || array.GetArrayLength() != EkycRules.FaceEmbeddingLength)
            {
                throw Unavailable();
            }

            float[] values = new float[EkycRules.FaceEmbeddingLength];
            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number)
                {
                    throw Unavailable();
                }
                float value = (float)item.GetDouble();
                if (!float.IsFinite(value))
                {
                    throw Unavailable();
                }
                values[index] = value;
                index++;
            }

            EkycFaceEmbedding embedding = new EkycFaceEmbedding();
            embedding.ModelName = ReadString(root, "model");
            embedding.Values = values;
            if (embedding.ModelName.Length == 0)
            {
                throw Unavailable();
            }
            return embedding;
        }
    }

    public async Task DeleteDocumentAsync(string documentToken, CancellationToken ct)
    {
        EnsureConfigured();
        try
        {
            using (HttpResponseMessage response = await httpClient.DeleteAsync(
                "api/documents/" + Uri.EscapeDataString(documentToken), ct))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw Unavailable();
                }
            }
        }
        catch (HttpRequestException)
        {
            throw Unavailable();
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    public async Task<EkycLivenessState> StartLivenessAsync(CancellationToken ct)
    {
        using (ByteArrayContent empty = new ByteArrayContent(Array.Empty<byte>()))
        {
            JsonElement root = await PostAsync("api/liveness/start", empty, ct, true);
            return ReadLivenessState(root);
        }
    }

    public async Task<EkycLivenessState> SendLivenessFrameAsync(
        string sessionId, byte[] frame, string fileName, string contentType, CancellationToken ct)
    {
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(sessionId), "session_id");
            form.Add(CreateImageContent(frame, contentType), "frame", fileName);

            JsonElement root = await PostAsync("api/liveness/frame", form, ct, true);
            EkycLivenessState state = ReadLivenessState(root);
            state.SessionId = sessionId;
            return state;
        }
    }

    public async Task<byte[]> GetLivenessSelfieAsync(string sessionId, CancellationToken ct)
    {
        EnsureConfigured();
        try
        {
            using (HttpResponseMessage response = await httpClient.GetAsync(
                "api/liveness/" + Uri.EscapeDataString(sessionId) + "/selfie?allow_captured=true", ct))
            {
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync(ct);
                }
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    throw new AppException(AppErrorType.Conflict, ErrorCode.EkycLivenessNotPassed,
                        "Bạn chưa hoàn thành thử thách camera.");
                }
                if (response.StatusCode == HttpStatusCode.Gone)
                {
                    throw LivenessExpired();
                }
                throw Unavailable();
            }
        }
        catch (HttpRequestException)
        {
            throw Unavailable();
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    public async Task DeleteLivenessAsync(string sessionId, CancellationToken ct)
    {
        EnsureConfigured();
        try
        {
            using (HttpResponseMessage response = await httpClient.DeleteAsync(
                "api/liveness/" + Uri.EscapeDataString(sessionId), ct))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw Unavailable();
                }
            }
        }
        catch (HttpRequestException)
        {
            throw Unavailable();
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    /// <summary>
    /// Đọc JSON phiên thử thách của dịch vụ. Cố ý bỏ điểm chống giả mạo (pad_score, pad_samples): không chuyển cho FE.
    /// </summary>
    private static EkycLivenessState ReadLivenessState(JsonElement root)
    {
        EkycLivenessState state = new EkycLivenessState();
        state.SessionId = ReadString(root, "session_id");
        state.ExpiresInSeconds = ReadInt(root, "expires_in");
        state.CurrentIndex = ReadInt(root, "current_index");
        state.Prompt = ReadString(root, "prompt");
        state.Calibrated = ReadBoolean(root, "calibrated");
        state.ChallengeCompleted = ReadBoolean(root, "challenge_completed");
        state.PadPassed = ReadBoolean(root, "pad_passed");
        state.Passed = ReadBoolean(root, "prototype_passed");
        state.FailedReason = ReadString(root, "failed_reason");
        state.FrameHint = ReadString(root, "frame_hint");

        if (root.TryGetProperty("actions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in actions.EnumerateArray())
            {
                EkycLivenessAction action = new EkycLivenessAction();
                action.Key = ReadString(item, "key");
                action.Label = ReadString(item, "label");
                state.Actions.Add(action);
            }
        }
        if (root.TryGetProperty("completed", out JsonElement completed) && completed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in completed.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    state.Completed.Add(item.GetString() ?? string.Empty);
                }
            }
        }
        return state;
    }

    private static int ReadInt(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number))
        {
            return number;
        }
        return 0;
    }

    private static AppException LivenessExpired()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.EkycLivenessRequired,
            "Phiên thử thách camera đã hết hạn hoặc chưa bắt đầu. Vui lòng bắt đầu lại.");
    }

    private static ByteArrayContent CreateImageContent(byte[] image, string contentType)
    {
        ByteArrayContent content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    /// <summary>POST multipart, đổi mã lỗi của dịch vụ thành AppException, trả JSON thành công.</summary>
    private async Task<JsonElement> PostAsync(
        string path, HttpContent form, CancellationToken ct, bool livenessSession = false)
    {
        EnsureConfigured();
        try
        {
            logger?.LogInformation("eKYC: Đang gọi {BaseAddress}{Path} (Timeout {Timeout}s)...",
                httpClient.BaseAddress, path, httpClient.Timeout.TotalSeconds);

            using (HttpResponseMessage response = await httpClient.PostAsync(path, form, ct))
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    using (JsonDocument document = JsonDocument.Parse(body))
                    {
                        // Clone để JsonElement còn dùng được sau khi document bị giải phóng.
                        return document.RootElement.Clone();
                    }
                }
                logger?.LogWarning("eKYC: Dịch vụ trả HTTP {StatusCode} cho {Path}: {Body}", response.StatusCode, path, body);
                if (response.StatusCode == HttpStatusCode.UnprocessableEntity || response.StatusCode == HttpStatusCode.BadRequest)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.EkycImageRejected,
                        ReadDetail(body, "Ảnh không dùng được. Vui lòng chụp lại rõ hơn."));
                }
                if (response.StatusCode == HttpStatusCode.Gone && livenessSession)
                {
                    throw LivenessExpired();
                }
                if (response.StatusCode == HttpStatusCode.Gone)
                {
                    throw new AppException(AppErrorType.Conflict, ErrorCode.EkycDocumentExpired,
                        "Vui lòng đọc lại mặt trước giấy tờ trước khi gửi ảnh chân dung.");
                }
                throw Unavailable();
            }
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "eKYC: Lỗi kết nối HTTP tới {BaseAddress}{Path}: {Message}", httpClient.BaseAddress, path, ex.Message);
            throw Unavailable();
        }
        catch (JsonException ex)
        {
            logger?.LogError(ex, "eKYC: Lỗi phân tích JSON từ {Path}", path);
            throw Unavailable();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogError(ex, "eKYC: Quá thời gian chờ (Timeout {Timeout}s) khi gọi {BaseAddress}{Path}", httpClient.Timeout.TotalSeconds, httpClient.BaseAddress, path);
            // HttpClient hết thời gian chờ (không phải người dùng hủy request).
            throw Unavailable();
        }
    }

    /// <summary>Chưa cấu hình Ekyc:BaseUrl thì không có địa chỉ để gọi → 503.</summary>
    private void EnsureConfigured()
    {
        if (httpClient.BaseAddress == null)
        {
            logger?.LogError("eKYC: httpClient.BaseAddress rỗng hoặc chưa được cấu hình.");
            throw Unavailable();
        }
    }

    private static AppException Unavailable()
    {
        return new AppException(AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable, UnavailableMessage);
    }

    /// <summary>Lấy chuỗi "detail" của dịch vụ; detail dạng khác (lỗi validate của FastAPI) thì dùng câu mặc định.</summary>
    private static string ReadDetail(string body, string fallback)
    {
        try
        {
            using (JsonDocument document = JsonDocument.Parse(body))
            {
                if (document.RootElement.TryGetProperty("detail", out JsonElement detail)
                    && detail.ValueKind == JsonValueKind.String)
                {
                    string text = detail.GetString() ?? string.Empty;
                    if (text.Length > 0 && text.Length <= MaxDetailLength)
                    {
                        return text;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return fallback;
        }
        return fallback;
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static bool ReadBoolean(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        return false;
    }

    private static double ReadDouble(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }
        return 0;
    }
}
