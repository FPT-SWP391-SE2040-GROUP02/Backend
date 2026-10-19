# Giai đoạn nhận tài sản — API và cách vận hành

Tài liệu này mô tả phần "người nhận mở link mời, nhận lượt, đồng ý và tải tài sản" (SRS §6.3: INVITE, CLAIM, ACCEPT, DOWNLOAD).
Nó nằm ở file riêng thay vì `api-contract.md` để không xung đột khi gộp nhánh; khi cần có thể chép sang `api-contract.md`.

## 1. Phạm vi

**Có:** kiểm link mời, nhận lượt, đồng ý/từ chối, xem danh sách tài sản, tải từng tài sản (nội dung luôn được kiểm toàn vẹn trước khi gửi),
job hết hạn, trang web `/receive`.

**Chưa có (làm ở giai đoạn sau):** xác minh danh tính eKYC, gọi video, Executor xác nhận, xác nhận qua đời (F2B), lưu giữ/xóa dữ liệu.

## 2. Mở bàn giao: chưa có endpoint

Không có API nào gọi `IHandoverStarter.StartAsync(vaultId)`. Luồng xác nhận qua đời (F2B) sau này sẽ gọi nó. Hiện nay:

- **Tự động kiểm thử:** `HandoverStarterTests` (không cần SQL) và `HandoverFlowTests` (SQL) gọi trực tiếp service.
- **Thử tay:** chạy `http/handover-dev.sql` (thay `<vaultId>`). Script làm giống `StartAsync` và đặt token cố định `dev-token-1`, `dev-token-2`, ...

`StartAsync` cần kế hoạch ở `DeathCaseOpen` và ít nhất một gói `READY` có người nhận được chỉ định. Nó tạo mỗi chỉ định một lượt mời
(hạn phản hồi 5 ngày), ghim snapshot các tài sản (phiên bản hiện hành) và gửi lời mời qua email **sau khi đã lưu**. Gọi lặp lại không tạo thêm gì.

## 3. Công tắc xác minh danh tính

Cấu hình `Handover:RequireIdentityVerification`:

| Giá trị | Hiệu lực |
|---|---|
| `true` (mặc định trong code) | `claim` dừng ở `IDENTITY_IN_PROGRESS`; người nhận làm eKYC (mục đích `BENEFICIARY_CLAIM`, kèm `handoverTurnId`, xem `api-contract.md` mục 6.10) rồi gọi `POST turns/{turnId}/identity/complete` để sang `READY_TO_ACCEPT`. Nhóm đã bỏ bước gọi video: xác minh eKYC ở bước này thay cho việc gọi video và Executor kết luận "đúng người". |
| `false` | `claim` đi thẳng tới `READY_TO_ACCEPT` và lượt có `identityBypassed: true`. **Chỉ hợp lệ ở môi trường Development**: ở môi trường khác API không khởi động (`OptionsValidationException`). |

`appsettings.Development.json` đặt `false` để bạn thử được. Lúc khởi động API ghi một dòng log cho biết đang bật hay bỏ qua.

## 4. Trạng thái và thời hạn

Lượt (`HandoverTurn`): `INVITED → IDENTITY_IN_PROGRESS → READY_TO_ACCEPT → HANDOVER_COMMITTED | DECLINED`; ngoài ra `INITIAL_RESPONSE_EXPIRED`
(quá 5 ngày chưa nhận, vẫn nhận được), `DECISION_EXPIRED` (quá 7 ngày kể từ lúc sẵn sàng mà chưa quyết định), `CANCELLED`, `LAPSED` (dành cho giai đoạn sau).

| Hạn | Tính từ | Thời gian |
|---|---|---|
| Phản hồi lời mời | lúc mời | 5 ngày |
| Quyết định đồng ý/từ chối | lúc `READY_TO_ACCEPT` | 7 ngày |
| Quyền xem và tải (grant) | lúc đồng ý | 30 ngày |

Đúng giây hạn là đã hết hạn (`now >= hạn`), kể cả khi job nền chưa chạy. Job nền quét mỗi `Handover:Job:PollSeconds` giây (mặc định 60).
Kế hoạch tự chuyển `Closed` khi mọi lượt của nó đã kết thúc.

### Gửi lại lời mời tự động (OPS-04)

Lượt được lưu **trước** khi gửi thư, nên nếu lần gửi đầu lỗi hoặc tiến trình dừng giữa chừng thì `InvitationSentAt` vẫn là `null`.
`HandoverInvitationRetryJob` (chạy trong cùng vòng quét của `HandoverMonitor`) xử lý các lượt như vậy:

- Chọn lượt còn `INVITED`, chưa ai nhận, `InvitationSentAt = null`, đã tạo ít nhất 2 phút và mới thử dưới 5 lần.
- Giãn cách giữa các lần thử tăng dần: lần lỗi thứ n chờ n x 5 phút. Kho có hold hoặc kế hoạch không còn `HANDOVER_IN_PROGRESS` thì tạm dừng, không tính là lần lỗi.
- Mỗi lần gửi dùng token mới; DB chỉ lưu mã băm mới sau khi email được nhận, nên gửi lỗi thì link cũ còn nguyên. Giao thành công lần đầu thì hạn phản hồi 5 ngày bắt đầu tính từ lúc đó (INVITE-04).
- Đủ 5 lần lỗi, hoặc người nhận không có email: ngừng thử và mở sự cố `NOTIFICATION_FAILED` (đối tượng `HANDOVER_TURN`) có người phụ trách để Executor/Admin xử lý; Executor vẫn có thể gửi lại thủ công bằng `resend-invitation`. Giao được về sau thì sự cố tự đóng.
- Log và sự cố không chứa email, token hay tên gói. Hai tiến trình cùng xử lý một lượt thì bên lưu sau bị xung đột (`RowVersion`).

## 5. API

Tiền tố `/api/v1/handover`. Tất cả cần `Authorization: Bearer` trừ endpoint đầu tiên.

| # | Phương thức và đường dẫn | Mô tả |
|---|---|---|
| 1 | `GET invitations/{token}` | Công khai. `200 { "valid": true, "responseExpired": false }`; token lạ `404 HANDOVER_INVITATION_INVALID`. Không lộ dữ liệu nào khác. |
| 2 | `POST invitations/{token}/claim` | Gắn tài khoản đang đăng nhập vào lượt. Cùng người gọi lại: trả trạng thái hiện tại. Trả `HandoverTurn`. |
| 3 | `GET turns/me` | Các lượt tôi đã nhận, mới nhất trước. |
| 4 | `GET turns/{turnId}` | Một lượt của tôi. |
| 4a | `POST turns/{turnId}/identity/complete` | Chốt xác minh danh tính: lượt `IDENTITY_IN_PROGRESS` sang `READY_TO_ACCEPT` (đặt hạn quyết định 7 ngày) khi lượt eKYC mới nhất của tôi cho đúng lượt này đã `PASSED` và `identityMatch = MATCHED`. Gọi lặp khi đã `READY_TO_ACCEPT` trả lại lượt, không đổi hạn. Trả `HandoverTurn`. Không tự đóng lượt khi sai người. |
| 5 | `POST turns/{turnId}/accept` | Đồng ý: tạo grant 30 ngày. Gọi lặp trả lại cùng grant. |
| 6 | `POST turns/{turnId}/decline` | Từ chối. |
| 7 | `GET grants/{grantId}/assets` | Danh sách tài sản trong snapshot. |
| 8 | `GET grants/{grantId}/assets/{itemId}/content` | Tải nội dung (nhị phân). |

Ví dụ `HandoverTurn`:

```json
{
  "id": "0198...",
  "status": "HANDOVER_COMMITTED",
  "serverTime": "2026-10-14T08:00:00+00:00",
  "invitedAt": "2026-10-14T07:00:00+00:00",
  "responseDeadline": "2026-10-19T07:00:00+00:00",
  "decisionDeadline": "2026-10-21T08:00:00+00:00",
  "identityBypassed": true,
  "container": { "name": "Gói thừa kế" },
  "items": [
    { "id": "0198...", "name": "Giấy tờ nhà", "kind": "FILE", "fileName": "giay-to.pdf", "contentType": "application/pdf", "sizeBytes": 200000 }
  ],
  "grant": { "id": "0198...", "status": "ACTIVE", "expiresAt": "2026-11-13T08:00:00+00:00" }
}
```

`container` và `items` chỉ có khi lượt ở `READY_TO_ACCEPT` hoặc `HANDOVER_COMMITTED`. `grant.status` đã tính cả trường hợp quá hạn mà job chưa chạy.
Dùng `serverTime` để đếm ngược, đừng dùng đồng hồ máy người dùng.

### Tải nội dung (endpoint 8)

