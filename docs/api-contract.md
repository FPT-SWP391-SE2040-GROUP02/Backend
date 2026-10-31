# Hợp đồng API — Đăng nhập và Tạo kho

> Cập nhật Auth: 15/10/2026 — mục 3 và 6.1–6.3 đã đối chiếu controller, DTO và service hiện có. Mục 6.4 là contract dự kiến, chưa có controller/implementation. Các module khác giữ trạng thái đối soát riêng; không suy ra toàn bộ tài liệu đã triển khai.
> Căn cứ: SRS v3.14.0; `AuthController`, `GoogleAuthController`, DTO và services trong Backend; FE `features/auth/api/authService.ts`.
> Đổi hợp đồng thì sửa file này trong cùng PR và báo FE.

Swagger Auth được cập nhật trực tiếp từ metadata của `AuthController`, `GoogleAuthController` và `AuthOpenApiConfig`: nhóm Auth, summary/description tiếng Việt, request DTO, typed response, CSRF header, Bearer và hai ví dụ response Google. Chỉ thay metadata, không đổi route/logic auth/DB. Swagger chỉ hiển thị 10 endpoint `/api/v1/auth/...`, ẩn alias `/v1/auth/...` bị lặp; alias runtime vẫn giữ để tương thích. Dùng prefix canonical cho cookie như mục 3. Automated check `AuthOpenApiTests` đọc `/openapi/v1.json` thật, kiểm tra 10 endpoint canonical, schema, response không body, CSRF và security optional/required; chạy cùng `AuthHttpSecurityTests` đã pass 10 tests. Khi host đang chạy bản cũ, restart Backend rồi tải lại `/swagger`.

---

## 1. Quy ước chung

| Mục | Quy ước |
|---|---|
| Base URL | FE: `VITE_API_BASE_URL=http://localhost:5000/api/v1`. Gọi relative path `/auth/login` để tạo URL `/api/v1/auth/login`; không ghép lặp `/api/v1`. Các ví dụ `/v1/...` cũ là alias, không dùng cho auth có cookie |
| Định dạng | JSON, tên trường `camelCase` |
| Response thành công | Trả **thẳng object/mảng**, **không bọc** `{ success, data }` (axiosClient của FE đã trả `response.data`; baseService coi đó là entity). Type `ApiResponse<T>` ở FE không dùng cho API này |
| Id | Guid dạng chuỗi, ví dụ `"0199e7a4-5c1b-7c3e-9a51-3f0f5d2b8a10"` |
| Thời gian | ISO 8601 UTC, ví dụ `"2026-10-13T08:30:00+00:00"`. FE tự hiển thị theo giờ Việt Nam (TIME-01) |
| Enum/trạng thái | Chuỗi `UPPER_SNAKE_CASE`, ví dụ `"PAID_ACTIVE"` |
| Cập nhật | `PUT` (khớp `baseService.update`). Body có trường `etag` lấy từ lần đọc gần nhất (chống ghi đè, xem mục 4) |
| Xóa/lưu trữ | `DELETE` không body (khớp `baseService.remove`); tham số phụ đi qua query string |
| Danh sách | `GET` có phân trang, trả `PaginatedList<T>` (mục 5) |
| Phân quyền | Không bao giờ gửi `userId`/`ownerId` trong body/query: BE lấy người dùng từ token (SEC-04) |

---

## 2. Lỗi

Lỗi ứng dụng và validation DTO trả **ProblemDetails** (RFC 9457) kèm `code` để FE so sánh. Lỗi antiforgery và rate limiter do framework xử lý có thể không có `code`; FE phải xử lý HTTP status riêng:

```json
{
  "status": 401,
  "title": "Email hoặc mật khẩu chưa đúng.",
  "code": "INVALID_CREDENTIALS",
  "attemptsRemaining": 3,
  "traceId": "00-4bf92f..."
}
```

- **FE dựa vào `code`**, không dựa vào `title` (title là câu hiển thị, có thể đổi).
- Một số lỗi có trường phụ ở cấp cao nhất (`attemptsRemaining`, `remainingSeconds`, `reason`, `maxContainers`, `containerId`, `method`).
- Lỗi validate (`400 VALIDATION_ERROR`) có `errors` dạng **object theo tên trường**, khác `errors: string[]` trong `ApiResponse` của FE:

```json
{
  "status": 400,
  "title": "Dữ liệu gửi lên không hợp lệ.",
  "code": "VALIDATION_ERROR",
  "errors": { "email": ["Email không hợp lệ."], "password": ["Mật khẩu phải có ít nhất 8 ký tự."] }
}
```

- `500 INTERNAL_ERROR` có `status`, `title` chung, `code`, `traceId`; không trả exception/stack trace. Gửi `traceId` cho BE khi báo lỗi.
- Global exception handler giữ `application/problem+json` cả ở nhánh ghi JSON dự phòng khi Accept không được writer hỗ trợ. `BadHttpRequestException` với status 400 trả `VALIDATION_ERROR`, status 413 trả `FILE_TOO_LARGE`; không đưa thông báo exception request vào response.
- Auth CSRF: **400** khi proof thiếu/sai/khác principal. Rate limit: **429**, `Retry-After` tính bằng giây khi server cấp được thời gian chờ; không giả định response có JSON/code và không tự retry POST.

### Bảng mã lỗi

| HTTP | `code` | Khi nào | Trường phụ |
|---|---|---|---|
| 400 | `VALIDATION_ERROR` | Thiếu/sai định dạng | `errors` |
| 400 | `INVALID_VERIFICATION_TOKEN` | Link xác minh email sai/hết hạn/đã dùng | |
| 400 | `INVALID_LINK_TICKET` | Vé liên kết Google sai/hết hạn/đã dùng | |
| 401 | `INVALID_CREDENTIALS` | Sai email/mật khẩu | `attemptsRemaining` |
| 401 | `SESSION_EXPIRED` | Access token hết hạn/không hợp lệ, hoặc refresh thất bại | |
| 401 | `INVALID_GOOGLE_TOKEN` | Google ID token không hợp lệ | |
| 401 | `REAUTHENTICATION_FAILED` | Xác thực lại thất bại khi đổi phương thức đăng nhập (**không** đá về trang login) | |
| 403 | `ACCOUNT_NOT_ACTIVE` | Tài khoản bị khóa/vô hiệu | |
| 403 | `EMAIL_NOT_VERIFIED` | Tạo kho khi email chưa xác minh | |
| 403 | `ROLE_NOT_ALLOWED` | Gọi API Verifier khi tài khoản (đọc từ DB, không tin claim trong token) không phải Verifier | |
| 403 | `PAID_TIER_REQUIRED` | Hạng Free đưa tài sản vào gói, tạo hồ sơ người nhận hoặc gắn người nhận vào gói | |
| 404 | `VAULT_NOT_FOUND` | Chưa có kho, hoặc kho của người khác | |
| 404 | `CONTAINER_NOT_FOUND` | Không có gói, hoặc gói của kho khác | |
| 404 | `ASSET_NOT_FOUND` | Tài sản không thuộc kho | |
| 404 | `AVATAR_NOT_FOUND` | Tài khoản chưa có ảnh đại diện | |
| 404 | `IDENTITY_IMAGE_NOT_FOUND` | Verifier xem ảnh CCCD lúc eKYC mà Owner xác minh trước khi có chức năng lưu ảnh, hoặc xem selfie báo còn sống khi chưa có lần thử nào được so khớp | |
| 409 | `ASSET_IN_USE` | Tài sản còn trong gói hoặc đã ghim vào bàn giao nên không xóa được (+`reason`) | |
| 404 | `BENEFICIARY_NOT_FOUND` | Không có hồ sơ người nhận, hoặc hồ sơ của kho khác | |
| 409 | `STORAGE_QUOTA_EXCEEDED` | Tạo tài sản vượt dung lượng còn lại của kho | `storageQuotaBytes`, `storageRemainingBytes` |
| 413 | `FILE_TOO_LARGE` | Request tải lên vượt giới hạn server (500 MiB) | |
| 409 | `EDIT_CONFLICT` | `etag` đã cũ: dữ liệu bị sửa ở tab/máy khác. FE tải lại rồi cho sửa tiếp | |
| 409 | `CHECKIN_NOT_ALLOWED` | Điểm danh khi kế hoạch chưa kích hoạt (`DRAFT`) hoặc đã sang chứng tử/bàn giao/đóng | `planStatus` |
| 400 | `EKYC_IMAGE_REJECTED` | Ảnh không phải JPEG/PNG, rỗng, hoặc dịch vụ eKYC từ chối (không thấy mặt, nhầm mặt thẻ...); `title` là câu hướng dẫn cho người dùng | |
| 409 | `EKYC_DOCUMENT_EXPIRED` | Gửi selfie khi chưa đọc mặt trước, đã so khớp rồi, hoặc quá 30 phút | |
| 503 | `EKYC_SERVICE_UNAVAILABLE` | Không gọi được dịch vụ eKYC (chưa cấu hình, tắt, quá thời gian chờ) | |
| 409 | `EKYC_CONSENT_REQUIRED` | Gửi mặt trước khi chưa đồng ý (`POST /ekyc/consent`), đồng ý đã quá 30 phút hoặc theo phiên bản thông báo cũ (EKYC-03) | |
| 400 | `EKYC_DOCUMENT_MISMATCH` | Mặt sau không cùng thẻ với mặt trước vừa đọc (EKYC-04); chụp lại mặt sau | |
| 409 | `EKYC_LIVENESS_REQUIRED` | Chưa bắt đầu thử thách camera (kể cả khi gửi ảnh selfie tĩnh), hoặc phiên thử thách đã hết hạn (90 giây); gọi lại `liveness/start` | |
| 409 | `EKYC_LIVENESS_NOT_PASSED` | Chốt thử thách, hoặc gửi ảnh selfie tĩnh, khi chưa làm xong các động tác hoặc chống giả mạo chưa đạt (EKYC-14); tiếp tục gửi khung hình | |
| 409 | `EKYC_IDENTITY_MISMATCH` | Owner đã gắn danh tính (EKYC-27) nhưng giấy tờ đăng ký lại (`OWNER_ENROLLMENT`) có số CCCD khác rõ ràng với danh tính đã gắn (EKYC-24); không có cách bỏ qua | |
| 404 | `DEATH_CASE_NOT_FOUND` | Không có hồ sơ chứng tử, hoặc người gọi không phải Executor đang hiệu lực của kho, hoặc là Owner của kho | |
| 409 | `DEATH_CASE_NOT_ALLOWED` | Thao tác hồ sơ chứng tử không được lúc này | `reason`: `PLAN_NOT_RUNNING` (kèm `planStatus`) / `VERSION_LOCKED` / `CASE_CLOSED` / `CASE_NOT_REVISABLE`; phía Verifier (mục 6.12): `NOT_UNDER_REVIEW` / `NEWER_VERSION_PENDING` / `OWNER_NOT_NOTIFIED` / `OBJECTION_PERIOD_NOT_OVER` / `FLAGS_NOT_REVIEWED` / `VERIFIER_CONFLICT` / `PLAN_STATE_INVALID` | `approveBlockers` (khi duyệt bị chặn) |
| 409 | `HANDOVER_NOT_ALLOWED` | Duyệt hồ sơ chứng tử nhưng không mở được bàn giao (không có gói `READY`, hoặc kế hoạch chưa sẵn sàng); hồ sơ vẫn đang xét | `reason`: `NOTHING_TO_HAND_OVER` / `PLAN_NOT_READY` |
| 409 | `DEATH_CASE_INCOMPLETE` | Gửi hồ sơ khi còn thiếu trường hoặc chưa có trang giấy nào, hoặc AI không thấy trang nào là giấy chứng tử | `missingRequirements`, `fileChecks` (chỉ khi `DEATH_CERTIFICATE_NOT_RECOGNIZED`) |
| 409 | `DEATH_CASE_FILE_LIMIT_EXCEEDED` | Bản hồ sơ đã đủ 10 trang giấy | `maxFiles` |
| 503 | `STORAGE_UNAVAILABLE` | Không ghi/đọc được kho lưu nội dung tài sản (R2 lỗi mạng, sai khóa). Object không còn trên R2 thì báo lỗi toàn vẹn như khi lưu trên đĩa, không dùng mã này | |
| 409 | `VAULT_NOT_EDITABLE` | Kho không cho sửa lúc này | `reason`: `VAULT_CLOSED` / `PLAN_LOCKED` / `SERVICE_READ_ONLY` |
| 409 | `CONTAINER_LIMIT_EXCEEDED` | Đủ 50 gói chưa bị xóa | `maxContainers` |
| 409 | `CONTAINER_REMOVED` | Sửa gói đã bị xóa | |
| 409 | `ASSET_ALREADY_IN_CONTAINER` | Tài sản đang ở gói khác | `containerId` |
| 409 | `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` | Thao tác làm kế hoạch không còn gói sẵn sàng; cần Owner xác nhận | |
| 409 | `BENEFICIARY_IDENTITY_CONFLICT` | Kho đã có hồ sơ cùng số CCCD nhưng khác họ tên hoặc ngày sinh | `beneficiaryId` |
| 409 | `BENEFICIARY_IN_USE` | Xóa hồ sơ người nhận đang ở gói; gỡ khỏi các gói trước | `containerIds` |
| 409 | `BENEFICIARY_SLOTS_EXCEEDED` | Gắn thêm một người nhận mới vượt số suất của kho | `usedSlots`, `maxSlots` |
| 409 | `GOOGLE_ACCOUNT_ALREADY_LINKED` | Google này đã gắn tài khoản khác / tài khoản đã có Google | |
| 409 | `LAST_LOGIN_METHOD` | Gỡ phương thức đăng nhập cuối cùng | |
| 409 | `LOGIN_METHOD_ALREADY_EXISTS` | Đã có phương thức này | `method` |
| 423 | `ACCOUNT_LOCKED` | Sai mật khẩu 5 lần trong 15 phút | `attemptsRemaining: 0`, `remainingSeconds` |
| 500 | `INTERNAL_ERROR` | Lỗi bất ngờ | `traceId` |
| 400 | `INVALID_RESET_TOKEN` | Link đặt lại mật khẩu sai, hết hạn, đã dùng hoặc tài khoản không còn hoạt động | |
| 409 | `PAYMENT_NOT_ALLOWED` | Báo giá hoặc tạo đơn thanh toán không được lúc này (mục 6.18) | `reason`: `VAULT_CLOSED` / `PLAN_LOCKED` / `OWNER_EKYC_REQUIRED` / `RENEWAL_NOT_SUPPORTED` / `USAGE_EXCEEDS_PLAN` |
| 409 | `PAYMENT_ORDER_PENDING` | Kho đang có đơn `PENDING` còn hạn với lựa chọn khác; hủy đơn cũ trước (PAY-04) | `orderId` |
| 409 | `PAYMENT_ORDER_NOT_PAYABLE` | Hủy hoặc ghi nhận thanh toán một đơn không còn ở trạng thái cho phép | `reason`: `PAID` / `EXPIRED` / `CANCELLED` / `VAULT_CLOSED` / `ENTITLEMENT_CHANGED` |
| 404 | `PAYMENT_ORDER_NOT_FOUND` | Không có đơn thanh toán, hoặc đơn của kho khác; cũng là kết quả của `dev-pay` khi công tắc dev tắt | |
| 409 | `DEV_TOOL_NOT_ALLOWED` | Công cụ thử nghiệm (chỉ Development): kho không có mốc này ở trạng thái hiện tại hoặc đã qua mốc | `milestone`, `state` |
| 409 | `VAULT_PIN_NOT_SET` | Kho đã có tài sản nhưng chưa đặt mã PIN (mục 6.20): chặn tạo thêm tài sản và các thao tác cần PIN | |
| 409 | `VAULT_PIN_ALREADY_SET` | Kho đã có PIN, không đặt lần đầu nữa; dùng đổi PIN | |
| 403 | `VAULT_PIN_REQUIRED` | Thao tác cần PIN nhưng thiếu header `X-Vault-Pin`; không tính là một lần đoán | |
| 403 | `INVALID_VAULT_PIN` | Sai mã PIN | `attemptsRemaining` |
| 423 | `VAULT_PIN_LOCKED` | PIN bị khóa tạm vì sai 5 lần liên tiếp; đang khóa thì PIN đúng cũng bị từ chối | `lockedUntil` |
| 400 | `VAULT_PIN_TOO_WEAK` | PIN quá dễ đoán (6 số giống nhau, dãy tăng hoặc giảm liên tiếp) | |
| 409 | `VAULT_PIN_RESET_NEEDS_PASSWORD` | Quên PIN nhưng tài khoản không có mật khẩu (chỉ đăng nhập bằng Google) | |
| 403 | `SIGNING_PASSWORD_INVALID` | Verifier Duyệt/Từ chối hồ sơ chứng tử nhập sai mật khẩu xác nhận ký, hoặc tài khoản chưa có mật khẩu (mục 6.12, DEATH-14) | `attemptsRemaining` (không có khi tài khoản chưa có mật khẩu) |
| 423 | `SIGNING_LOCKED` | Chức năng ký số của Verifier bị khóa tạm vì sai mật khẩu 5 lần liên tiếp; đang khóa thì mật khẩu đúng cũng bị từ chối | `lockedUntil` |

---

## 3. Xác thực (mô hình lai)

| | Access token | Refresh token |
|---|---|---|
| Nằm ở | **JSON body** (`accessToken`) | **Cookie HttpOnly** `lv_refresh` |
| FE lưu | Chỉ RAM; interceptor gửi `Authorization: Bearer`. Cấm Web Storage | Không lưu, JS không đọc được |
| Sống | Theo `expiresIn` (giây); `Jwt:AccessTokenMinutes` mặc định 15 phút | Idle 30 phút từ lần cấp/refresh thành công, tối đa 12 giờ từ lúc đăng nhập (NFR-06); không suy ra hạn từ hoạt động UI/API thông thường |
| Cookie | — | `HttpOnly; SameSite=Lax; Path=/api/v1/auth`; `Secure` ngoài môi trường dev |

**Việc FE cần làm:**

1. Các request tới `/api/v1/auth/*` gửi kèm `withCredentials: true` để trình duyệt gửi/nhận cookie `lv_refresh`. Các API khác không cần.
2. Cấp proof mới bằng `GET /api/v1/auth/csrf` (raw `{ "requestToken": "<opaque-proof>" }`, `Cache-Control: no-store`), gửi `X-CSRF-Token` cùng cookie HttpOnly `lv_antiforgery` (`Path=/api/v1`, `SameSite=Lax`, Secure ngoài Development) trước login/refresh/logout/Google/link-confirm. Proof không phải access JWT. Dùng cùng Bearer principal ở GET và POST; lấy lại sau đổi danh tính, không gửi POST khi acquisition lỗi. Register/verify-email/resend-verification hiện không gắn filter antiforgery hay policy rate limit riêng này.
3. Đăng xuất: gọi `POST /api/v1/auth/logout`; chỉ xóa RAM/cache và báo hoàn tất sau server success. Logout-all cần bearer principal active hoặc refresh token active, không chấp nhận cookie thuộc user khác. Thu hồi refresh không tự vô hiệu access JWT đang còn hạn.
4. Không đưa token vào URL/query hoặc log (SEC-06).

BE bật CORS credentials cho origins cấu hình rõ. Alias `/v1` còn tồn tại nhưng cookie Path dùng `/api/v1/auth`; dùng prefix `/api/v1` cho luồng có cookie. Refresh tự động chỉ bật sau checks phối hợp single-flight, nhiều tab và logout/login đang bay; không tự retry mutation.

Refresh xoay token: token cũ bị thu hồi; gửi lại token cũ làm thu hồi cả family và trả `401 SESSION_EXPIRED`. Client phải phối hợp refresh giữa các request/tab, không gọi song song hoặc retry mù. Khi reload, access token RAM đã mất: lấy CSRF → refresh bằng cookie → lưu `accessToken` RAM và dùng `user` trong response. `GET /session` cần Bearer, không khôi phục phiên chỉ bằng refresh cookie. Logo/chuyển trang dùng điều hướng nội bộ, không gọi logout hoặc xóa phiên/cache.

---

## 4. Chống ghi đè (etag)

- `VaultResponse` và `ContainerResponse` có trường `etag`.
- Khi `PUT`, gửi lại `etag` đó trong body. Nếu ai khác đã sửa trước → `409 EDIT_CONFLICT`; FE tải lại dữ liệu rồi cho người dùng sửa trên bản mới.
- Sau mỗi lần sửa thành công, dùng `etag` mới trong response cho lần sửa tiếp theo.

---

## 5. Phân trang

Query (khớp `PaginationParams` của FE):

| Tham số | Mặc định | Ràng buộc |
|---|---|---|
| `pageIndex` | 1 | ≥ 1 |
| `pageSize` | 10 | 1–100 |
| `searchTerm` | — | ≤ 200 ký tự |
| `sortBy` | tùy endpoint | Chỉ nhận cột endpoint cho phép, khác → 400 |
| `isAscending` | `true` | |

Response (khớp `PaginatedList<T>` của FE):

```json
{
  "items": [],
  "totalCount": 0,
  "pageIndex": 1,
  "pageSize": 10,
  "totalPages": 0,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

---

## 6. Endpoint

Cột **Quyền**: `Công khai` = không cần token; `Đăng nhập` = cần Bearer token.

### 6.1. Đăng ký và xác minh email

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| POST | `/api/v1/auth/register` | Công khai | `RegisterRequest` | **202**, không body | 400 `VALIDATION_ERROR` |
| POST | `/api/v1/auth/verify-email` | Công khai | `{ token }` | 200 `UserResponse` | 400 `VALIDATION_ERROR` / `INVALID_VERIFICATION_TOKEN` |
| POST | `/api/v1/auth/resend-verification` | Công khai | `{ email }` | **202**, không body | 400 `VALIDATION_ERROR` |

- Với DTO hợp lệ và xử lý thành công, `register` trả 202 kể cả email đã tồn tại; `resend-verification` trả 202 cả khi email không tồn tại/đã xác minh. Đây là response chống dò tài khoản, không bảo đảm email đã được giao; lỗi validation/hạ tầng vẫn có thể thất bại. FE hiện: "Nếu email hợp lệ, bạn sẽ nhận được link xác minh".
- Link trong email có dạng `<FE>/verify-email?token=...`; trang FE đọc `token` rồi gọi `verify-email`.
- Token xác minh tối đa 256 ký tự, sống 24 giờ, dùng một lần. `UserResponse` sau xác minh **không** có access token hoặc refresh cookie mới, không tự cấp phiên.
- Register: email hợp lệ ≤256 ký tự; password 8–128; confirmPassword phải khớp; fullName bắt buộc ≤200. Resend nhận email hợp lệ ≤256. Không gửi role/userId hoặc tự coi Google name là định danh đã xác minh.

```json
// RegisterRequest
{ "email": "an@gmail.com", "password": "Matkhau123!", "confirmPassword": "Matkhau123!", "fullName": "Nguyễn Văn An" }
```

### 6.2. Đăng nhập, phiên, đăng xuất

Login và Google/link-confirm dùng chung policy 5 request/phút/IP; refresh và logout có policy riêng, mỗi policy 30/phút/IP (fixed window, không xếp hàng). Limiter dùng `Connection.RemoteIpAddress`, không parse `X-Forwarded-For` tự gửi. Khóa mật khẩu là cơ chế riêng theo email + dải IP: 5 lần sai trong 15 phút → khóa 15 phút. Login/refresh/logout có xử lý Serializable; không suy rộng bảo đảm transaction này sang Google/link-confirm.

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/api/v1/auth/csrf` | Công khai; Bearer nếu đang có principal | — | 200 `{ requestToken }` + cookie antiforgery | Lỗi hạ tầng |
| POST | `/api/v1/auth/login` | Công khai + CSRF | `LoginRequest` | 200 `AuthSessionResponse` + Set-Cookie `lv_refresh` | 400, 401 `INVALID_CREDENTIALS`, 423 `ACCOUNT_LOCKED`, 403 `ACCOUNT_NOT_ACTIVE`, 429 |
| POST | `/api/v1/auth/refresh` | Cookie `lv_refresh` + CSRF | Không body | 200 `AuthSessionResponse` + cookie mới | 400, 401 `SESSION_EXPIRED` (cookie bị xóa), 429 |
| POST | `/api/v1/auth/logout` | CSRF; logout-all cần bằng chứng phiên | `{ revokeAllDevices? }` hoặc không body | **204**, không body + xóa cookie | 400, 401 `SESSION_EXPIRED` khi logout-all thiếu quyền, 429 |
| GET | `/api/v1/auth/session` | Bearer | — | 200 `UserResponse` | 401 `SESSION_EXPIRED` (gồm user không còn active) |

```json
// LoginRequest
{ "email": "an@gmail.com", "password": "Matkhau123!", "rememberMe": true }

// AuthSessionResponse
{
  "accessToken": "eyJhbGciOi...",
  "expiresIn": 900,
  "user": {
    "id": "0199e7a4-...",
    "email": "an@gmail.com",
    "fullName": "Nguyễn Văn An",
    "phone": null,
    "role": "USER",
    "status": "ACTIVE",
    "emailVerified": true,
    "hasPassword": true,
    "hasAvatar": false,
    "avatarUpdatedAt": null,
    "createdAt": "2026-10-13T08:30:00+00:00"
  }
}
```

