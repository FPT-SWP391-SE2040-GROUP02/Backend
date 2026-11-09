# Luồng nghiệp vụ đang chạy trong code (sau khi gộp phần Admin vào nhánh Executor)

Tài liệu này mô tả **đúng những gì code hiện làm**, đọc từ nhánh `merge/admin-into-executor-finish-flow` (08/11/2026). Chỗ nào code và tài liệu cũ nói khác nhau thì ghi rõ ở mục 12.

## 1. Vai trò

| Vai trò | Làm gì |
|---|---|
| Chủ kho (Owner) | Lập kho, gói bàn giao, tài sản, người nhận, mời Người thực thi, điểm danh định kỳ, cấp khóa mở gói cho Người thực thi |
| Người thực thi (Executor) | Được Chủ kho mời (hoặc Admin giao). Điểm danh thay khi Chủ kho quá hạn, nộp hồ sơ chứng tử, theo dõi tiến độ bàn giao, gửi lại lời mời cho người nhận |
| Người xét duyệt (Verifier) | Được hệ thống giao hồ sơ chứng tử theo vòng xoay. Chỉ có **Bổ sung** và **Đồng ý** (không còn Từ chối) |
| Người nhận (Beneficiary) | Nhận lời mời qua email, xác minh danh tính, nhập khóa mở gói nếu có, đồng ý hoặc từ chối, rồi xem và tải tài sản |
| Quản trị viên (Admin) | Giám sát, xử lý sự cố, giao lại Người thực thi, cấu hình tham số và gói dịch vụ, xem giao dịch, nhật ký. Không đọc nội dung kho |

## 2. Lập kho và kích hoạt kế hoạch

1. Chủ kho đăng ký, xác minh email, bắt buộc nhập số điện thoại Việt Nam trước khi vào dashboard.
2. Chủ kho làm eKYC mục đích `OWNER_ENROLLMENT` (mục 9) rồi mới kích hoạt được kế hoạch (`OWNER_EKYC_REQUIRED`).
3. Chủ kho tạo gói (Container), tài sản trong gói, người nhận được chỉ định cho từng gói; tài sản được mã hóa phía server.
4. Chủ kho mời Người thực thi (chính, có thể thêm dự phòng). Lời mời hết hạn sau 7 ngày (`Executor.InvitationExpiresDays`), nhắc sau 48 giờ (`Executor.ReminderAfterHours`).
5. Kích hoạt kế hoạch: kế hoạch chuyển `DRAFT` thành `ACTIVE`, mở kỳ điểm danh đầu tiên và gửi thư báo cho người nhận kèm liên hệ Chủ kho và Người thực thi.
6. Admin có thể giao hoặc đổi Người thực thi cho kho (nhật ký `EXECUTOR_ASSIGNED_BY_ADMIN`, kèm lý do); người được giao phải chấp nhận lời mời, thư mời ghi rõ do quản trị viên giao.

## 3. Điểm danh định kỳ (Chủ kho)

- Chu kỳ: Chủ kho chỉ chọn **30, 60 hoặc 90 ngày**. Đổi chu kỳ khi đang có kỳ chạy thì áp dụng từ kỳ sau.
- Mỗi ngày (giờ Việt Nam) chỉ ghi nhận một lần điểm danh. Đăng nhập hay thanh toán không phải điểm danh.
- Không còn "ân hạn cố định". Thay vào đó Admin đặt hai tham số, được **chốt vào từng kỳ lúc mở kỳ**:
  - `CheckIn.MaxLateReminders` (mặc định 3, từ 1 đến 5): số thư nhắc trễ gửi Chủ kho.
  - `CheckIn.LateReminderGapDays` (mặc định 3, từ 1 đến 30): khoảng cách giữa hai thư.
