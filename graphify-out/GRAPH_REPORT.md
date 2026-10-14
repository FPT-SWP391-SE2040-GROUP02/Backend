# Graph Report - Backend  (2026-10-14)

## Corpus Check
- 278 files · ~87,158 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: .http 7, (none) 5, .props 2)

## Summary
- 2169 nodes · 6817 edges · 107 communities (93 shown, 14 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 780 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `f8cb40a8`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- SqlFact
- Task
- LegacyVault.Application.Services
- ContainerResponse
- Guid
- LegacyVault.Domain.Entities
- AppException
- GoogleAuthServiceTests
- VaultAssetFlowTests.cs
- Hướng dẫn Git và quy trình code chức năng
- LegacyVault.Infrastructure.Data.Migrations
- Vault
- .AddInfrastructure
- BeneficiaryService
- AuthResult
- CheckInSchedule
- LoginThrottle
- InfrastructureConfig.cs
- LegacyVault.Domain.Enums
- .CheckInAsync
- .UpdateSettingsAsync
- ExternalLogin
- LegacyVault.Infrastructure.csproj
- Task 7: Nối vào hệ thống — job nền, gửi nhắc, DI, controller, cấu hình
- UserResponse
- EmailVerificationToken
- CheckInReminderKind
- HandoverPlan
- Beneficiary
- SmtpEmailTests
- BeneficiaryPersonalData
- ICheckInReminderSender
- VaultResponse
- ErrorCode
- ExternalLoginLinkTicket
- User
- CheckInStatusResponse
- Container
- EntityTypeBuilder
- .RefreshAsync
- CheckInReminder
- RefreshToken
- DateTimeOffset
- AppDbContext
- CheckInPeriodResponse
- VaultEntitlement
- AssetEncryptionTests
- AuthSessionResponse
- Fact
- PlanStatus
- SmtpSettings
- CheckInPeriod
- VaultAssetFlowTests
- EmailStartupLogger
- Asset
- AssetVersion
- AssetEncryptionSettings
- PaginatedList
- BeneficiaryResponse
- .SaveAsync
- system_componentmodel_dataannotations
- Hiểu dự án LegacyVault: dự án làm gì và mã nguồn nằm ở đâu
- DataKeyProtector
- GoogleAuthFakes.cs
- GlobalExceptionHandler
- http
- Designation
- .EncryptAsync
- .VerifyAsync
- ContainerAsset
- Program.cs
- Hợp đồng API — Đăng nhập và Tạo kho
- ApiFactory
- AssetVersionResponse
- .SaveChangesAsync
- CreateBeneficiaryRequest
- CreateFileAssetRequest
- UpdateBeneficiaryRequest
- AppErrorType
- ApiConfig
- CreateFileAssetForm
- DateOnly
- 6. Endpoint
- LoginMethodResponse
- CurrentUser
- SetPasswordRequest
- EntitlementStatus
- 5. API (route theo handoff mục 7; JSON camelCase, lỗi ProblemDetails như `docs/api-contract.md`)
- DatabaseHealthCheck
- CreateTextAssetRequest
- LinkGoogleRequest
- RemovePasswordRequest
- UpdateContainerRequest
- UpdateVaultRequest
- VaultNotEditableReason
- UserRole
- UserStatus
- Global Constraints
- pull_request_template.md
- CreateContainerRequest
- CreateBeneficiaryResult
- OpenVaultResult
- .From
- LogoutRequest
- CLAUDE.md
- .claude/CLAUDE.md
- .ToResponse

## God Nodes (most connected - your core abstractions)
1. `LegacyVault.Domain.Entities` - 95 edges
2. `AppDbContext` - 69 edges
3. `LegacyVault.Domain.Enums` - 57 edges
4. `Vault` - 56 edges
5. `LegacyVault.Application.Services` - 55 edges
6. `LegacyVault.Application.Dtos.Requests` - 52 edges
7. `AppException` - 50 edges
8. `User` - 50 edges
9. `CheckInPeriod` - 49 edges
10. `3. Tính năng ↔ SRS ↔ mã nguồn` - 48 edges

## Surprising Connections (you probably didn't know these)
- `6.7. Tài sản (Asset)` --references--> `AssetResponse`  [INFERRED]
  docs/api-contract.md → src/LegacyVault.Application/Dtos/Responses/AssetResponse.cs
- `6.8. Người nhận (Beneficiary)` --references--> `BeneficiaryResponse`  [INFERRED]
  docs/api-contract.md → src/LegacyVault.Application/Dtos/Responses/BeneficiaryResponse.cs
- `6.6. Gói (Container)` --references--> `ContainerResponse`  [INFERRED]
  docs/api-contract.md → src/LegacyVault.Application/Dtos/Responses/ContainerResponse.cs
- `6.4. Phương thức đăng nhập của tôi` --references--> `LoginMethodResponse`  [INFERRED]
  docs/api-contract.md → src/LegacyVault.Application/Dtos/Responses/LoginMethodResponse.cs
- `6.5. Kho` --references--> `VaultResponse`  [INFERRED]
  docs/api-contract.md → src/LegacyVault.Application/Dtos/Responses/VaultResponse.cs

## Import Cycles
- None detected.

## Communities (107 total, 14 thin omitted)

### Community 0 - "SqlFact"
Cohesion: 0.07
Nodes (8): Task 8: Test tích hợp SQL, tài liệu, kiểm chứng toàn bộ, BeneficiaryFlowTests, CheckInFlowTests, ContainerFlowTests, DesignationFlowTests, ApiFactory, StorageRoot, ApiTestHelpers

### Community 1 - "Task"
Cohesion: 0.10
Nodes (3): ICheckInScheduler, ILoginMethodService, CheckInRepository

### Community 2 - "LegacyVault.Application.Services"
Cohesion: 0.09
Nodes (13): LegacyVault.Application.Dtos.Responses, LegacyVault.Application.Dtos.Requests, LegacyVault.Application.Mappings, LegacyVault.Application.Services, LegacyVault.Api.Security, LegacyVault.Api.Controllers, LegacyVault.Api.Exceptions, LegacyVault.Application.Security (+5 more)

### Community 3 - "ContainerResponse"
Cohesion: 0.08
Nodes (21): BeneficiariesController, ContainersController, VaultsController, AddContainerBeneficiaryRequest, BeneficiaryId, ConfirmPlanImpactRequest, ConfirmPlanDeactivation, ContainerResponse (+13 more)

### Community 4 - "Guid"
Cohesion: 0.07
Nodes (22): 5.2. Trong mã nguồn hiện có, AssetsController, AddContainerAssetRequest, AssetId, AssetResponse, CreatedAt, CurrentVersion, Description (+14 more)

### Community 5 - "LegacyVault.Domain.Entities"
Cohesion: 0.11
Nodes (5): LegacyVault.Infrastructure.Repositories, LegacyVault.Infrastructure.Data, LegacyVault.Application.Repositories, LegacyVault.Infrastructure.Data.Configurations, LegacyVault.Domain.Entities

### Community 6 - "AppException"
Cohesion: 0.10
Nodes (8): AppException, Code, Extensions, Type, IContainerRepository, ContainerService, VaultRules, ContainerRepository

### Community 7 - "GoogleAuthServiceTests"
Cohesion: 0.08
Nodes (22): 6.3. Đăng nhập Google, ConfirmGoogleLinkRequest, LinkTicket, Password, RememberMe, GoogleLoginRequest, IdToken, Nonce (+14 more)

### Community 8 - "VaultAssetFlowTests.cs"
Cohesion: 0.09
Nodes (6): LegacyVault.IntegrationTests.Email, LegacyVault.IntegrationTests.TestSupport, LegacyVault.Infrastructure.Notifications, LegacyVault.Application.Notifications, LegacyVault.IntegrationTests, SqlFactAttribute

### Community 9 - "Hướng dẫn Git và quy trình code chức năng"
Cohesion: 0.05
Nodes (39): Khi nhiều người cùng làm một chức năng, Quy ước làm việc nhóm, Code có sẵn, Các quyết định nghiệp vụ phải giữ theo SRS 3.14.0, Kiến trúc bộ khung, Môi trường, Quyết định, Trách nhiệm (+31 more)

### Community 10 - "LegacyVault.Infrastructure.Data.Migrations"
Cohesion: 0.11
Nodes (7): LegacyVault.Infrastructure.Data.Migrations, InitialCreate, AddAssetsAndVaultRules, AddBeneficiaries, AddDesignations, AddCheckIn, AppDbContextModelSnapshot

### Community 11 - "Vault"
Cohesion: 0.09
Nodes (19): IVaultRepository, IVaultService, VaultService, Vault, ClosedAt, Containers, CreatedAt, Description (+11 more)

### Community 12 - ".AddInfrastructure"
Cohesion: 0.10
Nodes (26): 3.1. Đăng nhập (đã có), 3.3. Tài sản và mã hóa (đã có, phần tôi làm), 3.4. Điểm danh, deadman switch (đã có phần lõi), 3. Tính năng ↔ SRS ↔ mã nguồn, GoogleAuthController, ILoginThrottleRepository, IUnitOfWork, IUserRepository (+18 more)

### Community 13 - "BeneficiaryService"
Cohesion: 0.14
Nodes (6): IBeneficiaryDataProtector, BeneficiaryService, MatchedBeneficiary, Beneficiary, Data, IBeneficiaryService

### Community 14 - "AuthResult"
Cohesion: 0.09
Nodes (15): AuthController, AuthCookieWriter, AuthResult, IsPersistent, RefreshToken, RefreshTokenExpiresAt, Session, GoogleLoginResult (+7 more)

### Community 15 - "CheckInSchedule"
Cohesion: 0.10
Nodes (19): File Structure, Task 1: Dữ liệu — entity, enum, cấu hình EF, repository, migration, Task 3: Dữ liệu giả cho test, `CheckInScheduler` và `ICheckInReminderSender`, ICheckInRepository, CheckInScheduler, CheckInSchedule, CreatedAt, CycleDays (+11 more)

### Community 16 - "LoginThrottle"
Cohesion: 0.10
Nodes (16): AccountLockedException, ApiException, ErrorCode, ExtraData, StatusCode, GoogleAccountAlreadyLinkedException, LoginThrottle, Email (+8 more)

### Community 17 - "InfrastructureConfig.cs"
Cohesion: 0.12
Nodes (5): LegacyVault.Infrastructure.Jobs, LegacyVault.Application.Storage, LegacyVault.Infrastructure.Security, LegacyVault.Infrastructure.Storage, LegacyVault.Infrastructure.Health

### Community 18 - "LegacyVault.Domain.Enums"
Cohesion: 0.10
Nodes (6): LegacyVault.Domain.Enums, LegacyVault.IntegrationTests.CheckIn, LegacyVault.IntegrationTests.GoogleAuth, VaultStatus, Closed, Open

### Community 19 - ".CheckInAsync"
Cohesion: 0.18
Nodes (9): Task 4: `CheckInService` — xem trạng thái và điểm danh, PaginationRequest, IsAscending, PageIndex, PageSize, SearchTerm, SortBy, CheckInService (+1 more)

### Community 20 - ".UpdateSettingsAsync"
Cohesion: 0.17
Nodes (6): Task 5: `CheckInService.UpdateSettingsAsync` (PATCH cấu hình), EnumText, FakeCurrentUser, IsAuthenticated, UserId, CheckInSettingsTests

### Community 21 - "ExternalLogin"
Cohesion: 0.14
Nodes (15): IExternalLoginRepository, ExternalLogin, Id, LastUsedAt, LinkedAt, Provider, ProviderEmail, ProviderSubject (+7 more)

### Community 22 - "LegacyVault.Infrastructure.csproj"
Cohesion: 0.09
Nodes (20): Google.Apis.Auth, Konscious.Security.Cryptography.Argon2, MailKit, Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Mvc.Testing, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.Extensions.DependencyInjection.Abstractions (+12 more)

### Community 23 - "Task 7: Nối vào hệ thống — job nền, gửi nhắc, DI, controller, cấu hình"
Cohesion: 0.12
Nodes (15): Deadman switch (lõi điểm danh) — Kế hoạch triển khai, Review Focus, Task 7: Nối vào hệ thống — job nền, gửi nhắc, DI, controller, cấu hình, Điều chỉnh so với spec (do cách code thật hoạt động), 6. Job nền, EmailMasker, ICheckInJob, CheckInJobSettings (+7 more)

### Community 24 - "UserResponse"
Cohesion: 0.09
Nodes (19): 6.1. Đăng ký và xác minh email, RegisterRequest, ConfirmPassword, Email, FullName, Password, VerifyEmailRequest, Token (+11 more)

### Community 25 - "EmailVerificationToken"
Cohesion: 0.11
Nodes (14): ResendVerificationRequest, Email, IAppUrlProvider, IEmailVerificationTokenRepository, EmailVerificationToken, CreatedAt, ExpiresAt, Id (+6 more)

### Community 26 - "CheckInReminderKind"
Cohesion: 0.10
Nodes (14): Self-review, CheckInReminderKind, DueInOneDay, DueInSevenDays, GraceStarted, GraceTwoDaysLeft, CheckInReminderEmail, EmailContent (+6 more)

### Community 27 - "HandoverPlan"
Cohesion: 0.08
Nodes (22): 10. Quy ước code, 11. Sản phẩm bàn giao cuối đợt, 1. Mục tiêu và phạm vi, 2. Quyết định đã chốt, 3. Dữ liệu (3 bảng mới), 4. Luật thời gian — class thuần `CheckInTimeline` (Application, không đụng DB), 9. Kiểm thử, `CheckInPeriods` — mỗi kỳ một dòng (lịch sử điểm danh) (+14 more)

### Community 28 - "Beneficiary"
Cohesion: 0.13
Nodes (14): IBeneficiaryRepository, Beneficiary, CitizenIdLast4, ContactEncrypted, CreatedAt, EncryptionKeyId, Id, IdentityEncrypted (+6 more)

### Community 29 - "SmtpEmailTests"
Cohesion: 0.16
Nodes (6): SmtpMailSender, FakeSmtpServer, Port, ReceivedMessages, RejectRecipientWith, SmtpEmailTests

### Community 30 - "BeneficiaryPersonalData"
Cohesion: 0.11
Nodes (14): BeneficiaryPersonalData, CitizenId, DateOfBirth, Email, FullName, Phone, BeneficiaryDataProtector, ContactPayload (+6 more)

### Community 31 - "ICheckInReminderSender"
Cohesion: 0.18
Nodes (9): 3.5. Email (đã có, cần cấu hình), 7. Ranh giới với module khác, ICheckInReminderSender, IEmailSender, DevEmailSender, EmailConfig, LogCheckInReminderSender, SmtpCheckInReminderSender (+1 more)

### Community 32 - "VaultResponse"
Cohesion: 0.08
Nodes (23): VaultResponse, ActiveContainerCount, BeneficiarySlots, ClosedAt, CreatedAt, Description, Etag, ExtraBeneficiarySlots (+15 more)

### Community 33 - "ErrorCode"
Cohesion: 0.08
Nodes (24): ErrorCode, AccountNotActive, AssetAlreadyInContainer, AssetNotFound, BeneficiaryIdentityConflict, BeneficiaryInUse, BeneficiaryNotFound, BeneficiarySlotsExceeded (+16 more)

### Community 34 - "ExternalLoginLinkTicket"
Cohesion: 0.13
Nodes (16): IExternalLoginLinkTicketRepository, ExternalLoginLinkTicket, CreatedAt, ExpiresAt, Id, Provider, ProviderEmail, ProviderSubject (+8 more)

### Community 35 - "User"
Cohesion: 0.12
Nodes (15): User, CreatedAt, Email, EmailVerifiedAt, FullName, Id, LastLoginAt, PasswordHash (+7 more)

### Community 36 - "CheckInStatusResponse"
Cohesion: 0.11
Nodes (15): CheckInsController, UpdateCheckInSettingsRequest, CycleDays, Etag, GraceDays, CheckInStatusResponse, GraceEndsAt, History (+7 more)

### Community 37 - "Container"
Cohesion: 0.09
Nodes (19): ContainerMappings, ContainerRules, Container, ArchivedAt, Assets, CreatedAt, Description, Designations (+11 more)

### Community 38 - "EntityTypeBuilder"
Cohesion: 0.13
Nodes (10): BeneficiaryConfiguration, CheckInPeriodConfiguration, CheckInScheduleConfiguration, EmailVerificationTokenConfiguration, ExternalLoginConfiguration, HandoverPlanConfiguration, LoginThrottleConfiguration, RefreshTokenConfiguration (+2 more)

### Community 39 - ".RefreshAsync"
Cohesion: 0.13
Nodes (8): SessionExpiredException, AccessToken, ExpiresAt, Value, OpaqueToken, Hash, Value, FakeTokenService

### Community 40 - "CheckInReminder"
Cohesion: 0.11
Nodes (17): CheckInReminder, CreatedAt, Id, Kind, Period, PeriodId, SentAt, Status (+9 more)

### Community 41 - "RefreshToken"
Cohesion: 0.12
Nodes (16): IRefreshTokenRepository, RefreshToken, CreatedAt, ExpiresAt, FamilyId, Id, IsPersistent, ReplacedByTokenId (+8 more)

### Community 42 - "DateTimeOffset"
Cohesion: 0.22
Nodes (4): Task 2: `CheckInRules` và `CheckInTimeline` (luật thời gian thuần), CheckInJob, CheckInTimeline, CheckInTimelineTests

### Community 43 - "AppDbContext"
Cohesion: 0.10
Nodes (19): AppDbContext, Assets, AssetVersions, Beneficiaries, CheckInPeriods, CheckInReminders, CheckInSchedules, ContainerAssets (+11 more)

### Community 44 - "CheckInPeriodResponse"
Cohesion: 0.10
Nodes (15): CheckInPeriodResponse, AwaitingCheckAt, CheckedInAt, DueAt, GraceEndsAt, OverdueAt, Sequence, StartedAt (+7 more)

### Community 45 - "VaultEntitlement"
Cohesion: 0.11
Nodes (18): 3.2. Kho và gói (đã có), VaultEntitlement, CreatedAt, ExtraBeneficiarySlots, Id, IncludedBeneficiarySlots, ServiceExpiresAt, Status (+10 more)

### Community 47 - "AuthSessionResponse"
Cohesion: 0.11
Nodes (16): 6.2. Đăng nhập, phiên, đăng xuất, LoginRequest, Email, Password, RememberMe, AuthSessionResponse, AccessToken, ExpiresIn (+8 more)

### Community 48 - "Fact"
Cohesion: 0.34
Nodes (3): Task 6: `CheckInJob` (chuyển trạng thái và gửi nhắc), CheckInJobTests, VaultApiWithoutDatabaseTests

### Community 49 - "PlanStatus"
Cohesion: 0.13
Nodes (10): CheckInRules, PlanStatus, Active, AwaitingCheck, Blocked, CheckinOverdue, Closed, DeathCaseOpen (+2 more)

### Community 50 - "SmtpSettings"
Cohesion: 0.14
Nodes (10): SmtpSettings, FromAddress, FromName, Host, IsConfigured, Password, Port, SecureSocket (+2 more)

### Community 51 - "CheckInPeriod"
Cohesion: 0.12
Nodes (14): CheckInPeriod, AwaitingCheckAt, CheckedInAt, ClosedAt, DueAt, GraceEndsAt, Id, OverdueAt (+6 more)

### Community 53 - "EmailStartupLogger"
Cohesion: 0.16
Nodes (4): EmailStartupLogger, CapturingLogger, Lines, EmailStartupLoggerTests

### Community 54 - "Asset"
Cohesion: 0.12
Nodes (15): Asset, CreatedAt, CurrentVersionNumber, Description, Id, Kind, Name, UpdatedAt (+7 more)

### Community 55 - "AssetVersion"
Cohesion: 0.13
Nodes (14): AssetVersion, Asset, AssetId, ContentType, CreatedAt, EncryptionKeyId, FileName, Id (+6 more)

### Community 56 - "AssetEncryptionSettings"
Cohesion: 0.13
Nodes (10): AssetEncryptionSettings, ActiveKeyId, Keys, AssetStorageSettings, RootPath, TestHostEnvironment, ApplicationName, ContentRootFileProvider (+2 more)

### Community 57 - "PaginatedList"
Cohesion: 0.15
Nodes (9): PaginatedList, HasNextPage, HasPreviousPage, Items, PageIndex, PageSize, TotalCount, TotalPages (+1 more)

### Community 58 - "BeneficiaryResponse"
Cohesion: 0.14
Nodes (13): BeneficiaryResponse, CitizenIdLast4, ContainerIds, CreatedAt, DateOfBirth, Email, Etag, FullName (+5 more)

### Community 59 - ".SaveAsync"
Cohesion: 0.20
Nodes (7): StoredContent, EncryptionKeyId, Sha256, SizeBytes, StorageKey, WrappedDataKey, LocalAssetContentStore

### Community 60 - "system_componentmodel_dataannotations"
Cohesion: 0.15
Nodes (5): CreateVaultRequest, Description, Name, UnlinkGoogleRequest, CurrentPassword

### Community 61 - "Hiểu dự án LegacyVault: dự án làm gì và mã nguồn nằm ở đâu"
Cohesion: 0.17
Nodes (11): 1. Dự án làm gì (nói đơn giản), 2. Mã nguồn tổ chức thế nào, 4. Toàn cảnh quy trình bàn giao và phần đã có, 5.0. Kết quả chạy kiểm tra (14/10/2026), 5.1. Cần quyết định ở cấp nhóm, 5.3. Đã xử lý trong lần rà này, 5.4. Ghi chú về SRS, 5. Vấn đề đã phát hiện khi rà quy trình bàn giao (+3 more)

### Community 62 - "DataKeyProtector"
Cohesion: 0.23
Nodes (4): DataKeyProtector, WrappedDataKey, KeyId, Value

### Community 63 - "GoogleAuthFakes.cs"
Cohesion: 0.23
Nodes (4): Argon2PasswordHasher, FakePasswordHasher, FakeUnitOfWork, SaveCount

### Community 65 - "http"
Cohesion: 0.18
Nodes (10): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, launchUrl, profiles (+2 more)

### Community 66 - "Designation"
Cohesion: 0.20
Nodes (9): Designation, Beneficiary, BeneficiaryId, Container, ContainerId, CreatedAt, Id, UserId (+1 more)

### Community 68 - ".VerifyAsync"
Cohesion: 0.31
Nodes (4): GoogleAuthSettings, ClientId, GoogleTokenVerifier, GoogleTokenVerifierTests

### Community 69 - "ContainerAsset"
Cohesion: 0.22
Nodes (8): ContainerAsset, AddedAt, Asset, AssetId, Container, ContainerId, Id, ContainerAssetConfiguration

### Community 70 - "Program.cs"
Cohesion: 0.22
Nodes (5): LegacyVault.Infrastructure, LegacyVault.Api.Config, LegacyVault.Application, CorsConfig, Program

### Community 71 - "Hợp đồng API — Đăng nhập và Tạo kho"
Cohesion: 0.22
Nodes (8): 1. Quy ước chung, 2. Lỗi, 3. Xác thực (mô hình lai), 4. Chống ghi đè (etag), 5. Phân trang, 7. Tóm tắt thay đổi FE cần làm, Bảng mã lỗi, Hợp đồng API — Đăng nhập và Tạo kho

### Community 73 - "AssetVersionResponse"
Cohesion: 0.22
Nodes (7): AssetVersionResponse, ContentType, CreatedAt, FileName, SizeBytes, VersionNumber, AssetMappings

### Community 75 - "CreateBeneficiaryRequest"
Cohesion: 0.25
Nodes (7): CreateBeneficiaryRequest, CitizenId, ConfirmMinor, DateOfBirth, Email, FullName, Phone

### Community 76 - "CreateFileAssetRequest"
Cohesion: 0.25
Nodes (7): CreateFileAssetRequest, Content, ContentType, Description, FileName, Length, Name

### Community 77 - "UpdateBeneficiaryRequest"
Cohesion: 0.25
Nodes (7): UpdateBeneficiaryRequest, ConfirmMinor, DateOfBirth, Email, Etag, FullName, Phone

### Community 78 - "AppErrorType"
Cohesion: 0.25
Nodes (7): AppErrorType, Conflict, Forbidden, NotFound, PayloadTooLarge, Unauthorized, Validation

### Community 80 - "CreateFileAssetForm"
Cohesion: 0.29
Nodes (5): LegacyVault.Api.Models, CreateFileAssetForm, Description, File, Name

### Community 82 - "6. Endpoint"
Cohesion: 0.29
Nodes (7): 6.4. Phương thức đăng nhập của tôi, 6.5. Kho, 6.6. Gói (Container), 6.7. Tài sản (Asset), 6.8. Người nhận (Beneficiary), 6.9. Điểm danh (deadman switch), 6. Endpoint

### Community 83 - "LoginMethodResponse"
Cohesion: 0.29
Nodes (6): LoginMethodResponse, CanRemove, Email, LastUsedAt, LinkedAt, Method

### Community 84 - "CurrentUser"
Cohesion: 0.33
Nodes (3): CurrentUser, IsAuthenticated, UserId

### Community 85 - "SetPasswordRequest"
Cohesion: 0.33
Nodes (5): SetPasswordRequest, ConfirmPassword, GoogleIdToken, NewPassword, Nonce

### Community 86 - "EntitlementStatus"
Cohesion: 0.33
Nodes (5): EntitlementStatus, ExpiredReview, Free, GraceReadOnly, PaidActive

### Community 87 - "5. API (route theo handoff mục 7; JSON camelCase, lỗi ProblemDetails như `docs/api-contract.md`)"
Cohesion: 0.40
Nodes (5): 5. API (route theo handoff mục 7; JSON camelCase, lỗi ProblemDetails như `docs/api-contract.md`), `GET /api/v1/check-ins/me?pageIndex=&pageSize=`, Mã lỗi mới, `PATCH /api/v1/vaults/me/check-in-settings`, `POST /api/v1/check-ins` (không body)

### Community 89 - "CreateTextAssetRequest"
Cohesion: 0.40
Nodes (4): CreateTextAssetRequest, Content, Description, Name

### Community 90 - "LinkGoogleRequest"
Cohesion: 0.40
Nodes (4): LinkGoogleRequest, CurrentPassword, IdToken, Nonce

### Community 91 - "RemovePasswordRequest"
Cohesion: 0.40
Nodes (4): RemovePasswordRequest, CurrentPassword, GoogleIdToken, Nonce

### Community 92 - "UpdateContainerRequest"
Cohesion: 0.40
Nodes (4): UpdateContainerRequest, Description, Etag, Name

### Community 93 - "UpdateVaultRequest"
Cohesion: 0.40
Nodes (4): UpdateVaultRequest, Description, Etag, Name

### Community 94 - "VaultNotEditableReason"
Cohesion: 0.40
Nodes (4): VaultNotEditableReason, PlanLocked, ServiceReadOnly, VaultClosed

### Community 95 - "UserRole"
Cohesion: 0.40
Nodes (4): UserRole, Admin, User, Verifier

### Community 96 - "UserStatus"
Cohesion: 0.40
Nodes (4): UserStatus, Active, Disabled, Locked

### Community 97 - "Global Constraints"
Cohesion: 0.67
Nodes (4): Global Constraints, 8. Cấu trúc file, ApplicationConfig, InfrastructureConfig

### Community 98 - "pull_request_template.md"
Cohesion: 0.50
Nodes (3): Database / API, Kiểm tra, Thay đổi

### Community 99 - "CreateContainerRequest"
Cohesion: 0.50
Nodes (3): CreateContainerRequest, Description, Name

### Community 100 - "CreateBeneficiaryResult"
Cohesion: 0.50
Nodes (3): CreateBeneficiaryResult, Beneficiary, Created

### Community 101 - "OpenVaultResult"
Cohesion: 0.50
Nodes (3): OpenVaultResult, Created, Vault

## Knowledge Gaps
- **559 isolated node(s):** `Microsoft.AspNetCore.OpenApi`, `Microsoft.OpenApi`, `Swashbuckle.AspNetCore.SwaggerUI`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.NET.Sdk.Web` (+554 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 695 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **14 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `AppDbContext` connect `AppDbContext` to `SqlFact`, `Task`, `Guid`, `LegacyVault.Domain.Entities`, `AppException`, `Hướng dẫn Git và quy trình code chức năng`, `LegacyVault.Infrastructure.Data.Migrations`, `Vault`, `.AddInfrastructure`, `CheckInSchedule`, `LoginThrottle`, `ExternalLogin`, `EmailVerificationToken`, `HandoverPlan`, `Beneficiary`, `ExternalLoginLinkTicket`, `User`, `Container`, `CheckInReminder`, `RefreshToken`, `VaultEntitlement`, `CheckInPeriod`, `VaultAssetFlowTests`, `Asset`, `AssetVersion`, `Designation`, `ContainerAsset`, `.SaveChangesAsync`, `DatabaseHealthCheck`, `Global Constraints`?**
  _High betweenness centrality (0.076) - this node is a cross-community bridge._
- **What connects `Microsoft.AspNetCore.OpenApi`, `Microsoft.OpenApi`, `Swashbuckle.AspNetCore.SwaggerUI` to the rest of the system?**
  _559 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `SqlFact` be split into smaller, more focused modules?**
  _Cohesion score 0.07407407407407407 - nodes in this community are weakly interconnected._
- **Why does `AssetVersion` connect `AssetVersion` to `Guid`, `DateTimeOffset`, `AppDbContext`, `.AddInfrastructure`, `VaultAssetFlowTests`, `Asset`?**
  _High betweenness centrality (0.046) - this node is a cross-community bridge._
- **Should `Task` be split into smaller, more focused modules?**
  _Cohesion score 0.09518773135906927 - nodes in this community are weakly interconnected._
- **Why does `3. Tính năng ↔ SRS ↔ mã nguồn` connect `.AddInfrastructure` to `ContainerResponse`, `Guid`, `AppException`, `Vault`, `BeneficiaryService`, `AuthResult`, `CheckInSchedule`, `LoginThrottle`, `.CheckInAsync`, `ExternalLogin`, `EmailVerificationToken`, `HandoverPlan`, `Beneficiary`, `SmtpEmailTests`, `ICheckInReminderSender`, `ExternalLoginLinkTicket`, `User`, `CheckInStatusResponse`, `Container`, `CheckInReminder`, `RefreshToken`, `DateTimeOffset`, `VaultEntitlement`, `CheckInPeriod`, `Asset`, `AssetVersion`, `Hiểu dự án LegacyVault: dự án làm gì và mã nguồn nằm ở đâu`, `ContainerAsset`, `DateOnly`?**
  _High betweenness centrality (0.044) - this node is a cross-community bridge._
- **Should `LegacyVault.Application.Services` be split into smaller, more focused modules?**
  _Cohesion score 0.08831306187202538 - nodes in this community are weakly interconnected._