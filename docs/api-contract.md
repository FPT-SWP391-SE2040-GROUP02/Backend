# Hợp đồng API — Đăng nhập và Tạo kho

> Phiên bản: 0.1 (13/10/2026) — bản thống nhất giữa BE và FE, **chưa cài đặt**.
> Căn cứ: SRS v3.14.x, interface trong `src/LegacyVault.Application/Services`, và FE `src/shared/api` (axiosClient, baseService).
> Đổi hợp đồng thì sửa file này trong cùng PR và báo FE.

---

## 1. Quy ước chung

| Mục | Quy ước |
|---|---|
| Base URL | FE: `VITE_API_BASE_URL=http://localhost:5000/api`. Mọi route nghiệp vụ bắt đầu bằng `/v1`, nên FE gọi ví dụ `apiClient.post("/v1/auth/login", ...)` |
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

Mọi lỗi trả theo chuẩn **ProblemDetails** (RFC 9457) kèm `code` để FE so sánh:

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

- `500 INTERNAL_ERROR` chỉ có `traceId`; gửi `traceId` cho BE khi báo lỗi.

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
| 403 | `PAID_TIER_REQUIRED` | Hạng Free đưa tài sản vào gói, tạo hồ sơ người nhận hoặc gắn người nhận vào gói | |
| 404 | `VAULT_NOT_FOUND` | Chưa có kho, hoặc kho của người khác | |
| 404 | `CONTAINER_NOT_FOUND` | Không có gói, hoặc gói của kho khác | |
| 404 | `ASSET_NOT_FOUND` | Tài sản không thuộc kho | |
| 404 | `BENEFICIARY_NOT_FOUND` | Không có hồ sơ người nhận, hoặc hồ sơ của kho khác | |
| 409 | `STORAGE_QUOTA_EXCEEDED` | Tạo tài sản vượt dung lượng còn lại của kho | `storageQuotaBytes`, `storageRemainingBytes` |
| 413 | `FILE_TOO_LARGE` | Request tải lên vượt giới hạn server (500 MiB) | |
| 409 | `EDIT_CONFLICT` | `etag` đã cũ: dữ liệu bị sửa ở tab/máy khác. FE tải lại rồi cho sửa tiếp | |
| 409 | `CHECKIN_NOT_ALLOWED` | Điểm danh khi kế hoạch chưa kích hoạt (`DRAFT`) hoặc đã sang chứng tử/bàn giao/đóng | `planStatus` |
| 409 | `VAULT_NOT_EDITABLE` | Kho không cho sửa lúc này | `reason`: `VAULT_CLOSED` / `PLAN_LOCKED` / `SERVICE_READ_ONLY` |
| 409 | `CONTAINER_LIMIT_EXCEEDED` | Đủ 50 gói chưa lưu trữ | `maxContainers` |
| 409 | `CONTAINER_ARCHIVED` | Sửa gói đã lưu trữ | |
| 409 | `ASSET_ALREADY_IN_CONTAINER` | Tài sản đang ở gói khác | `containerId` |
| 409 | `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` | Thao tác làm kế hoạch không còn gói sẵn sàng; cần Owner xác nhận | |
| 409 | `BENEFICIARY_IDENTITY_CONFLICT` | Kho đã có hồ sơ cùng số CCCD nhưng khác họ tên hoặc ngày sinh | `beneficiaryId` |
| 409 | `MINOR_CONFIRMATION_REQUIRED` | Người nhận chưa đủ 18 tuổi; cần Owner xác nhận cảnh báo | |
| 409 | `BENEFICIARY_IN_USE` | Xóa hồ sơ người nhận đang ở gói; gỡ khỏi các gói trước | `containerIds` |
| 409 | `BENEFICIARY_SLOTS_EXCEEDED` | Gắn thêm một người nhận mới vượt số suất của kho | `usedSlots`, `maxSlots` |
| 409 | `GOOGLE_ACCOUNT_ALREADY_LINKED` | Google này đã gắn tài khoản khác / tài khoản đã có Google | |
| 409 | `LAST_LOGIN_METHOD` | Gỡ phương thức đăng nhập cuối cùng | |
| 409 | `LOGIN_METHOD_ALREADY_EXISTS` | Đã có phương thức này | `method` |
| 423 | `ACCOUNT_LOCKED` | Sai mật khẩu 5 lần trong 15 phút | `attemptsRemaining: 0`, `remainingSeconds` |
| 500 | `INTERNAL_ERROR` | Lỗi bất ngờ | `traceId` |

