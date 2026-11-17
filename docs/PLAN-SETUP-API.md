# Executor invitation and plan activation

Implemented 15/10/2026. Canonical prefix is `/api/v1` only. Swagger UI: `/swagger/index.html`; OpenAPI: `/openapi/v1.json`.

| Method / path | Caller / body | Result |
|---|---|---|
| GET `/vaults/{vaultId}/plan-setup` | Owner Bearer | Status, configVersion, missingRequirements, policy/scope versions and text, invitation metadata |
| POST `/vaults/{vaultId}/executors` | Owner + `X-Vault-Pin`: `{ email }` (không còn `role`, `beneficiaryId`) | 201 invitation metadata; no raw token. Người thực thi là tài khoản nhân sự (role EXECUTOR), không phải người nhận |
| POST `/vaults/{vaultId}/executors/{executorId}/resend` | Owner: etag | Rotates token and expiry; old link invalid |
| POST `/executor-invitations/inspect` | Public: token | Minimal role/status/expiry/scope; no owner/vault content |
| POST `/executor-invitations/respond` | Verified active account: token, accept, acknowledgeScope, scopeVersion, `password?` (≤128) | Explicit accept/decline. **Chấp nhận = Người thực thi ký nhận vai bằng mật khẩu tài khoản**: `accept=true` mà thiếu/để trống `password` → 400 `VALIDATION_ERROR` (trường `password`: "Nhập mật khẩu tài khoản để ký nhận vai Người thực thi."); sai → 403 `SIGNING_PASSWORD_INVALID` kèm `attemptsRemaining`; sai 5 lần liên tiếp → 423 `SIGNING_LOCKED` kèm `lockedUntil` (15 phút → 1 giờ → 24 giờ, đang khóa thì mật khẩu đúng cũng bị từ chối); tài khoản chưa có mật khẩu → 403 `SIGNING_PASSWORD_INVALID` (không kèm `attemptsRemaining`). Mật khẩu chỉ được kiểm khi lời mời còn hợp lệ cho đúng tài khoản này (link sai/hết hạn/khác email không tốn lượt thử). Từ chối (`accept=false`) không cần và bỏ qua `password`. Mật khẩu không được log hay ghi nhật ký; Owner, second claimant and an account whose verified email differs from the invited email (409 `EXECUTOR_NOT_ALLOWED`, reason `INVITEE_EMAIL_MISMATCH`) are rejected |
| GET `/executor-invitations/mine` | Signed-in account | `{ scopeVersion, scopeText, pending[], ended[] }`. Mỗi lời mời: `id, ownerName, inviteeEmail, status (INVITED/DECLINED/EXPIRED/REVOKED), invitedAt, expiresAt, endedAt`. Chỉ lời mời gửi tới email **đã xác minh** của tài khoản; email chưa xác minh hoặc tài khoản không Active thì hai danh sách rỗng. Không có token, không có nội dung kho. `pending` sắp hết hạn trước, `ended` mới nhất trước (tối đa 50 lời mời) |
| POST `/executor-invitations/{invitationId}/respond` | Verified active account: accept, acknowledgeScope, scopeVersion, `password?` (≤128) | Giống respond bằng token (cùng luật và mã lỗi, gồm `password` khi chấp nhận: 400 / 403 `SIGNING_PASSWORD_INVALID` / 423 `SIGNING_LOCKED`). Lời mời không gửi tới email của người gọi → 404 `EXECUTOR_INVITATION_INVALID` (không lộ lời mời của người khác) |
| POST `/vaults/{vaultId}/activate` | Owner + **`X-Vault-Pin`** (bắt buộc): configVersion, policyVersion, acknowledgePolicy=true | Recheck requirements, atomically activate and open first period; ký di chúc bằng PIN; server tự tạo mã mở gói (không trả mã) |

Paths in the table follow `/api/v1`. Request/response schemas are generated from DTOs in OpenAPI. FE validates wire responses with Zod.

Activation requires an active email-verified Owner, open vault, unexpired paid service, latest Passed Owner eKYC with purpose OWNER_ENROLLMENT, a Ready container, and an active verified distinct primary Executor who accepted the current scope. (Executor dự phòng đã bỏ từ 30/10/2026; một Người thực thi là nhân sự role EXECUTOR, không phải người nhận.) Policy acknowledgement is explicit and stores version/time. A stale configVersion returns EDIT_CONFLICT. Duplicate activation does not reset deadlines. Deactivation suspends accepted Executors; reactivation requires fresh acceptance. Existing pending check-in settings apply when a new period starts.