- `role`: `USER` | `VERIFIER` | `ADMIN`. Owner/Executor/Beneficiary **không phải role**, là quan hệ với từng kho.
- `status`: `ACTIVE` | `LOCKED` | `DISABLED`.
- `emailVerified = false` → FE hiện nhắc xác minh và nút gửi lại link; vẫn đăng nhập được nhưng chưa tạo được kho.
- `rememberMe = true` thì cookie sống qua khi tắt trình duyệt, nhưng phiên vẫn tối đa 12 giờ.
- Login chỉ nhận email hợp lệ ≤256 ký tự, password bắt buộc ≤128, rememberMe boolean mặc định false. `AuthSessionResponse` là raw body; `user` bên trong chính là `UserResponse`. Không có `refreshToken` trong JSON. `expiresIn: 900` ở ví dụ là cấu hình mặc định, không phải giá trị client được hardcode.

### 6.2a. Xác thực hai bước TOTP (Admin)

Tự nguyện bật cho tài khoản Admin. Chưa bật thì đăng nhập như cũ. Khi đã bật, mọi đăng nhập Admin (mật khẩu, Google, link-confirm) **chưa cấp phiên**: server trả thử thách, FE hỏi mã 6 số (hoặc mã khôi phục) rồi gọi `verify`. Bí mật TOTP lưu đã bọc bằng KEK; mã khôi phục và token thử thách chỉ lưu hash. Kiểm thử bằng fake (`TwoFactorServiceTests`), chưa E2E với ứng dụng xác thực thật.

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/api/v1/auth/2fa/status` | Admin | — | 200 `{ enabled, setupPending, recoveryCodesRemaining }` | 401, 403 `TWO_FACTOR_ADMIN_ONLY` |
| POST | `/api/v1/auth/2fa/setup` | Admin | — | 200 `{ secret, otpAuthUri }` | 403, 409 `TWO_FACTOR_ALREADY_ENABLED`, 429 |
| POST | `/api/v1/auth/2fa/enable` | Admin | `{ code }` mã TOTP đầu tiên | 200 `{ recoveryCodes[8] }` (hiển thị một lần) | 401 `INVALID_TWO_FACTOR_CODE`, 409 `TWO_FACTOR_SETUP_REQUIRED` / `TWO_FACTOR_ALREADY_ENABLED`, 429 |
| POST | `/api/v1/auth/2fa/disable` | Admin | `{ code }` TOTP hoặc mã khôi phục | 204 | 401 `INVALID_TWO_FACTOR_CODE`, 409 `TWO_FACTOR_NOT_ENABLED`, 429 |
| POST | `/api/v1/auth/2fa/verify` | Công khai + CSRF | `{ challengeToken, code }` | 200 `AuthSessionResponse` + cookie `lv_refresh` | 401 `INVALID_TWO_FACTOR_CODE` / `TWO_FACTOR_CHALLENGE_INVALID`, 429 |

```jsonc
// POST /auth/login (và POST /auth/google, /auth/google/link-confirm) khi Admin đã bật 2FA → 200, KHÔNG có cookie
{ "twoFactorRequired": true, "challengeToken": "opaque", "expiresIn": 300 }
// /auth/google trả thêm dạng phẳng: { signedIn: false, twoFactorRequired: true, challengeToken, challengeExpiresIn }
```

- Thử thách sống 5 phút, dùng một lần, sai 5 lần thì bị hủy (phải đăng nhập lại). Mỗi mã TOTP chỉ dùng được một lần; chấp nhận lệch ±1 bước 30 giây.
- Mã khôi phục dạng `XXXXXX-XXXXXX`, dùng một lần, không phân biệt hoa thường. Mất cả ứng dụng lẫn mã khôi phục cần Admin khác xử lý ngoài API (chưa có endpoint đặt lại 2FA).
- `INVALID_TWO_FACTOR_CODE` là 401 nhưng với `setup/enable/disable` phiên vẫn còn: FE không coi là hết phiên.

### 6.3. Đăng nhập Google

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| POST | `/api/v1/auth/google` | Công khai + CSRF | `GoogleLoginRequest` | 200 `GoogleLoginResponse` (2 nhánh, xem dưới) | 400 validation/CSRF, 401 `INVALID_GOOGLE_TOKEN`, 403 `ACCOUNT_NOT_ACTIVE`, 429 |
| POST | `/api/v1/auth/google/link-confirm` | Công khai + CSRF | `ConfirmGoogleLinkRequest` | 200 `AuthSessionResponse` + cookie | 400 validation/CSRF/`INVALID_LINK_TICKET`, 401 `INVALID_CREDENTIALS`, 423 `ACCOUNT_LOCKED`, 403 `ACCOUNT_NOT_ACTIVE`, 409 `GOOGLE_ACCOUNT_ALREADY_LINKED`, 429 |

```json
// GoogleLoginRequest — idToken lấy từ Google Identity Services
{ "idToken": "eyJhbGciOi...", "nonce": "random-from-fe", "rememberMe": true, "returnUrl": "/invitations/abc" }

// ConfirmGoogleLinkRequest — người dùng nhập mật khẩu tài khoản app
{ "linkTicket": "k3Jd...", "password": "Matkhau123!", "rememberMe": true }
```

`GoogleLoginResponse` có đầy đủ sáu trường dưới đây (các trường không dùng có thể là `null`):

| Trường | `signedIn: true` | `signedIn: false` |
|---|---|---|
| `signedIn` | `true` | `false` |
| `accountCreated` | `true` khi vừa tạo user; `false` khi đã liên kết | `false` |
| `returnUrl` | Đường dẫn đã lọc hoặc `null` | Đường dẫn đã lọc hoặc `null` |
| `session` | `AuthSessionResponse` như mục 6.2 | `null` |
| `linkTicket` | `null` | Vé opaque |
| `maskedEmail` | `null` | Email đã che của tài khoản app |

- Subject Google (`sub`) đã liên kết → đăng nhập user đó. Chưa có liên kết nhưng email trùng user app → chỉ cấp vé, **không tự gộp và không cấp refresh cookie mới**. Chưa có user → tạo user `USER/ACTIVE` không mật khẩu và liên kết Google; `emailVerified` theo claim Google. Không tự tạo kho. Google display name không phải bằng chứng eKYC.
- Gửi GIS `credential` dưới tên `idToken` (bắt buộc ≤4096 ký tự), không gửi access token OAuth hoặc email/name để Backend tin trực tiếp. Backend kiểm tra chữ ký, issuer, audience và expiry. `nonce` optional ≤256 theo DTO: nếu có giá trị thì phải khớp claim token; FE hiện tạo nonce cho lần lấy credential. Nonce không thay CSRF và Backend chưa có kho nonce dùng một lần.
- `returnUrl` optional ≤2048; chỉ giữ đường dẫn bắt đầu `/`, loại `//`, `/\` và ký tự điều khiển; không hợp lệ trả `null`. Backend chỉ trả dữ liệu, không tự redirect. FE kiểm tra lại trước điều hướng; link-confirm không nhận/trả returnUrl, nên FE giữ đường dẫn đã kiểm tra trong RAM khi chờ xác nhận.
- Vé liên kết sống 10 phút, dùng một lần. Giữ vé trong RAM; không lưu URL/storage/log. Ticket ≤256 ký tự; password bắt buộc ≤128; rememberMe boolean mặc định false. Sai mật khẩu giữ màn xác nhận, không coi `401 INVALID_CREDENTIALS` là phiên hết hạn. Vé sai/hết hạn/đã dùng → lấy Google credential mới và bắt đầu lại, không tự retry mutation.

#### Cấu hình và kiểm thử Google local

- Dùng OAuth Client **Web application**, không phải service account. `VITE_GOOGLE_CLIENT_ID` ở FE phải bằng `GoogleAuth:ClientId` ở BE (biến môi trường BE: `GoogleAuth__ClientId`). Đây là public Client ID; luồng GIS ID token này không cần client secret hoặc callback đổi authorization code.
- Google Cloud → Google Auth Platform → Clients → chọn Web client → Authorized JavaScript origins: thêm chính xác `http://localhost:5173`, Save. Origin không có path; scheme/host/port phải khớp FE thực tế. `Cors:AllowedOrigins` của Backend cũng phải cho origin FE và credentials; hai cấu hình này độc lập.
- Thiếu Client ID BE hiện phát sinh lỗi cấu hình và trả 500; audience/nonce/token không hợp lệ trả 401. Lỗi `origin_mismatch` thuộc cấu hình GIS trước khi gọi API. Không đưa client secret vào biến `VITE_*`.
- Chạy local theo [Google Auth local recovery](GOOGLE-AUTH-LOCAL-RECOVERY.md); ví dụ request ở [google-auth.http](../http/google-auth.http). Swagger/OpenAPI Development: `/swagger`, `/openapi/v1.json`; route xuất hiện trong tài liệu này chưa bảo đảm xuất hiện trong OpenAPI.
- Đã có xác nhận người dùng đăng nhập Google thật vào dashboard ngày 15/10/2026. Nhánh email trùng → link-confirm đã có implementation và automated checks; xác nhận trên không chứng minh đã E2E nhánh này bằng tài khoản Google thật.

### 6.4. Phương thức đăng nhập của tôi

> **Đã triển khai** (`LoginMethodsController`, `LoginMethodService`; kiểm thử bằng fake trong `LoginMethodServiceTests`, chưa E2E với Google thật). Dùng Bearer, giới hạn 10 request/phút/IP (policy `login-methods`). Thao tác thêm/bỏ phải xác thực lại (mật khẩu hiện tại và/hoặc Google ID token mới trùng `sub` đã liên kết). Không đổi `User.Email` theo email Google.

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/v1/me/login-methods` | Đăng nhập | — | 200 `LoginMethodResponse[]` | 401 |
| POST | `/v1/me/login-methods/password/verify` | Đăng nhập | `{ currentPassword }` | 204 (chỉ kiểm tra, không đổi dữ liệu) | 400, 401 `REAUTHENTICATION_FAILED` |
| POST | `/v1/me/login-methods/google` | Đăng nhập | `{ idToken, nonce?, currentPassword }` | 201 `LoginMethodResponse` | 401 `REAUTHENTICATION_FAILED` / `INVALID_GOOGLE_TOKEN`, 409 `GOOGLE_ACCOUNT_ALREADY_LINKED` |
| POST | `/v1/me/login-methods/google/unlink` | Đăng nhập | `{ currentPassword }` | 204 | 401 `REAUTHENTICATION_FAILED`, 409 `LAST_LOGIN_METHOD` |
| POST | `/v1/me/login-methods/password` | Đăng nhập | `{ googleIdToken, nonce?, newPassword, confirmPassword }` | 204 | 401, 409 `LOGIN_METHOD_ALREADY_EXISTS` |
| POST | `/v1/me/login-methods/password/remove` | Đăng nhập | `{ currentPassword, googleIdToken, nonce? }` | 204 | 401, 409 `LAST_LOGIN_METHOD` |

```json
// LoginMethodResponse
{ "method": "GOOGLE", "email": "an@gmail.com", "linkedAt": "2026-10-13T08:30:00+00:00", "lastUsedAt": null, "canRemove": true }
```

- `REAUTHENTICATION_FAILED` là 401 nhưng phiên vẫn còn: interceptor **không** được coi là hết phiên.
- Gỡ phương thức không còn lại cái nào → `409 LAST_LOGIN_METHOD`. Gỡ Google khi chưa liên kết, hoặc gỡ mật khẩu khi chưa có mật khẩu, trả `204` (idempotent). Đặt mật khẩu khi đã có → `409 LOGIN_METHOD_ALREADY_EXISTS`.
- OTP và passkey chưa có endpoint trong Backend hiện tại; UI/scaffold hoặc type FE không chứng minh đã có API.

### 6.4a. Ảnh đại diện của tôi

> **Đã triển khai** (`MeAvatarController`, `AvatarService`). Mọi vai trò dùng Bearer. Ảnh lưu **mã hóa** trong cùng kho lưu trữ private với tài sản (không có link công khai) và **không tính vào quota** của kho.

| Method | Path | Auth | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/v1/me/avatar` | Đăng nhập | — | 200 nội dung ảnh (`image/jpeg` \| `image/png` \| `image/webp`, `Cache-Control: private, no-store`) | 404 `AVATAR_NOT_FOUND`, 500 `CONTENT_INTEGRITY_FAILED` |
| PUT | `/v1/me/avatar` | Đăng nhập | `multipart/form-data`: `file` | 200 `UserResponse` | 400 `VALIDATION_ERROR` (rỗng, quá 2 MiB, không phải JPEG/PNG/WebP), 413 |
| DELETE | `/v1/me/avatar` | Đăng nhập | — | 200 `UserResponse` (chưa có ảnh vẫn 200) | 401 |

- `UserResponse` có thêm `hasAvatar` (bool) và `avatarUpdatedAt` (null khi chưa có ảnh). FE tải ảnh bằng GET kèm Bearer rồi tạo object URL tạm; đổi `avatarUpdatedAt` thì tải lại.
- Loại ảnh nhận theo **nội dung thật** (chữ ký đầu tệp), không theo đuôi tệp hay `Content-Type`. Tối đa 2 MiB. Không lưu tên tệp gốc. Đặt ảnh mới thì ảnh cũ bị xóa sau khi bản ghi đã lưu.

### 6.5. Kho

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| POST | `/v1/vaults` | Đăng nhập | `{ name?, description? }` (có thể rỗng) | **201** khi vừa tạo / **200** khi đã có kho; body `VaultResponse` | 403 `EMAIL_NOT_VERIFIED`, 403 `ACCOUNT_NOT_ACTIVE` |
| GET | `/v1/vaults/me` | Đăng nhập | — | 200 `VaultResponse` | 404 `VAULT_NOT_FOUND` (chưa tạo kho) |
| GET | `/v1/vaults/{vaultId}` | Đăng nhập | — | 200 `VaultResponse` | 404 |
| PUT | `/v1/vaults/{vaultId}` | Đăng nhập | `{ etag, name?, description? }` | 200 `VaultResponse` | 400, 404, 409 `EDIT_CONFLICT`, 409 `VAULT_NOT_EDITABLE` |

- Nút **"Tạo kho của tôi"** gọi `POST /v1/vaults`. Mỗi người chỉ có **một** kho: bấm lặp hay mở hai tab vẫn trả cùng một kho (lần sau trả 200, bỏ qua body).
- Trang chủ sau đăng nhập gọi `GET /v1/vaults/me`: 404 → hiện nút "Tạo kho của tôi"; 200 → vào kho.
- `PUT`: trường `null`/không gửi = giữ nguyên; `description: ""` = xóa mô tả.

```json
// VaultResponse
{
  "id": "0199e7a4-...",
  "name": "Kho của tôi",
  "description": null,
  "status": "OPEN",
  "closedAt": null,
  "planStatus": "DRAFT",
  "planConfigVersion": 1,
  "planActivatedAt": null,
  "serviceTier": "FREE",
  "serviceStatus": "FREE",
  "serviceExpiresAt": null,
  "storageQuotaBytes": 20971520,
  "storageUsedBytes": 0,
  "storageRemainingBytes": 20971520,
  "beneficiarySlots": 0,
  "includedBeneficiarySlots": 0,
  "extraBeneficiarySlots": 0,
  "usedBeneficiarySlots": 0,
  "activeContainerCount": 0,
  "createdAt": "2026-10-13T08:30:00+00:00",
  "updatedAt": null,
  "etag": "\"0\""
}
```

| Trường | Giá trị |
|---|---|
| `status` | `OPEN`, `CLOSED` |
| `planStatus` | `DRAFT`, `ACTIVE`, `CHECKIN_OVERDUE`, `AWAITING_CHECK`, `DEATH_CASE_OPEN`, `HANDOVER_IN_PROGRESS`, `CLOSED`, `BLOCKED` |
| `serviceTier` | `FREE`, `XS`, `XS_MAX` |
| `serviceStatus` | `FREE`, `PAID_ACTIVE`, `GRACE_READ_ONLY`, `EXPIRED_REVIEW` |

Ba trạng thái **độc lập**: kho đã trả tiền vẫn có thể `planStatus = DRAFT`. Dung lượng tính bằng byte, `1 MiB = 1.048.576 byte`.

Suất người nhận (QUOTA-06): `beneficiarySlots` = tổng = `includedBeneficiarySlots` (kèm hạng) + `extraBeneficiarySlots` (mua thêm). `usedBeneficiarySlots` = số **người** khác nhau đang ở ít nhất một gói; một người ở nhiều gói tính một suất, hồ sơ chưa ở gói nào không tính.

### 6.6. Gói (Container)

Endpoint gốc theo kho: `/v1/vaults/{vaultId}/containers`. FE có thể tạo service bằng `createBaseService({ endpoint: \`/v1/vaults/${vaultId}/containers\` })`.

| Method | URL | Quyền | Body / Query | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `.../containers` | Đăng nhập | Query phân trang + `includeRemoved` (mặc định `false`); `sortBy`: `createdAt` (mặc định) \| `name` | 200 `PaginatedList<ContainerResponse>` | 400, 404 |
| POST | `.../containers` | Đăng nhập | `{ name, description? }` | **201** `ContainerResponse` | 400, 404, 409 `CONTAINER_LIMIT_EXCEEDED`, 409 `VAULT_NOT_EDITABLE` |
| GET | `.../containers/{containerId}` | Đăng nhập | — | 200 `ContainerResponse` | 404 |
| PUT | `.../containers/{containerId}` | Đăng nhập | `{ etag, name?, description? }` | 200 `ContainerResponse` | 400, 404, 409 `EDIT_CONFLICT` / `CONTAINER_REMOVED` / `VAULT_NOT_EDITABLE` |
| DELETE | `.../containers/{containerId}?confirmPlanDeactivation=false` | Đăng nhập | — | 200 `ContainerResponse` (đã `REMOVED`) | 404, 409 `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` |
| POST | `.../containers/{containerId}/assets` | Đăng nhập | `{ assetId }` | 200 `ContainerResponse` | 403 `PAID_TIER_REQUIRED`, 404, 409 `ASSET_ALREADY_IN_CONTAINER` / `CONTAINER_REMOVED` |
| DELETE | `.../containers/{containerId}/assets/{assetId}?confirmPlanDeactivation=false` | Đăng nhập | — | 200 `ContainerResponse` | 404, 409 `CONTAINER_REMOVED` / `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` |
| POST | `.../containers/{containerId}/beneficiaries` | Đăng nhập | `{ beneficiaryId }` | 200 `ContainerResponse` | 400, 403 `PAID_TIER_REQUIRED`, 404 `CONTAINER_NOT_FOUND` / `BENEFICIARY_NOT_FOUND`, 409 `BENEFICIARY_SLOTS_EXCEEDED` / `CONTAINER_REMOVED` / `VAULT_NOT_EDITABLE` |
| DELETE | `.../containers/{containerId}/beneficiaries/{beneficiaryId}?confirmPlanDeactivation=false` | Đăng nhập | — | 200 `ContainerResponse` | 404, 409 `CONTAINER_REMOVED` / `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` |

```json
// ContainerResponse
{
  "id": "0199e7b0-...",
  "vaultId": "0199e7a4-...",
  "name": "Tài liệu gia đình",
  "description": null,
  "status": "DRAFT",
  "assetIds": [],
  "beneficiaryIds": [],
  "missingRequirements": ["NO_ASSET", "NO_DESIGNATION"],
  "createdAt": "2026-10-13T08:35:00+00:00",
  "updatedAt": null,
  "removedAt": null,
  "etag": "\"0\""
}
```

- `DELETE` gói = **xóa gói**, không xóa hẳn dòng và **không khôi phục được**: gói chuyển `REMOVED`, tài sản và người nhận được gỡ ra nhưng **không bị xóa**. Người nhận không còn ở gói nào khác thì hết chiếm suất.
- Gặp `409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED`: FE hiện cảnh báo "kế hoạch sẽ trở về bản nháp"; Owner đồng ý thì gọi lại với `?confirmPlanDeactivation=true`.
- Hạng Free: được tạo/đổi tên/xóa gói rỗng; đưa tài sản hoặc người nhận vào gói → `403 PAID_TIER_REQUIRED` (FE hiện gợi ý nâng cấp).
- `status`: `DRAFT` (thiếu tài sản hoặc người nhận), `READY` (có ≥1 tài sản **và** ≥1 người nhận), `REMOVED` (Owner đã xóa, không khôi phục được). BE tự tính, FE không gửi trạng thái. `missingRequirements` cho biết còn thiếu gì: `NO_ASSET`, `NO_DESIGNATION`. Người nhận chưa đủ 18 tuổi (đã xác nhận) vẫn tính.
- `beneficiaryIds`: id hồ sơ người nhận trong gói, theo thứ tự gắn; FE lấy họ tên từ danh sách người nhận của kho (mục 6.8).
- **Gắn người nhận** (`POST .../beneficiaries`): hồ sơ phải thuộc kho này (tạo trước ở mục 6.8). Gắn lại người đã có trong gói → 200, không đổi gì. Người **chưa ở gói nào** chiếm thêm một suất; vượt suất → `409 BENEFICIARY_SLOTS_EXCEEDED` kèm `usedSlots`, `maxSlots` (FE gợi ý mua thêm suất). Người đã ở gói khác thì không tốn thêm suất.
- **Gỡ người nhận** không xóa hồ sơ. Người còn ở gói khác vẫn chiếm suất; gỡ khỏi mọi gói thì trả suất. Gỡ người cuối cùng của gói READY cuối cùng khi kế hoạch đang chạy → `409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` như gỡ tài sản.
- Mọi thay đổi gói (kể cả gắn/gỡ người nhận) tăng `planConfigVersion`. Dịch vụ đang ân hạn: gắn bị chặn (`VAULT_NOT_EDITABLE`, `SERVICE_READ_ONLY`), gỡ vẫn được.

### 6.7. Tài sản (Asset)

Tài sản thuộc **kho**, không thuộc gói: tạo tài sản trước, rồi mới đưa vào gói bằng `POST .../containers/{containerId}/assets`.

| Method | URL | Quyền | Body / Query | Thành công | Lỗi |
|---|---|---|---|---|---|
| POST | `/v1/vaults/{vaultId}/assets` | Đăng nhập | `multipart/form-data`: `file` (bắt buộc), `name?`, `description?` | **201** `AssetResponse` | 400, 404, 409 `STORAGE_QUOTA_EXCEEDED` / `VAULT_NOT_EDITABLE`, 413 `FILE_TOO_LARGE` |
| POST | `/v1/vaults/{vaultId}/assets/text` | Đăng nhập | `{ name, description?, content }` | **201** `AssetResponse` | 400, 404, 409 như trên |
| GET | `/v1/vaults/{vaultId}/assets` | Đăng nhập | Query phân trang; `sortBy`: `createdAt` (mặc định) \| `name` | 200 `PaginatedList<AssetResponse>` | 400, 404 |
| GET | `/v1/vaults/{vaultId}/assets/{assetId}` | Đăng nhập | — | 200 `AssetResponse` | 404 `VAULT_NOT_FOUND` / `ASSET_NOT_FOUND` |
| GET | `/v1/vaults/{vaultId}/assets/{assetId}/content` | Đăng nhập (Owner) | — | 200 nội dung gốc (`application/octet-stream`, `Content-Disposition: attachment`, `Cache-Control: no-store`) | 404 `ASSET_NOT_FOUND` (kể cả nội dung đã dọn), 500 `CONTENT_INTEGRITY_FAILED` |
| PUT | `/v1/vaults/{vaultId}/assets/{assetId}` | Đăng nhập | `{ name?, description?, etag }` | 200 `AssetResponse` | 400, 404, 409 `EDIT_CONFLICT` / `VAULT_NOT_EDITABLE` |
| DELETE | `/v1/vaults/{vaultId}/assets/{assetId}` | Đăng nhập | — | 204 | 404, 409 `ASSET_IN_USE` (+`reason`: `IN_CONTAINER` \| `PINNED_IN_HANDOVER`) / `VAULT_NOT_EDITABLE` |

```json
// AssetResponse — chỉ metadata, KHÔNG có nội dung tài sản
{
  "id": "0199e7c1-...",
  "vaultId": "0199e7a4-...",
  "kind": "FILE",
  "name": "Giấy tờ nhà",
  "description": null,
  "fileName": "giay-to.pdf",
  "contentType": "application/pdf",
  "sizeBytes": 1500,
  "createdAt": "2026-10-13T08:40:00+00:00",
  "updatedAt": null,
  "etag": "\"0\""
}
```