---

## 3. Xác thực (mô hình lai)

| | Access token | Refresh token |
|---|---|---|
| Nằm ở | **JSON body** (`accessToken`) | **Cookie HttpOnly** `lv_refresh` |
| FE lưu | Chỉ RAM; interceptor gửi `Authorization: Bearer`. Cấm Web Storage | Không lưu, JS không đọc được |
| Sống | 15 phút | Hết khi không dùng 30 phút, tối đa 12 giờ từ lúc đăng nhập (NFR-06) |
| Cookie | — | `HttpOnly; SameSite=Lax; Path=/api/v1/auth`; `Secure` ngoài môi trường dev |

**Việc FE cần làm:**

1. Các request tới `/api/v1/auth/*` gửi kèm `withCredentials: true` để trình duyệt gửi/nhận cookie `lv_refresh`. Các API khác không cần.
2. Cấp proof mới bằng `GET /api/v1/auth/csrf` (raw `{ requestToken }`), gửi `X-CSRF-Token` cùng antiforgery cookie trước login/refresh/logout/Google/link-confirm. Proof gắn principal hiện tại: lấy lại sau đổi danh tính; không gửi POST khi acquisition lỗi. Thiếu/sai proof trả 400; vượt limiter trả 429 và Retry-After.
3. Đăng xuất: gọi `POST /api/v1/auth/logout`; chỉ xóa RAM/cache và báo hoàn tất sau server success. Logout-all cần bearer principal active hoặc refresh token active, không chấp nhận cookie thuộc user khác. Thu hồi refresh không tự vô hiệu access JWT đang còn hạn.
4. Không đưa token vào URL/query hoặc log (SEC-06).

BE bật CORS credentials cho origins cấu hình rõ. Alias `/v1` còn tồn tại nhưng cookie Path dùng `/api/v1/auth`; dùng prefix `/api/v1` cho luồng có cookie. Refresh tự động chỉ bật sau checks phối hợp single-flight, nhiều tab và logout/login đang bay; không tự retry mutation.

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
| POST | `/v1/auth/register` | Công khai | `RegisterRequest` | **202**, không body | 400 |
| POST | `/v1/auth/verify-email` | Công khai | `{ token }` | 200 `UserResponse` | 400 `INVALID_VERIFICATION_TOKEN` |
| POST | `/v1/auth/resend-verification` | Công khai | `{ email }` | **202**, không body | 400 |

- `register` và `resend-verification` **luôn trả 202** kể cả khi email đã có tài khoản, để không lộ email nào đã đăng ký. FE hiện: "Nếu email hợp lệ, bạn sẽ nhận được link xác minh".
- Link trong email có dạng `<FE>/verify-email?token=...`; trang FE đọc `token` rồi gọi `verify-email`.

```json
// RegisterRequest
{ "email": "an@gmail.com", "password": "Matkhau123!", "confirmPassword": "Matkhau123!", "fullName": "Nguyễn Văn An" }
```

### 6.2. Đăng nhập, phiên, đăng xuất