Phản hồi thành công: `200`, `Content-Type: application/octet-stream`, `Content-Disposition: attachment; filename*=UTF-8''...`,
`X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, `Content-Length`.

Kho nội dung chỉ kiểm kích thước và SHA-256 ở **cuối** luồng giải mã, nên server giải mã **hai lượt**: lượt một vào `Stream.Null` để xác nhận
toàn vẹn, lượt hai mới ghi ra response. Nhờ vậy tệp hỏng **không bao giờ** gửi byte nội dung nào: nhận `500 CONTENT_INTEGRITY_FAILED`
(`application/problem+json`) và nhật ký ghi `IntegrityFailed`. Đổi lại mỗi lần tải giải mã hai lần (chấp nhận được với giới hạn 20 MiB/tệp).

## 6. Mã lỗi

Lỗi trả `application/problem+json` có trường `code` (UPPER_SNAKE) và đôi khi `reason`.

| Mã | HTTP | Khi nào |
|---|---|---|
| `HANDOVER_INVITATION_INVALID` | 404 | Token trong link không tồn tại. |
| `HANDOVER_NOT_FOUND` | 404 | Lượt/grant/tài sản không tồn tại **hoặc không phải của bạn** (cùng một lỗi, không lộ sự tồn tại, SEC-04). |
| `HANDOVER_ALREADY_CLAIMED` | 409 | Lượt đã được tài khoản khác nhận. |
| `HANDOVER_NOT_ALLOWED` | 409 | `reason`: `OWNER_CANNOT_CLAIM`, `PLAN_NOT_IN_HANDOVER`, `WRONG_STATE`, `PLAN_NOT_READY`, `NOTHING_TO_HAND_OVER`; ở `identity/complete` thêm `IDENTITY_NOT_VERIFIED` (chưa eKYC cho lượt này), `IDENTITY_NOT_FINISHED` (chưa so khớp mặt), `IDENTITY_FAILED`, `IDENTITY_NEEDS_REVIEW`, `IDENTITY_WRONG_PERSON`. |
| `HANDOVER_DECISION_EXPIRED` | 409 | Quá hạn 7 ngày quyết định. |
| `GRANT_NOT_ACTIVE` | 403 | `reason`: `EXPIRED`, `BLOCKED`, `REVOKED`, `PLAN_NOT_ACTIVE`. |
| `CONTENT_INTEGRITY_FAILED` | 500 | Nội dung không qua kiểm toàn vẹn. |
| `EDIT_CONFLICT` | 409 | Hai thao tác cùng sửa một lượt cùng lúc (ví dụ bấm "Đồng ý" hai lần song song: một bên thua). Bấm lại sẽ thấy lượt đã hoàn tất. |
| `EMAIL_NOT_VERIFIED`, `ACCOUNT_NOT_ACTIVE`, `SESSION_EXPIRED` | 403/403/401 | Tài khoản chưa đủ điều kiện hoặc chưa đăng nhập. |

## 7. Bảo mật và dữ liệu

- Token mời chỉ lưu dạng hash SHA-256 (`InvitationTokenHash`); token gốc chỉ đi tới bộ gửi email. Email mời chỉ có tên và link, không có tên gói hay tài sản.
- Chủ kho không thể tự nhận lượt của mình (`OWNER_CANNOT_CLAIM`).
- Snapshot ghim phiên bản tài sản lúc mở bàn giao; chủ kho sửa tài sản sau đó không đổi gì ở lượt.
- Chỉ mục unique: một grant cho mỗi lượt; mỗi chỉ định chỉ một lượt đang hiệu lực/đã nhận (`UX_HandoverTurns_ActivePerDesignation`).
- Nhật ký truy cập (`HandoverAccessLogs`) chỉ ghi id và kết quả (`Succeeded`, `Denied`, `IntegrityFailed`), không ghi tên tệp hay nội dung.

## 8. Khi gộp với nhánh khác

Chỉ có một xung đột không tránh được: `AppDbContextModelSnapshot.cs` (file do EF sinh) nếu nhánh kia cũng thêm bảng. Cách xử lý cho bên gộp sau:
xóa migration `*_AddHandoverReceive` (cả `.Designer.cs`), lấy snapshot của nhánh đích, rồi chạy lại
`dotnet ef migrations add AddHandoverReceive --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api --output-dir Data/Migrations`.
Các file dùng chung chỉ bị thêm vài dòng ở đúng vị trí (xem `docs/superpowers/plans/2026-10-14-handover-receive.md`, mục Global Constraints).

## 9. Chạy thử nhanh

1. Chạy `http/handover-dev.sql` cho kho của bạn.
2. Đăng nhập web bằng tài khoản khác chủ kho (đã xác minh email) rồi mở `http://localhost:5173/receive?token=dev-token-1`.
3. Hoặc dùng `http/handover-receive.http`.