- Mốc thư: trước hạn 7 ngày, trước hạn 1 ngày, rồi thư nhắc trễ thứ nhất đúng lúc tới hạn, các thư sau cách nhau `LateReminderGapDays`.
- Mốc báo Người thực thi = hạn điểm danh + số thư × khoảng cách. Ví dụ hạn ngày 30, 3 thư cách 3 ngày: thư ngày 30, 33, 36, báo Người thực thi ngày 39.
- Trạng thái kế hoạch theo giờ máy chủ: `ACTIVE`, đến hạn thì `CHECKIN_OVERDUE`, quá mốc báo thì `AWAITING_CHECK`. Chủ kho điểm danh được ở cả ba trạng thái, kế hoạch về `ACTIVE`.
- Chế độ demo (`Time.DemoSeconds`): mọi "ngày, giờ, phút" nghiệp vụ tính bằng giây, mọi mốc điểm danh đi qua `TimeUnits`.

## 4. Người thực thi điểm danh thay

- Chỉ khi kế hoạch `CHECKIN_OVERDUE` hoặc `AWAITING_CHECK` và kho chưa có hồ sơ chứng tử đang mở.
- Phải khai: đã liên lạc với ai (2 đến 200 ký tự), lúc nào (không ở tương lai, dung sai 5 phút), cách nào (`PHONE`, `IN_PERSON`, `MESSAGE`, `EMAIL`).
- Hệ quả: đóng kỳ quá hạn (không ghi giờ điểm danh của Chủ kho), mở kỳ mới `StartedByExecutor`, kế hoạch về `ACTIVE`.
- **Giới hạn liên tiếp:** khóa `CheckIn.ExecutorProxyLimit`, mặc định **3** (Admin đặt, từ 1 đến 20). Chủ kho tự điểm danh thì đếm lại từ 0. Hết lượt thì `409 PROXY_LIMIT_REACHED` và hệ thống mở sự cố `PROXY_LIMIT` cho Admin.
- Nhật ký kho ghi mã cách liên lạc, không ghi tên người đã liên lạc.

## 5. Hồ sơ chứng tử (Người thực thi nộp)

1. Người thực thi đã chấp nhận lời mời mở hồ sơ (mỗi kho một hồ sơ đang mở). Hồ sơ nháp `DRAFT`, kế hoạch chưa bị khóa.
2. Khai thông tin trên giấy (họ tên, CCCD 12 số, ngày mất, số giấy, ngày cấp, nơi cấp) và tải trang giấy: PDF, JPEG, PNG, WEBP, tối đa 20 MiB mỗi tệp, tối đa 10 tệp mỗi bản; loại tệp xác định theo nội dung thật.
3. Mỗi loại giấy một tệp; gửi hồ sơ chỉ cần giấy chứng tử.
4. Gửi (`submit`, cần `confirm = true`): hồ sơ thành `UNDER_REVIEW`, kế hoạch thành `DEATH_CASE_OPEN`, **kho và tài khoản Chủ kho bị khóa ngay**. Không còn AI đọc giấy (đã gỡ Gemini), chỉ kiểm đủ trường, loại tệp, số tệp, dung lượng.
5. Hệ thống ghi mốc phản đối bằng đúng lúc nộp để các luật cũ (`OwnerNotNotified`, `ObjectionPeriodNotOver`) tự qua. Không còn cửa sổ phản đối 72 giờ và không còn "Tôi còn sống".
5b. Không còn cờ "điểm danh sau ngày mất": gửi hồ sơ không so ngày mất với lần điểm danh của Chủ kho.
6. Hồ sơ được giao cho một Người xét duyệt theo vòng xoay (loại Chủ kho, Người thực thi của kho, người gửi). Hạn xét `DeathCase.ReviewDueHours` (mặc định 48 giờ).

## 6. Người xét duyệt

- Xem chi tiết, xem các trang giấy, đối chiếu thông tin. Chỉ có hai quyết định:
  - **Bổ sung:** hồ sơ thành `SUPPLEMENT_REQUIRED`, kèm lý do và phần cần bổ sung; Người thực thi có `DeathCase.SupplementDueDays` (mặc định 7 ngày) tạo phiên bản mới (`versions`) rồi gửi lại.
  - **Đồng ý:** cần đủ điều kiện duyệt và nhập lại mật khẩu để ký; hồ sơ thành `APPROVED_FOR_HANDOVER`, kế hoạch sang `HANDOVER_IN_PROGRESS`, snapshot bàn giao được ghim lúc duyệt.