Bổ sung 14/10/2026: `GET /api/v1/auth/csrf` trả raw requestToken/no-store + cookie HttpOnly; login/refresh/logout/Google/link-confirm bắt buộc proof. Policy IP: login và Google/link-confirm chung 5/phút; refresh/logout mỗi policy 30/phút. Limiter chỉ tin Connection.RemoteIpAddress, không parse X-Forwarded-For tự gửi. Login/refresh/logout chạy Serializable; lỗi credentials/replay chỉ trả sau commit bộ đếm/thu hồi. Không retry deadlock mù. Các HTTP status 400/429 này bổ sung cho bảng phía dưới.

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| POST | `/v1/auth/login` | Công khai | `LoginRequest` | 200 `AuthSessionResponse` + Set-Cookie `lv_refresh` | 400, 401 `INVALID_CREDENTIALS`, 423 `ACCOUNT_LOCKED`, 403 `ACCOUNT_NOT_ACTIVE` |
| POST | `/v1/auth/refresh` | Cookie `lv_refresh` | — | 200 `AuthSessionResponse` + cookie mới | 401 `SESSION_EXPIRED` (cookie bị xóa) |
| POST | `/v1/auth/logout` | Công khai | `{ revokeAllDevices? }` (không bắt buộc) | **204** + xóa cookie | 400 CSRF, 401 SESSION_EXPIRED khi logout-all thiếu quyền, 429 |
| GET | `/v1/auth/session` | Đăng nhập | — | 200 `UserResponse` | 401 `SESSION_EXPIRED` |

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
    "createdAt": "2026-10-13T08:30:00+00:00"
  }
}
```

- `role`: `USER` | `VERIFIER` | `ADMIN`. Owner/Executor/Beneficiary **không phải role**, là quan hệ với từng kho.
- `status`: `ACTIVE` | `LOCKED` | `DISABLED`.
- `emailVerified = false` → FE hiện nhắc xác minh và nút gửi lại link; vẫn đăng nhập được nhưng chưa tạo được kho.
- `rememberMe = true` thì cookie sống qua khi tắt trình duyệt, nhưng phiên vẫn tối đa 12 giờ.

### 6.3. Đăng nhập Google

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| POST | `/v1/auth/google` | Công khai | `GoogleLoginRequest` | 200 (2 dạng, xem dưới) | 401 `INVALID_GOOGLE_TOKEN`, 403 `ACCOUNT_NOT_ACTIVE` |
| POST | `/v1/auth/google/link-confirm` | Công khai | `ConfirmGoogleLinkRequest` | 200 `AuthSessionResponse` + cookie | 400 `INVALID_LINK_TICKET`, 401 `INVALID_CREDENTIALS`, 423, 403, 409 `GOOGLE_ACCOUNT_ALREADY_LINKED` |

```json
// GoogleLoginRequest — idToken lấy từ Google Identity Services
{ "idToken": "eyJhbGciOi...", "nonce": "random-from-fe", "rememberMe": true, "returnUrl": "/invitations/abc" }

// Dạng 1: đăng nhập được (kèm Set-Cookie lv_refresh)
{ "signedIn": true, "accountCreated": false, "returnUrl": "/invitations/abc", "session": { /* AuthSessionResponse */ } }

// Dạng 2: email Google trùng tài khoản app chưa liên kết (AUTH-02) — KHÔNG có cookie
{ "signedIn": false, "linkTicket": "k3Jd...", "maskedEmail": "an***@gmail.com" }

