# Kế hoạch các chức năng còn lại (trừ gọi video)

Lập ngày 16/10/2026 trên `origin/develop` (đến PR #18). Căn cứ: `LegacyVault-SRS-v3.15.0.md`, backlog LV-01..LV-14.
**Ngoài phạm vi:** phòng gọi video, đặt/đổi/hủy lịch video (phần video của LV-09) và SePay (LV-04, theo yêu cầu).

## Trạng thái hiện tại (cập nhật 16/10/2026)

| Đợt | Nội dung | Trạng thái |
|---|---|---|
| 1 | Chuyển code đã viết sẵn lên `develop`: F2C, giữ chỗ dung lượng, chống trùng CCCD, hạn bổ sung + DEATH-13, lưu 30 ngày và dọn dữ liệu, thu hồi/đổi Executor, nhắc giờ 48 | **XONG** (nhánh `feature/dot1-port`) |
| 2 | Hold, sự cố có người phụ trách, API Admin, giao lại Verifier, job nhắc 48/72 giờ | **XONG** (cùng nhánh) |
| 3 | Danh tính Owner, mẫu khuôn mặt, phiên eKYC theo mục đích (LV-02) | Chưa làm, **chờ quyết định #2 ở mục 10** |
| 4 | Xác minh người nhận không video (LV-09) | Chưa làm, **chờ quyết định #1 ở mục 10** |
| 5 | Thông báo bền vững: outbox, gửi lại lời mời F3, hạn tính từ lúc thư được giao (LV-14) | Chưa làm, **làm được ngay** |
| 6 | SePay (LV-04) | Ngoài phạm vi |
| 7 | Quên mật khẩu, sửa tài sản/phiên bản, JSON, PDF, xác thực lại ân hạn (LV-12, LV-10) | Chưa làm, **làm được ngay** |
| FE | Frontend | Chưa làm (thư mục Frontend chưa nằm trong git) |

Chi tiết những gì đã làm: `docs/TIEN-DO-BACKEND.md`. Các mục 1–10 bên dưới là kế hoạch gốc, mục nào thuộc Đợt 1–2 thì đã xong.

## 0. Nguyên tắc chung

- Mỗi việc là một nhánh từ `develop`, một PR, có test SQL thật và mục hợp đồng trong `docs/api-contract.md`.
- Thêm mã lỗi vào **cuối** `ErrorCode.cs` và bảng lỗi → hay conflict; khi gặp thì giữ cả hai bên.
- Migration: mỗi PR một migration; trước khi mở PR thì `git pull origin develop` và tạo lại migration nếu snapshot đã đổi.
- Không push file Claude/graphify (hook đã chặn).

## 1. Đợt 1 — XONG: đưa code đã viết sẵn lên `develop`

Code đã có test ở các nhánh local `feature/plan-activation`, `feature/data-integrity`, `feature/retention`, nhưng dựng trên bản Verifier cũ. Chuyển từng phần sang nhánh mới từ `develop`, đổi tên cho khớp `DeathCaseReviewService` / `PlanSetupService`.

| # | Việc | Mã SRS | Nguồn code | Cần sửa khi chuyển |
|---|---|---|---|---|
| 1.1 | Owner báo còn sống (F2C): báo, chặn ngay, selfie, Verifier kết luận | ALIVE-01..09 | `feature/plan-activation` (commit `3f9e7d6`) | Gate Verifier dùng của `DeathCaseReviewService`; hàng chờ thêm `SUSPENDED` |
| 1.2 | Giữ chỗ dung lượng khi tải lên đồng thời | QUOTA-02, ASSET-04 | `feature/data-integrity` (`84e9ac3`) | Kiểm `R2AssetContentStore` dùng được với giữ chỗ |
| 1.3 | Chống trùng CCCD người nhận khi gửi đồng thời | QUOTA-04 | `feature/data-integrity` | Thêm `AssetEncryption:BlindIndexKey` vào cấu hình |
| 1.4 | Không coi "chưa cấu hình email" là đã giao | DEATH-08 | `feature/data-integrity` | Áp thêm cho sender của `PlanSetup` |
| 1.5 | Quá hạn bổ sung của Executor, cờ DEATH-13 | DEATH-01, DEATH-13 | `feature/retention` (`e479fe5`) | Job phân công đã có trên develop → chỉ lấy phần quá hạn bổ sung và cờ |
| 1.6 | Lưu 30 ngày, dọn dữ liệu, đóng kho | RETENTION-01..09, SETUP-18 | `feature/retention` (`f72f1b2`) | Xóa tệp qua R2; ghi `AuditLogs` khi dọn |
| 1.7 | Executor: thu hồi, đổi Executor chính, job hết hạn 7 ngày + nhắc giờ 48, danh sách cho Owner | SETUP-06, SETUP-20 | nhánh `feature/executor-receive` | Gắn vào `PlanSetupService` (không thêm controller thứ hai) |

**Xong khi:** các luồng trên chạy trên `develop`, không còn API trùng, test xanh.

## 2. Đợt 2 — XONG: nền dùng chung Hold, sự cố, Admin (LV-07)

Nhiều phần đang để `TODO(LV-07)` chờ việc này.

| # | Việc | Mã SRS | Ghi chú |
|---|---|---|---|
| 2.1 | Bảng `Holds`: phạm vi (kho/gói/chỉ định/claim/grant), loại, lý do, người phụ trách, ngày xem lại, gỡ | §6.4, STATE | Thay chỗ kế hoạch `BLOCKED` tạm của F2C |
| 2.2 | Kiểm hold trong cùng giao dịch ở: duyệt chứng tử, nhận lượt, đồng ý, tải, dọn dữ liệu, kích hoạt | ALIVE-01, SETUP-01, RETENTION-06 | |
| 2.3 | Bảng `Incidents`: loại, đối tượng, trạng thái, người phụ trách, hạn, lịch sử | INC-01..07 | Tạo tự động khi: không có Verifier/Executor ("bị kẹt"), gửi thông báo lỗi hết lượt, eKYC lỗi kỹ thuật |
| 2.4 | API Admin: quản lý tài khoản và role (cấp Verifier/Admin), khóa tài khoản | ROLE-01, ADMIN-* | Admin **không** mở tài sản, không tự duyệt |
| 2.5 | Admin phân công lại Verifier có lý do | ASSIGN-01 | Dùng lại luồng chọn Verifier của job |
| 2.6 | Nhắc việc thủ công: nhắc 48 giờ, báo Admin 72 giờ | Bảng thời hạn §7 | |
| 2.7 | Bổ sung `AuditLogs` cho: quyết định Verifier, phản đối, grant, hold, dọn dữ liệu | Audit | AuditLogs đã có trên develop |

**Xong khi:** mọi việc chờ đều có người phụ trách và hạn; không còn `TODO(LV-07)` trong code.

## 3. Đợt 3 — Danh tính Owner và eKYC theo mục đích (LV-02)

| # | Việc | Mã SRS |
|---|---|---|
| 3.1 | Phiên eKYC theo mục đích (`OWNER_ENROLLMENT`, `OWNER_ALIVE`, `BENEFICIARY_CLAIM`), có hạn 15 phút, dùng một lần | EKYC-01, EKYC-17 |
| 3.2 | Ghi đồng ý (mục đích, phiên bản thông báo, thời điểm) trước khi chụp | EKYC-03 |
| 3.3 | Lưu **danh tính đã gắn** (CCCD đủ 12 số mã hóa, họ tên, ngày sinh) và **mẫu khuôn mặt** riêng | EKYC-22..29 |
| 3.4 | Giới hạn 3 lần thất bại / 24 giờ theo tài khoản + mục đích + IP; lỗi server không tính | EKYC-19, EKYC-20 |
| 3.5 | Nối vào: điều kiện kích hoạt (thay "có eKYC đạt"), selfie F2C, cờ DEATH-13 so đủ 12 số | SETUP-01, ALIVE-02, DEATH-13 |
| 3.6 | Thử thách camera (nonce, 90 giây) — **phụ thuộc dịch vụ Python** | EKYC-10..14 |

**Rủi ro:** 3.3 và 3.6 cần dịch vụ eKYC trả được vector/mẫu khuôn mặt. Cần chốt với người làm dịch vụ Python trước.

## 4. Đợt 4 — Xác minh người nhận, phần không video (LV-09)

| # | Việc | Mã SRS |
|---|---|---|
| 4.1 | Claim có trạng thái theo SRS (`EKYC_IN_PROGRESS`, `EKYC_REVIEW_PENDING`, `SUPPLEMENT_REQUIRED`, `IDENTITY_VERIFIED`, `REJECTED_IDENTITY`, `CLOSED_INSUFFICIENT_EVIDENCE`, `EXPIRED_INCOMPLETE`...) | §7, CLAIM-* |
| 4.2 | Người nhận làm eKYC `BENEFICIARY_CLAIM`; đối chiếu CCCD, họ tên, ngày sinh với snapshot người nhận | EKYC-02, MATCH-* |
| 4.3 | Executor xem hồ sơ claim và kết luận "đúng người" (thay phần video bằng xem xét hồ sơ, ghi lý do) | REVIEW-01..05 |
| 4.4 | Claim của Executor chính do Executor dự phòng xác minh (`ExecutorRules.PickClaimVerifier` đã viết sẵn) | ROLE-03, SETUP-10 |
| 4.5 | Hạn: claim 30 ngày, bổ sung 7 ngày, giữ dữ liệu khi xem xét tối đa 7 ngày | CLAIM-06, REVIEW-04 |
| 4.6 | Tranh chấp claim → hold (dùng Đợt 2) | |
| 4.7 | Bỏ công tắc bypass khi nghiệm thu: `RequireIdentityVerification = true` phải đi hết luồng | |

**Lưu ý:** SRS yêu cầu video để kết luận đúng người. Bỏ video là **thay đổi phạm vi**, cần nhóm/giảng viên đồng ý và ghi vào SRS (ví dụ: "MVP: Executor kết luận dựa trên eKYC + giấy tờ").

## 5. Đợt 5 — Thông báo bền vững (LV-14)

| # | Việc | Mã SRS |
|---|---|---|
| 5.1 | Bảng outbox chung cho thư quan trọng (lời mời F3, mốc lưu, nhắc việc), thử lại có giới hạn, hết lượt → sự cố | OPS-04, INVITE-01 |
| 5.2 | Lời mời F3 không mất khi process dừng giữa chừng; nút "gửi lại" cho Admin | INVITE-01 |
| 5.3 | Hạn phản hồi 5 ngày và hạn quyết định 7 ngày tính **từ lúc thông báo được giao** | Bảng thời hạn §7 |
| 5.4 | Thông báo người nhận khi grant bị chặn/thu hồi; thông báo Executor khi có quyết định Verifier | ALIVE-07, DEATH-* |
| 5.5 | Giới hạn tần suất endpoint công khai (lời mời F3, lời mời Executor) | SEC |

## 6. Đợt 6 — Thanh toán SePay (LV-04)

| # | Việc | Mã SRS |
|---|---|---|
| 6.1 | Danh mục giá XS / XS Max, đơn mua/gia hạn, mua thêm suất; một đơn Pending mỗi kho, hạn 15 phút | PAY-*, ADDON-* |
| 6.2 | Webhook SePay: xác thực, đối chiếu tài khoản/mã đơn/số tiền/ID giao dịch, chống lặp | PAY-* |
| 6.3 | Kỳ dịch vụ, nhắc hết kỳ, ân hạn 180 ngày, chuyển Free (kế hoạch về nháp, Executor tạm dừng) | OPLAN-01..05 |
| 6.4 | Bỏ việc sửa DB để nâng hạng trong test/seed | |

**Phụ thuộc:** tài khoản SePay Test; eKYC Owner đạt (Đợt 3) để được mua lần đầu.

## 7. Đợt 7 — Các phần còn lại

| # | Việc | LV |
|---|---|---|
| 7.1 | Quên/đặt lại mật khẩu; API `/me/login-methods` (đã có trong hợp đồng mục 6.4) | LV-12 |
| 7.2 | Owner đọc/sửa nội dung → phiên bản mới; bản ghi JSON `ACCOUNT`/`WALLET`/`NOTE` (≤ 64 KiB); xóa tài sản theo tham chiếu | LV-10 |
| 7.3 | Xuất PDF cấu hình (XS Max), không chứa bí mật/CCCD | LV-10 |
| 7.4 | Xác thực lại trong 10 phút cho thao tác giảm dữ liệu khi ân hạn | OPLAN-01 |
| 7.5 | README, hướng dẫn cấu hình, backup/restore, kịch bản demo F1 → F2 → F3 | LV-14 |

## 8. Frontend (làm song song từ Đợt 1)

| # | Màn | Phụ thuộc backend |
|---|---|---|
| 8.1 | Đưa thư mục `Frontend` vào git (hiện **không có** git) | — |
| 8.2 | `/trusted`: mời, xem trạng thái, mời lại, thu hồi Executor | Đợt 1.7 |
| 8.3 | Trang nhận lời mời Executor (theo link email) | Đã có (`/executor-invitations/inspect`, `/respond`) |
| 8.4 | `/handover`: điều kiện còn thiếu và nút kích hoạt kế hoạch | Đã có (`/plan-setup`, `/activate`) |
| 8.5 | Màn Verifier: hàng chờ, xem hồ sơ/trang giấy, bổ sung, từ chối, duyệt | Đã có |
| 8.6 | Owner báo còn sống + selfie | Đợt 1.1 |
| 8.7 | Màn Admin: tài khoản, sự cố, phân công | Đợt 2 |
| 8.8 | eKYC người nhận và trạng thái claim | Đợt 4 |
| 8.9 | `/billing` | Đợt 6 |

## 9. Thứ tự và phụ thuộc

```
Đợt 1 (chuyển code sẵn) ──► Đợt 2 (Hold/sự cố/Admin) ──► Đợt 4 (claim không video)
                                     │                        ▲
Đợt 3 (danh tính Owner/eKYC) ────────┴────────────────────────┘
Đợt 5 (outbox) ── làm sau Đợt 2
Đợt 6 (SePay) ── cần Đợt 3 + tài khoản SePay Test
Đợt 7 ── độc lập, chen vào khi rảnh
Frontend ── song song, theo backend đã xong
```

## 10. Cần nhóm quyết định trước khi làm

1. **Bỏ video:** Executor kết luận "đúng người" chỉ dựa trên eKYC + giấy tờ có được chấp nhận không? Cần ghi vào SRS.
2. **Mẫu khuôn mặt:** dịch vụ eKYC Python có trả vector/mẫu để lưu không? Nếu không, F2C selfie và thử thách camera phải đổi cách làm.
3. **Ai làm phần nào:** đề xuất chia theo đợt (Đợt 1: người viết code gốc; Đợt 2–4: người làm death-case/handover; Đợt 3: người làm eKYC; Đợt 6: người làm R2/hạ tầng; Frontend: 1–2 người riêng).
4. **Hạn nộp/demo** để cắt bớt: nếu gấp, ưu tiên Đợt 1 → 2 → 4 → Frontend 8.2–8.6; để SePay và PDF sau.