- Mật khẩu ký sai 5 lần thì khóa 15 phút; lần khóa sau dài hơn (1 giờ, rồi 1 ngày).
- Không còn nút **Từ chối**. Giá trị `Rejected` vẫn nằm trong dữ liệu cũ để không hỏng hồ sơ đã lưu.
- Quá hạn xét mà chưa quyết định: sự cố `STALE_CASE`. Bị yêu cầu bổ sung nhiều lần liên tiếp: sự cố `SUPPLEMENT_LOOP`.

## 7. Bàn giao cho người nhận

Mỗi chỉ định (một gói cho một người nhận) là một **lượt**:

`INVITED` → (người nhận bấm link) `IDENTITY_IN_PROGRESS` → `READY_TO_ACCEPT` → `HANDOVER_COMMITTED` hoặc `DECLINED`. Phụ: `INITIAL_RESPONSE_EXPIRED`, `DECISION_EXPIRED`, `CANCELLED`, `LAPSED`.

| Bước | Hạn | Ghi chú |
|---|---|---|
| Phản hồi lời mời | 5 ngày | Quá hạn chưa coi là từ chối, link vẫn dùng được |
| Quyết định | 7 ngày kể từ `READY_TO_ACCEPT` | |
| Quyền xem và tải | 30 ngày kể từ lúc đồng ý | |
| Lưu dữ liệu trước khi xóa | 30 ngày | Thư mốc: lúc bắt đầu, còn 7 ngày, còn 24 giờ |

Các bước:

1. Lời mời gửi qua email (lưu lượt trước, gửi sau; lỗi thì thử lại tối đa 5 lần rồi mở sự cố `NOTIFICATION_FAILED`). Người thực thi có thể gửi lại lời mời thủ công.
2. Người nhận xác minh danh tính bằng eKYC mục đích `BENEFICIARY_CLAIM`: họ tên, CCCD, ngày sinh đọc từ giấy phải khớp thông tin Chủ kho đã khai (`MATCHED`, `NEEDS_REVIEW`, `WRONG_PERSON`). `NEEDS_REVIEW` thì Người thực thi xác nhận danh tính (`confirm-identity`). Ở môi trường Development có công tắc `Handover:RequireIdentityVerification = false` để bỏ qua bước này.
3. **Khóa mở gói:** nếu kho đã được Chủ kho cấp khóa cho Người thực thi thì người nhận phải nhập đúng khóa đó (`POST turns/{id}/verify-key`) trước khi đồng ý. Sai 5 lần thì khóa lượt, cần Người thực thi hoặc Admin xử lý. Kho chưa cấp khóa thì bỏ qua bước này. Chủ kho cấp lại được khi Người thực thi quên khóa; mảnh khóa cũ hết hiệu lực. DB chỉ lưu băm SHA-256 có muối.
4. Người nhận đồng ý hoặc từ chối. Đồng ý thì có quyền xem và tải từng tài sản trong 30 ngày.
5. Kế hoạch tự chuyển `CLOSED` khi mọi lượt kết thúc; job lưu và xóa dọn nội dung sau thời hạn lưu.

**Người thực thi không thấy nội dung tài sản.** Tổng quan của Người thực thi không còn thanh 8 bước vòng đời mà là **thanh tiến trình bàn giao** 5 bước (Báo tin, Xét duyệt, Xác minh eKYC, Đồng ý hoặc từ chối, Tải nội dung) kèm dòng "x/y người nhận đã đồng ý, a/b tài sản đã được tải". Màn Tài sản của Người thực thi chỉ liệt kê theo từng gói: người nhận (họ tên hoặc 4 số cuối CCCD) và danh sách tài sản (tên, loại, dung lượng). Màn tiến độ bàn giao cho biết từng lượt đang ở đâu, người nhận đã đồng ý chưa, tài sản nào đã được tải. Không có mã tải qua email.

## 8. Admin