- `kind`: `FILE` (tệp tải lên) | `TEXT` (nội dung chữ: thông tin tài khoản, ghi chú...). `fileName` là `null` với `TEXT`.
- `name` của tệp không gửi thì lấy theo tên tệp. Tên/mô tả **không mã hóa**: không ghi mật khẩu vào đây; bí mật để trong `content`/tệp.
- Nội dung tệp và `content` được BE mã hóa (AES-256-GCM, khóa riêng mỗi tài sản) trước khi lưu. FE gửi nội dung gốc qua HTTPS, **không** tự mã hóa hay Base64 nội dung. `sizeBytes` là kích thước nội dung gốc, cũng là số tính vào quota.
- Hạng Free được lưu tài sản trong 20 MiB; mọi nội dung đã lưu đều tính vào `storageUsedBytes` của kho, kể cả khi tài sản không ở gói nào.
- **Tải nội dung (LV-10):** chỉ Owner của kho. Server giải mã hai lượt: lượt đầu vào `Stream.Null` để kiểm kích thước và SHA-256 trước khi gửi byte đầu tiên; nội dung hỏng trả 500 và không có byte nội dung nào. Mỗi lần tải ghi nhật ký `AssetContentDownloaded` (chỉ id, không tên tài sản).
- **Nội dung không đổi sau khi tạo:** không có phiên bản. Muốn thay nội dung, tạo tài sản mới rồi xóa tài sản cũ (các đường dẫn `/versions` và tham số `?version` đã bỏ).
- **Đổi tên/mô tả** là thao tác "thêm": chặn khi kho đóng, kế hoạch bị khóa (đang chờ/bàn giao) hoặc dịch vụ chỉ đọc. Kho đang có hold: `409 VAULT_NOT_EDITABLE` với `reason = HOLD_ACTIVE`.
- **Xóa** là thao tác "giảm" (vẫn cho khi ân hạn, chặn khi kho đóng/kế hoạch khóa). Chỉ xóa được khi tài sản không còn trong gói nào (gỡ khỏi gói trước), chưa ghim vào snapshot của lượt bàn giao nào và kho không có hold. Xóa nội dung đã mã hóa, giải phóng quota. Nội dung được xóa sau khi bản ghi đã xóa; nếu kho lưu trữ lỗi, object mồ côi (đã mã hóa) có thể còn lại.
- Xuất cấu hình PDF (XS Max) và khôi phục tài sản đã xóa chưa có.

### 6.8. Người nhận (Beneficiary)

Hồ sơ người nhận thuộc **kho** (danh bạ người nhận), dùng lại cho nhiều gói. Endpoint gốc: `/v1/vaults/{vaultId}/beneficiaries`. Gắn/gỡ hồ sơ vào gói ở mục 6.6 (`.../containers/{containerId}/beneficiaries`).

| Method | URL | Quyền | Body / Query | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `.../beneficiaries` | Đăng nhập | Query phân trang; `sortBy`: chỉ `createdAt` (mặc định); `searchTerm`: 1–4 chữ số | 200 `PaginatedList<BeneficiaryResponse>` | 400, 404 |
| POST | `.../beneficiaries` | Đăng nhập | `{ citizenId, fullName, dateOfBirth, email?, phone? }` | **201** `BeneficiaryResponse` (vừa tạo) / **200** (đã có hồ sơ cùng người) | 400, 403 `PAID_TIER_REQUIRED`, 404, 409 `BENEFICIARY_IDENTITY_CONFLICT` / `BENEFICIARY_SLOTS_EXCEEDED` / `VAULT_NOT_EDITABLE` |
| GET | `.../beneficiaries/{beneficiaryId}` | Đăng nhập | — | 200 `BeneficiaryResponse` | 404 `VAULT_NOT_FOUND` / `BENEFICIARY_NOT_FOUND` |
| PUT | `.../beneficiaries/{beneficiaryId}` | Đăng nhập | `{ etag, fullName?, dateOfBirth?, email?, phone? }` | 200 `BeneficiaryResponse` | 400, 404, 409 `EDIT_CONFLICT` / `VAULT_NOT_EDITABLE` |
| DELETE | `.../beneficiaries/{beneficiaryId}` | Đăng nhập | — | **204** | 404, 409 `BENEFICIARY_IN_USE` / `VAULT_NOT_EDITABLE` |

```json
// POST body
{
  "citizenId": "012345678901",
  "fullName": "Nguyễn Văn An",
  "dateOfBirth": "1990-05-20",
  "email": "an.nguyen@example.com",
  "phone": "0901 234 567"
}

// BeneficiaryResponse — KHÔNG có số CCCD đầy đủ
{
  "id": "0199e7d2-...",
  "citizenIdLast4": "8901",
  "fullName": "Nguyễn Văn An",
  "dateOfBirth": "1990-05-20",
  "email": "an.nguyen@example.com",
  "phone": "0901234567",
  "minorAcknowledgedAt": null,
  "containerIds": [],
  "createdAt": "2026-10-14T08:00:00+00:00",
  "updatedAt": null,
  "etag": "\"0\""
}

// PUT body: chỉ gửi trường cần đổi
{ "etag": "\"0\"", "fullName": "Nguyễn Văn Bình", "email": "" }
```

- **Bảo mật (DATA-02):** BE mã hóa số CCCD, họ tên, ngày sinh, email, số điện thoại. Response **chỉ** có `citizenIdLast4`, không bao giờ trả số CCCD đầy đủ. FE không lưu số CCCD đầy đủ sau khi gửi.
- **Tìm kiếm:** họ tên đã mã hóa nên `searchTerm` **chỉ** tìm theo 4 số cuối CCCD: 1–4 chữ số, khớp phần cuối của `citizenIdLast4` (ví dụ `01` khớp `8901`). Có chữ hoặc quá 4 ký tự → 400.
- **CCCD:** đúng 12 chữ số, được gõ khoảng trắng (BE bỏ đi), giữ số 0 đầu. **Không sửa được** sau khi tạo; muốn đổi thì xóa hồ sơ rồi tạo lại.
- **Liên hệ:** cần ít nhất `email` hoặc `phone`. Khi tạo, `""` coi như không có. Khi sửa, `null`/không gửi = giữ nguyên, `""` = xóa. Email được đổi sang chữ thường; số điện thoại bỏ khoảng trắng, dấu chấm, dấu gạch, phải có 9–15 chữ số (cho phép `+` đầu).
- **Trùng CCCD trong kho:** cùng CCCD, cùng họ tên (không phân biệt hoa thường, bỏ khoảng trắng thừa) và cùng ngày sinh → **200** trả hồ sơ cũ, không đổi gì (kể cả liên hệ). Khác họ tên hoặc ngày sinh → `409 BENEFICIARY_IDENTITY_CONFLICT` kèm `beneficiaryId`; FE mở hồ sơ đó cho Owner sửa cho thống nhất.
- **Tuổi:** không kiểm tra và không trả nhãn tuổi (đã bỏ `isMinor`). `minorAcknowledgedAt` không còn dùng (hồ sơ mới luôn `null`). Trường `confirmMinor` của FE cũ nếu còn gửi sẽ bị bỏ qua, không lỗi. Ngày sinh ở tương lai → 400.
- Hạng Free tạo hồ sơ → `403 PAID_TIER_REQUIRED`. Dịch vụ đang ân hạn: tạo/sửa bị chặn (`VAULT_NOT_EDITABLE`, `SERVICE_READ_ONLY`), xóa vẫn được.
- `containerIds`: các gói đang có người này, theo thứ tự gắn; rỗng khi chưa ở gói nào.
- **Giới hạn số hồ sơ:** số hồ sơ của kho (tính cả hồ sơ chưa ở gói nào) không vượt tổng suất `beneficiarySlots`. Tạo hồ sơ mới khi đã đủ → `409 BENEFICIARY_SLOTS_EXCEEDED` kèm `usedSlots` (số hồ sơ hiện có) và `maxSlots`; FE gợi ý mua thêm suất hoặc xóa hồ sơ chưa dùng. Gửi lại đúng người đã có (trùng CCCD, cùng họ tên và ngày sinh) vẫn **200** dù đã đủ. FE lấy số hồ sơ từ `totalCount` của danh sách.
- Tạo hồ sơ **không** tăng `planConfigVersion`. `usedBeneficiarySlots` của kho vẫn chỉ đếm người đang ở gói (mục 6.6).
- Sửa hồ sơ **đang ở gói** (có thay đổi thật) → `planConfigVersion` tăng 1; hồ sơ chưa ở gói nào thì không tăng.
- Xóa hồ sơ đang ở gói → `409 BENEFICIARY_IN_USE` kèm `containerIds`; FE hướng dẫn gỡ khỏi các gói đó trước.
- BE không gửi thông báo, không tạo tài khoản cho người nhận.

---

### 6.9. Điểm danh (deadman switch)

Điểm danh chỉ để phát hiện việc cần kiểm tra, **không** chứng minh Owner đã mất (DMS-03). Đăng nhập hay thanh toán không phải điểm danh (DMS-02). Cần đăng nhập (Bearer); kho lấy theo người trong token.

| Method | URL | Quyền | Body / Query | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/v1/check-ins/me` | Đăng nhập | Query phân trang (`pageIndex`, `pageSize`) cho `history` | 200 `CheckInStatusResponse` | 401, 404 `VAULT_NOT_FOUND` |
| POST | `/v1/check-ins` | Đăng nhập | Không có body | 200 `CheckInStatusResponse` | 401, 404, 409 `CHECKIN_NOT_ALLOWED` / `EDIT_CONFLICT` |
| PATCH | `/v1/vaults/me/check-in-settings` | Đăng nhập | `{ etag, cycleDays?, graceDays? }` | 200 `CheckInStatusResponse` | 400, 404, 409 `EDIT_CONFLICT` / `VAULT_NOT_EDITABLE` |

```json
// CheckInStatusResponse
{
  "status": "ACTIVE",
  "planStatus": "ACTIVE",
  "serverTime": "2026-10-14T08:00:00+00:00",
  "lastCheckInAt": "2026-10-01T08:00:00+00:00",
  "nextCheckInDueAt": "2026-10-31T08:00:00+00:00",
  "graceEndsAt": "2026-11-07T08:00:00+00:00",
  "settings": { "cycleDays": 30, "graceDays": 7, "pendingCycleDays": null, "pendingGraceDays": null, "etag": "\"0\"" },
  "history": {
    "items": [ { "sequence": 2, "startedAt": "...", "dueAt": "...", "graceEndsAt": "...", "checkedInAt": null, "overdueAt": null, "awaitingCheckAt": null } ],
    "totalCount": 2, "pageIndex": 1, "pageSize": 10, "totalPages": 1, "hasPreviousPage": false, "hasNextPage": false
  }
}
```

- `status` tính theo giờ server nên luôn đúng dù job nền chạy chậm: `NOT_STARTED` (kế hoạch chưa kích hoạt) | `ACTIVE` | `CHECKIN_OVERDUE` (quá hạn, đang trong thời gian chờ) | `AWAITING_CHECK` (hết thời gian chờ, cần Executor kiểm tra) | `PAUSED` (kế hoạch đã sang giai đoạn chứng tử/bàn giao hoặc bị chặn). `planStatus` là trạng thái kế hoạch đang lưu trong DB (có thể chậm vài chục giây so với `status`).
- FE đếm ngược theo `serverTime`, **không** dùng giờ trình duyệt (TIME-01). `history` xếp kỳ mới nhất trước.
- **POST /check-ins** đóng kỳ hiện tại, mở kỳ mới và đưa kế hoạch về `ACTIVE`; cho phép khi kế hoạch `ACTIVE`/`CHECKIN_OVERDUE`/`AWAITING_CHECK`, kể cả khi dịch vụ đang ân hạn. Mỗi ngày lịch theo giờ Việt Nam (UTC+07:00) chỉ ghi nhận một lần, dùng giờ server. Gọi lại trong cùng ngày (kể cả từ web và smartwatch) trả trạng thái hiện tại, không đổi hạn, không mở kỳ mới và không ghi thêm nhật ký. Từ 00:00 ngày Việt Nam tiếp theo mới được ghi nhận lần mới, không phải chờ đủ 24 giờ. FE và đồng hồ dùng `lastCheckInAt` cùng `serverTime` để khóa nút và đọc lại trạng thái khi quay lại màn hình/qua ngày mới. Hai yêu cầu chạy song song: bên thua nhận `409 EDIT_CONFLICT` (gọi lại `GET`). Kế hoạch `DRAFT` hoặc đã sang chứng tử/bàn giao → `409 CHECKIN_NOT_ALLOWED` kèm trường phụ `planStatus`.
- **PATCH check-in-settings**: `cycleDays` ∈ {30, 60, 90}, `graceDays` ∈ {7, 14, 30}; phải gửi ít nhất một trong hai; `etag` lấy từ `settings.etag`. Đang có kỳ chạy thì lựa chọn mới nằm ở `pendingCycleDays`/`pendingGraceDays` và **chỉ áp dụng từ kỳ sau** (DMS-01). Kế hoạch đã sang chứng tử/bàn giao/đóng → `409 VAULT_NOT_EDITABLE` (`reason: PLAN_LOCKED`).
- Nhắc điểm danh gửi 4 mốc, mỗi mốc một lần: trước hạn 7 ngày, trước hạn 1 ngày, khi bắt đầu thời gian chờ, khi còn 2 ngày chờ (DMS-06).
- Chưa có API kích hoạt kế hoạch nên kỳ đầu tiên được mở khi Owner bấm "Điểm danh" lần đầu trên kế hoạch đang chạy; khi có chức năng kích hoạt sẽ gọi `ICheckInScheduler.StartFirstPeriodAsync`.

### 6.10. Xác minh danh tính (eKYC)

Người dùng đọc mặt trước CCCD rồi gửi selfie để so khớp khuôn mặt. Server gọi dịch vụ eKYC riêng (chương trình Python) và **chỉ lưu kết quả**: không lưu ảnh, không lưu họ tên/ngày sinh/địa chỉ, chỉ giữ 4 số cuối CCCD. Ngoại lệ (BR-48 sửa 25/10/2026): với `OWNER_ENROLLMENT`, ảnh mặt trước CCCD được lưu **mã hóa** (kho lưu trữ private như tài sản) cùng danh tính đã gắn khi lượt đạt, để Verifier được giao hồ sơ chứng tử của kho đối chiếu (mục 6.12); lượt thất bại hoặc bị lượt mới thay thì ảnh bị xóa. Cần đăng nhập (Bearer); người dùng lấy từ token. Ảnh JPEG/PNG, tối đa 8 MiB, gửi `multipart/form-data` với field `file`.

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/v1/ekyc/me` | Đăng nhập | — | 200 `EkycStatusResponse` | 401 |
| POST | `/v1/ekyc/consent` | Đăng nhập | JSON `{ "purpose", "noticeVersion" }` | 200 `EkycConsentResponse` | 400 `VALIDATION_ERROR`, 401 |
| POST | `/v1/ekyc/document` | Đăng nhập | multipart `file` (ảnh mặt trước), `handoverTurnId` (chỉ khi mục đích `BENEFICIARY_CLAIM`) | 200 `EkycDocumentResponse` | 400 `EKYC_IMAGE_REJECTED` / `VALIDATION_ERROR`, 401, 404 `HANDOVER_NOT_FOUND`, 409 `EKYC_CONSENT_REQUIRED` / `HANDOVER_NOT_ALLOWED` / `EKYC_IDENTITY_MISMATCH`, 413 `FILE_TOO_LARGE`, 503 `EKYC_SERVICE_UNAVAILABLE` |
| POST | `/v1/ekyc/document/confirm` | Đăng nhập | JSON bản tự khai sau OCR (xem dưới) | 200 `EkycStatusResponse` | 400 `VALIDATION_ERROR`, 401, 404 `HANDOVER_NOT_FOUND`, 409 `EKYC_DOCUMENT_EXPIRED` / `HANDOVER_NOT_ALLOWED` |
| POST | `/v1/ekyc/document-back` | Đăng nhập | multipart `file` (ảnh mặt sau) | 200 `EkycBackResponse` (= `EkycStatusResponse` + `residenceAddress`) | 400 `EKYC_DOCUMENT_MISMATCH` / `EKYC_IMAGE_REJECTED`, 401, 409 `EKYC_DOCUMENT_EXPIRED`, 413, 503 |
| POST | `/v1/ekyc/liveness/start` | Đăng nhập | — | 200 `EkycLivenessResponse` | 401, 409 `EKYC_DOCUMENT_EXPIRED`, 503 |
| POST | `/v1/ekyc/liveness/frame` | Đăng nhập | multipart `file` (một khung hình JPEG/PNG) | 200 `EkycLivenessResponse` | 400 `EKYC_IMAGE_REJECTED`, 401, 409 `EKYC_DOCUMENT_EXPIRED` / `EKYC_LIVENESS_REQUIRED`, 413, 503 |
| POST | `/v1/ekyc/liveness/complete` | Đăng nhập | — | 200 `EkycStatusResponse` | 400 `EKYC_IMAGE_REJECTED`, 401, 409 `EKYC_DOCUMENT_EXPIRED` / `EKYC_LIVENESS_REQUIRED` / `EKYC_LIVENESS_NOT_PASSED`, 503 |
| POST | `/v1/ekyc/face-match` | Đăng nhập | multipart `file` (selfie) | 200 `EkycStatusResponse` | 400, 401, 409 `EKYC_DOCUMENT_EXPIRED` / `EKYC_LIVENESS_REQUIRED`, 413, 503 |

```jsonc
// POST /v1/ekyc/document → 200 (thông tin chỉ trả lần này, server không lưu)
{ "verificationId": "0199...", "idNumber": "079200012345", "fullName": "NGUYỄN VĂN AN", "dateOfBirth": "2000-02-01",
  "residenceAddress": "", "canMatchFace": true, "expiresAt": "2026-10-14T08:30:00+00:00", "warnings": [] }

// POST /v1/ekyc/document/confirm — chỉ sau khi người dùng kiểm tra/sửa và chủ động xác nhận
{ "verificationId": "UUID từ document", "purpose": "OWNER_ENROLLMENT",
  "idNumber": "079200012345", "fullName": "Nguyễn Văn An", "dateOfBirth": "2000-02-01",
  "residenceAddress": "Địa chỉ người dùng đã đối chiếu" }
// BENEFICIARY_CLAIM gửi thêm handoverTurnId, đúng lượt đã gửi ở document.
// CCCD 12 chữ số ASCII, họ tên 1–200 ký tự, ngày sinh ISO hợp lệ không ở tương lai,
// địa chỉ 1–1000 ký tự. Lượt pending mới nhất phải thuộc principal, đúng purpose/turn và còn hạn.
// Bản tự khai người nhận phải khớp hồ sơ BE. Không lưu full PII, không sửa identityMatch,
// citizenIdLast4 hoặc bằng chứng từ ảnh. Sai OCR vẫn cần chụp lại/review, không tự nâng MATCHED.
// Endpoint kiểm tra bản khai; chưa có trạng thái xác nhận lưu DB hoặc guard bắt buộc ở các API cũ.

// POST /v1/ekyc/face-match và GET /v1/ekyc/me → 200
{ "status": "PASSED", "verificationId": "0199...", "citizenIdLast4": "2345", "faceMatchScore": 0.52,
  "matchThreshold": 0.363, "spoofCheckPassed": true, "createdAt": "...", "completedAt": "..." }
```

- **Đồng ý trước khi chụp (EKYC-03):** gọi `POST /ekyc/consent` với `purpose` (`OWNER_ENROLLMENT` | `BENEFICIARY_CLAIM` | `OWNER_ALIVE`) và `noticeVersion` (hiện `2026-10-v1`; sai phiên bản → `400 VALIDATION_ERROR`). Đồng ý có hiệu lực 30 phút (`expiresAt`) và quyết định `purpose` của lượt eKYC kế tiếp. **Thay đổi hợp đồng:** `POST /ekyc/document` không còn chạy được nếu chưa đồng ý (`409 EKYC_CONSENT_REQUIRED`), FE phải gọi `consent` trước.
- **Đối chiếu với người được chỉ định (EKYC-02, MATCH-01..07):** mục đích `BENEFICIARY_CLAIM` bắt buộc gửi `handoverTurnId` (lượt đang ở `IDENTITY_IN_PROGRESS` và thuộc về người gọi, kho người khác → `404`); mục đích khác gửi `handoverTurnId` → `400`. Server tự lấy hồ sơ người nhận của lượt đó, **không** nhận danh tính từ FE. `EkycDocumentResponse.identityMatch` và `EkycStatusResponse.identityMatch`: `NOT_CHECKED` | `MATCHED` (CCCD, họ tên NFC không phân biệt hoa thường, ngày sinh đọc từ dịch vụ eKYC dạng `yyyy-MM-dd`, chấp nhận thêm `dd/MM/yyyy`, đều trùng) | `NEEDS_REVIEW` (OCR không chắc, hoặc CCCD trùng mà tên/ngày sinh lệch) | `WRONG_PERSON` (CCCD khác rõ ràng, OCR không cảnh báo). Response **không** trả CCCD/họ tên/ngày sinh mong đợi (CLAIM-05). eKYC chỉ ghi kết quả; việc đóng lượt khi `WRONG_PERSON` (MATCH-05) thuộc luồng bàn giao.
- **Mặt sau (EKYC-04):** gọi `POST /ekyc/document-back` sau khi `canMatchFace = true`. Dịch vụ đọc số căn cước trong dòng MRZ mặt sau; 4 số cuối phải trùng mặt trước, khác thì `400 EKYC_DOCUMENT_MISMATCH`. Thẻ không có MRZ chứa số căn cước thì không kết luận được: `backSideChecked = false`, không phải lỗi. Hiện **chưa** chặn `face-match` khi chưa kiểm mặt sau. Chưa đọc mã QR (EKYC-06, EKYC-07): dịch vụ eKYC (Python) chưa có chức năng này.
- **Nơi cư trú ở mặt sau (BR-88):** thẻ căn cước gắn chip kiểu mới in địa chỉ ở mặt sau nên mặt trước thường trả `residenceAddress` rỗng. `POST /ekyc/document-back` trả thêm `residenceAddress` đọc từ mặt sau (rỗng khi không đọc được). Địa chỉ chỉ đi qua phản hồi này để người dùng đối chiếu rồi điền vào bản khai ở `POST /ekyc/document/confirm`: **không lưu DB, không ghi log**, `GET /ekyc/me` không bao giờ trả. Mặt sau có thể gửi **trước** bước xác nhận bản khai, chỉ cần đã đọc mặt trước và còn trong 30 phút. `ResidenceAddress` ở bước xác nhận vẫn bắt buộc (người dùng tự nhập khi mặt sau không đọc được).
- **Thử thách camera (EKYC-10..14):** dịch vụ eKYC chọn ngẫu nhiên 2 động tác (chớp mắt và quay đầu sang trái hoặc phải, thứ tự ngẫu nhiên). Thứ tự gọi sau khi `canMatchFace = true`: `liveness/start` (nhận `actions`, `prompt`, `expiresInSeconds` = 90) → gửi từng khung hình vào `liveness/frame` (khoảng vài khung hình mỗi giây, nhận `completed`, `currentIndex`, `prompt`, `frameHint`, `passed`) → khi `passed = true` gọi `liveness/complete`. Backend giữ mã phiên (FE không gửi và không thấy mã phiên, nên không dùng được phiên của người khác); selfie lấy từ **chính phiên đã đạt**, so khớp mặt với thẻ rồi chốt `PASSED`/`FAILED` như `face-match`. Phiên thất bại (`failedReason` khác rỗng: đổi người giữa chừng, quá nhiều khung hình) thì gọi lại `liveness/start`; gọi lại cũng hủy phiên cũ. Response không có điểm chống giả mạo (CLAIM-05).
- **Hai đường so khớp mặt, cả hai đều phải qua thử thách camera trước:** (1) **đường thử thách**: `liveness/complete` lấy selfie của phiên đã đạt và so khớp luôn, không cần gửi thêm ảnh; (2) **đường ảnh selfie**: `face-match` gửi một ảnh, nhưng chỉ được nhận sau khi thử thách đã đạt (`passed = true` ở `liveness/frame`), chưa bắt đầu thì `409 EKYC_LIVENESS_REQUIRED`, đã bắt đầu mà chưa đạt thì `409 EKYC_LIVENESS_NOT_PASSED`. Đường (2) chỉ kiểm cờ đã qua thử thách, không ràng buộc ảnh gửi với người trong thử thách; muốn ràng buộc thì dùng đường (1).
- **Mẫu khuôn mặt Owner (EKYC-22, EKYC-23, PRIV-03):** khi `liveness/complete` ra `PASSED` cho lượt `OWNER_ENROLLMENT` và Owner chưa có mẫu, server lấy vector đặc trưng (128 số, dịch vụ eKYC `POST /api/face-embedding`) của **chính selfie trong phiên** rồi lưu **mã hóa** (AES-256-GCM, DEK riêng bọc bằng KEK) vào bảng `OwnerFaceTemplates`, phiên bản 1. Response không có vector. Mẫu đã có thì không bị thay (EKYC-23). Không lấy được vector (dịch vụ lỗi) thì lượt giữ `PENDING` và trả `503 EKYC_SERVICE_UNAVAILABLE`, FE gọi lại `liveness/complete` được. Đường `face-match` (ảnh selfie tải lên) **không** tạo mẫu vì SRS chỉ cho tạo mẫu từ selfie trực tiếp của phiên. Mục đích khác `OWNER_ENROLLMENT` không tạo mẫu. Server chưa có việc xóa mẫu theo hạn (EKYC-29).
- **Danh tính đã gắn (EKYC-24, EKYC-27, DATA-02):** với mục đích `OWNER_ENROLLMENT`, `POST /ekyc/document` phải đọc được đủ CCCD 12 số, họ tên và ngày sinh, thiếu một thứ thì `400 EKYC_IMAGE_REJECTED` (chụp lại, không ai duyệt thay Owner) và khuôn mặt vừa giữ ở dịch vụ eKYC bị hủy. Dữ liệu đọc được được giữ **mã hóa** trên lượt eKYC chờ selfie (FE không gửi lại, EKYC-16). Lượt `OWNER_ENROLLMENT` đầu tiên ra `PASSED` (qua `liveness/complete` hoặc `face-match`) thì server gắn danh tính vào bảng `OwnerIdentities` (CCCD đủ 12 số, họ tên, ngày sinh mã hóa envelope; chỉ 4 số cuối để rõ) rồi xóa bản tạm; lượt `FAILED` cũng xóa bản tạm. Từ đó mọi lượt `OWNER_ENROLLMENT` phải ra **đúng** danh tính này: CCCD khác rõ ràng → `409 EKYC_IDENTITY_MISMATCH`; CCCD trùng mà tên/ngày sinh lệch hoặc OCR chưa chắc → `400 EKYC_IMAGE_REJECTED` (chụp lại). Response không trả danh tính đã gắn. Mục đích khác không bị ràng buộc này.
- `status`: `NOT_STARTED` (chưa làm; các trường còn lại null) | `PENDING` (đã đọc thẻ, chờ selfie) | `PASSED` | `FAILED`.
- `canMatchFace = false` khi dịch vụ chưa tìm được đúng một khuôn mặt trên thẻ: vẫn trả chữ đọc được nhưng chưa có `verificationId`; FE yêu cầu chụp lại thẻ.
- Mỗi lần gọi `/document` là một lượt mới và hủy lượt `PENDING` trước. Mỗi lượt chỉ so khớp **một lần**: sau khi có `PASSED`/`FAILED` muốn thử lại phải đọc lại mặt trước. Selfie bị nghi giả mạo (ảnh in, màn hình) → `FAILED`, `spoofCheckPassed = false`.
- Ngưỡng `matchThreshold` do server cố định, FE không gửi. `faceMatchScore` là điểm cosine, **không** phải phần trăm.
- eKYC chỉ là bằng chứng hỗ trợ. Luồng nhận tài sản dùng kết quả này qua `POST /handover/turns/{turnId}/identity/complete` (xem `docs/HANDOVER-RECEIVE-API.md`): chỉ khi lượt eKYC `BENEFICIARY_CLAIM` ghim đúng lượt, `PASSED` và `identityMatch = MATCHED` thì lượt mới sang `READY_TO_ACCEPT`. Nhóm đã bỏ bước gọi video: xác minh eKYC ở bước nhận thay cho việc gọi video và Executor kết luận "đúng người".
- Chưa có kiểm tra liveness theo động tác (chớp mắt, quay đầu); chỉ có kiểm tra giả mạo trên một ảnh selfie.