// ConfirmGoogleLinkRequest — người dùng nhập mật khẩu tài khoản app
{ "linkTicket": "k3Jd...", "password": "Matkhau123!", "rememberMe": true }
```

- `returnUrl` chỉ nhận đường dẫn nội bộ bắt đầu bằng `/`; giá trị khác bị bỏ (trả `null`).
- Vé liên kết sống khoảng 10 phút, dùng một lần.

### 6.4. Phương thức đăng nhập của tôi

| Method | URL | Quyền | Body | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `/v1/me/login-methods` | Đăng nhập | — | 200 `LoginMethodResponse[]` | 401 |
| POST | `/v1/me/login-methods/google` | Đăng nhập | `{ idToken, nonce?, currentPassword }` | 201 `LoginMethodResponse` | 401 `REAUTHENTICATION_FAILED` / `INVALID_GOOGLE_TOKEN`, 409 `GOOGLE_ACCOUNT_ALREADY_LINKED` |
| POST | `/v1/me/login-methods/google/unlink` | Đăng nhập | `{ currentPassword }` | 204 | 401 `REAUTHENTICATION_FAILED`, 409 `LAST_LOGIN_METHOD` |
| POST | `/v1/me/login-methods/password` | Đăng nhập | `{ googleIdToken, nonce?, newPassword, confirmPassword }` | 204 | 401, 409 `LOGIN_METHOD_ALREADY_EXISTS` |
| POST | `/v1/me/login-methods/password/remove` | Đăng nhập | `{ currentPassword, googleIdToken, nonce? }` | 204 | 401, 409 `LAST_LOGIN_METHOD` |

```json
// LoginMethodResponse
{ "method": "GOOGLE", "email": "an@gmail.com", "linkedAt": "2026-10-13T08:30:00+00:00", "lastUsedAt": null, "canRemove": true }
```

- `REAUTHENTICATION_FAILED` là 401 nhưng phiên vẫn còn: interceptor **không** được coi là hết phiên.

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
| GET | `.../containers` | Đăng nhập | Query phân trang + `includeArchived` (mặc định `false`); `sortBy`: `createdAt` (mặc định) \| `name` | 200 `PaginatedList<ContainerResponse>` | 400, 404 |
| POST | `.../containers` | Đăng nhập | `{ name, description? }` | **201** `ContainerResponse` | 400, 404, 409 `CONTAINER_LIMIT_EXCEEDED`, 409 `VAULT_NOT_EDITABLE` |
| GET | `.../containers/{containerId}` | Đăng nhập | — | 200 `ContainerResponse` | 404 |
| PUT | `.../containers/{containerId}` | Đăng nhập | `{ etag, name?, description? }` | 200 `ContainerResponse` | 400, 404, 409 `EDIT_CONFLICT` / `CONTAINER_ARCHIVED` / `VAULT_NOT_EDITABLE` |
| DELETE | `.../containers/{containerId}?confirmPlanDeactivation=false` | Đăng nhập | — | 200 `ContainerResponse` (đã `ARCHIVED`) | 404, 409 `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` |
| POST | `.../containers/{containerId}/assets` | Đăng nhập | `{ assetId }` | 200 `ContainerResponse` | 403 `PAID_TIER_REQUIRED`, 404, 409 `ASSET_ALREADY_IN_CONTAINER` / `CONTAINER_ARCHIVED` |
| DELETE | `.../containers/{containerId}/assets/{assetId}?confirmPlanDeactivation=false` | Đăng nhập | — | 200 `ContainerResponse` | 404, 409 `CONTAINER_ARCHIVED` / `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` |
| POST | `.../containers/{containerId}/beneficiaries` | Đăng nhập | `{ beneficiaryId }` | 200 `ContainerResponse` | 400, 403 `PAID_TIER_REQUIRED`, 404 `CONTAINER_NOT_FOUND` / `BENEFICIARY_NOT_FOUND`, 409 `BENEFICIARY_SLOTS_EXCEEDED` / `CONTAINER_ARCHIVED` / `VAULT_NOT_EDITABLE` |
| DELETE | `.../containers/{containerId}/beneficiaries/{beneficiaryId}?confirmPlanDeactivation=false` | Đăng nhập | — | 200 `ContainerResponse` | 404, 409 `CONTAINER_ARCHIVED` / `PLAN_DEACTIVATION_CONFIRMATION_REQUIRED` |

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
  "archivedAt": null,
  "etag": "\"0\""
}
```

- `DELETE` gói = **lưu trữ**, không xóa hẳn: gói chuyển `ARCHIVED`, tài sản và người nhận được gỡ ra nhưng **không bị xóa**. Người nhận không còn ở gói nào khác thì hết chiếm suất.
- Gặp `409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED`: FE hiện cảnh báo "kế hoạch sẽ trở về bản nháp"; Owner đồng ý thì gọi lại với `?confirmPlanDeactivation=true`.
- Hạng Free: được tạo/đổi tên/lưu trữ gói rỗng; đưa tài sản hoặc người nhận vào gói → `403 PAID_TIER_REQUIRED` (FE hiện gợi ý nâng cấp).
- `status`: `DRAFT` (thiếu tài sản hoặc người nhận), `READY` (có ≥1 tài sản **và** ≥1 người nhận), `ARCHIVED`. BE tự tính, FE không gửi trạng thái. `missingRequirements` cho biết còn thiếu gì: `NO_ASSET`, `NO_DESIGNATION`. Người nhận chưa đủ 18 tuổi (đã xác nhận) vẫn tính.
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