**Lời mời của tôi (FE: mục Lời mời).** Người được mời xem và trả lời lời mời ngay trong app, không cần link email: `GET /executor-invitations/mine` rồi `POST /executor-invitations/{invitationId}/respond`. Vẫn phải bấm chấp nhận rõ ràng; chỉ email đã xác minh trùng email được mời mới thấy và trả lời được. `GET mine` không áp giới hạn tần suất `executor-invitations` (chỉ đọc theo tài khoản).

Invitations expire after seven days. Only the token hash is persisted. The email link uses `/executor-invitation#token=...`; inspect/respond send the token in the JSON body. No token appears in API responses or cache keys. The token is a capability: a verified separate account explicitly claims it; invitation email alone does not bind an account. Do not forward links.

SMTP is opt-in: `ExecutorInvitations:DeliveryEnabled=true` plus existing configured Email:Smtp settings and App:ClientUrl. google-dev keeps delivery disabled. Null invitationSentAt means delivery is unconfirmed; the UI must not say mail was sent. Resend is explicit and rotates the previous link. Changing/revoking a pending assignment is not exposed by these endpoints; this checkpoint implements invite, resend and respond only.

Business failures use the existing ProblemDetails contract and traceId: EXECUTOR_INVITATION_INVALID (404), EXECUTOR_INVITATION_CONFLICT / EXECUTOR_NOT_ALLOWED / PLAN_ACTIVATION_NOT_ALLOWED / PLAN_ACTIVATION_REQUIREMENTS_NOT_MET (409), POLICY_ACKNOWLEDGEMENT_REQUIRED (400), plus existing VAULT_NOT_FOUND, PAID_TIER_REQUIRED and EDIT_CONFLICT. Requirements failures include missingRequirements. Serializable deadlock conflicts return EDIT_CONFLICT without blind mutation retries.

## Schema and dev rollout

Migration `20261015094749_AddPlanSetupConsent` adds four nullable fields: Executor invitationSentAt/acceptedScopeVersion and plan activationPolicyVersion/activationPolicyAcknowledgedAt. No existing rows are rewritten. Applied only to LegacyVault_OfficialDev after a COPY_ONLY backup and successful RESTORE VERIFYONLY WITH CHECKSUM (not a full restore drill). Backup is in the SQL instance's Backup directory, named `LegacyVault_OfficialDev_before_plan_setup_20261015_f0c2300581a741a6ba803997f844709a.bak`.

Rollback application binaries while retaining the additive nullable columns. Do not drop consent columns after live use; a destructive schema rollback requires a separate data recovery decision. Production rollout is not performed by this task.

## Verification

PlanSetupFlowTests exercise real HTTP/SQL against isolated GUID test databases, with synthetic paid/eKYC fixtures and an in-memory capturing email sender. They cover full invite/accept/activate/check-in, duplicates, concurrent activation, forced failure after a DB flush with full transaction rollback, missing requirements, expired service/token, stale policy/config, wrong Owner/claimant and backup independence. They do not verify a real eKYC provider or SMTP delivery. No real documents or external mail are used.

## Thu hồi, đổi Executor chính, hết hạn và nhắc (bổ sung 16/10/2026)

| Method / path | Caller / body | Result |
|---|---|---|
| POST `/vaults/{vaultId}/executors/{executorId}/revoke` | Owner: `{ etag }` | 200 trạng thái mới (`REVOKED`); gọi lặp trên dòng đã kết thúc trả trạng thái hiện tại |

- **Quyền quản lý** (SETUP-21): thu hồi bị chặn `VAULT_NOT_EDITABLE` khi hồ sơ chứng tử đang mở, đang bàn giao, bị chặn hoặc kho đóng.
- **Kế hoạch đang chạy** không được bỏ trống Executor chính (ASSIGN-05): thu hồi Executor chính đã chấp nhận → `409 EXECUTOR_NOT_ALLOWED` `REPLACEMENT_REQUIRED`. Muốn đổi, Owner `POST .../executors` với `role = PRIMARY` mời người thay: người cũ **giữ vai trò** tới khi người mới chấp nhận, lúc đó người cũ thành `REVOKED` trong cùng giao dịch (cột `ReplacesExecutorId`). Executor chính cũng là người nhận thì không được thu hồi Executor dự phòng độc lập cuối cùng (`BACKUP_EXECUTOR_REQUIRED`).
- Mời và chấp nhận lời mời giờ làm được cả khi kế hoạch đang chạy (không chỉ `DRAFT`); các trạng thái khóa vẫn bị chặn.
- **Job nền** (`Executor:InvitationJobEnabled`, `Executor:InvitationPollSeconds`, mặc định 60): lời mời quá 7 ngày → `EXPIRED` (`EndedAt` = `ExpiresAt`); nhắc **một lần** ở giờ thứ 48 qua `IExecutorReminderSender` (cùng công tắc `ExecutorInvitations:DeliveryEnabled`; chưa bật hoặc gửi lỗi thì không đánh dấu, vòng sau thử lại). Thư nhắc không có link vì DB chỉ lưu mã băm token; không đổi `etag`.