### 6.11. Executor nộp hồ sơ chứng tử

Executor (đã chấp nhận lời mời) mở hồ sơ chứng tử của kho, khai thông tin trên giấy, tải các trang giấy rồi xác nhận gửi (SRS §6.2 F2B). Cần đăng nhập (Bearer); người dùng lấy từ token. **Người gọi phải là Executor đang hiệu lực (`Accepted`) của kho và không phải Owner của kho đó**; không đủ điều kiện thì `404` (`VAULT_NOT_FOUND` / `DEATH_CASE_NOT_FOUND`) để không lộ kho hay hồ sơ có tồn tại hay không. Phần Verifier xét hồ sơ nằm ở mục 6.12; Owner phản đối chưa có.

| Method | URL | Body | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/v1/executor/vaults` | — | 200 `ExecutorVaultResponse[]` | 401 |
| POST | `/v1/executor/vaults/{vaultId}/death-case` | — | **201** vừa tạo / **200** kho đã có hồ sơ mở; `DeathCaseResponse` | 404, 409 `DEATH_CASE_NOT_ALLOWED` (`PLAN_NOT_RUNNING`) |
| GET | `/v1/executor/death-cases/{caseId}` | — | 200 `DeathCaseResponse` | 404 |
| PUT | `/v1/executor/death-cases/{caseId}/draft` | `{ etag, ownerFullName?, ownerCitizenId?, dateOfDeath?, certificateNumber?, issuedOn?, issuedBy? }` | 200 | 400, 404, 409 `EDIT_CONFLICT` / `DEATH_CASE_NOT_ALLOWED` (`VERSION_LOCKED`, `CASE_CLOSED`) |
| POST | `/v1/executor/death-cases/{caseId}/files` | `multipart/form-data`, field `file` | **201** `DeathCaseResponse` | 400 `VALIDATION_ERROR` (loại tệp sai), 404, 409 `DEATH_CASE_FILE_LIMIT_EXCEEDED` / `DEATH_CASE_NOT_ALLOWED`, 413 `FILE_TOO_LARGE` |
| DELETE | `/v1/executor/death-cases/{caseId}/files/{fileId}` | — | 200 `DeathCaseResponse` | 404, 409 `DEATH_CASE_NOT_ALLOWED` (`VERSION_LOCKED`) |
| POST | `/v1/executor/death-cases/{caseId}/submit` | `{ etag, confirm: true }` | 200 `DeathCaseResponse` (gửi lặp: trạng thái hiện tại) | 400, 404, 409 `DEATH_CASE_INCOMPLETE` / `DEATH_CASE_NOT_ALLOWED` / `EDIT_CONFLICT` |
| POST | `/v1/executor/death-cases/{caseId}/versions` | `{ etag }` | **201** `DeathCaseResponse` (bản nháp mới) | 404, 409 `DEATH_CASE_NOT_ALLOWED` (`CASE_NOT_REVISABLE`) / `EDIT_CONFLICT` |

```jsonc
// ExecutorVaultResponse
{ "vaultId": "0199...", "ownerFullName": "Nguyễn Văn Chủ", "role": "PRIMARY", "planStatus": "CHECKIN_OVERDUE",
  "checkInOverdue": true, "openDeathCaseId": null, "lastRejectedDeathCaseId": null }
// lastRejectedDeathCaseId: hồ sơ bị từ chối gần nhất của kho (null khi chưa có); mở bằng GET /v1/executor/death-cases/{caseId} để xem rejectReason

// DeathCaseResponse — KHÔNG có CCCD đầy đủ, KHÔNG có thông tin Verifier
{
  "id": "0199...", "vaultId": "0199...", "status": "UNDER_REVIEW",
  "currentVersion": {
    "versionNo": 1, "isDraft": false,
    "ownerFullName": "Nguyễn Văn An", "ownerCitizenIdLast4": "8901", "dateOfDeath": "2026-10-10",
    "certificateNumber": "123/2026/TT", "issuedOn": "2026-10-11", "issuedBy": "UBND phường Bến Nghé",
    "files": [ { "id": "0199...", "position": 1, "fileName": "giay-chung-tu.pdf", "contentType": "application/pdf",
                 "sizeBytes": 1048576, "uploadedAt": "2026-10-15T03:00:00+00:00" } ],
    "submittedAt": "2026-10-15T03:05:00+00:00"
  },
  "reviewVersionNo": 1,
  "missingRequirements": [],
  "supplementRequest": null,   // khi SUPPLEMENT_REQUIRED: { "versionNo": 1, "reason": "...", "requestedParts": ["FILES"], "requestedAt": "..." }
  "rejectReason": null,        // khi REJECTED: lý do Verifier từ chối
  "ownerNotice": { "status": "DELIVERED", "deliveredAt": "2026-10-15T03:06:00+00:00" },
  "objectionEndsAt": "2026-10-18T03:06:00+00:00",
  "createdAt": "2026-10-15T03:00:00+00:00", "updatedAt": "2026-10-15T03:05:00+00:00", "etag": "\"639...\""
}
```

- `status`: `DRAFT` (mới mở, chưa gửi lần nào) | `UNDER_REVIEW` | `SUPPLEMENT_REQUIRED` | `APPROVED_FOR_HANDOVER` | `REJECTED` | `SUSPENDED`. Bốn trạng thái sau `UNDER_REVIEW` do phần Verifier/Owner đặt; mục này chỉ trả về chúng.
- **Mở hồ sơ**: mỗi kho chỉ có một hồ sơ đang mở (mọi trạng thái trừ `REJECTED`, kể cả `DRAFT`). Hai Executor mở cùng lúc vẫn ra một hồ sơ: một bên nhận 201, bên kia 200 cùng `id`. Chỉ mở được khi kế hoạch đang chạy (`ACTIVE`/`CHECKIN_OVERDUE`/`AWAITING_CHECK`); dịch vụ đang ân hạn (chỉ đọc) **vẫn mở được**. Mở hồ sơ nháp **chưa** khóa kế hoạch.
- **Sửa bản nháp**: chuỗi `null`/không gửi = giữ nguyên, `""` = xóa; ngày `null` = giữ nguyên. `ownerCitizenId` được bỏ khoảng trắng và phải đủ 12 chữ số. Ngày mất không được ở tương lai; ngày cấp không được trước ngày mất và không được ở tương lai (theo giờ Việt Nam). `etag` lấy từ response gần nhất; lệch → `409 EDIT_CONFLICT`. Họ tên Owner, CCCD Owner, số giấy và nơi cấp được mã hóa; response chỉ có `ownerCitizenIdLast4`.
- **Trang giấy**: PDF, JPEG, PNG hoặc WEBP, tối đa 20 MiB mỗi tệp, tối đa 10 tệp mỗi bản. Loại tệp xác định theo **nội dung thật**, không theo đuôi hay Content-Type: tệp đổi đuôi (ví dụ `.exe` thành `.pdf`) bị `400`. Nội dung được mã hóa khi lưu và **không tính vào dung lượng kho**. Response chỉ có metadata, chưa có API tải trang về.
- **Gửi hồ sơ** (`submit`): cần `confirm = true` (Executor xác nhận thông tin đúng), đủ 6 trường và ít nhất một trang, nếu không `409 DEATH_CASE_INCOMPLETE` kèm `missingRequirements` (`OWNER_FULL_NAME`, `OWNER_CITIZEN_ID`, `DATE_OF_DEATH`, `CERTIFICATE_NUMBER`, `ISSUED_ON`, `ISSUED_BY`, `FILES`). Trong **một lần lưu**: khóa bản vừa gửi (không bao giờ sửa lại), chuyển kế hoạch sang `DEATH_CASE_OPEN` (lần gửi đầu; từ đó Owner không sửa kho được: `VAULT_NOT_EDITABLE` / `PLAN_LOCKED`, không điểm danh được: `CHECKIN_NOT_ALLOWED`), đặt cờ nếu Owner có điểm danh sau ngày mất, xếp thông báo cho Owner và phân công Verifier. Kế hoạch bị đổi ở nơi khác đúng lúc đó (Owner điểm danh, job quá hạn) → `409 EDIT_CONFLICT`, không ghi đè. Gửi lặp bản đã gửi trả trạng thái hiện tại.
- **AI nhận diện giấy chứng tử khi gửi**: sau khi đủ trường và có trang, server đọc lần lượt các trang (theo thứ tự) và hỏi AI (Gemini) trang đó có phải giấy chứng tử / trích lục khai tử không; dừng ở trang đầu tiên AI nhận ra (chắc ≥ 0,7). AI xem hết mà **không trang nào** là giấy chứng tử → `409 DEATH_CASE_INCOMPLETE` với `missingRequirements = ["DEATH_CERTIFICATE_NOT_RECOGNIZED"]` và `fileChecks` = danh sách `{ fileId, fileName, status, documentType }` (`status`: `RECOGNIZED` | `NOT_RECOGNIZED` | `UNAVAILABLE`; `documentType` là loại giấy AI nhận ra, có thể `null`); hồ sơ vẫn là bản nháp, kế hoạch không khóa. AI **không kiểm tra được** (chưa cấu hình `Gemini:ApiKey`, lỗi, timeout 20 giây mỗi trang, trang > 14 MiB) và chưa trang nào được nhận ra → vẫn gửi bình thường nhưng đặt cờ `CERTIFICATE_UNVERIFIED` (chặn duyệt tới khi Verifier xem xét như mọi cờ). Gửi bị chặn rồi gửi lại thì AI chạy lại từ đầu. Mã `DEATH_CERTIFICATE_NOT_RECOGNIZED` không dùng được trong `requestedParts` của Verifier (Verifier yêu cầu `FILES`). Nội dung trang giấy được gửi sang Google Gemini; AI chỉ nhận ra hình thức giấy, không xác thực giấy thật hay giả.
- **Phiên bản bổ sung** (`versions`): khi hồ sơ `UNDER_REVIEW` hoặc `SUPPLEMENT_REQUIRED`, tạo bản `versionNo + 1` chép dữ liệu và các trang của bản đã gửi (dùng chung nội dung đã lưu). Mỗi hồ sơ chỉ có một bản nháp (`CASE_NOT_REVISABLE` nếu đã có hoặc chưa có bản nào được gửi). Verifier vẫn xét bản gửi gần nhất (`reviewVersionNo`) cho tới khi bản mới được gửi và xác nhận lại. Bản đã gửi không đổi.
- **Phía Verifier tác động đến Executor (mục 6.12)**: `supplementRequest` chỉ có khi hồ sơ `SUPPLEMENT_REQUIRED` (yêu cầu bổ sung gần nhất; mã `requestedParts` viết hoa như `missingRequirements`; **không** có thông tin Verifier) và về `null` khi Executor gửi lại. `rejectReason` chỉ có khi hồ sơ `REJECTED` (lý do Verifier nhập, để Executor mở hồ sơ mới); trạng thái khác thì `null`. `ownerNotice.status` có thể là `CANCELLED` (hồ sơ bị từ chối trước khi gửi được thư cho Owner). **Thao tác của Verifier làm đổi `etag` hồ sơ**: Executor đang sửa bản nháp có thể nhận `409 EDIT_CONFLICT` và phải tải lại hồ sơ rồi sửa tiếp. Executor gửi bản mới (không phải lần đầu) thì hạn xét của Verifier được tính lại. **Hồ sơ bị job nền giao lại cho Verifier khác (mục 6.12) cũng làm đổi `etag`**: Executor đang sửa bản nháp đúng lúc đó có thể nhận `409 EDIT_CONFLICT` và phải tải lại hồ sơ. Ngoài khả năng đó, Executor không thấy gì về Verifier hay việc giao lại (response không có thông tin Verifier).
- **Thông báo Owner**: gửi hồ sơ chỉ ghi nhận thông báo (`ownerNotice.status = PENDING`); job nền gửi email, được SMTP nhận thì `DELIVERED` và `objectionEndsAt` = lúc giao + 72 giờ. Thử tối đa 5 lần rồi `FAILED` (đồng hồ chưa chạy). Gửi bản bổ sung đổi **họ tên Owner, CCCD Owner hoặc ngày mất** thì báo lại và chạy lại 72 giờ; đổi số giấy, nơi cấp, ngày cấp thì giữ nguyên đồng hồ.

**Các điểm khác SRS (đã chốt với nhóm):**
- Trạng thái kế hoạch trước khi gửi lần đầu lưu ở hồ sơ (`StatusBeforeCase`), không thêm cột vào `HandoverPlans` (SRS §11.6).
- "Điểm danh sau ngày mất" (DEATH-10) tính từ 00:00 giờ Việt Nam của ngày kế tiếp ngày mất.
- Thông báo Owner theo hàng đợi (outbox) và SMTP nhận thư được coi là "đã giao" (DEATH-08); sự cố khi mọi kênh đều lỗi (F4) chưa làm, hiện chỉ ghi log.
- Phân công Verifier xoay vòng theo lần được giao gần nhất; loại Owner, Executor đang hiệu lực của kho và người gửi. Chưa loại được người nhận vì người nhận chưa gắn tài khoản trước F3 (ROLE-03 một phần). Không có Verifier phù hợp thì hồ sơ để trống và job nền giao người ngay khi có Verifier phù hợp (ASSIGN-04; xem đoạn "Phân công Verifier (job nền)" ở mục 6.12).
- Snapshot bàn giao ghim lúc **duyệt** (`IHandoverStarter`), không ghim lúc Executor gửi.
- Chưa làm: Owner phản đối "Tôi còn sống" (DEATH-07), tạm dừng (DEATH-13), sự cố F4, nhật ký kiểm toán. Verifier xét/duyệt/từ chối đã có ở mục 6.12.

### 6.12. Verifier xét hồ sơ chứng tử

Verifier được giao hồ sơ xem danh sách, chi tiết và các trang giấy chứng tử (SRS §6.2 F2B, §6.4 ASSIGN). Cần đăng nhập (Bearer). Mọi API kiểm tài khoản từ **DB** (không tin claim `role` trong token, ROLE-04) theo thứ tự: không có tài khoản `401 SESSION_EXPIRED`; không `Active` `403 ACCOUNT_NOT_ACTIVE`; không phải Verifier `403 ROLE_NOT_ALLOWED`. Sau đó hồ sơ không tồn tại, hoặc **tôi không có phân công đang hiệu lực** (`EndedAt` rỗng) trên hồ sơ đó, đều là `404 DEATH_CASE_NOT_FOUND` giống nhau (SEC-04, SEC-05). Phân công đã kết thúc (giao lại người khác) thì không còn thấy hồ sơ.

| Method | URL | Body | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/v1/verifier/death-cases` | query mục 5: `pageIndex`, `pageSize`, `sortBy` = `dueAt` (mặc định) \| `assignedAt` (không phân biệt hoa thường), `isAscending`; `searchTerm` **bị bỏ qua** | 200 `PaginatedList<VerifierDeathCaseSummaryResponse>` | 400 (`sortBy` lạ), 401, 403 |
| GET | `/v1/verifier/death-cases/{caseId}` | — | 200 `VerifierDeathCaseResponse`, `Cache-Control: no-store` | 401, 403, 404 |
| GET | `/v1/verifier/death-cases/{caseId}/files/{fileId}/content` | — | 200 nội dung trang (byte gốc) | 401, 403, 404, 500 `CONTENT_INTEGRITY_FAILED` |
| GET | `/v1/verifier/death-cases/{caseId}/owner-identity/document-image` | — | 200 ảnh mặt trước CCCD Owner chụp lúc eKYC (`image/jpeg` \| `image/png` \| `image/webp`, `Content-Disposition: inline`, `Cache-Control: private, no-store`) | 401, 403, 404 `DEATH_CASE_NOT_FOUND` / `IDENTITY_IMAGE_NOT_FOUND`, 500 `CONTENT_INTEGRITY_FAILED` |
| POST | `/v1/verifier/death-cases/{caseId}/flags/{flagId}/review` | `{ etag, note }` | 200 `VerifierDeathCaseResponse` (etag mới) | 400, 403, 404, 409 `EDIT_CONFLICT` / `DEATH_CASE_NOT_ALLOWED` |
| POST | `/v1/verifier/death-cases/{caseId}/request-supplement` | `{ etag, reason, requestedParts: [...] }` | 200 `VerifierDeathCaseResponse` | 400, 403, 404, 409 `EDIT_CONFLICT` / `DEATH_CASE_NOT_ALLOWED` |
| POST | `/v1/verifier/death-cases/{caseId}/approve` | `{ etag, reason, password }` | 200 `VerifierDeathCaseResponse` (kèm `handover` ở lần duyệt đầu) | 400, 403 `SIGNING_PASSWORD_INVALID`, 404, 409 `DEATH_CASE_NOT_ALLOWED` (+ `approveBlockers`) / `EDIT_CONFLICT` / `HANDOVER_NOT_ALLOWED`, 423 `SIGNING_LOCKED` |
| POST | `/v1/verifier/death-cases/{caseId}/reject` | `{ etag, reason, password }` | 200 `VerifierDeathCaseResponse` | 400, 403 `SIGNING_PASSWORD_INVALID`, 404, 409 `EDIT_CONFLICT` / `DEATH_CASE_NOT_ALLOWED` (`CASE_CLOSED`, `NOT_UNDER_REVIEW`, `VERIFIER_CONFLICT`, `PLAN_STATE_INVALID`), 423 `SIGNING_LOCKED` |
| GET | `/v1/verifier/death-cases/{caseId}/decision-signature` | — | 200 `DecisionSignatureResponse`, `Cache-Control: no-store` | 401, 403 `ROLE_NOT_ALLOWED`, 404 `DEATH_CASE_NOT_FOUND` |

```jsonc
// VerifierDeathCaseSummaryResponse (mỗi phần tử của items) — chỉ 4 số cuối CCCD
{ "id": "0199...", "status": "UNDER_REVIEW", "reviewVersionNo": 1, "ownerCitizenIdLast4": "8901",
  "dateOfDeath": "2026-10-10", "submittedAt": "2026-10-15T03:05:00+00:00",
  "assignedAt": "2026-10-15T03:05:00+00:00", "dueAt": "2026-10-17T03:05:00+00:00",
  "objectionEndsAt": null, "supplementOverdueAt": null, "unreviewedFlagCount": 0, "closedAt": null }

// VerifierDeathCaseResponse
{
  "id": "0199...", "vaultId": "0199...", "status": "UNDER_REVIEW", "etag": "\"639...\"",
  "createdAt": "...", "firstSubmittedAt": "...", "closedAt": null,
  "reviewVersion": { "versionNo": 2, "ownerFullName": "Nguyễn Văn An", "ownerCitizenId": "012345678901",
    "ownerCitizenIdLast4": "8901", "dateOfDeath": "2026-10-10", "certificateNumber": "123/2026/TT",
    "issuedOn": "2026-10-11", "issuedBy": "UBND phường Bến Nghé", "submittedAt": "...",
    "files": [ { "id": "0199...", "position": 1, "fileName": "trang.pdf", "contentType": "application/pdf", "sizeBytes": 531, "uploadedAt": "..." } ] },
  "versions": [ { "versionNo": 1, "ownerCitizenIdLast4": "8901", "dateOfDeath": "2026-10-10", "submittedAt": "...", "files": [] } ],
  "flags": [ { "id": "0199...", "kind": "CHECKIN_AFTER_DEATH", "detail": "...", "createdAt": "...", "reviewedAt": null, "reviewNote": null } ],
  "decisions": [],
  "ownerNotice": { "status": "DELIVERED", "deliveredAt": "..." },
  "objectionEndsAt": "...",
  "supplementOverdueAt": null,
  "assignment": { "assignedAt": "...", "dueAt": "..." },
  "canApprove": false, "approveBlockers": ["OBJECTION_PERIOD_NOT_OVER"],
  "previousCase": null, "handover": null,
  "ownerIdentity": { "verified": true, "citizenId": "012345678901", "fullName": "NGUYỄN VĂN AN",
    "dateOfBirth": "1950-03-04", "verifiedAt": "...", "citizenIdMatches": true, "fullNameMatches": true,
    "hasDocumentImage": true, "documentImageCapturedAt": "..." },
  "decisionSignatureStatus": "NO_FINAL_DECISION"
}

// DecisionSignatureResponse (GET .../decision-signature)
{ "status": "VALID", "decisionKind": "APPROVED", "versionNo": 2, "decidedAt": "2026-10-30T10:15:30.1234567+00:00",
  "signedByUserId": "0199...", "signedByFullName": "Trần Thị B", "algorithm": "RSA-2048-PSS-SHA256",
  "keyFingerprint": "7F3A-91C2-04BE-55D1", "signatureBase64": "k3Jx...==" }
```

- **`ownerIdentity`** (BR-48 sửa 25/10/2026): danh tính Owner đã gắn qua eKYC (`OWNER_ENROLLMENT` đạt), giải mã để Verifier được giao đối chiếu với bản Executor khai, gồm **đủ 12 số CCCD**, họ tên, ngày sinh. Luôn có object; Owner chưa xác minh thì `verified = false` và các trường còn lại `null`/`false`. `citizenIdMatches` so đủ 12 số; `fullNameMatches` so họ tên theo cách chuẩn hóa của eKYC (NFC, bỏ khoảng trắng thừa, không phân biệt hoa thường, **giữ dấu**); `null` khi bản đang xét chưa có trường đó. Ngày sinh không có trong bản Executor khai nên không so. `hasDocumentImage` cho biết có ảnh mặt trước CCCD lúc eKYC để xem qua `GET .../owner-identity/document-image` (Owner xác minh trước khi có chức năng này thì `false`).
- **Ảnh CCCD lúc eKYC** (`owner-identity/document-image`): cùng cách bảo vệ với trang giấy chứng tử (Bearer, chỉ Verifier đang được giao, giải mã hai lượt, hỏng thì `500 CONTENT_INTEGRITY_FAILED` không kèm byte nào), nhưng trả `inline` với kiểu ảnh thật để FE hiển thị (`X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`). Máy chủ ghi log "đã xem ảnh CCCD" (chỉ Id hồ sơ).

