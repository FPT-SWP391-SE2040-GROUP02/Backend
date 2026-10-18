# Chuyển Auth từ test vào Backend

Ngày: 14/10/2026. Người dùng xác nhận: chỉ auth/hạ tầng/tests liên quan; giữ mô hình Backend Bearer RAM + refresh cookie. Đây là chuyển logic/hạ tầng có sẵn, không thay thế repository hoặc chuyển toàn bộ schema mẫu. Nhánh feature/auth-environment; .env ignored, không auto-load/gửi SMTP thật.

## 1. Use case/contract

Nguồn test: Identity/AuthService, AuthCsrfContract, AuthRateLimitPolicies, Argon2PasswordHasher và security checks. Đích giữ raw AuthSessionResponse/UserResponse, role USER/VERIFIER/ADMIN, API đăng ký/Google và các module kho/tài sản/điểm danh hiện có. Cookie POST login/refresh/logout/Google/link-confirm thêm proof từ GET /api/v1/auth/csrf (raw requestToken) và named IP rate limit. Thay đổi contract này cần FE lấy proof mới và validate response Bearer; adapter cookie/envelope FE cũ chưa đồng bộ.

## 2. Thứ tự/kiến trúc

Application contracts/UnitOfWork → Infrastructure implementation/Argon2 → Api antiforgery/rate/CORS/controller → tests → docs. Không đưa IAppDbContext/EF Core của test vào Application. Không đổi SDK, thêm dependency hoặc migration schema. Không xóa repository test.

## 3. Ma trận

MODIFY ApiConfig/CorsConfig/Program, AuthController/GoogleAuthController, ClientIpRange; CREATE AuthSecurityConfig/AuthCsrfContract/AuthRateLimitPolicies. MODIFY IUnitOfWork/AuthService/IAuthService/AppDbContext/InfrastructureConfig/Argon2PasswordHasher/JwtTokenService và fake UnitOfWork. CREATE tests/Auth HTTP/session/hash/IP/SQL rollback. MODIFY docs/api-contract.md/http samples/profile/client instructions; CREATE tài liệu này. DELETE: không có. Các tệp cùng phục vụ một migration auth có liên quan nên đi cùng checkpoint; không giới hạn cứng 2–3 tệp.

## 4. Micro-tasks

Port antiforgery/rate/CORS → copy bounded hasher đúng định dạng cũ → Serializable wrappers → commit failed-login counters/replay trước error → logout-all principal/cookie authorization → HTTP/use-case regression → SQL rollback check → build/test và trước/sau. Không copy file-based console runner cũ: checks chuyển vào xUnit hiện có để dotnet test thực sự chạy.

## 5. Biên và policy giữ lại

- Hash malformed/params không đúng bị từ chối trước Argon2, tránh tài nguyên không giới hạn; giữ định dạng m=19456,t=2,p=1, salt16/hash32 và max password128. Không có migration dữ liệu/hash mới.
- Token opaque giữ bytes, không trim trước hash. Refresh replay thu hồi family và commit trước SESSION_EXPIRED. Login/refresh/logout serialized trong transaction; không retry deadlock mù.
- Logout-all: active bearer principal có thể không có cookie; cookie nếu có phải cùng user. Không có bearer cần refresh active/chưa hết hạn/user Active. Single-device không-cookie vẫn no-op. Thu hồi refresh không vô hiệu JWT đã cấp.
- IP lấy từ Connection.RemoteIpAddress; không parse X-Forwarded-For tự gửi. Trusted-proxy middleware/deploy cần cấu hình riêng.
- Không đổi idle30/max12h sang refresh7days của test; không đổi USER thành OWNER hoặc VERIFIER thành NOTARY. Không chuyển guard Admin NotImplementedException của mẫu: Backend chưa có contract challenge; cần quyết định 2FA trước triển khai, không tuyên bố Admin đạt MFA.
- Không thay các tests dùng fake thành bằng chứng SQL/concurrency. SQL rollback test chỉ chạy khi cấu hình LEGACYVAULT_TEST_CONNECTION và dùng database test tên ngẫu nhiên. Không tự cấu hình/chạy SQL, migration hoặc xóa dữ liệu thật.

## 6. DoD