## Màn kích hoạt: gói, tài sản ngoài gói, cảnh báo (bổ sung 17/10/2026)

`GET /api/v1/vaults/{vaultId}/plan-setup` và phản hồi `200` của `POST /api/v1/vaults/{vaultId}/activate` có thêm ba field ở cuối phản hồi:

| Field | Kiểu | Nội dung |
|---|---|---|
| `containers` | Mảng object | Gói `READY` và `DRAFT`, không có `ARCHIVED`. Mỗi phần tử gồm `id`, `name`, `status`, `assetCount`, `designationCount`, `missingRequirements`. |
| `assetsOutsideContainer` | Mảng object | Tài sản chưa có liên kết trong `ContainerAssets`. Mỗi phần tử gồm `id`, `name`, `kind` (`FILE` hoặc `TEXT`). Chỉ trả metadata, không chứa nội dung tài sản. |
| `warnings` | Mảng chuỗi | Cảnh báo cấu hình; hiện có `NO_BACKUP_EXECUTOR`. Không có cảnh báo thì trả `[]`. |

- **SETUP-01:** `containers` sắp theo `CreatedAt`, rồi `Id`, tăng dần. `missingRequirements` của từng gói dùng `ContainerRules.GetMissingRequirements`: `NO_ASSET`, `NO_DESIGNATION`; gói `READY` có danh sách rỗng. FE có thể tách nhóm "Sẽ bàn giao" (`READY`) và "Gói chưa đủ" (`DRAFT`).
- **ASSET-03 / AC-68:** `assetsOutsideContainer` sắp theo `CreatedAt`, rồi `Id`, tăng dần. Tài sản trong gói `DRAFT` đã được tính ở `containers[].assetCount`, không lặp lại trong danh sách này. Gói lưu trữ đã gỡ các liên kết tài sản. Hai danh sách không phân trang.
- **SETUP-10:** chỉ bỏ `NO_BACKUP_EXECUTOR` khi có Executor vai trò Backup trong danh sách đã chấp nhận, đúng scope version hiện hành, có tài khoản hoạt động, email đã xác minh và khác Owner. Backup mới được mời (`INVITED`) vẫn có cảnh báo.
- **Cảnh báo không chặn kích hoạt:** `warnings` không được thêm vào `missingRequirements`, không đổi cách tính `canActivate` hay điều kiện của `POST /activate`. Nếu Executor chính cũng là người nhận và thiếu Backup độc lập đủ điều kiện, mã chặn `BACKUP_EXECUTOR_REQUIRED` vẫn có thể xuất hiện cùng cảnh báo `NO_BACKUP_EXECUTOR`.
- **EKYC-01 / AC-46:** `OWNER_EKYC_REQUIRED` xét lượt mới nhất theo `CreatedAt`, rồi `Id`, giảm dần, có mục đích `OWNER_ENROLLMENT` và yêu cầu lượt đó `PASSED`. Lượt `BENEFICIARY_CLAIM` hoặc `OWNER_ALIVE` mới hơn không thay thế kết quả này. Dữ liệu dev cũ có mục đích mặc định OwnerEnrollment vẫn được tính, kể cả chưa có cờ liveness.

Ví dụ GET rút gọn, giả định các điều kiện kích hoạt khác đã đủ:

```json
{
  "planStatus": "DRAFT",
  "configVersion": 7,
  "canActivate": true,
  "missingRequirements": [],
  "containers": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "name": "Gói tài liệu thử nghiệm",
      "status": "READY",
      "assetCount": 1,
      "designationCount": 1,
      "missingRequirements": []
    },
    {
      "id": "00000000-0000-0000-0000-000000000002",
      "name": "Gói chưa có người nhận",
      "status": "DRAFT",
      "assetCount": 1,
      "designationCount": 0,
      "missingRequirements": ["NO_DESIGNATION"]
    }
  ],
  "assetsOutsideContainer": [
    {
      "id": "00000000-0000-0000-0000-000000000003",
      "name": "Ghi chú thử nghiệm ngoài gói",
      "kind": "TEXT"
    }
  ],
  "warnings": ["NO_BACKUP_EXECUTOR"]
}
```

Lỗi `409 PLAN_ACTIVATION_REQUIREMENTS_NOT_MET` giữ nguyên ProblemDetails với `missingRequirements`; ba field mới thuộc phản hồi thành công. Thử tay bằng `http/plan-setup.http`; dữ liệu giả lập cho DB local nằm trong `http/plan-dev.sql`.

## Di chúc điện tử, mã mở gói do server tạo, người nhận (bổ sung 16/11/2026, nhánh feat/will-signature)

Hiện trạng nghiệp vụ: Người thực thi là **tài khoản nhân sự LegacyVault** (role `EXECUTOR`) do Admin tạo, Chủ kho chọn một người. Người nhận không eKYC, không gọi video. Trang bàn giao của Chủ kho là "Di chúc" điện tử: Chủ kho ký bằng PIN kho lúc kích hoạt, Người thực thi ký khi chấp nhận lời mời. Mọi trường mới trong response đều tùy chọn để client cũ không vỡ.

**`GET /vaults/{vaultId}/plan-setup` (và response của `POST .../activate`)** thêm:

| Field | Kiểu | Nội dung |
|---|---|---|
| `ownerFullName`, `ownerEmail` | string | Họ tên và email tài khoản Chủ kho. |
| `activatedAt` | ISO hoặc null | Thời điểm kích hoạt. FE chỉ coi Chủ kho "đã ký" khi `planStatus != DRAFT` và `activatedAt != null`. |
| `recipients[]` | mảng | Người nhận theo từng người, lấy từ chỉ định của các gói đang mở (không gồm gói đã xóa), sắp theo `fullName`; người không có gói nào thì không xuất hiện. Mỗi phần tử: `beneficiaryId`, `fullName` (giải mã từ dữ liệu cá nhân, không log), `hasMessage` (đã có lời nhắn; **không trả nội dung lời nhắn**), `containers[]` (`id`, `name`, `assetCount`, `status` `READY`/`DRAFT`). |
| `executors[].fullName` | string hoặc null | Họ tên tài khoản Người thực thi nếu đã liên kết (sau khi chấp nhận), ngược lại null. |

**Kích hoạt (`POST .../activate`).** Bắt buộc header `X-Vault-Pin` (thiếu/sai/khóa PIN trả cùng mã lỗi `VAULT_PIN_REQUIRED` / `INVALID_VAULT_PIN` / `VAULT_PIN_LOCKED` như các thao tác PIN khác). Cùng một giao dịch: đặt kế hoạch `ACTIVE`, mở kỳ điểm danh đầu, **tự tạo mã mở gói** nếu kho chưa có (hoặc chỉ có bản cũ chỉ-hash thì tạo mới, version + 1; không trả mã trong response) và ghi nhật ký `PlanActivated` (actor Owner, đích Vault). Gọi lặp khi đã `ACTIVE` không ghi thêm.

**Mã mở gói (executor key).**

| Method / path | Ai | Kết quả |
|---|---|---|
| `GET /vaults/{vaultId}/executor-key` | Owner | `{ hasKey, version, issuedAt, reissuedAt, canIssue }`, không có mã. |
| `POST /vaults/{vaultId}/executor-key/reissue` | Owner + `X-Vault-Pin` | Đổi mã (version + 1), trả `ExecutorKeyStatusResponse` như GET, **không có trường `shard`**. Chưa có mã → 409 `EXECUTOR_KEY_NOT_ISSUED`; kế hoạch đã khóa → 409 `VAULT_NOT_EDITABLE`. |
| `GET /executor/vaults/{vaultId}/handover-key` | Người thực thi đã chấp nhận | Đọc mã (16 ký tự Crockford base32, dạng `XXXX-XXXX-XXXX-XXXX`). Không phải Executor đã chấp nhận → 404 `VAULT_NOT_FOUND`. Kế hoạch chưa `HANDOVER_IN_PROGRESS` → 409 `HANDOVER_NOT_ALLOWED`, `reason = HANDOVER_NOT_STARTED`. |

