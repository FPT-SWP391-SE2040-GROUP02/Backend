# Executor invitation and plan activation

Implemented 15/10/2026. Canonical prefix is `/api/v1` only. Swagger UI: `/swagger/index.html`; OpenAPI: `/openapi/v1.json`.

| Method / path | Caller / body | Result |
|---|---|---|
| GET `/vaults/{vaultId}/plan-setup` | Owner Bearer | Status, configVersion, missingRequirements, policy/scope versions and text, invitation metadata |
| POST `/vaults/{vaultId}/executors` | Owner: email, role PRIMARY/BACKUP, optional beneficiaryId | 201 invitation metadata; no raw token |
| POST `/vaults/{vaultId}/executors/{executorId}/resend` | Owner: etag | Rotates token and expiry; old link invalid |
| POST `/executor-invitations/inspect` | Public: token | Minimal role/status/expiry/scope; no owner/vault content |
| POST `/executor-invitations/respond` | Verified active account: token, accept, acknowledgeScope, scopeVersion | Explicit accept/decline; Owner and second claimant rejected |
| POST `/vaults/{vaultId}/activate` | Owner: configVersion, policyVersion, acknowledgePolicy=true | Recheck requirements, atomically activate and open first period |

Paths in the table follow `/api/v1`. Request/response schemas are generated from DTOs in OpenAPI. FE validates wire responses with Zod.

Activation requires an active email-verified Owner, open vault, unexpired paid service, latest Passed Owner eKYC with purpose OWNER_ENROLLMENT, a Ready container, and an active verified distinct primary Executor who accepted the current scope. If primary is also a beneficiary, an independent accepted non-beneficiary backup is required. Policy acknowledgement is explicit and stores version/time. A stale configVersion returns EDIT_CONFLICT. Duplicate activation does not reset deadlines. Deactivation suspends accepted Executors; reactivation requires fresh acceptance. Existing pending check-in settings apply when a new period starts.

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