- **Dữ liệu trả (DATA-02, cách hiểu):** `reviewVersion` (bản Executor gửi gần nhất, `reviewVersionNo`) có đủ họ tên, **CCCD 12 số**, số giấy, nơi cấp, vì Verifier được giao là "người cần" (SEC-05) và ảnh giấy chứng tử vốn có số đầy đủ. Câu "không trả CCCD đầy đủ trước khi xác minh xong" áp cho **người nhận ở F3**, không áp cho CCCD của Owner trong hồ sơ chứng tử. `versions[]` (các bản đã gửi khác) chỉ có cột không mã hóa (4 số cuối, ngày mất, `submittedAt`, metadata trang). Danh sách chỉ có 4 số cuối. Verifier **không bao giờ** thấy bản nháp. Không trả thông tin Executor, email Owner, khóa lưu trữ, SHA-256, Id người quyết định.
- **`approveBlockers`** (cộng dồn, theo thứ tự): `NOT_UNDER_REVIEW` (hồ sơ không ở `UNDER_REVIEW`), `NEWER_VERSION_PENDING` (Executor đang có bản nháp mới hơn bản đang xét), `OWNER_NOT_NOTIFIED` (đồng hồ 72 giờ chưa chạy), `OBJECTION_PERIOD_NOT_OVER` (DEATH-09), `FLAGS_NOT_REVIEWED` (DEATH-10, chỉ cờ của bản đang xét). `VERIFIER_CONFLICT` (ROLE-03: Verifier là Owner, Executor đang hiệu lực của kho, người gửi bản đang xét hoặc người nhận của kho) được thêm vào **cuối** danh sách ở response chi tiết và response ghi, để FE không hiện nút duyệt với người sẽ bị chặn. `canApprove` = danh sách rỗng. Chưa có hold/phản đối F2C.
- **`ownerNotice.status`**: `PENDING` | `DELIVERED` | `FAILED` | `CANCELLED`.
- **`supplementOverdueAt`** (DEATH-01, có ở danh sách và chi tiết): lúc job nền đánh dấu Executor bổ sung quá hạn 7 ngày kể từ khi Verifier yêu cầu bổ sung; `null` khi chưa quá hạn. Về `null` khi Executor gửi lại bản mới. Chỉ để hiển thị (FE hiện nhãn "Bổ sung quá hạn"); hồ sơ vẫn `SUPPLEMENT_REQUIRED`, Verifier tự quyết định từ chối hay chờ tiếp.
- **`previousCase`** (DEATH-12): hồ sơ bị từ chối trước đó của cùng kho (`id`, `closedAt`, `rejectReason`), hoặc `null`.
- **Xem trang (DATA-06):** gọi bằng Bearer access token ngắn hạn, gắn đúng người dùng, **không** phải đường link ký sẵn dùng chung. Chỉ xem được trang của bản **đã gửi** của đúng hồ sơ (kể cả bản cũ); trang của bản nháp hoặc của hồ sơ khác là `404`. Nội dung được giải mã hai lượt: lượt 1 kiểm toàn vẹn (kích thước, SHA-256), chỉ khi qua mới gửi byte đầu tiên; hỏng hoặc mất tệp thì `500 CONTENT_INTEGRITY_FAILED` và không có byte nội dung nào. Response: `Content-Type` giữ nguyên nếu là PDF/JPEG/PNG/WEBP (còn lại `application/octet-stream`), `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`. Máy chủ ghi log "đã xem trang" (chỉ Id hồ sơ, Id tệp, Id người xem); TODO chuyển sang nhật ký kiểm toán (SEC-07, DATA-07).

- **Thao tác ghi của Verifier** (xem xét cờ, yêu cầu bổ sung, từ chối, duyệt) đều bắt buộc `etag`, kiểm theo thứ tự: chuẩn hóa input (400) → tài khoản Verifier và phân công đang hiệu lực (401/403/404) → [xem xét cờ: tìm cờ của bản đang xét, không thấy 404] → [từ chối, duyệt: mật khẩu xác nhận ký `403 SIGNING_PASSWORD_INVALID` / `423 SIGNING_LOCKED`, xem "Chữ ký số" bên dưới] → gọi lặp → hồ sơ đã đóng (`CASE_CLOSED`) → `etag` (`EDIT_CONFLICT`) → trạng thái (`NOT_UNDER_REVIEW`) → `VERIFIER_CONFLICT` → ghi một lần. Mọi thao tác ghi làm đổi `etag` hồ sơ; FE dùng `etag` trong response cho thao tác kế tiếp (ví dụ xem xét cờ xong thì lấy `etag` mới để bấm duyệt).
- **Xem xét cờ** (`flags/{flagId}/review`): chỉ cờ của bản đang xét (cờ của bản cũ hoặc hồ sơ khác: `404`). `note` bắt buộc, ≤ 1000 ký tự. Gọi lặp trên cờ đã xem xét trả `200` và **giữ ghi chú đầu**, không kiểm `etag`. Hết cờ chưa xem xét thì `FLAGS_NOT_REVIEWED` biến mất khỏi `approveBlockers`.
- **Yêu cầu bổ sung** (`request-supplement`): `reason` bắt buộc ≤ 1000 ký tự; `requestedParts` bắt buộc có ít nhất một phần, **mỗi phần phải là mã viết hoa đúng như `missingRequirements`** (`OWNER_FULL_NAME`, `OWNER_CITIZEN_ID`, `DATE_OF_DEATH`, `CERTIFICATE_NUMBER`, `ISSUED_ON`, `ISSUED_BY`, `FILES`; chữ thường hoặc mã lạ là `400`). Server bỏ trùng, sắp xếp rồi lưu. Hồ sơ → `SUPPLEMENT_REQUIRED`; **phân công, kế hoạch và đồng hồ 72 giờ giữ nguyên**. Làm được cả khi Executor đang soạn bản nháp. Gọi lặp cùng nội dung khi hồ sơ đang `SUPPLEMENT_REQUIRED` trả `200`, không thêm dòng (kiểm trước `etag`). Sau khi lưu, gửi thư cho mọi Executor còn nhận việc được của kho (mã hồ sơ và hạn bổ sung, không có lý do); gửi lỗi chỉ ghi log, không ảnh hưởng quyết định; gọi lặp không gửi lại. Mỗi lần Executor gửi bản mới **không phải lần đầu**, hạn xét (`assignment.dueAt`) của phân công đang hiệu lực được tính lại = lúc gửi + 48 giờ, nhưng không sớm hơn 48 giờ sau khi hết phản đối (`assignedAt` giữ nguyên; xem "Phân công Verifier (job nền)").
- **Từ chối** (`reject`): `reason` bắt buộc ≤ 1000 ký tự; Executor sẽ thấy (`rejectReason` ở mục 6.11). Làm được từ `UNDER_REVIEW` hoặc `SUPPLEMENT_REQUIRED`, kể cả khi Executor đang có bản nháp. Trong **một lần lưu**: thêm quyết định `REJECTED` (gắn `reviewVersionNo` và phân công đang hiệu lực), hồ sơ → `REJECTED` + `closedAt`, **kế hoạch trở về trạng thái trước khi gửi hồ sơ** (`ACTIVE` / `CHECKIN_OVERDUE` / `AWAITING_CHECK`; Owner điểm danh lại được), thông báo Owner còn `PENDING` → `CANCELLED` (job không gửi nữa; `DELIVERED`/`FAILED` giữ nguyên; không gửi email cho Owner khi từ chối). Sau khi lưu, gửi thư báo Executor còn nhận việc được của kho (không có lý do); gửi lỗi chỉ ghi log; gọi lặp không gửi lại. Phân công **không** kết thúc (chỉ giao lại mới kết thúc). Kế hoạch không ở `DEATH_CASE_OPEN` hoặc hồ sơ thiếu trạng thái trước → `409 DEATH_CASE_NOT_ALLOWED` `PLAN_STATE_INVALID`, không ghi gì. Gọi lặp trên hồ sơ đã bị từ chối → `200` trạng thái hiện tại (không thêm dòng, kiểm trước `etag`); hồ sơ đã được duyệt → `409 CASE_CLOSED`. Mỗi hồ sơ chỉ có tối đa một quyết định cuối (`REJECTED` hoặc `APPROVED`, DB chặn bằng chỉ mục unique); duyệt và từ chối cùng lúc: xem mục "Duyệt và từ chối đồng thời" bên dưới.
- **Trễ tối đa 60 giây sau khi từ chối (NFR-03)**: kế hoạch chỉ được trả về trạng thái trước đó; kỳ điểm danh không bị sửa. Job điểm danh tính lại theo kỳ (ví dụ `ACTIVE` → `CHECKIN_OVERDUE` nếu kỳ đã quá hạn trong lúc xét) trong vòng 60 giây, nên trạng thái kế hoạch có thể trễ tối đa 60 giây so với lịch. API điểm danh trả trạng thái hiệu lực theo giờ server nên FE không sai.
- **"Nhả snapshot" (SRS §11.2)**: snapshot bàn giao chỉ được ghim lúc **duyệt**, nên khi từ chối chưa có snapshot nào để nhả.
- **Duyệt** (`approve`, DEATH-06): `reason` bắt buộc ≤ 1000 ký tự. Kiểm theo thứ tự: chuẩn hóa input → tài khoản Verifier và phân công đang hiệu lực → mật khẩu xác nhận ký (DEATH-14) → hồ sơ đã đóng (đã duyệt: `200`, `handover = null`, không thêm dòng, không gọi lại bàn giao; đã bị từ chối: `409 CASE_CLOSED`) → `etag` → `approveBlockers` (khác rỗng: `409 DEATH_CASE_NOT_ALLOWED`, `reason` = phần tử đầu, `approveBlockers` = cả danh sách) → `VERIFIER_CONFLICT` → kế hoạch không ở `DEATH_CASE_OPEN`: `409 PLAN_STATE_INVALID`. Rồi gọi `IHandoverStarter.StartAsync` và **lưu một lần duy nhất**: quyết định `APPROVED` (gắn `reviewVersionNo`, người duyệt, phân công), hồ sơ → `APPROVED_FOR_HANDOVER` + `closedAt`, kế hoạch → `HANDOVER_IN_PROGRESS`, mỗi chỉ định của gói `READY` một lượt bàn giao kèm snapshot. Sau đó gửi lời mời cho người nhận; gửi lỗi không làm mất lượt. **Không** cấp quyền tải (grant) và không giải mã tài sản ở bước này (DEATH-06).
- **`handover`** (chỉ ở response duyệt lần đầu, còn lại `null`): `{ turnCount, invitationsSent, invitationsFailed }`.
- **Duyệt không mở được bàn giao**: không có gói `READY` nào → `409 HANDOVER_NOT_ALLOWED` với `reason = NOTHING_TO_HAND_OVER`; hồ sơ vẫn `UNDER_REVIEW`, không có dòng `APPROVED`, không có lượt. Kế hoạch đã có lượt bàn giao mà vẫn `DEATH_CASE_OPEN` (dữ liệu lệch) → `409 PLAN_STATE_INVALID`, hồ sơ vẫn đang xét.
- **Duyệt và từ chối đồng thời (AC-71)**: mỗi hồ sơ chỉ có tối đa **một quyết định cuối** (`APPROVED` hoặc `REJECTED`; DB chặn bằng chỉ mục unique) và mỗi chỉ định của gói chỉ một lượt bàn giao. Khi hai thao tác tranh nhau (hai Verifier hoặc hai tab), server đọc lại hồ sơ trước khi báo lỗi: bên thua nhận `200` với trạng thái hiện tại nếu bên thắng cùng quyết định (duyệt: `handover = null`), nhận `409 DEATH_CASE_NOT_ALLOWED` / `CASE_CLOSED` nếu bên thắng quyết định khác (ví dụ một bên duyệt, một bên từ chối), hoặc `409 EDIT_CONFLICT` nếu `etag` lệch (hiếm). Không bao giờ trả `PLAN_STATE_INVALID` chỉ vì bên kia vừa đổi kế hoạch. Gọi lặp cùng quyết định trên hồ sơ đã đóng luôn trả `200` (không thêm dòng, không mở bàn giao lần nữa): duyệt lặp trả `handover = null`.
- **Chữ ký số của Verifier (DEATH-14, đề xuất; chờ cập nhật SRS)**: đây là **chữ ký số nội bộ hệ thống**, không phải chữ ký số có giá trị pháp lý của CA công cộng.
  - **Nhập lại mật khẩu**: Duyệt và Từ chối bắt buộc `password` (mật khẩu tài khoản, ≤ 256 ký tự; thiếu → `400 VALIDATION_ERROR`). Kiểm **sau** khi xác định đúng Verifier đang được giao (người không được giao vẫn `404`, không tốn lần đoán) và **trước** nhánh gọi lặp (gọi lặp cũng phải đúng mật khẩu). Sai → `403 SIGNING_PASSWORD_INVALID` kèm `attemptsRemaining` (dùng 403, **không** 401, để FE không đăng xuất). Tài khoản chỉ đăng nhập Google (chưa có mật khẩu) → `403 SIGNING_PASSWORD_INVALID` với câu báo riêng, không có `attemptsRemaining`, không tính là một lần đoán. Mật khẩu không được log hay ghi nhật ký. Yêu cầu bổ sung và xem xét cờ không cần mật khẩu.
  - **Khóa lũy tiến** (như PIN kho mục 6.20): sai 5 lần liên tiếp (tính chung Duyệt và Từ chối, mọi hồ sơ của Verifier đó) thì **chỉ chức năng ký** bị khóa: lần 1 15 phút, lần 2 1 giờ, từ lần 3 24 giờ; lần sai thứ 5 trả luôn `423 SIGNING_LOCKED` kèm `lockedUntil`. Đang khóa thì mật khẩu đúng cũng `423`. Đúng mật khẩu thì bộ đếm và bậc khóa về 0. Vẫn đăng nhập, xem danh sách, chi tiết, trang giấy bình thường. Hai request sai cùng lúc tranh lưu bộ đếm → `409 EDIT_CONFLICT` ("thử lại"). Bị khóa lâu có thể làm quá hạn xét và job giao hồ sơ cho Verifier khác (ASSIGN-04).
  - **Ký**: quyết định `APPROVED` và `REJECTED` được ký bằng RSA-2048, SHA-256, đệm PSS (`RSA-2048-PSS-SHA256`) trong cùng lần lưu với quyết định; `SUPPLEMENT_REQUESTED` không ký. Mỗi Verifier một khóa đang dùng, tạo tự động ở lần ký đầu; khóa riêng mã hóa envelope bằng KEK của server (giống dữ liệu nhạy cảm khác), không bao giờ trả về. Chuỗi được ký gồm Id quyết định, Id hồ sơ, bản được xét, loại quyết định, Id Verifier, thời điểm quyết định, lý do và Id các trang giấy của bản được xét (theo thứ tự); chuỗi **không lưu** mà dựng lại từ dữ liệu khi kiểm.
  - **Kiểm tra lại** (`decision-signature`, chỉ Verifier đang được giao hồ sơ; Executor, Admin chưa xem được): `status` = `VALID` (khớp) / `INVALID` (dữ liệu quyết định hoặc trang giấy đã bị sửa sau khi ký, hoặc khóa không khớp) / `NOT_SIGNED` (quyết định cuối cũ trước khi có chữ ký số) / `NO_FINAL_DECISION` (chưa duyệt hay từ chối; các trường còn lại `null`). Kiểm cả chữ ký với khóa công khai **và** khóa riêng đã bọc phải đúng cặp với khóa công khai, nên thay cả khóa công khai lẫn chữ ký trong DB vẫn ra `INVALID`. `keyFingerprint` = 16 ký tự hex đầu SHA-256 của khóa công khai, nhóm 4. Không ghi gì.
  - **Tự kiểm khi mở chi tiết (D12)**: `VerifierDeathCaseResponse.decisionSignatureStatus` dùng cùng luật và cùng giá trị như `status` ở trên, có ở GET chi tiết và mọi response ghi của Verifier, để FE hiện nhãn "đã ký số hợp lệ" / "dữ liệu đã bị sửa sau khi ký" mà không cần bấm nút. Danh sách hồ sơ **không** có trường này.
  - **Giới hạn**: server giữ khóa riêng (đã bọc bằng KEK), nên ai có cả KEK và DB về kỹ thuật vẫn ký thay được; mật khẩu chỉ là bước xác nhận trong code. Chưa có thu hồi khóa, Admin mở khóa ký, email báo khi bị khóa ký, nhật ký lần nhập sai.
- **ROLE-03 (`VERIFIER_CONFLICT`)**: Verifier là Owner của kho, Executor đang hiệu lực của kho, người gửi bản đang xét, hoặc người nhận của kho thì không được ghi (xem thì vẫn được). "Người nhận của kho" là người có chỉ định trong gói `READY` (đúng tập vào snapshot lúc duyệt), nhận ra bằng **email** người nhận khớp email tài khoản Verifier (đã chuẩn hóa chữ thường); lúc chọn Verifier cũng loại những tài khoản này. Giới hạn: người nhận không khai email, hoặc tạo tài khoản bằng email khác, thì chưa bắt được trước F3.

**Phân công Verifier (job nền, ASSIGN-02, ASSIGN-03, ASSIGN-04, TIME-02, NFR-03).** Không có API mới; job `DeathCaseAssignmentMonitor` quét mỗi 60 giây (`DeathCase:AssignmentPollSeconds`, tắt bằng `DeathCase:AssignmentJobEnabled = false`) và chỉ ảnh hưởng tới những gì FE đã đọc như sau:
- **`dueAt` = hạn Verifier phải ra quyết định**, không sớm hơn 48 giờ sau khi hồ sơ duyệt được: mốc muộn hơn giữa (lúc giao hoặc lúc Executor gửi bản mới gần nhất + 48 giờ) và (`objectionEndsAt` + 48 giờ). Danh sách Verifier xếp theo `dueAt` nên hồ sơ còn trong 72 giờ phản đối sẽ có `dueAt` xa hơn (D1). Hạn Verifier, 72 giờ phản đối và 7 ngày báo còn sống là các đồng hồ độc lập (TIME-07).
- **Hồ sơ `SUPPLEMENT_REQUIRED`** đang chờ Executor có thể hiện `dueAt` đã qua; job không nhắc và không giao lại vì quá hạn trong lúc chờ (khi Executor gửi lại, `dueAt` được tính lại).
- **Nhắc:** khi hồ sơ `UNDER_REVIEW` và còn không quá 12 giờ tới `dueAt`, Verifier đang giữ hồ sơ nhận một email nhắc (chỉ có mã hồ sơ rút gọn và hạn). Mỗi hạn chỉ nhắc thành công một lần; hạn bị dời thì nhắc lại cho hạn mới; gửi lỗi thì vòng sau thử lại. Hồ sơ quá hạn mà chưa có người thay thì người đang giữ vẫn nhận cảnh báo quá hạn.
- **Giao người:** hồ sơ `UNDER_REVIEW` hoặc `SUPPLEMENT_REQUIRED` chưa có Verifier đang giữ sẽ được giao khi có Verifier phù hợp (xoay vòng, loại Owner, Executor đang hiệu lực, người gửi bản đang xét).
- **Giao lại:** quá hạn khi `UNDER_REVIEW` (`OVERDUE`), hoặc Verifier đang giữ bị khóa/vô hiệu hóa/mất vai trò Verifier (`VERIFIER_UNAVAILABLE`, giao ngay cả khi `SUPPLEMENT_REQUIRED`). Phân công cũ chỉ kết thúc khi tìm được người thay; không có ai thì giữ nguyên và thử lại. Có thể giao lại cho người từng giữ trước đó (chỉ loại người đang giữ).
- **Tác động tới Verifier cũ:** giao lại làm đổi `etag` hồ sơ. Verifier cũ đang thao tác giữa chừng đúng lúc đó nhận `409 EDIT_CONFLICT`; tải lại hoặc gọi lại sau đó thì `404 DEATH_CASE_NOT_FOUND` (phân công đã kết thúc). Giao người cho hồ sơ chưa có người giữ và gửi nhắc **không** đổi `etag`.
- **Job không bao giờ duyệt, từ chối hay đổi trạng thái hồ sơ** (ASSIGN-04: quá hạn không tự cho đạt). Quyết định cũ (ví dụ yêu cầu bổ sung) vẫn giữ nguyên và gắn với phân công đã kết thúc; người mới tự kết luận các việc mới (ASSIGN-03).
- Chưa có email "bạn được giao" cho Verifier mới và chưa báo Admin khi hồ sơ quá hạn mà không có người thay (F4).

**Các điểm khác SRS, cách hiểu và phần chưa làm của mục 6.12:**
- **DATA-02 (cách hiểu):** câu "không trả CCCD đầy đủ trước khi xác minh xong" áp cho **người nhận ở F3**, không áp cho CCCD Owner trong hồ sơ chứng tử; Verifier được giao (SEC-05) thấy CCCD 12 số của bản đang xét, các bản cũ và danh sách chỉ có 4 số cuối.
- **DATA-06:** xem trang giấy bằng Bearer access token ngắn hạn gắn đúng người, không dùng link ký sẵn; kiểm toàn vẹn hai lượt trước khi gửi byte đầu tiên.
- **ROLE-03:** `VERIFIER_CONFLICT` kiểm Owner, Executor đang hiệu lực của kho, người gửi bản đang xét và người nhận của kho (so email người nhận trong gói `READY`; không bắt được người nhận không khai email hoặc dùng email khác cho tài khoản, chờ gắn danh tính ở F3). Response chi tiết thêm `VERIFIER_CONFLICT` vào cuối `approveBlockers` để FE không hiện nút duyệt.
- **ROLE-04 phần Verifier:** vai trò đọc từ DB mỗi lần gọi, đổi `Role` có hiệu lực ngay không cần đăng nhập lại (FE vẫn đọc claim trong token).
- **"Nhả snapshot" (§11.2):** snapshot ghim lúc duyệt nên khi từ chối chưa có gì để nhả.
- **Trễ tối đa 60 giây (NFR-03)** sau khi từ chối, xem bullet trên; API điểm danh trả trạng thái hiệu lực theo giờ server.
- **Một quyết định cuối** cho mỗi hồ sơ; yêu cầu bổ sung được nhiều lần. Quyết định cuối chỉ đóng hồ sơ (`closedAt`), không kết thúc phân công.
- **Chữ ký số (DEATH-14 đề xuất)**: SRS 3.15.1 hiện còn ghi "chữ ký số" ở phần ngoài phạm vi và DEATH-03 ghi "giao diện không gọi đó là chữ ký số"; chức năng này chờ nhóm cập nhật SRS (bỏ khỏi ngoài phạm vi, sửa DEATH-03, thêm DEATH-14). Chữ ký nội bộ, không có chứng thư CA công cộng nên không có giá trị pháp lý.
- **Chưa làm:** email "bạn được giao" cho Verifier mới và báo Admin khi hồ sơ quá hạn không có người thay (F4); ASSIGN-05 (Executor bị kẹt); hold/phản đối "Tôi còn sống" của Owner (F2C, DEATH-07); nhật ký kiểm toán cho việc Verifier xem trang giấy (DATA-07, hiện chỉ ghi log; quyết định của Verifier đã ghi AuditLogs, xem mục nhật ký); email báo Executor khi có yêu cầu bổ sung; cho Executor xem lại trang giấy đã gửi.

### 6.13. Nhật ký hoạt động của kho

Owner xem nhật ký các thao tác trên kho của mình (SRS SEC-13, SEC-07). Nhật ký **chỉ đọc**: không có API sửa hay xóa (ADMIN-06, DATA-07). Cần đăng nhập (Bearer). Kho không tồn tại hoặc của người khác đều trả `404 VAULT_NOT_FOUND` giống nhau (SEC-04).