- **Tổng quan:** số liệu vận hành, kế hoạch theo giai đoạn, tải việc của Người xét duyệt, hoạt động gần đây. Không còn ba thẻ Sự cố, Tạm giữ, Nhân sự.
- **Sự cố:** lọc theo trạng thái (có số đếm), loại, "của tôi", quá hạn; có vai trò chịu trách nhiệm (Admin, Người xét duyệt, Người thực thi); ghi chú trong lịch sử. Loại sự cố: `NO_VERIFIER`, `NO_EXECUTOR`, `CHECKIN_OVERDUE`, `NOTIFICATION_FAILED`, `INTEGRATION`, `MANUAL`, `PAYMENT`, `SUSPICIOUS_LOGIN`, `STALE_CASE`, `SUPPLEMENT_LOOP`, `PLAN_EXPIRING`, `PROXY_LIMIT`.
- **Quét vận hành:** tự mở và tự đóng các sự cố nói trên (gói sắp hết hạn, hết lượt điểm danh thay, hồ sơ quá hạn…).
- **Giao dịch:** xem giao dịch SePay, lọc trạng thái, môi trường, từ ngày đến ngày và nhanh hôm nay, tháng này, quý này. Giao dịch lệch tự mở sự cố loại thanh toán. Chỉ xem, không sửa.
- **Người dùng:** danh sách theo vai trò (có số đếm), chi tiết, đổi vai trò, tạo tài khoản Người xét duyệt hoặc Admin, khóa hoặc mở khóa.
- **Tạm giữ:** chỉ xem, nằm trong chi tiết sự cố; hiện không còn nơi nào đặt tạm giữ mới.
- **Gói dịch vụ:** thẻ gói sửa ngay tại chỗ (giá từng kỳ, dung lượng, số người nhận, số gói, dòng tính năng), bật tắt kỳ bán, xóa gói; bảng nâng cao để thêm gói hoặc kỳ mới.
- **Cấu hình:** tham số hệ thống theo nhóm gập (mục 10), ghi nhật ký kiểm toán kèm lý do bắt buộc.
- **Nhật ký hoạt động** và **Bảo mật tài khoản** (TOTP).
- **Công cụ thử nghiệm** (chỉ Development hoặc Staging): tua thời gian của kho thử, đặt lại eKYC của một tài khoản, đưa kho về trước eKYC, chỉnh ngưỡng khuôn mặt, **tạm ngừng eKYC** (mục 9).

## 9. eKYC

- Dịch vụ eKYC là chương trình Python riêng; backend chỉ lưu kết quả, không lưu ảnh giấy tờ thô.
- Chuỗi bước: đồng ý (`consent`, phiên bản thông báo `2026-10-v1`) → chụp mặt trước (`document`) → xác nhận bản khai (`document/confirm`) → chụp mặt sau (`document-back`) → thử thách camera (`liveness/start`, `liveness/frame`, `liveness/complete`) hoặc ảnh selfie tĩnh (`face-match`) → kết quả `PASSED` hoặc `FAILED`. Mỗi lượt so khớp một lần.
- Ba mục đích: `OWNER_ENROLLMENT` (Chủ kho, một lần, gắn danh tính đã mã hóa và mẫu khuôn mặt), `BENEFICIARY_CLAIM` (người nhận, đối chiếu với người được chỉ định), `OWNER_ALIVE` (còn trong enum nhưng luồng "Tôi còn sống" đã gỡ).
- Số CCCD không được trùng giữa các tài khoản Chủ kho và không được trùng người nhận của chính Chủ kho đó.

### Tạm ngừng eKYC (mới, để chạy thử)

- Admin vào **Công cụ thử nghiệm** bấm **Tạm ngừng eKYC**. Chỉ có ở Development hoặc Staging (môi trường khác trả 404), chỉ Admin dùng được, chỉ ở bộ nhớ máy chủ (khởi động lại là về tắt), có ghi nhật ký `DEV_TOGGLE_EKYC_PAUSE`.
- Khi bật: mọi lời gọi tới dịch vụ eKYC Python được thay bằng bản giả luôn đạt. Phần lưu kết quả, danh tính, mẫu khuôn mặt vẫn chạy như thật.
  - Người nhận: giấy tờ đọc ra đúng họ tên, CCCD, ngày sinh Chủ kho đã khai nên đối chiếu ra `MATCHED`.
  - Chủ kho: nhận một số CCCD thử 12 số (bắt đầu 079) tính từ mã tài khoản, nên mỗi tài khoản một số khác nhau; họ tên thử "NGUYEN VAN THU NGHIEM".
  - Kiểm tra khuôn mặt Chủ kho với mẫu đã lưu: coi như khớp.