```json
// AssetResponse — chỉ metadata, KHÔNG có nội dung tài sản
{
  "id": "0199e7c1-...",
  "vaultId": "0199e7a4-...",
  "kind": "FILE",
  "name": "Giấy tờ nhà",
  "description": null,
  "currentVersion": {
    "versionNumber": 1,
    "fileName": "giay-to.pdf",
    "contentType": "application/pdf",
    "sizeBytes": 1500,
    "createdAt": "2026-10-13T08:40:00+00:00"
  },
  "createdAt": "2026-10-13T08:40:00+00:00",
  "updatedAt": null,
  "etag": "\"0\""
}
```

- `kind`: `FILE` (tệp tải lên) | `TEXT` (nội dung chữ: thông tin tài khoản, ghi chú...). `fileName` là `null` với `TEXT`.
- `name` của tệp không gửi thì lấy theo tên tệp. Tên/mô tả **không mã hóa**: không ghi mật khẩu vào đây; bí mật để trong `content`/tệp.
- Nội dung tệp và `content` được BE mã hóa (AES-256-GCM, khóa riêng mỗi phiên bản) trước khi lưu. FE gửi nội dung gốc qua HTTPS, **không** tự mã hóa hay Base64 nội dung. `sizeBytes` là kích thước nội dung gốc, cũng là số tính vào quota.
- Hạng Free được lưu tài sản trong 20 MiB; mọi phiên bản đã lưu đều tính vào `storageUsedBytes` của kho, kể cả khi tài sản không ở gói nào.
- Chưa có (làm sau cùng phần bảo mật): tải nội dung về, thay nội dung (phiên bản mới), đổi tên/xóa tài sản.

### 6.8. Người nhận (Beneficiary)

Hồ sơ người nhận thuộc **kho** (danh bạ người nhận), dùng lại cho nhiều gói. Endpoint gốc: `/v1/vaults/{vaultId}/beneficiaries`. Gắn/gỡ hồ sơ vào gói ở mục 6.6 (`.../containers/{containerId}/beneficiaries`).

| Method | URL | Quyền | Body / Query | Thành công | Lỗi |
|---|---|---|---|---|---|
| GET | `.../beneficiaries` | Đăng nhập | Query phân trang; `sortBy`: chỉ `createdAt` (mặc định); `searchTerm`: 1–4 chữ số | 200 `PaginatedList<BeneficiaryResponse>` | 400, 404 |
| POST | `.../beneficiaries` | Đăng nhập | `{ citizenId, fullName, dateOfBirth, email?, phone?, confirmMinor? }` | **201** `BeneficiaryResponse` (vừa tạo) / **200** (đã có hồ sơ cùng người) | 400, 403 `PAID_TIER_REQUIRED`, 404, 409 `BENEFICIARY_IDENTITY_CONFLICT` / `MINOR_CONFIRMATION_REQUIRED` / `VAULT_NOT_EDITABLE` |
| GET | `.../beneficiaries/{beneficiaryId}` | Đăng nhập | — | 200 `BeneficiaryResponse` | 404 `VAULT_NOT_FOUND` / `BENEFICIARY_NOT_FOUND` |
| PUT | `.../beneficiaries/{beneficiaryId}` | Đăng nhập | `{ etag, fullName?, dateOfBirth?, email?, phone?, confirmMinor? }` | 200 `BeneficiaryResponse` | 400, 404, 409 `EDIT_CONFLICT` / `MINOR_CONFIRMATION_REQUIRED` / `VAULT_NOT_EDITABLE` |
| DELETE | `.../beneficiaries/{beneficiaryId}` | Đăng nhập | — | **204** | 404, 409 `BENEFICIARY_IN_USE` / `VAULT_NOT_EDITABLE` |