| Method | URL | Quyền | Body / Query | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/api/v1/vaults/{vaultId}/audit-logs` | Owner của kho | Query phân trang (`pageIndex`, `pageSize`, `sortBy`, `isAscending`) và bộ lọc `action?`, `from?`, `to?` | 200 `PaginatedList<AuditLogResponse>` | 400 `VALIDATION_ERROR`, 401, 404 `VAULT_NOT_FOUND` |

```json
// AuditLogResponse
{
  "id": "0199e7a4-5c1b-7c3e-9a51-3f0f5d2b8a10",
  "at": "2026-10-15T08:30:00+00:00",
  "actorRole": "OWNER",
  "actorUserId": "0199e7a4-0000-7c3e-9a51-3f0f5d2b8a10",
  "action": "CONTAINER_BENEFICIARY_ADDED",
  "targetType": "CONTAINER",
  "targetId": "0199e7a4-1111-7c3e-9a51-3f0f5d2b8a10",
  "targetVersion": 7,
  "outcome": "SUCCEEDED",
  "reason": "Người nhận 0199e7a4-2222-7c3e-9a51-3f0f5d2b8a10"
}
```

- **Sắp xếp:** `sortBy` chỉ nhận `at` (mặc định). `isAscending` mặc định `true` như mọi danh sách khác; FE gửi `isAscending=false` để xem mới nhất trước. `searchTerm` không dùng.
- **Lọc:** `action` là một giá trị trong bảng dưới (đúng dạng `UPPER_SNAKE_CASE`, giá trị khác → 400). `from` tính cả mốc, `to` không tính mốc, cả hai là ISO 8601; `from` phải trước `to`.
- **`targetVersion`:** phiên bản đối tượng **sau** thao tác. Với thao tác đổi cấu hình bàn giao (gói, người nhận đang ở gói, kế hoạch về nháp) là `config_version` của kế hoạch (SETUP-04, STATE-03); với điểm danh là số thứ tự kỳ mới; còn lại `null`.
- **Không có dữ liệu nhạy cảm (SEC-07):** không ghi tên tài sản, tên tệp, họ tên, số CCCD, liên hệ hay nội dung. Thao tác có đối tượng thứ hai chỉ ghi id của nó trong `reason` (`"Tài sản <id>"`, `"Người nhận <id>"`); FE tự tra tên qua API tài sản hoặc người nhận nếu cần.
- **Chỉ ghi thao tác đã lưu:** nhật ký lưu cùng giao dịch với thao tác (STATE-02), nên thao tác bị từ chối, lỗi hoặc bấm lặp không đổi gì thì không có dòng nào. Hiện mọi dòng đều có `outcome = SUCCEEDED`.

| `action` | `targetType` | Khi nào |
|---|---|---|
| `VAULT_CREATED` | `VAULT` | Tạo kho lần đầu (mở lại kho cũ không ghi) |
| `VAULT_UPDATED` | `VAULT` | Sửa tên hoặc mô tả kho |
| `ASSET_CREATED` | `ASSET` | Tải tài sản lên |
| `CONTAINER_CREATED` / `CONTAINER_UPDATED` / `CONTAINER_REMOVED` | `CONTAINER` | Tạo, đổi tên/mô tả, xóa gói. Nhật ký trước khi đổi tên còn `CONTAINER_ARCHIVED` (cùng nghĩa xóa gói) |
| `CONTAINER_ASSET_ADDED` / `CONTAINER_ASSET_REMOVED` | `CONTAINER` | Thêm, gỡ tài sản khỏi gói (`reason` có id tài sản) |
| `CONTAINER_BENEFICIARY_ADDED` / `CONTAINER_BENEFICIARY_REMOVED` | `CONTAINER` | Thêm, gỡ người nhận khỏi gói (`reason` có id hồ sơ) |
| `PLAN_DEACTIVATED` | `PLAN` | Owner xác nhận cảnh báo, kế hoạch về `DRAFT` vì mất gói `READY` cuối cùng (SETUP-16) |
| `BENEFICIARY_CREATED` / `BENEFICIARY_UPDATED` / `BENEFICIARY_DELETED` | `BENEFICIARY` | Thêm, sửa, xóa hồ sơ người nhận |
| `CHECKED_IN` | `CHECK_IN` | Điểm danh, `targetId` là kỳ mới |
| `CHECK_IN_SETTINGS_UPDATED` | `CHECK_IN` | Đổi chu kỳ hoặc thời gian chờ; `reason` ghi lựa chọn mới và áp dụng ngay hay từ kỳ sau |
| `DEATH_CASE_OPENED` | `DEATH_CASE` | Executor mở hồ sơ chứng tử (`actorRole = EXECUTOR`, `targetVersion = 1`) |
| `DEATH_CASE_SUBMITTED` | `DEATH_CASE` | Executor gửi một bản, kể cả bản bổ sung (`targetVersion` = số bản vừa gửi) |
| `DEATH_CASE_VERSION_CREATED` | `DEATH_CASE` | Executor tạo bản bổ sung (`targetVersion` = số bản mới) |
| `DEATH_CASE_FLAG_REVIEWED` | `DEATH_CASE` | Verifier xem xét cờ (`actorRole = VERIFIER`, `targetVersion` = số bản đang xét) |
| `DEATH_CASE_SUPPLEMENT_REQUESTED` | `DEATH_CASE` | Verifier yêu cầu bổ sung; `reason` chỉ chứa mã phần cần bổ sung (ví dụ `DATE_OF_DEATH,FILES`), không chứa lý do tự do |
| `DEATH_CASE_REJECTED` | `DEATH_CASE` | Verifier từ chối hồ sơ |
| `DEATH_CASE_APPROVED` | `DEATH_CASE` | Verifier duyệt, mở bàn giao |
| `PAYMENT_ORDER_CREATED` | `PAYMENT_ORDER` | Owner tạo đơn thanh toán (gọi lặp trả lại đơn cũ thì không ghi thêm); `targetId` là đơn |
| `PAYMENT_ORDER_CANCELLED` | `PAYMENT_ORDER` | Owner hủy đơn đang chờ (hủy lặp không ghi thêm) |
| `PAYMENT_PAID` | `PAYMENT_ORDER` | Đơn được thanh toán và quyền dịch vụ được cấp (`actorRole = SYSTEM`, không có `actorUserId`; không ghi số tiền hay mã thanh toán) |
| `DEV_TIME_TRAVEL` | `CHECK_IN` / `DEATH_CASE` | Admin tua thời gian kho thử bằng công cụ thử nghiệm, `reason` là mã mốc (chỉ có ở Development) |
| `DEV_CLOCK_RESET` | `VAULT` | Admin đưa đồng hồ kho thử về thời gian thực (chỉ có ở Development) |
| `DEV_EKYC_RESET` | `USER` | Admin xóa dữ liệu eKYC của một tài khoản để làm lại từ đầu (chỉ có ở Development; không gắn kho nên Owner không thấy) |
| `DEV_REWIND_BEFORE_EKYC` | `VAULT` | Admin đưa kho thử về trước bước eKYC và mua gói (chỉ có ở Development; Owner của kho thấy dòng này) |

- **Hồ sơ chứng tử:** chỉ ghi thao tác Executor/Verifier đã lưu thành công (7 loại ở bảng trên); không ghi sửa nháp, thêm/xóa trang giấy, gọi lặp, thao tác bị từ chối, và `reason` không chép lý do tự do vì Owner xem được nhật ký (SEC-13). Lý do đầy đủ nằm ở quyết định của hồ sơ.
- **Chưa làm:** ghi nhật ký cho thiết lập/kích hoạt kế hoạch, mời Executor và bàn giao (thuộc các module khác, cần chủ module đồng ý); lịch sử người nhận xem/tải nội dung (`HandoverAccessLogs`) chưa có API đọc cho Owner; Admin tra cứu nhật ký toàn hệ thống (ADMIN-06).

### 6.14. Owner báo còn sống (F2C)

Owner xem hồ sơ chứng tử của kho và báo "Tôi còn sống" (SRS §6.3, ALIVE-01..09); Verifier được giao hồ sơ kết luận.

| Method | URL | Body | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/v1/vaults/{vaultId}/death-case` | — | 200 `OwnerDeathCaseResponse` (có `objection` nếu đã báo) | 404 `VAULT_NOT_FOUND` / `DEATH_CASE_NOT_FOUND` |
| POST | `/v1/vaults/{vaultId}/death-case/objection` | — | 200 | 404, 409 `DEATH_CASE_NOT_ALLOWED` (`OBJECTION_ALREADY_REJECTED`) / `EDIT_CONFLICT` |
| POST | `/v1/vaults/{vaultId}/death-case/objection/selfie` | `multipart`, field `file` (JPEG/PNG ≤ 8 MiB) | 200 | 400, 404, 409 `DEATH_CASE_NOT_ALLOWED` (`OBJECTION_WINDOW_CLOSED` / `SELFIE_ATTEMPTS_EXCEEDED`), 413, 503 `EKYC_SERVICE_UNAVAILABLE`. Sau khi lời báo bị bác (`OBJECTION_REJECTED`), selfie khớp là bằng chứng mới và mở lại lời báo (ALIVE-09); không khớp thì tính vào giới hạn 3 lần/24 giờ |
| GET | `/v1/verifier/death-cases/{caseId}/objection` | — | 200 `ObjectionResponse` | 404 |
| GET | `/v1/verifier/death-cases/{caseId}/objection/selfie-image` | — | 200 ảnh selfie của lần thử gần nhất (`image/jpeg` \| `image/png` \| `image/webp`, `inline`, `Cache-Control: private, no-store`) | 404 `DEATH_CASE_NOT_FOUND` / `IDENTITY_IMAGE_NOT_FOUND`, 500 `CONTENT_INTEGRITY_FAILED` |
| POST | `/v1/verifier/death-cases/{caseId}/objection/conclude` | `{ conclusion, reason }` | 200 `ObjectionResponse` | 400, 404, 409 `DEATH_CASE_NOT_ALLOWED` (`OBJECTION_NOT_OPEN` / `ALIVE_EVIDENCE_REQUIRED` / `OWNER_SELFIE_PASSED`) / `EDIT_CONFLICT` |

- **Báo** (hồ sơ `UNDER_REVIEW` / `SUPPLEMENT_REQUIRED` / `APPROVED_FOR_HANDOVER`): trong một giao dịch, hồ sơ sang `SUSPENDED`, kế hoạch sang `BLOCKED`, mọi grant `ACTIVE` sang `BLOCKED`. Từ đó: không duyệt (`DEATH_CASE_NOT_ALLOWED` / `NOT_UNDER_REVIEW`), không nhận lượt hay đồng ý (`HANDOVER_NOT_ALLOWED` / `PLAN_NOT_IN_HANDOVER`), không tải (`GRANT_NOT_ACTIVE` / `BLOCKED`), Owner không sửa kho (`VAULT_NOT_EDITABLE` / `PLAN_LOCKED`). Thao tác nhận chạy cùng lúc với báo thua `RowVersion` của kế hoạch (nhận lượt, đồng ý, từ chối đều ghi `plan.UpdatedAt`).
- **Báo lặp** (ALIVE-09): chỉ tăng `reportCount`; `selfieDeadlineAt` (7 ngày từ lần báo đầu) và hold không đổi. Sau khi phản đối bị bác, báo trơn vẫn bị chặn (`OBJECTION_ALREADY_REJECTED`); muốn mở lại thì Owner gửi selfie khớp qua `POST .../objection/selfie`, và lời báo mở lại đó không bị bác lần nữa (ALIVE-05).
- **Ảnh selfie cho Verifier** (BR-50 sửa 25/10/2026): mỗi lần thử được so khớp xong (khớp hoặc không) thì ảnh selfie đó được lưu **mã hóa** trên lời báo, thay ảnh của lần thử trước; lỗi kỹ thuật (`503`) không lưu ảnh. `ObjectionResponse` có thêm `hasSelfieImage`, `selfieImageCapturedAt`, `selfieImageMatched` (kết quả so khớp của chính ảnh đang lưu). Verifier xem ảnh qua `GET .../objection/selfie-image` để tự đối chiếu với ảnh CCCD lúc eKYC (`GET /v1/verifier/death-cases/{caseId}/owner-identity/document-image`, mục 6.12).
- **Selfie** so với mẫu khuôn mặt qua `IOwnerFaceVerifier`. Tối đa 3 lần không khớp trong 24 giờ; lỗi kỹ thuật không tính. Selfie được so với mẫu khuôn mặt Owner (xem mục 6.10): server lấy vector của selfie rồi tự tính cosine với mẫu đã lưu, ngưỡng `0.363`. **Owner chưa có mẫu** (chưa hoàn thành `OWNER_ENROLLMENT` bằng thử thách camera) hoặc dịch vụ eKYC lỗi thì trả `503`, không tính là một lần không khớp; ảnh không có đúng một khuôn mặt trả `400 EKYC_IMAGE_REJECTED` và cũng không tính. Verifier vẫn kết luận được sau 7 ngày.
- **Kết luận** (`ALIVE_CONFIRMED` / `OBJECTION_REJECTED` / `INSUFFICIENT_EVIDENCE`, bắt buộc lý do), chỉ Verifier đang được giao hồ sơ:
  - `ALIVE_CONFIRMED`: chỉ khi selfie đã đạt hoặc đã quá 7 ngày. Hồ sơ `REJECTED`, lượt chưa xong `CANCELLED`, grant `REVOKED`, kế hoạch `ACTIVE` với kỳ điểm danh mới.
  - `OBJECTION_REJECTED`: trả hồ sơ, kế hoạch và grant về trạng thái trước; **không tự duyệt chứng tử**.
  - Luật ALIVE-05: lời báo đã có selfie khớp (`selfiePassed = true`) thì Verifier không được bác; trả `409 DEATH_CASE_NOT_ALLOWED` với `reason = OWNER_SELFIE_PASSED`, không ghi gì. Chỉ được `ALIVE_CONFIRMED` hoặc `INSUFFICIENT_EVIDENCE` (hồ sơ vẫn bị chặn và Owner phải chụp selfie mới).
  - `INSUFFICIENT_EVIDENCE` (BR-76 c, sửa 26/10/2026): giữ chặn và **yêu cầu Owner chụp selfie trực tiếp mới**: bỏ kết quả selfie đạt cũ (`selfiePassed = false`), trả đủ 3 lượt thử, mở cửa sổ chụp 7 ngày mới tính từ lúc kết luận (`selfieDeadlineAt` mới), `verifierDueAt` = hạn selfie mới + 48 giờ, ngày xem lại của hold `OWNER_ALIVE` dời theo hạn selfie mới. `insufficientEvidenceCount` tăng 1, lý do nằm ở `conclusionReason` (lời báo vẫn `OPEN`, `conclusion` vẫn `null`) để FE Owner hiện. Ảnh selfie gần nhất vẫn giữ để Verifier xem lại. Sau đó: Owner chụp đạt thì Verifier mới `ALIVE_CONFIRMED` được (trước đó `409 ALIVE_EVIDENCE_REQUIRED`); Owner không chụp đạt thì `OBJECTION_REJECTED` được phép (selfie không còn đạt).
- **Hạn Verifier** (ALIVE-04, ALIVE-08): `verifierDueAt` = muộn hơn giữa (lúc báo hoặc lúc giao + 48 giờ) và (`selfieDeadlineAt` + 48 giờ). `selfieDeadlineAt` (7 ngày) là hạn của lời báo, báo lặp hay thử lại không gia hạn; chỉ `INSUFFICIENT_EVIDENCE` mở cửa sổ 7 ngày mới và tính lại `verifierDueAt` theo hạn đó.
- Hàng chờ Verifier hiện hồ sơ `SUSPENDED`. `DeathCaseAssignmentJob` giao Verifier cho báo chưa có người, giao lại khi Verifier mất quyền hoặc quá `verifierDueAt` (ALIVE-08); không kết luận, không gỡ chặn; nhắc qua sự cố `OWNER_ALIVE` (`IncidentJob`). Verifier cũ nhận `404` sau khi bị giao lại. Admin chưa đổi Verifier tay được cho hồ sơ `SUSPENDED`.
- **Chưa làm:** sự cố theo dõi và thông báo Verifier/Executor/người nhận (LV-07/LV-14); thử thách camera (EKYC-10); đăng nhập lại trước khi báo.


### 6.15. Hold, sự cố và Admin

Khu vực quản trị (SRS §6.4, §9, ASSIGN-01/04/05, AC-40). Tất cả cần Bearer; quyền kiểm từ DB: không phải Admin đang hoạt động → `404` như khu vực không tồn tại (SEC-04). **Không có endpoint nào đọc nội dung tài sản, trang giấy chứng tử hay danh tính**; Admin không duyệt thay và không tự thay Executor bằng người Owner chưa cho phép.

| Method | URL | Body | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/v1/admin/dashboard` | — | 200 `AdminDashboardResponse` (sự cố mở/quá hạn/chưa có người, hold, hồ sơ chưa có Verifier) | 404 |
| GET | `/v1/admin/users?search=&role=&status=&pageIndex=&pageSize=` | — | 200 `PaginatedList<AdminUserResponse>` (không có mật khẩu) | 400, 404 |
| PUT | `/v1/admin/users/{userId}/role` | `{ role: USER/VERIFIER/ADMIN, reason }` | 200 | 400, 404, 409 `ADMIN_ACTION_NOT_ALLOWED` (`CANNOT_CHANGE_SELF`, `LAST_ADMIN`) |
| PUT | `/v1/admin/users/{userId}/status` | `{ status: ACTIVE/LOCKED/DISABLED, reason }` | 200 | như trên |
| POST | `/v1/admin/death-cases/{caseId}/reassign-verifier` | `{ verifierUserId, reason }` | 204 (gọi lặp cùng người: 204) | 400 (không phải Verifier hợp lệ / là chủ kho, Executor, người gửi), 404, 409 `ADMIN_ACTION_NOT_ALLOWED` (`CASE_NOT_PENDING`) / `EDIT_CONFLICT` |
| GET | `/v1/admin/incidents?status=&type=&assigneeUserId=&overdueOnly=` | — | 200 `PaginatedList<IncidentSummaryResponse>` | 400 |
| GET | `/v1/admin/incidents/{id}` | — | 200 `IncidentDetailResponse` (kèm lịch sử `events`) | 404 `INCIDENT_NOT_FOUND` |
| POST | `/v1/admin/incidents/{id}/assign` | `{ assigneeUserId, reason, etag }` | 200 | 400 (không phải Admin/Verifier hoạt động, hoặc là chủ kho/Executor của kho), 409 `EDIT_CONFLICT` / `INCIDENT_NOT_OPEN` |
| POST | `/v1/admin/incidents/{id}/notes` | `{ note }` | 200 | 404 |
| POST | `/v1/admin/incidents/{id}/review` | `{ nextReviewAt, note, etag }` | 200 | 400 (quá 7 ngày), 409 |
| POST | `/v1/admin/incidents/{id}/resolve` | `{ note, etag }` | 200 (gọi lặp: trạng thái hiện tại) | 409 `EDIT_CONFLICT` |
| GET | `/v1/admin/holds?status=&vaultId=` | — | 200 `PaginatedList<HoldResponse>` | 400 |
| GET | `/v1/admin/holds/{id}` | — | 200 | 404 `HOLD_NOT_FOUND` |
| POST | `/v1/admin/holds` | `{ vaultId, scope?: VAULT/CONTAINER, targetId?, reason, reviewAt? }` | **201** `HoldResponse` | 400 (`reviewAt` quá 7 ngày), 404 `VAULT_NOT_FOUND` / `CONTAINER_NOT_FOUND` |
| POST | `/v1/admin/holds/{id}/review` | `{ nextReviewAt, etag }` | 200 | 400, 409 |
| POST | `/v1/admin/holds/{id}/release` | `{ reason, etag }` | 200 (gọi lặp: trạng thái hiện tại) | 409 `ADMIN_ACTION_NOT_ALLOWED` (`HOLD_OWNER_ALIVE`) / `EDIT_CONFLICT` |

- **Hold** (`Holds`): phạm vi, loại (`OWNER_ALIVE` đặt tự động khi Owner báo còn sống và chỉ gỡ khi Verifier kết luận; `ADMIN`; `DISPUTE`), lý do, người phụ trách, `reviewAt` (tối đa 7 ngày một lần), thời điểm gỡ. Hold đang hiệu lực chặn: duyệt chứng tử (`DEATH_CASE_NOT_ALLOWED` / `HOLD_ACTIVE`), nhận lượt và đồng ý (`HANDOVER_NOT_ALLOWED` / `HOLD_ACTIVE`), liệt kê và tải tài sản (`GRANT_NOT_ACTIVE` / `HOLD_ACTIVE`), kích hoạt kế hoạch (`missingRequirements` có `HOLD_ACTIVE`) và dọn dữ liệu (đồng hồ lưu tạm dừng). Đặt/gỡ hold ghi `plan.UpdatedAt` nên thao tác nhận chạy cùng lúc thua `RowVersion`.
- **Sự cố** (`Incidents`, `IncidentEvents`): loại `NO_VERIFIER`, `NO_EXECUTOR`, `CHECK_IN_OVERDUE`, `NOTIFICATION_FAILED`, `OWNER_ALIVE`, `INTEGRATION`, `MANUAL`, `PAYMENT` (giao dịch thanh toán lệch cần đối chiếu, mục 6.18). Mỗi (loại, đối tượng) chỉ một sự cố đang mở (chỉ mục unique có lọc). **Luôn có người phụ trách (hoặc Admin ít việc nhất), hạn và ngày xem lại**; chưa ai nhận được thì `assigneeUserId = null` kèm `nextReviewAt` sau 1 ngày và job giao cho Admin khi có.
- **Tự động tạo:** job phân công Verifier không có người (`NO_VERIFIER`, tự đóng khi giao được); kỳ điểm danh sang `AWAITING_CHECK` (`CHECK_IN_OVERDUE` giao Executor chính, không có Executor thì `NO_EXECUTOR` giao Admin; tự đóng khi Owner điểm danh, DMS-05); Owner báo còn sống (`OWNER_ALIVE` giao Verifier phụ trách, đóng khi kết luận).
- **Job sự cố** (`Incident:JobEnabled`, `Incident:PollSeconds` mặc định 300): nhắc người phụ trách sau 48 giờ (một lần), báo mọi Admin sau 72 giờ (một lần), giao sự cố mồ côi. Quá hạn không tự đóng. Thư chỉ có loại sự cố, mã rút gọn và hạn.
- **Nhật ký:** mọi thao tác Admin (đổi vai trò/trạng thái, giao lại Verifier, giao/đóng sự cố, đặt/gỡ hold) và F2C ghi `AuditLogs` trong cùng giao dịch.
- **Chưa làm:** hold phạm vi `DESIGNATION`/`CLAIM`/`GRANT` và `DISPUTE` (chờ LV-09); sự cố từ lỗi eKYC/thanh toán (chờ LV-02/LV-04); `DeathCaseAssignmentJob` chưa giao lại hồ sơ `SUSPENDED`; khóa tài khoản hiện thu hồi mọi refresh token (access token đã cấp còn sống tối đa `Jwt:AccessTokenMinutes`).

### 6.16. Executor theo dõi tiến độ bàn giao

Executor đang hiệu lực xem tiến độ bàn giao của hồ sơ chứng tử: từng lượt (một gói cho một người nhận), hạn, quyền tải và từng tài sản đã được tải hay chưa. Chỉ đọc. Cần đăng nhập (Bearer). Người gọi không phải Executor đã chấp nhận của kho, hoặc là Owner, nhận `404 DEATH_CASE_NOT_FOUND` như hồ sơ không tồn tại.

| Method | URL | Quyền | Body / Query | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/api/v1/executor/death-cases/{caseId}/handover-progress` | Executor đang hiệu lực của kho | — | 200 `HandoverProgressResponse` | 401, 404 `DEATH_CASE_NOT_FOUND` |
| POST | `/api/v1/executor/death-cases/{caseId}/handover-turns/{turnId}/resend-invitation` | Executor đang hiệu lực của kho | — | 200 `ResendHandoverInvitationResponse` `{ turnId, invitationSentAt, responseDeadline }` | 401, 404 `DEATH_CASE_NOT_FOUND` / `HANDOVER_NOT_FOUND`, 409 `HANDOVER_NOT_ALLOWED` (`PLAN_NOT_IN_HANDOVER` / `WRONG_STATE` / `HOLD_ACTIVE` / `RESEND_TOO_SOON` / `NO_EMAIL`), 503 `HANDOVER_NOT_ALLOWED` (`INVITATION_SEND_FAILED`) |

**Gửi lại lời mời (INVITE-02, INVITE-04):** chỉ cho lượt chưa ai nhận (`INVITED`, `INITIAL_RESPONSE_EXPIRED`). Mỗi lần gửi lại tạo token mới và link cũ hết dùng (DB chỉ giữ mã băm). Hạn phản hồi không đặt lại, trừ khi lời mời chưa từng được giao thì hạn 5 ngày tính từ lần giao thành công đầu tiên. Cách nhau tối thiểu 15 phút. Người nhận không có email thì `NO_EMAIL` (chưa có kênh khác). Gửi lỗi thì không đổi gì.

**Hold và đồng hồ bàn giao (TIME-04):** kho có hold hoặc kế hoạch `BLOCKED` thì job không chuyển lượt/grant sang hết hạn; hoàn tất xác minh danh tính cũng bị chặn (`HOLD_ACTIVE`). Gỡ hold cuối cùng của kho thì hạn phản hồi, hạn quyết định và hạn grant đang chạy được dời đúng bằng thời gian đã dừng.