- **Đã xóa** endpoint Chủ kho cấp mã lần đầu (`POST /vaults/{vaultId}/executor-key`); gọi sẽ nhận 405. Mã chỉ do server tạo.
- Tự đổi mã (version + 1, nhật ký `ExecutorKeyReissued`, actor System) khi **đổi Người thực thi**: người thay chấp nhận làm người cũ `REVOKED`, hoặc Admin giao người thay khi người cũ bị thu hồi ngay. Chỉ đổi nếu kho đã có mã.
- Khi **bắt đầu bàn giao** (`HandoverStarter` đặt `HANDOVER_IN_PROGRESS`) server đảm bảo có mã đọc được: chưa có hoặc chỉ có hash thì tạo mới (actor System).
- Mã lưu dạng hash SHA-256 (gắn kho và phiên bản) và bản mã hóa phong bì AES-256-GCM; chỉ là cổng mở khóa việc tải của người nhận (người nhận nhập mã trước khi tải), không dùng mã hóa nội dung. Nhật ký chỉ ghi version, không ghi mã hay hash (SEC-07). `send-key` (gửi mã cho người nhận) giữ nguyên.

**Nhân sự không được làm người nhận.**
- Tạo/sửa người nhận với email trùng tài khoản role `EXECUTOR`, `VERIFIER`, `ADMIN` → 400 `VALIDATION_ERROR`, trường `email`: "Email này thuộc tài khoản nhân sự của LegacyVault nên không thể làm người nhận."
- Nhận gói trong app (`claim`, `turns/{turnId}/claim`): tài khoản nhân sự → 409 `HANDOVER_NOT_ALLOWED`, `reason = STAFF_CANNOT_CLAIM`.
- Admin đổi role sang nhân sự (`PUT /admin/users/{id}/role`): người dùng đang có lượt bàn giao đã nhận còn mở (chưa `HANDOVER_COMMITTED`/`DECLINED`/`DECISION_EXPIRED`/`CANCELLED`/`LAPSED`) → 409 `ADMIN_ACTION_NOT_ALLOWED`, `reason = RECIPIENT_HAS_ACTIVE_HANDOVER`. Email người nhận được mã hóa nên không dò theo email ở bước này; hai bước chặn ở trên lo phần còn lại.
- `GET /vaults/{vaultId}/executor-candidates?q=`: chỉ trả tài khoản role `EXECUTOR`, Active, đã xác minh email và đã có `LegalVerifiedAt` (cùng điều kiện được mời), không gồm Chủ kho.

**Nhật ký mới (`AuditAction`, lưu dạng chuỗi, không migration):** `PlanActivated = 62` (Chủ kho ký bằng PIN), `ExecutorAccepted = 63` (Người thực thi tự chấp nhận lời mời, actor Executor, đích Vault).

## Phiên bản di chúc đã ký, ký lại, danh tính lập di chúc (bổ sung 17/11/2026, nhánh feat/will-signature)

**Quy tắc nghiệp vụ.** Bàn giao chỉ theo **bản ký gần nhất**. Mỗi lần Chủ kho ký (kích hoạt hoặc ký lại bằng PIN) server lưu một phiên bản trong bảng `WillVersions`; khi bắt đầu bàn giao, `HandoverStarter` chỉ dùng phần **có trong bản ký gần nhất VÀ vẫn còn tồn tại** (giao của bản chụp và cấu hình hiện tại):

- Xóa gói/tài sản/người nhận sau khi ký: có hiệu lực ngay (không bàn giao thứ đã xóa); phần còn lại vẫn bàn giao theo bản ký.
- Thêm gói, thêm tài sản vào gói, đổi người nhận hoặc gắn người nhận mới mà **chưa ký lại**: bị **bỏ qua** khi bàn giao. Gói mà mọi tài sản trong bản ký đã bị gỡ (chỉ còn tài sản thêm sau khi ký) cũng không tạo lượt; không còn lượt nào → 409 `HANDOVER_NOT_ALLOWED`, `reason = NOTHING_TO_HAND_OVER`.
- Kế hoạch đang chạy từ trước khi có tính năng này và **chưa có dòng `WillVersions` nào**: giữ cách cũ (theo cấu hình hiện tại). Người thực thi vẫn **không** ký lại khi đổi tài sản/người nhận (chỉ ký lại khi phạm vi nhiệm vụ đổi, như trước).
- Chưa có hạn ân hạn hay tự động đưa kế hoạch về nháp.