Restore/build Release và dotnet test; HTTP checks chứng minh cookie/proof pair, rejection trước service, raw Bearer response, no refresh secret trong body, rate Retry-After, CORS allowlist. Use-case checks cho wrong password counter, refresh expiry/replay, logout-all/identity, single-family và inactive session. SQL isolation/rollback chưa đạt khi SqlFact skip; browser/proxy/deploy chưa kiểm tra. Không commit/push nếu chưa có yêu cầu mới.

## Bảng trước / sau

| Phần Backend | Trước | Sau |
| --- | --- | --- |
| CSRF | Chưa endpoint/filter proof | GET csrf raw + MVC validation trên cookie-changing Auth POST |
| IP rate limit | Chưa có named Auth limiter | Login/Google/link 5/phút; refresh/logout mỗi policy 30/phút, Retry-After |
| CORS | Thiếu AllowCredentials | Credentials chỉ cho configured origins |
| Login/refresh/logout | Read-modify-write thiếu isolation | UnitOfWork Serializable, rollback khi operation lỗi; failed count/replay commit trước lỗi |
| Logout-all | Cookie revoked/expired vẫn có thể revoke user sessions | Kiểm tra active principal hoặc active cookie; mismatch user bị từ chối |
| Client IP | Tin X-Forwarded-For từ caller | Chỉ Connection.RemoteIpAddress |
| Argon2 | Parse và dùng params từ hash không tin cậy | Parser bounded cố định, cùng định dạng; clear temporary buffers |
| Opaque hash | Trim token trước hash | Hash nguyên bytes |
| Auth wire format | Raw Bearer + refresh cookie | Giữ raw Bearer + refresh cookie theo người dùng |
| Tests từ test | Console/file checks không vào Backend test runner | xUnit HTTP/use-case/hash/IP/SQL rollback được đưa vào runner |
| FE đồng bộ | Adapter cookie/envelope mẫu | Chưa chuyển trong scope Backend migration; giữ thay đổi FE local trước đó |

Nguồn test được giữ nguyên để tra cứu. Chính sách profile cập nhật theo quyết định người dùng; không tự đổi chế độ AI cho submit FE mới.

Bổ sung token safety từ test: từ chối JWT khi user ID rỗng/role ngoài enum; khóa ký cần >=32 byte UTF-8, lifetime access dương. Giữ opaque token hex64 của Backend để không phá hash/ticket hiện có; không đổi sang base64url của test.

## Kết quả kiểm chứng 14/10/2026

Restore PASS; build Release PASS (0 warnings/0 errors); toàn bộ dotnet test PASS: 177 passed / 77 skipped / 254 total. SQL-dependent tests, gồm Serializable rollback mới, chưa chạy vì chưa cấu hình LEGACYVAULT_TEST_CONNECTION. HTTP tests chạy ASP.NET TestServer với fake auth service; use-case tests dùng repositories giả, không chứng minh SQL concurrency. Diff check PASS. Không gửi email thật, không chạy migration/database/browser/deploy. Không commit/push. .env vẫn ignored; test source không thay đổi.

## Kiểm chứng SQL thật — 14/10/2026

Theo yêu cầu người dùng, khởi động Docker Desktop và docker compose up -d --wait --wait-timeout 180. SQL container Healthy tại 127.0.0.1:14331. Đặt LEGACYVAULT_TEST_CONNECTION từ appsettings.Development.json chỉ trong process test sau khi kiểm tra target local; không in connection string, xóa biến trong finally. Các tests mutation tạo database LegacyVault_Test_<GUID> và dọn riêng; không drop LegacyVault hoặc volume.

Hai factories HealthEndpointTests/VaultAssetFlowTests được bổ sung cấu hình CheckIn:Enabled=false và SMTP credentials rỗng để không chạy job/gửi thư thật. Build Release PASS, 0 warnings/errors. Full SQL suite PASS: 254 passed, 0 skipped, 0 failed; duration 26s. Serializable isolation/rollback trên SQL đã được kiểm tra, nhưng chưa có browser/multi-tab acceptance hay đầy đủ load/concurrency tests. Docker/SQL được giữ chạy để tiếp tục local; chưa commit/push.

Kiểm tra sau suite: docker compose ps báo Healthy; truy vấn sys.databases chỉ còn LegacyVault, không còn LegacyVault_Test_% tạm.