```json
// HandoverProgressResponse
{
  "caseId": "0199...",
  "handoverStarted": true,
  "serverTime": "2026-10-20T08:00:00+00:00",
  "summary": { "totalTurns": 2, "waitingResponse": 1, "inVerification": 0, "accepted": 1, "declined": 0, "ended": 0 },
  "turns": [
    {
      "turnId": "0199...",
      "containerName": "Tài liệu gia đình",
      "beneficiaryCitizenIdLast4": "8901",
      "status": "HANDOVER_COMMITTED",
      "invitedAt": "...", "responseDeadline": "...", "claimedAt": "...", "decisionDeadline": "...", "decidedAt": "...",
      "grantStatus": "ACTIVE", "grantExpiresAt": "2026-11-19T08:00:00+00:00",
      "totalItems": 2, "downloadedItems": 1,
      "items": [
        { "itemId": "0199...", "name": "Giấy tờ nhà", "kind": "FILE", "downloaded": true, "firstDownloadedAt": "..." },
        { "itemId": "0199...", "name": "Tài khoản ngân hàng", "kind": "TEXT", "downloaded": false, "firstDownloadedAt": null }
      ]
    }
  ]
}
```

- **Chưa duyệt:** hồ sơ chưa ở `APPROVED_FOR_HANDOVER` thì `handoverStarted = false`, `turns` rỗng, `summary` toàn 0.
- **Nhóm trạng thái trong `summary`:** `waitingResponse` = `INVITED`, `INITIAL_RESPONSE_EXPIRED`; `inVerification` = `IDENTITY_IN_PROGRESS`, `READY_TO_ACCEPT`; `accepted` = `HANDOVER_COMMITTED`; `declined` = `DECLINED`; `ended` = `DECISION_EXPIRED`, `CANCELLED`, `LAPSED`.
- **Đã tải:** tài sản có ít nhất một lần tải thành công. Lần tải bị từ chối hoặc lỗi toàn vẹn không tính. `firstDownloadedAt` là lần tải thành công đầu tiên.
- **Không có dữ liệu nhạy cảm:** chỉ có tên và loại tài sản; không có nội dung tài sản, họ tên, số CCCD đầy đủ hay liên hệ của người nhận. Người nhận được phân biệt bằng 4 số cuối CCCD.
- **Thứ tự:** lượt xếp theo tên gói rồi theo lúc mời; tài sản xếp theo thứ tự đã ghim.
- FE đếm thời gian còn lại theo `serverTime`.

### 6.17. Quên và đặt lại mật khẩu

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| POST | `/api/v1/auth/forgot-password` | Công khai | `{ email }` | **202**, không body | 400 `VALIDATION_ERROR`, 429 |
| POST | `/api/v1/auth/reset-password` | Công khai | `{ token, newPassword, confirmPassword }` | **204**, không body | 400 `VALIDATION_ERROR` / `INVALID_RESET_TOKEN`, 429 |

- Link gửi qua email dạng `{App:ClientUrl}/reset-password?token=...`, hạn 30 phút, dùng một lần; yêu cầu mới vô hiệu link cũ; trong 60 giây kể từ link gần nhất thì không gửi thêm.
- `forgot-password` luôn 202, kể cả email không tồn tại, tài khoản bị khóa hoặc chỉ dùng Google (không lộ tài khoản).
- Với DTO hợp lệ, lỗi xử lý/gửi thư cũng giữ phản hồi 202 trung tính để lỗi SMTP không biến thành cách dò email tồn tại; log chỉ ghi loại exception, không ghi recipient/token/body. Request bị hủy vẫn truyền cancellation. Validation/rate limit vẫn trả 400/429 trước action. 202 không xác nhận thư đã giao; chưa có outbox/retry cho luồng này. Khi SMTP chưa cấu hình, sender dev không giao thư và không ghi link reset vào log.
- `reset-password` thành công: đổi mật khẩu, thu hồi mọi refresh token (mọi thiết bị phải đăng nhập lại), email chưa xác minh được coi là đã xác minh. Không tự đăng nhập; FE chuyển về trang đăng nhập.
- Không cần CSRF (không dùng cookie). Rate limit 5 request/phút/IP cho mỗi API.
- Lỗi kiểm dữ liệu ở nhóm `/auth` trả Content-Type `application/json` (do `[Produces]` của AuthController), body vẫn là ProblemDetails có `code`.

### 6.18. Thanh toán (Billing)

Mua dịch vụ, mua thêm suất người nhận, giả lập trả tiền và nhận tiền thật qua webhook SePay (SRS §8.1–8.5, §11.5, §11.6, AC-90, AC-94, AC-95). Các endpoint `/billing/*` cần đăng nhập (Bearer), riêng webhook SePay xác thực bằng API key (xem "Webhook SePay" bên dưới). Kho luôn là kho của người đang đăng nhập: **không gửi `vaultId`, số tiền, loại đơn hay trạng thái** (SEC-04, PAY-11). Thử tay bằng `http/billing.http` và `http/billing-webhook.http`.

| Method | URL | Body | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/api/v1/billing/plans` | — | 200 `BillingPlansResponse` | 401 |
| POST | `/api/v1/billing/quote` | `{ tier?, years?, extraSlots }` | 200 `BillingQuoteResponse` (không ghi DB) | 400 `VALIDATION_ERROR`, 404 `VAULT_NOT_FOUND`, 409 `PAYMENT_NOT_ALLOWED` |
| POST | `/api/v1/billing/orders` | như `quote` | **201** đơn mới / **200** trả lại đơn `PENDING` cùng lựa chọn; `PaymentOrderResponse` | 400, 404 `VAULT_NOT_FOUND`, 409 `PAYMENT_NOT_ALLOWED`, 409 `PAYMENT_ORDER_PENDING` |
| GET | `/api/v1/billing/orders` | Query phân trang (`pageIndex`, `pageSize`, `sortBy` = `createdAt`, `isAscending`) | 200 `PaginatedList<PaymentOrderSummaryResponse>` (đơn của kho mình, lịch sử thanh toán) | 400 `VALIDATION_ERROR`, 404 `VAULT_NOT_FOUND` |
| GET | `/api/v1/billing/orders/current` | — | 200 `PaymentOrderResponse` / **204** không có đơn chờ | 404 `VAULT_NOT_FOUND` |
| GET | `/api/v1/billing/orders/{orderId}` | — | 200 `PaymentOrderResponse` | 404 `PAYMENT_ORDER_NOT_FOUND` |
| POST | `/api/v1/billing/orders/{orderId}/cancel` | — | 200 `PaymentOrderResponse` | 404 `PAYMENT_ORDER_NOT_FOUND`, 409 `PAYMENT_ORDER_NOT_PAYABLE`, 409 `EDIT_CONFLICT` |
| POST | `/api/v1/billing/orders/{orderId}/dev-pay` | — | 200 `PaymentOrderResponse` (đơn `PAID`) | 404, 409 `PAYMENT_ORDER_NOT_PAYABLE`, 409 `EDIT_CONFLICT` |
| POST | `/api/v1/billing/sepay/webhook` | JSON của SePay (xem dưới) + header `Authorization: Apikey <key>` | 200 đúng `{"success":true}` | 400 `VALIDATION_ERROR`, 401 `WEBHOOK_UNAUTHORIZED`, 409 `EDIT_CONFLICT`, 500 |
| GET | `/api/v1/admin/payment-transactions?status=&pageIndex=&pageSize=` | — | 200 `PaginatedList<AdminPaymentTransactionResponse>` (Admin, chỉ đọc) | 400 `VALIDATION_ERROR`, 401, 404 (không phải Admin) |

**Body `quote` / `orders`:** `tier` là `"XS"` hoặc `"XS_MAX"`, `years` là `1`, `3` hoặc `5` (bắt buộc khi mua mới); `extraSlots` là số suất người nhận mua thêm; `renew` (bool, mặc định `false`) = true để **gia hạn** khi kho còn hạn. Backend tự xác định loại đơn từ quyền dịch vụ của kho, client không gửi:

| Kho đang ở | Loại đơn | Ghi chú |
|---|---|---|
| `serviceStatus = FREE` | `PURCHASE` (mua mới) | Cần eKYC Owner (`OwnerEnrollment`) đạt trong 90 ngày; `extraSlots` 0–20 |
| `PAID_ACTIVE` và còn hạn | `ADDON` (mua thêm suất) | `tier` và `years` gửi vào bị bỏ qua; `extraSlots` 1–20; không đổi hạn |
| `PAID_ACTIVE` còn hạn, đang hạng `XS`, gửi `tier = "XS_MAX"` **và** `extraSlots = 0` | `UPGRADE` (nâng hạng giữa kỳ) | `years` bị bỏ qua; không cần eKYC; giá và cách cấp quyền xem bên dưới. Kèm `extraSlots` ≥ 1 vẫn là `ADDON` (tier bị bỏ qua). Kho đã ở `XS_MAX` không có đường nâng hạng |
| `PAID_ACTIVE` còn hạn, gửi `renew = true` | `RENEWAL` (gia hạn sớm) | Bắt buộc `tier`, `years`; `tier` phải **giữ nguyên hạng hiện tại** (đổi hạng → 400 `VALIDATION_ERROR` theo `tier`: lên hạng dùng `UPGRADE`, hạ hạng chờ hết kỳ); `extraSlots` 0–20 là số suất thêm **của kỳ mới** (không tự giữ suất cũ); không cần eKYC; kỳ mới nối tiếp hạn cũ |
| Đã hết hạn: `PAID_ACTIVE` quá hạn, `GRACE_READ_ONLY` (ân hạn 180 ngày), `EXPIRED_REVIEW` (chờ rà soát) | `RENEWAL` (gia hạn muộn) | Như trên nhưng không cần `renew`, và **được chọn hạng tự do** (kỳ mới tính từ lúc trả); không cần eKYC; kỳ mới tính từ lúc thanh toán; không cộng bù ngày ân hạn |

Không tạo được đơn khi kho `CLOSED` (`VAULT_CLOSED`) hoặc kế hoạch đang `DEATH_CASE_OPEN` / `HANDOVER_IN_PROGRESS` / `BLOCKED` (`PLAN_LOCKED`). Kiểm tra theo đúng thứ tự: kho (404) → kho đóng → kế hoạch khóa → loại đơn → eKYC → giá trị `tier` / `years` / `extraSlots` (400 `VALIDATION_ERROR`, theo tên trường).

```json
// BillingPlansResponse
{
  "policyVersion": "2026-10",
  "currency": "VND",
  "tiers": [
    {
      "tier": "XS",
      "storageQuotaBytes": 209715200,
      "includedBeneficiarySlots": 2,
      "prices": [
        { "years": 1, "amount": 199000, "serviceDays": 365 },
        { "years": 3, "amount": 499000, "serviceDays": 1095 },
        { "years": 5, "amount": 799000, "serviceDays": 1825 }
      ]
    },
    { "tier": "XS_MAX", "storageQuotaBytes": 524288000, "includedBeneficiarySlots": 5, "prices": [ "..." ] }
  ],
  "extraSlot": { "pricePerYear": 49000, "maxPerOrder": 20 },
  "notice": "Thanh toán không tự kích hoạt bàn giao."
}

// BillingQuoteResponse (POST /billing/quote) — ví dụ XS 3 năm + 1 suất (AC-94)
{
  "kind": "PURCHASE",
  "tier": "XS",
  "years": 3,
  "extraSlots": 1,
  "tierAmount": 499000,
  "slotAmount": 147000,
  "totalAmount": 646000,
  "currency": "VND",
  "storageQuotaBytes": 209715200,
  "includedBeneficiarySlots": 2,
  "beneficiarySlots": 3,
  "serviceDays": 1095,
  "serviceStartsAt": "ON_PAYMENT",
  "serviceExpiresAt": null,
  "policyVersion": "2026-10",
  "notice": "Thanh toán không tự kích hoạt bàn giao.",
  "serverTime": "2026-10-18T12:00:00+00:00"
}

// PaymentOrderResponse
{
  "id": "0199e7a4-5c1b-7c3e-9a51-3f0f5d2b8a10",
  "kind": "PURCHASE",
  "status": "PENDING",
  "tier": "XS",
  "years": 3,
  "extraSlots": 1,
  "tierAmount": 499000,
  "slotAmount": 147000,
  "totalAmount": 646000,
  "currency": "VND",
  "storageQuotaBytes": 209715200,
  "includedBeneficiarySlots": 2,
  "paymentCode": "LV7K2M9QXA",
  "transferContent": "LV7K2M9QXA",
  "bank": { "bankCode": "MBBank", "accountNumber": "0000000000", "accountName": "LEGACYVAULT TEST" },
  "qrImageUrl": "https://qr.sepay.vn/img?acc=0000000000&bank=MBBank&amount=646000&des=LV7K2M9QXA",
  "expiresAt": "2026-10-18T12:15:00+00:00",
  "createdAt": "2026-10-18T12:00:00+00:00",
  "paidAt": null,
  "cancelledAt": null,
  "serverTime": "2026-10-18T12:00:01+00:00",
  "notice": "Thanh toán không tự kích hoạt bàn giao."
}
```

| Trường | Giá trị |
|---|---|
| `kind` | `PURCHASE`, `ADDON`, `UPGRADE`, `RENEWAL` |
| `status` | `PENDING`, `PAID`, `EXPIRED`, `CANCELLED` |
| `tier` | `XS`, `XS_MAX` (đơn `ADDON` là hạng hiện tại của kho; đơn `UPGRADE` là `XS_MAX`; đơn `RENEWAL` là hạng của kỳ mới: giữ nguyên hạng hiện tại khi còn hạn, tự do khi đã hết hạn) |

- **Giá gia hạn** (`RENEWAL`): giá hạng của kỳ mới + `49.000 × extraSlots × số năm` (như mua mới). Ví dụ XS 3 năm giữ 1 suất = 646.000 đ. Phản hồi quote/order: `serviceStartsAt` = `AFTER_CURRENT_TERM` (còn hạn: nối tiếp, `serviceExpiresAt` là hạn hiện tại) hoặc `ON_PAYMENT` (đã hết hạn: tính từ lúc trả). Gia hạn sang hạng hoặc số suất **nhỏ hơn mức đang dùng** (dung lượng đã dùng, số suất người nhận đã dùng) → 409 `PAYMENT_NOT_ALLOWED` / `USAGE_EXCEEDS_PLAN`; không tự xóa dữ liệu. Hạn đơn là 15 phút (không bị chặn bởi hết kỳ).
- **Giá nâng hạng** (`UPGRADE`): phần chênh giá theo thời gian còn lại của kỳ hiện tại = `ceil((giá XS_MAX − giá XS của kỳ N năm) × số giây còn lại / (N × 31.536.000))`, N là số năm của đơn `PURCHASE` đã thanh toán gần nhất của kho (không có đơn mua thì N = 1); số giây còn lại không vượt tổng số giây của kỳ. Ví dụ mua XS 3 năm, còn 1,5 năm: (999.000 − 499.000) × 1/2 = 250.000 đ. Phản hồi: `years = null`, `tierAmount` = số tiền nâng hạng, `slotAmount = 0`, `serviceExpiresAt` = hạn hiện tại (giữ nguyên), `beneficiarySlots` = 5 + suất thêm đang có. Hạn đơn như `ADDON` (mốc sớm hơn giữa 15 phút và hết kỳ).
- **Giá do backend tính:** mua mới = giá hạng + `49.000 × extraSlots × số năm` (ADDON-01); mua thêm giữa kỳ = `ceil(49.000 × extraSlots × số giây còn lại / 31.536.000)` (ADDON-02; ví dụ còn đúng 180 ngày, 1 suất = 24.165 đ). Giá, dung lượng, số suất và `policyVersion` được **chốt trên đơn** lúc tạo (PAY-11).
- **Lịch sử đơn** (`GET /billing/orders`): chỉ đơn của kho mình, mặc định thứ tự theo `createdAt` (gửi `isAscending=false` để mới nhất trước). Mỗi phần tử `PaymentOrderSummaryResponse`: `id`, `kind`, `status`, `tier`, `years` (null với `ADDON`/`UPGRADE`), `extraSlots`, `totalAmount`, `currency`, `paymentCode` (mã tham chiếu đơn), `createdAt`, `expiresAt`, `paidAt`, `cancelledAt`. **Không có** thông tin ngân hàng, mã QR hay nội dung chuyển khoản (xem một đơn đầy đủ bằng `GET /billing/orders/{orderId}`). Đơn `PENDING` đã quá hạn hiện là `EXPIRED` khi đọc lịch sử, không ghi DB. `sortBy` khác `createdAt` → 400.
- **Một đơn chờ mỗi kho** (PAY-04): tạo lại cùng lựa chọn khi đang có đơn `PENDING` còn hạn thì trả lại đơn cũ (200); khác lựa chọn thì 409 `PAYMENT_ORDER_PENDING` kèm `orderId`. Muốn đổi thì hủy đơn cũ trước. Hai request cùng lúc vẫn chỉ ra một đơn.
- **Hạn đơn:** mua mới = lúc tạo + 15 phút; mua thêm suất = mốc sớm hơn giữa lúc tạo + 15 phút và hết kỳ hiện tại (ADDON-03). FE tính thời gian còn lại theo `serverTime`, không theo đồng hồ máy khách.
- **Chưa có job hết hạn đơn:** đơn `PENDING` đã quá hạn được đổi sang `EXPIRED` ngay trong lần đọc, tạo hoặc hủy tiếp theo. `GET /orders/current` trả 204 sau khi đánh dấu. Đơn không còn `PENDING` thì `qrImageUrl = null`.
- **Mã thanh toán:** `LV` + 8 ký tự ngẫu nhiên (bỏ `0`, `O`, `1`, `I`), duy nhất toàn hệ thống; nội dung chuyển khoản phải đúng bằng mã này. Đơn không trả `ownerId`, `vaultId`, `rowVersion`. `includedBeneficiarySlots` là suất kèm theo hạng đã chốt; tổng suất của kho đọc ở `GET /vaults/me`.
- **Hủy đơn:** hủy lặp vẫn 200 (trả đơn đã hủy). Đơn đã `PAID` → 409 `PAYMENT_ORDER_NOT_PAYABLE` (`reason = PAID`), đã quá hạn → `EXPIRED`.
- **Cấp quyền** (D8): khi đơn được thanh toán, **chỉ** quyền dịch vụ của kho được ghi. Mua mới: hạng, `PAID_ACTIVE`, dung lượng, suất kèm theo, suất thêm = `extraSlots`, hạn = lúc trả + số năm × 365 ngày (PAY-08). Mua thêm suất: `extraBeneficiarySlots += extraSlots`, giữ nguyên hạn. Gia hạn: hạng, dung lượng, suất kèm theo và suất thêm theo đơn, trạng thái về `PAID_ACTIVE`; hạn mới = (hạn cũ nếu đơn gia hạn sớm và hạn đó chưa qua, ngược lại lúc trả) + số năm × 365 ngày; chỉ cấp khi kỳ chưa đổi so với lúc tạo đơn (gia hạn sớm cần đúng hạn cũ; gia hạn muộn cần kho chưa có kỳ còn hạn), ngược lại `ENTITLEMENT_CHANGED`. Nâng hạng: hạng `XS_MAX`, dung lượng 500 MiB, suất kèm theo 5; giữ nguyên hạn và suất thêm; chỉ cấp khi quyền vẫn là `PAID_ACTIVE` hạng `XS`, đúng hạn lúc tạo đơn và kỳ chưa hết, ngược lại `ENTITLEMENT_CHANGED`. Mỗi đơn chỉ cấp một lần (trả lặp trả lại đơn `PAID`). Không đổi kế hoạch (vẫn `DRAFT` nếu đang `DRAFT`, SETUP-14), không tính là điểm danh, không gỡ chặn (PAY-09). Đơn tạo trước khi kế hoạch bị khóa, tiền tới đúng hạn vẫn được cấp (§8.3). Không cấp và trả 409 `PAYMENT_ORDER_NOT_PAYABLE` khi: đơn đã hủy (`CANCELLED`), tiền tới sau hạn đơn (`EXPIRED`), kho đã đóng (`VAULT_CLOSED`), hoặc quyền dịch vụ đã đổi so với lúc tạo đơn (`ENTITLEMENT_CHANGED`: mua mới nhưng kho không còn Free; mua thêm nhưng hạn đã đổi, đã hết hạn hoặc không còn `PAID_ACTIVE`).
- **`dev-pay` (chỉ Development):** giả lập trả tiền cho đơn của kho mình, gọi đúng hàm cấp quyền mà webhook SePay sẽ dùng. Không có body. Route chỉ được đăng ký khi môi trường là Development **và** `Billing:DevPaymentEnabled = true`; ngoài ra là 404. Hai thao tác trả tiền chạy cùng lúc có thể nhận 409 `EDIT_CONFLICT`; thử lại sẽ thấy đơn `PAID`.
- **Cấu hình** (`Billing`): `Bank.BankCode`, `Bank.AccountNumber`, `Bank.AccountName` (thông tin công khai để hiện QR; `appsettings.Development.json` dùng số tài khoản giả, tài khoản thật đặt trong user-secrets hoặc biến môi trường), `DevPaymentEnabled` (mặc định tắt), `SePay:Environment` (`Test` mặc định hoặc `Live`; giá trị khác là lỗi cấu hình) và `SePay:WebhookApiKey` (**secret**, xem dưới). Chưa cấu hình ngân hàng thì tạo đơn trả `500 INTERNAL_ERROR`, không ghi đơn nào.
- **Bảng giá** (`Billing:Pricing`, trong `appsettings.json`): `PolicyVersion` (tối đa 16 ký tự), `SlotPricePerYear`, `Xs` và `XsMax` mỗi hạng có `OneYear`, `ThreeYears`, `FiveYears` (đồng). Đây là con số công khai, không phải secret. Dung lượng, số suất kèm theo, các mốc 1/3/5 năm, hạn đơn 15 phút và hạn eKYC 90 ngày vẫn là luật trong code. **Đổi giá:** sửa các giá trị này (hoặc ghi đè bằng biến môi trường, ví dụ `Billing__Pricing__Xs__OneYear`), đổi `PolicyVersion`, rồi khởi động lại API. Đơn đã tạo giữ giá và `policyVersion` đã chốt (PAY-11); báo giá, `GET /billing/plans` và đơn mới dùng giá mới ngay sau khi khởi động. Giá nâng hạng giữa kỳ tính theo bảng giá hiện hành. Cấu hình sai (thiếu giá, giá ≤ 0, `XsMax` rẻ hơn `Xs` cùng kỳ, `PolicyVersion` rỗng hoặc quá dài) là lỗi cấu hình máy chủ: báo giá và tạo đơn trả `500 INTERNAL_ERROR`, không ghi đơn nào.
- **Chưa làm:** job tự chuyển `PAID_ACTIVE` → `GRACE_READ_ONLY` → `EXPIRED_REVIEW` theo hạn (gia hạn và nâng hạng giữa kỳ đã có, xem `RENEWAL`, `UPGRADE`; gia hạn nhận cả kho quá hạn dù chưa có job đổi trạng thái), kỳ chờ, nhắc hạn, job hết hạn đơn; xác thực HMAC; thao tác Admin đóng giao dịch (`RESOLVED` chưa được dùng); hoàn tiền (làm thủ công ngoài app, PAY-07).

#### Webhook SePay (SRS §8.3 bước 6–9, §8.5, PAY-01..07, AC-28, AC-29, AC-30, AC-97, AC-104)

`POST /api/v1/billing/sepay/webhook` do **SePay** gọi, không phải FE. Không dùng JWT của người dùng, không CSRF. Thử tay bằng `http/billing-webhook.http`.

- **Xác thực:** header `Authorization: Apikey <key>`, so sánh thời gian cố định với `Billing:SePay:WebhookApiKey`. Thiếu, sai, hoặc server chưa cấu hình key → `401 WEBHOOK_UNAUTHORIZED`, không lưu gì (kể cả khi gửi JWT của người dùng). Key là secret: chỉ đặt bằng user-secrets hoặc biến môi trường, không ghi vào appsettings, log hay tài liệu:

```powershell
dotnet user-secrets set "Billing:SePay:WebhookApiKey" "<key>" --project src/LegacyVault.Api
```

  Test và Live là hai cấu hình và hai key riêng (PAY-01): đặt `Billing:SePay:Environment`, mỗi giao dịch được lưu kèm môi trường.
- **Body** (JSON của SePay; trường khác bị bỏ qua): `id` (ID giao dịch phía SePay, > 0), `gateway`, `transactionDate` (giờ Việt Nam `yyyy-MM-dd HH:mm:ss`, đổi sang UTC; sai định dạng thì không có giờ tiền vào), `accountNumber`, `subAccount` (số tài khoản ảo/VA, tùy chọn), `code`, `content`, `transferType` (`in` hoặc `out`), `transferAmount` (đồng, ≥ 0), `referenceCode`. Thiếu `id`, `transferAmount`, `accountNumber`, `transferType` hoặc giá trị sai → `400 VALIDATION_ERROR`. Kiểm key trước kiểm dữ liệu.
- **Phản hồi:** chỉ trả `200` với body đúng `{"success":true}` **sau khi** giao dịch đã được lưu (PAY-05). Lưu lỗi hoặc xung đột đồng thời (`409 EDIT_CONFLICT`, hoặc `500`) thì không trả success để SePay gửi lại (tối đa 7 lần trong khoảng 33 phút); giao dịch chưa đối chiếu xong được xử lý tiếp ở lần gửi lại. Webhook gửi lặp cùng `id` luôn trả success, không tạo dòng hay cấp quyền thêm (AC-29).
- **Mã thanh toán** (nội dung chuyển khoản): lấy từ `code` của SePay nếu có (tối đa 16 ký tự), không thì tìm `LV` + 8 ký tự hợp lệ trong `content`, không phân biệt hoa thường.
- **Đối chiếu** theo thứ tự, gặp điều nào dừng ở đó. Khớp hết → cấp quyền đúng như `dev-pay` (đơn `PAID`, giờ trả = giờ tiền vào, nhật ký `PAYMENT_PAID`). Lệch → giao dịch `RECONCILIATION_REQUIRED` kèm `reconciliationReason`, **không cấp quyền**, Admin không có thao tác ghi `PAID` (PAY-07):

| `reconciliationReason` | Khi nào |
|---|---|
| `NOT_INCOMING` | `transferType` không phải `in` (không mở sự cố) |
| `WRONG_ACCOUNT` | Tài khoản nhận khác `Billing:Bank:AccountNumber` (PAY-03). Tài khoản nhận là `subAccount` (VA) nếu SePay gửi, không thì `accountNumber`; với BIDV cá nhân chỉ đồng bộ qua VA, đặt số VA vào `AccountNumber` |
| `CODE_MISSING` | Không tìm thấy mã thanh toán |
| `ORDER_NOT_FOUND` | Có mã nhưng không đơn nào mang mã đó |
| `AMOUNT_MISMATCH` | Số tiền khác `totalAmount` của đơn, thiếu hay thừa đều lệch, không cộng dồn (AC-28) |
| `ALREADY_PAID` | Đơn đã được thanh toán bởi giao dịch khác (PAY-04, AC-97) |
| `ORDER_CANCELLED` | Đơn đã bị hủy (PAY-07) |
| `TIME_UNKNOWN` | Không đọc được giờ tiền vào |
| `LATE_AFTER_EXPIRY` | Giờ tiền vào từ hạn đơn trở đi (PAY-06, AC-30). Đơn đã `EXPIRED` mà tiền vào trước hạn vẫn được cấp |
| `VAULT_CLOSED`, `ENTITLEMENT_CHANGED`, `EXPIRED`, `CANCELLED`, `PAID` | Khi bước cấp quyền từ chối, cùng `reason` của `PAYMENT_ORDER_NOT_PAYABLE` (AC-104, ADDON-03) |

  Giao dịch lệch (trừ `NOT_INCOMING`) gắn `paymentOrderId` khi đã xác định được đơn, và mở một sự cố loại `PAYMENT` (xem 6.15) với `targetType = PaymentTransaction`, `targetId` = Id giao dịch; nội dung sự cố chỉ có mã lý do, không có số tiền, mã đơn hay số tài khoản (SEC-07).
- **Trạng thái giao dịch** (`status`): `RECEIVED` (đã lưu, chưa đối chiếu xong), `MATCHED`, `RECONCILIATION_REQUIRED`, `RESOLVED` (chưa dùng). Một đơn có tối đa một giao dịch `MATCHED`.
- **Admin xem giao dịch** (`GET /api/v1/admin/payment-transactions`, ADMIN-09): chỉ đọc, mới nhất trước, `status` lọc theo bốn giá trị trên (giá trị khác → 400). Mỗi phần tử: `id`, `provider`, `environment`, `providerTransactionId`, `status`, `reconciliationReason`, `transferType` (`IN` / `OUT`), `transferAmount`, `currency`, `paymentOrderId`, `paymentCode`, `gateway`, `referenceCode`, `transactionAt`, `receivedAt`, `processedAt`. **Không có** nội dung chuyển khoản, số tài khoản, key hay `rowVersion`. Người không phải Admin đang hoạt động nhận 404 (SEC-04); không có API nào sửa hay ghi `PAID` cho giao dịch.

### 6.19. Công cụ thử nghiệm của Admin (chỉ Development)

Tua thời gian của một kho thử để chạy hết luồng điểm danh, chứng tử và báo còn sống mà không phải chờ ngày thật. **Chỉ chạy ở môi trường Development**; môi trường khác trả 404 như không tồn tại. Chỉ Admin đang hoạt động (đọc từ DB), người khác 404 `USER_NOT_FOUND`.

| Method | URL | Body | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/api/v1/admin/dev-tools/vaults` | — | 200 `DevVaultOptionResponse[]` `{ vaultId, vaultName, ownerName, planStatus }` (tối đa 200 kho, mới nhất trước) | 401, 404 |
| GET | `/api/v1/admin/dev-tools/vaults/{vaultId}/clock` | — | 200 `DevClockResponse` | 401, 404 `USER_NOT_FOUND` / `VAULT_NOT_FOUND` |
| POST | `/api/v1/admin/dev-tools/vaults/{vaultId}/time-travel` | `{ milestone }` | 200 `DevClockResponse` (trạng thái sau khi tua) | 400 `VALIDATION_ERROR`, 401, 404, 409 `DEV_TOOL_NOT_ALLOWED` |
| POST | `/api/v1/admin/dev-tools/vaults/{vaultId}/reset-clock` | — | 200 `DevClockResponse` | 401, 404 |
| POST | `/api/v1/admin/dev-tools/users/ekyc-reset` | `{ email }` | 200 `DevEkycResetResponse` `{ userId, email, verificationsRemoved, consentsRemoved, identityRemoved, faceTemplatesRemoved, vaultRemoved, filesRemoved }` | 400 `VALIDATION_ERROR`, 401, 404 `USER_NOT_FOUND` |
| POST | `/api/v1/admin/dev-tools/vaults/{vaultId}/rewind-before-ekyc` | — | 200 `DevRewindBeforeEkycResponse` `{ vaultId, ownerEmail, verificationsRemoved, consentsRemoved, identityRemoved, faceTemplatesRemoved, previousTier, entitlementReset, pendingOrdersCancelled }` | 401, 404 `USER_NOT_FOUND` / `VAULT_NOT_FOUND`, 409 `DEV_TOOL_NOT_ALLOWED` |