**Bảng `WillVersions`** (migration `AddWillVersions`): `Id`, `VaultId` (FK, unique cùng `VersionNo`), `VersionNo` (1, 2, ...), `ConfigVersion` (lúc ký), `SignedAt`, `SnapshotJson` (nvarchar(max)): `{"recipients":[{"containerId","beneficiaryId"}],"assets":[{"containerId","assetId"}]}`. Chỉ có id, không có tên tài sản/người nhận hay nội dung nhạy cảm; chỉ gói `READY` vào bản chụp. Kích hoạt tạo phiên bản kế tiếp trong cùng giao dịch (phiên bản 1 ở lần kích hoạt đầu; kích hoạt lại sau khi về nháp tạo phiên bản N+1).

| Method / path | Caller / body | Result |
|---|---|---|
| POST `/vaults/{vaultId}/will/sign` | Owner + **`X-Vault-Pin`**: `{ configVersion }` | Ký lại: tạo phiên bản N+1, ghi nhật ký `WillResigned` (đích Vault, `targetVersion` = số phiên bản), trả `PlanSetupResponse` mới. Không có thay đổi chưa ký → 200 trả nguyên trạng, **không** tạo phiên bản (gọi lặp an toàn). Kế hoạch chưa chạy (`IsRunning` = false) → 409 `WILL_SIGN_NOT_ALLOWED`. `configVersion` cũ → 409 `EDIT_CONFLICT`. Thiếu/sai/khóa PIN → 403 `VAULT_PIN_REQUIRED` / `INVALID_VAULT_PIN`, 423 `VAULT_PIN_LOCKED`. Người khác kể cả Admin/Executor → 404 `VAULT_NOT_FOUND`. Kế hoạch chạy từ trước, chưa có phiên bản nào: ký lần đầu tạo phiên bản 1 |
| GET `/vaults/{vaultId}/will/identity` | Owner + **`X-Vault-Pin`** | Danh tính đầy đủ đã giải mã để lập di chúc: `{ owner, recipients[] }`; `owner` = `{ fullName, dateOfBirth, citizenId (12 số), residenceAddress }` hoặc **null** khi Chủ kho chưa có danh tính eKYC đã gắn; `recipients[]` = `{ beneficiaryId, fullName, dateOfBirth, citizenId }` cho mọi hồ sơ người nhận của kho. `Cache-Control: no-store`. Giới hạn tần suất như policy `plan-setup`. Thiếu/sai/khóa PIN → 403 `VAULT_PIN_REQUIRED` / `INVALID_VAULT_PIN`, 423 `VAULT_PIN_LOCKED` (không ghi nhật ký). Mỗi lần gọi thành công ghi nhật ký `WillIdentityViewed` (không ghi giá trị giải mã; không log họ tên/CCCD). Người khác kể cả Admin → 404 `VAULT_NOT_FOUND`; chưa đăng nhập → 401 |

**`PlanSetupResponse`** (GET `plan-setup`, response của `activate` và `will/sign`) thêm bốn field (đều tùy chọn cho client cũ):

| Field | Kiểu | Nội dung |
|---|---|---|
| `willVersion` | int hoặc null | Số phiên bản ký gần nhất; null nếu chưa có phiên bản nào. |
| `willSignedAt` | ISO hoặc null | Thời điểm ký phiên bản gần nhất. |
| `hasUnsignedChanges` | bool | Cấu hình hiện tại khác bản chụp gần nhất. Chỉ tính khi kế hoạch đang chạy; nháp hoặc chưa có phiên bản → `false`. |
| `unsignedChanges` | object | Chỉ **số đếm**, không có tên: `removedContainers`, `addedContainers`, `removedAssets`, `addedAssets`, `removedRecipients`, `addedRecipients`, `changedRecipientCount`. Gói thêm/xóa tính ở mức gói; tài sản và người nhận chỉ đếm trong các gói có ở **cả** bản ký lẫn cấu hình hiện tại; `changedRecipientCount` = số gói như vậy mà tập người nhận khác bản ký. Gói đang `READY` mà về `DRAFT` tính là gói bị xóa. Không có thay đổi: toàn số 0. |

Lỗi mới: `WILL_SIGN_NOT_ALLOWED` (409). Nhật ký mới (`AuditAction`, lưu dạng chuỗi): `WillResigned = 64`, `WillIdentityViewed = 65`. Thử tay: `http/plan-setup.http`.