- Dữ liệu tạo ra khi đang tạm ngừng là dữ liệu thử, không dùng cho môi trường thật. Nhớ tắt sau khi thử.

## 10. Tham số Admin chỉnh được (khóa và mặc định)

| Nhóm | Khóa | Mặc định |
|---|---|---|
| Điểm danh | `CheckIn.DefaultCycleDays` | 30 |
| | `CheckIn.LateReminderGapDays` | 3 |
| | `CheckIn.MaxLateReminders` | 3 |
| | `CheckIn.ExecutorProxyLimit` | 3 |
| Bàn giao | `Handover.ResponseWindowDays` | 5 |
| | `Handover.DecisionWindowDays` | 7 |
| | `Handover.GrantWindowDays` | 30 |
| | `Handover.RetentionWindowDays` | 30 |
| | `Handover.ResendCooldownMinutes` | 15 |
| Người thực thi | `Executor.InvitationExpiresDays` | 7 |
| | `Executor.ReminderAfterHours` | 48 |
| Hồ sơ chứng tử | `DeathCase.SupplementDueDays` | 7 |
| | `DeathCase.MaxFilesPerVersion` | 10 |
| | `DeathCase.MaxFileSizeMb` | 20 |
| | `DeathCase.ReviewDueHours` | 48 |
| Sự cố | `Incident.ReminderAfterHours` | 48 |
| | `Incident.EscalateAfterHours` | 72 |
| | `Incident.MaxReviewIntervalDays` | 7 |
| Giới hạn | `Limits.*`, `Security.*`, `Features.MaintenanceMode` | xem màn Cấu hình |

## 11. Hai kịch bản chạy thử

**Happy case (một kho từ đầu đến cuối):**
đăng ký Chủ kho → bật tạm ngừng eKYC (nếu không có giấy tờ thật) → eKYC Chủ kho → tạo gói, tài sản, người nhận → mời và chấp nhận Người thực thi → kích hoạt → tua thời gian tới quá hạn (công cụ thử nghiệm) → báo `AWAITING_CHECK` → Người thực thi mở và gửi hồ sơ chứng tử → Người xét duyệt đồng ý → các lượt bàn giao được mời → người nhận eKYC, nhập khóa mở gói nếu có, đồng ý → xem và tải tài sản → Người thực thi thấy tiến độ "đã đồng ý, đã tải" → kế hoạch `CLOSED` → job dọn dữ liệu.

**Worst case thường gặp:**
- Chủ kho quên điểm danh, Người thực thi điểm danh thay đủ 3 lần liên tiếp rồi bị chặn (`PROXY_LIMIT_REACHED`), Admin nhận sự cố.
- Hồ sơ bị yêu cầu bổ sung nhiều lần (`SUPPLEMENT_LOOP`) hoặc quá hạn xét (`STALE_CASE`).
- Không có Người xét duyệt hoặc Người thực thi hoạt động (`NO_VERIFIER`, `NO_EXECUTOR`): Admin giao lại.
- Người nhận không phản hồi 5 ngày, không quyết định 7 ngày, từ chối, nhập sai khóa mở gói 5 lần.
- Thanh toán lệch đơn: giao dịch `RECONCILIATION_REQUIRED`, sự cố loại thanh toán.
- Nhập sai mật khẩu ký của Người xét duyệt 5 lần (khóa 15 phút).

## 12. Chỗ code và tài liệu cũ chưa khớp

- Luật thông báo và phản đối vẫn còn nguyên bộ khung ở backend (mốc phản đối đặt bằng lúc nộp để tự qua). Cờ "điểm danh sau ngày mất" đã gỡ.
- Bảng `DeathCaseObjection` và các enum phản đối vẫn còn; chưa có migration xóa. Cài đặt `DeathCase.MaxNoticeAttempts` và `DeathCase.ObjectionHours` còn trong dữ liệu mặc định phía backend.
- Hạn xử lý sự cố của Admin tính bằng ngày/giờ thật, chưa đi qua chế độ demo.
- `Pay11` (tạo đơn thanh toán trả 201) fail trên máy dev vì thiếu cấu hình tài khoản ngân hàng thử; không liên quan phần gộp.