```json
// POST body
{
  "citizenId": "012345678901",
  "fullName": "Nguyễn Văn An",
  "dateOfBirth": "1990-05-20",
  "email": "an.nguyen@example.com",
  "phone": "0901 234 567",
  "confirmMinor": false
}

// BeneficiaryResponse — KHÔNG có số CCCD đầy đủ
{
  "id": "0199e7d2-...",
  "citizenIdLast4": "8901",
  "fullName": "Nguyễn Văn An",
  "dateOfBirth": "1990-05-20",
  "email": "an.nguyen@example.com",
  "phone": "0901234567",
  "isMinor": false,
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
- **Dưới 18 tuổi** (tính theo giờ Việt Nam): lần đầu lưu cần `confirmMinor: true`, không thì `409 MINOR_CONFIRMATION_REQUIRED`; FE hiện cảnh báo "người này hiện chưa nhận được qua app" rồi gọi lại. Đã xác nhận rồi thì các lần sửa sau không cần gửi lại. Ngày sinh ở tương lai → 400.
- Hạng Free tạo hồ sơ → `403 PAID_TIER_REQUIRED`. Dịch vụ đang ân hạn: tạo/sửa bị chặn (`VAULT_NOT_EDITABLE`, `SERVICE_READ_ONLY`), xóa vẫn được.
- `containerIds`: các gói đang có người này, theo thứ tự gắn; rỗng khi chưa ở gói nào.
- Tạo hồ sơ **không** tăng `planConfigVersion` và không chiếm suất; suất chỉ tính khi hồ sơ được gắn vào gói (mục 6.6).
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
- **POST /check-ins** đóng kỳ hiện tại, mở kỳ mới và đưa kế hoạch về `ACTIVE`; cho phép khi kế hoạch `ACTIVE`/`CHECKIN_OVERDUE`/`AWAITING_CHECK`, kể cả khi dịch vụ đang ân hạn. Bấm lặp trong **60 giây** thì trả trạng thái hiện tại, không mở kỳ mới. Hai yêu cầu chạy song song: bên thua nhận `409 EDIT_CONFLICT` (gọi lại `GET`). Kế hoạch `DRAFT` hoặc đã sang chứng tử/bàn giao → `409 CHECKIN_NOT_ALLOWED` kèm trường phụ `planStatus`.
- **PATCH check-in-settings**: `cycleDays` ∈ {30, 60, 90}, `graceDays` ∈ {7, 14, 30}; phải gửi ít nhất một trong hai; `etag` lấy từ `settings.etag`. Đang có kỳ chạy thì lựa chọn mới nằm ở `pendingCycleDays`/`pendingGraceDays` và **chỉ áp dụng từ kỳ sau** (DMS-01). Kế hoạch đã sang chứng tử/bàn giao/đóng → `409 VAULT_NOT_EDITABLE` (`reason: PLAN_LOCKED`).
- Nhắc điểm danh gửi 4 mốc, mỗi mốc một lần: trước hạn 7 ngày, trước hạn 1 ngày, khi bắt đầu thời gian chờ, khi còn 2 ngày chờ (DMS-06).
- Chưa có API kích hoạt kế hoạch nên kỳ đầu tiên được mở khi Owner bấm "Điểm danh" lần đầu trên kế hoạch đang chạy; khi có chức năng kích hoạt sẽ gọi `ICheckInScheduler.StartFirstPeriodAsync`.

## 7. Tóm tắt thay đổi FE cần làm

| # | Việc | File FE |
|---|---|---|
| 1 | Gửi `withCredentials: true` cho các request `/v1/auth/*` | `shared/api/axiosClient.ts` |
| 2 | Thêm luồng 401 → refresh một lần → thử lại; refresh lỗi → về đăng nhập (bỏ qua `REAUTHENTICATION_FAILED`) | `shared/api/axiosClient.ts` |
| 3 | Đọc lỗi theo `code` của ProblemDetails; lỗi validate là `errors` dạng object theo field | `shared/types`, chỗ hiển thị lỗi form |
| 4 | Không dùng `ApiResponse<T>` cho API này (BE không bọc envelope) | `shared/types` |
| 5 | Bổ sung hằng trạng thái: `LOCKED`, `DISABLED`, `OPEN`, `CLOSED`, `READY`, `ARCHIVED`, `PAID_ACTIVE`... (hiện `STATUS` chưa có) | `shared/constants` |
| 6 | Gọi route có tiền tố `/v1` (base URL đã có `/api`) | các `features/*/api` |