- `DevClockResponse`: `{ vaultId, planStatus, checkInState: ON_TIME | OVERDUE | NOT_RUNNING, clockState: REAL_TIME | SHIFTED, milestones: [{ key, state: AVAILABLE | PASSED | NOT_APPLICABLE, note }] }`. `note` là lý do không tua được.
- `milestone` (theo thứ tự hiển thị):
  - `CHECKIN_OVERDUE`: kỳ điểm danh đang mở qua hạn, vẫn còn thời gian chờ → kế hoạch `CHECKIN_OVERDUE`. Cần kế hoạch `ACTIVE`.
  - `GRACE_ENDED`: hết thời gian chờ → kế hoạch `AWAITING_CHECK`, mở sự cố cho Executor (hoặc `NO_EXECUTOR`). Cần kế hoạch `ACTIVE` hoặc `CHECKIN_OVERDUE`.
  - `OBJECTION_WINDOW_ENDED`: hết 72 giờ phản đối của hồ sơ chứng tử đang mở → Verifier duyệt được ngay. Cần hồ sơ đã báo Owner.
  - `SELFIE_DEADLINE_PASSED`: hết hạn selfie 7 ngày của lời báo còn sống đang mở → Verifier kết luận "còn sống" được khi không có eKYC thật. Không áp dụng khi selfie đã đạt.
- Tua chỉ dời hạn tương ứng về quá khứ; trạng thái mới do luật và job sẵn có quyết định. Với hai mốc điểm danh, API chạy luôn vòng xử lý của job điểm danh nên kết quả có ngay trong response.
- `reset-clock` chỉ ghi nhận đồng hồ về thời gian thực (`clockState = REAL_TIME`); trạng thái đã phát sinh khi tua (kế hoạch, sự cố) giữ nguyên, không hoàn tác.
- Mỗi lần tua ghi nhật ký `DEV_TIME_TRAVEL`, mỗi lần về thời gian hiện tại ghi `DEV_CLOCK_RESET`; Owner của kho thấy các dòng này trong nhật ký kho.
- **Đặt lại tài khoản về ban đầu** (`users/ekyc-reset`): đưa tài khoản có email đã nhập (không phân biệt hoa thường) về lúc vừa đăng ký và đăng nhập được. Trong một giao dịch: xóa mọi lượt eKYC (mọi mục đích), lần đồng ý chụp ảnh, danh tính đã gắn và mẫu khuôn mặt; xóa hẳn kho của tài khoản cùng mọi dữ liệu trong kho (tài sản, gói, người nhận, Executor, kế hoạch, điểm danh, hồ sơ chứng tử, báo còn sống, lượt bàn giao, quyền tải, lượt eKYC của người nhận trong các lượt đó, thanh toán, tạm giữ, sự cố, PIN, nhật ký của kho). Sau khi lưu, xóa các tệp mã hóa liên quan (tài sản, giấy chứng tử, ảnh selfie, ảnh CCCD); `filesRemoved` là số tệp đã xóa. Email, mật khẩu, vai trò và vai Executor ở kho người khác giữ nguyên. Không có eKYC và chưa có kho thì trả các số `0`, `vaultRemoved = false`, không ghi nhật ký; ngược lại ghi `DEV_EKYC_RESET` không gắn kho. Response không có dữ liệu định danh. Sau khi đặt lại, tài khoản tạo kho mới, làm eKYC và mua gói lại từ đầu.
- **Về trước eKYC và mua gói** (`vaults/{vaultId}/rewind-before-ekyc`): đưa kho về thời điểm ngay trước bước eKYC của Owner để làm lại eKYC rồi mua gói. Trong một giao dịch: xóa dữ liệu eKYC của Owner (như `ekyc-reset`), trả quyền dịch vụ về Free (hạng `FREE`, dung lượng Free, 0 chỗ người nhận, bỏ hạn dịch vụ) và hủy các đơn thanh toán đang chờ. Đơn đã trả, tài sản, người nhận, gói và Executor giữ nguyên. Chỉ chạy khi kế hoạch còn `DRAFT` (khác → `409 DEV_TOOL_NOT_ALLOWED`, `reason = PLAN_NOT_DRAFT`). Kho vốn đã ở thời điểm đó thì trả các số `0`, `entitlementReset = false`, không ghi nhật ký; ngược lại ghi `DEV_REWIND_BEFORE_EKYC` gắn kho.
- FE chỉ hiện mục "Công cụ thử nghiệm" trong khu Admin khi chạy `npm run dev`.

### 6.20. Mã PIN của kho

Mã PIN 6 chữ số chặn người dùng tài khoản Owner (không phải Owner thật) khỏi các thao tác nhạy cảm. Chỉ Owner của kho gọi được; kho của người khác → `404 VAULT_NOT_FOUND`.

| Method | URL | Body | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/api/v1/vaults/{vaultId}/pin` | — | 200 `VaultPinStatus` | 401, 404 |
| POST | `/api/v1/vaults/{vaultId}/pin` | `{ pin, confirmPin }` | 200 `VaultPinStatus` | 400 `VALIDATION_ERROR` / `VAULT_PIN_TOO_WEAK`, 404, 409 `VAULT_PIN_ALREADY_SET`, 429 |
| POST | `/api/v1/vaults/{vaultId}/pin/change` | `{ currentPin, newPin, confirmPin }` | 200 `VaultPinStatus` | 400, 403 `INVALID_VAULT_PIN`, 404, 409 `VAULT_PIN_NOT_SET`, 423 `VAULT_PIN_LOCKED`, 429 |
| POST | `/api/v1/vaults/{vaultId}/pin/reset` | `{ password, newPin, confirmPin }` | 200 `VaultPinStatus` | 400, 401 `REAUTHENTICATION_FAILED` (sai mật khẩu), 404, 409 `VAULT_PIN_NOT_SET` / `VAULT_PIN_RESET_NEEDS_PASSWORD`, 429 |

`VaultPinStatus`: `{ isSet, isRequired, lockedUntil, attemptsRemaining }`. `isRequired = true` khi kho đã có tài sản; FE hiện hộp thoại bắt buộc đặt PIN khi `isRequired && !isSet`. `lockedUntil` là `null` khi không bị khóa. `attemptsRemaining` là `null` khi chưa đặt PIN. Không bao giờ trả PIN hay hash.

**Thao tác cần PIN.** Gửi PIN trong header `X-Vault-Pin: 482913` (6 chữ số). Áp dụng cho:

- `GET /vaults/{vaultId}/assets/{assetId}/content` (tải nội dung tài sản) và `DELETE /vaults/{vaultId}/assets/{assetId}`.
- `POST`, `PUT`, `DELETE /vaults/{vaultId}/beneficiaries...` (tạo, sửa, xóa hồ sơ người nhận).
- `DELETE /vaults/{vaultId}/containers/{containerId}` (xóa gói), `DELETE .../containers/{containerId}/assets/{assetId}` (gỡ tài sản khỏi gói), `POST` và `DELETE .../containers/{containerId}/beneficiaries...` (gán, gỡ người nhận của gói).
- `POST /vaults/{vaultId}/executors` (mời Executor) và `POST /vaults/{vaultId}/executors/{executorId}/revoke` (thu hồi).
- Token của đồng hồ thông minh (có claim `device_id`) bị từ chối `403 ROLE_NOT_ALLOWED`, không cho nhập PIN.

Không cần PIN: tạo tài sản, liệt kê, xem thông tin, sửa tên/mô tả tài sản, đưa tài sản vào gói.

**Luật kiểm tra** (theo thứ tự):

1. Kho không thuộc người gọi → `404 VAULT_NOT_FOUND`.
2. Kho chưa có PIN: chưa có tài sản nào → cho qua; đã có tài sản → `409 VAULT_PIN_NOT_SET`.
3. Đang bị khóa → `423 VAULT_PIN_LOCKED` kèm `lockedUntil`.
4. Thiếu header → `403 VAULT_PIN_REQUIRED` (không tính lần đoán).
5. Sai PIN (kể cả không đủ 6 chữ số) → `403 INVALID_VAULT_PIN` kèm `attemptsRemaining` (dùng 403, không dùng 401, để FE không chạy luồng làm mới token).
6. Đúng → bộ đếm sai và bậc khóa về 0.

**Khóa lũy tiến.** Sai 5 lần liên tiếp thì khóa: lần khóa thứ nhất 15 phút, thứ hai 1 giờ, từ lần thứ ba 24 giờ. Lần sai thứ 5 trả luôn `423`. Mỗi lần khóa gửi email cảnh báo cho Owner. Đúng PIN thì bậc khóa về 0. Đặt PIN hoặc đổi PIN từ chối PIN quá dễ đoán (`VAULT_PIN_TOO_WEAK`).

**Ép đặt PIN.** Kho đã có ít nhất 1 tài sản mà chưa có PIN thì `POST .../assets` và `POST .../assets/text` trả `409 VAULT_PIN_NOT_SET`. Tài sản đầu tiên được tạo bình thường.

**Quên PIN** (`pin/reset`): nhập đúng mật khẩu tài khoản thì đặt PIN mới ngay, không có độ trễ hay link xác nhận; gỡ luôn khóa. Sai mật khẩu → `401 REAUTHENTICATION_FAILED` (phiên đăng nhập vẫn còn, FE không coi là hết phiên). Tài khoản chỉ đăng nhập bằng Google không có mật khẩu → `409 VAULT_PIN_RESET_NEEDS_PASSWORD`. Gọi được cả khi đang bị khóa. Rate limit 10 request/phút/IP cho ba API ghi.

**Email báo Owner** (không chứa PIN): PIN vừa đặt, vừa đổi, vừa đặt lại, hoặc bị khóa. Gửi lỗi không làm hỏng thao tác PIN đã lưu.

**Nhật ký** (mục 6.13, `GET /vaults/{vaultId}/audit-logs`): `VAULT_PIN_SET`, `VAULT_PIN_CHANGED`, `VAULT_PIN_RESET`, `VAULT_PIN_WRONG` (kết quả `FAILED`), `VAULT_PIN_LOCKED` (kết quả `DENIED`). Không ghi PIN hay hash.

**Giới hạn.** Người biết mật khẩu tài khoản đặt lại được PIN ngay, và PIN không bảo vệ nội dung mã hóa của tài sản, chỉ chặn người dùng tài khoản Owner.

## 7. Tóm tắt thay đổi FE cần làm

| # | Việc | File FE |
|---|---|---|
| 1 | Gửi `withCredentials: true` cho các request `/v1/auth/*` | `shared/api/axiosClient.ts` |
| 2 | Thêm luồng 401 → refresh một lần → thử lại; refresh lỗi → về đăng nhập (bỏ qua `REAUTHENTICATION_FAILED`) | `shared/api/axiosClient.ts` |
| 3 | Đọc lỗi theo `code` của ProblemDetails; lỗi validate là `errors` dạng object theo field | `shared/types`, chỗ hiển thị lỗi form |
| 4 | Không dùng `ApiResponse<T>` cho API này (BE không bọc envelope) | `shared/types` |
| 5 | Bổ sung hằng trạng thái: `LOCKED`, `DISABLED`, `OPEN`, `CLOSED`, `READY`, `REMOVED`, `PAID_ACTIVE`... (hiện `STATUS` chưa có) | `shared/constants` |
| 6 | Gọi route có tiền tố `/v1` (base URL đã có `/api`) | các `features/*/api` |
| 7 | Màn thanh toán đang mock contract cũ: đổi sang DTO và endpoint `/billing/*` ở mục 6.18 (hạng `XS` / `XS_MAX`, năm 1/3/5, suất thêm; không còn `RECIPIENT_PLUS`, voucher, monthly/lifetime); không gửi số tiền, chỉ hiển thị `totalAmount` từ response | `features/billing/*` |
# Executor setup and activation (15/10/2026)

See [PLAN-SETUP-API.md](PLAN-SETUP-API.md) for the implemented canonical `/api/v1` endpoints, acknowledgement fields, business errors, SMTP opt-in and migration/verification limits. OpenAPI runtime is `/openapi/v1.json`.
Màn kích hoạt (SETUP-01, SETUP-10, ASSET-03/AC-68): xem [PLAN-SETUP-API.md](PLAN-SETUP-API.md) để biết các field `containers`, `assetsOutsideContainer`, `warnings` và cách thử tay.

## 9a. Tính đúng đắn dữ liệu khi chạy đồng thời (LV-01)

- **Giữ chỗ dung lượng** (QUOTA-02): mỗi lần tải tài sản kiểm `đã dùng + chỗ đang giữ còn hạn + lần này ≤ hạn mức` và ghi chỗ giữ (bảng `StorageReservations`, 15 phút) trong một bước dưới khóa riêng của kho (`sp_getapplock`). Sáu lần tải 5 MiB cùng lúc vào kho 20 MiB chỉ nhận bốn lần. Tải lỗi thì trả chỗ; chỗ giữ quá hạn không còn tính; tải xong thì chỗ giữ chuyển `COMMITTED` trong cùng lần lưu với tài sản. Kích thước khai báo là trần: nội dung thật vượt khai báo thì `409 STORAGE_QUOTA_EXCEEDED`.
- **Chống trùng CCCD** (QUOTA-04): cột `Beneficiaries.CitizenIdHash` = HMAC-SHA256 của số CCCD (không đảo ngược, không lưu CCCD rõ), unique theo `(VaultId, CitizenIdHash)`. Hai request cùng CCCD đến cùng lúc: một bên `201`, bên kia `200` trả hồ sơ đó (cùng họ tên, ngày sinh) hoặc `409 BENEFICIARY_IDENTITY_CONFLICT`. Khóa HMAC lấy từ `AssetEncryption:BlindIndexKey` (base64 32 byte, **không đổi khi xoay KEK**); bỏ trống thì suy ra từ KEK đang dùng, chỉ nên cho máy dev. Hồ sơ tạo trước migration có `CitizenIdHash = NULL` nên chưa được ràng buộc (vẫn dò trùng theo 4 số cuối như cũ).
- **Chưa cấu hình email** ngoài Development: bộ gửi chỉ-ghi-log **ném lỗi** thay vì báo đã giao. Thông báo chứng tử giữ `PENDING` (không bắt đầu 72 giờ), hết số lần thử thì `FAILED`; lời mời nhận tài sản để `InvitationSentAt = null`; lời mời Executor trả `invitationSent = false`.
- **Chưa làm:** dọn nội dung mồ côi khi process chết giữa lúc ghi nội dung và lưu DB; xác thực lại trong 10 phút cho thao tác giảm dữ liệu lúc ân hạn (OPLAN-01); khóa theo kho cho thanh toán/cleanup (chưa có module).

## 9b. Hạn bổ sung và cờ DEATH-13

- **Hạn bổ sung** (DEATH-01): yêu cầu bổ sung của Verifier có `dueAt` = lúc yêu cầu + 7 ngày (`supplementRequest.dueAt` trong response Executor). Job nền (`DeathCase:SupplementJobEnabled`, `DeathCase:SupplementPollSeconds`, mặc định 300 giây) ghi `supplementOverdueAt` khi hết hạn mà Executor chưa nộp bản mới; `supplementRequest.overdue = true`. Quá hạn **không tự quyết định**: hồ sơ vẫn `SUPPLEMENT_REQUIRED`, Verifier tự từ chối hoặc yêu cầu lại. Executor nộp bản mới thì mốc này bị xóa.
- **Cờ `CERTIFICATE_UNVERIFIED`**: đặt khi Executor gửi mà AI không kiểm tra được trang nào là giấy chứng tử (xem "AI nhận diện giấy chứng tử khi gửi" ở mục 6.11). Verifier tự mở các trang giấy và xem xét cờ trước khi duyệt.
- **DEATH-13**: khi Executor gửi, 4 số cuối CCCD trên hồ sơ khác 4 số cuối của lượt eKYC đạt gần nhất của Owner thì đặt cờ `IDENTITY_MISMATCH` (chặn duyệt tới khi Verifier xem xét). Owner chưa có eKYC đạt thì không kết luận. Owner đã có danh tính đã gắn (mục 6.10) thì so **đủ 12 số**; Owner đăng ký trước khi có danh tính đã gắn thì vẫn so 4 số cuối của lượt eKYC đạt gần nhất.
- Phân công và giao lại Verifier quá 48 giờ do `DeathCaseAssignmentJob` có sẵn xử lý (mục 6.12); hồ sơ `SUSPENDED` (Owner báo còn sống) chưa được job này giao lại, chờ LV-07.

## 10. Lưu và xóa dữ liệu sau bàn giao (SRS §7.7)

Job `Retention:Enabled`, `Retention:PollSeconds` (mặc định 300 giây). Chỉ áp dụng cho kế hoạch `HANDOVER_IN_PROGRESS` / `CLOSED` / `BLOCKED`; kế hoạch đã về `ACTIVE` (Owner còn sống) và lượt `CANCELLED` **không bao giờ** bị dọn.

- **Đồng hồ theo từng lượt** (cột mới trên `HandoverTurns`: `RetentionStatus`, `RetentionStartedAt`, `RetentionDeadline`, `RetentionRemainingSeconds`, `RetentionNoticeStage`, `DeletedAt`): `NOT_STARTED` → `RETENTION_ACTIVE` (30 ngày) → tới hạn thì dọn → `DELETED`.
- **Lý do giữ** (tạm dừng, lưu phần còn lại, không đặt lại 30 ngày — RETENTION-04): còn trong 5 ngày phản hồi; claim đang xử lý (`IDENTITY_IN_PROGRESS`, `READY_TO_ACCEPT`); grant còn hạn; Owner báo còn sống (kế hoạch `BLOCKED`). Mở link hay đăng nhập không phải lý do giữ (RETENTION-03).
- **Dọn** (RETENTION-06): lượt còn mở thành `LAPSED`, link mời bị vô hiệu; nội dung tài sản chỉ bị xóa khi không còn lượt nào khác chưa dọn cùng tham chiếu (RETENTION-02). Xóa = hủy khóa trước (ghi đè DEK đã bọc bằng 0, `Assets.ContentDeletedAt`) trong giao dịch, xóa tệp sau (`Assets.StorageDeletedAt`, lỗi thì thử lại). Ghi `plan.UpdatedAt` nên nhận lượt/đồng ý chạy cùng lúc thua RowVersion. Tải nội dung đã dọn: `403 GRANT_NOT_ACTIVE` (`CONTENT_DELETED`). Dung lượng đã dùng không tính nội dung đã dọn.
- **Đóng kho** (SETUP-18): kế hoạch `CLOSED` và mọi lượt đã dọn thì dọn nốt nội dung còn lại của kho (kể cả tài sản ngoài gói) và kho thành `CLOSED`.
- **Thư mốc** cho người nhận (email trong hồ sơ người nhận): lúc bắt đầu, còn 7 ngày, còn 24 giờ; gửi lỗi thì vòng sau gửi lại.
- **Chưa làm:** giới hạn 30 ngày cho claim đang xử lý (CLAIM-06) và giữ tối đa 7 ngày khi đang xem xét (REVIEW-04) — chờ LV-09; thư mốc gửi lỗi phải giao người xử lý có hạn (RETENTION-05, LV-07); hold chung (LV-07); không xóa trang giấy chứng tử (hồ sơ bằng chứng, chưa có chính sách).
