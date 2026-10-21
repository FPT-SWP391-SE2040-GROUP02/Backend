# Hướng dẫn cài đặt thanh toán SePay (máy local)

Tài liệu cho thành viên nhóm. Contract API: `docs/api-contract.md` mục 6.18. Luật nghiệp vụ: SRS mục 8.

## 1. Cần setup gì? (chọn theo mục đích)

| Mục đích | Cần SePay? | Làm gì |
|---|---|---|
| Xem gói, báo giá, tạo đơn, hủy đơn, nâng hạng, gia hạn, lịch sử | Không | Chạy API ở Development là đủ |
| Thử "trả tiền" nhanh | Không | Bấm **Thanh toán thử** trên FE (gọi `dev-pay`) |
| Thử đúng đường webhook, không cần tiền thật | Không | Dùng `http/billing-webhook.http` (mục 4) |
| Chuyển khoản thật, SePay tự gọi webhook | **Có** | Làm hết mục 2 và 3 |

Mặc định `appsettings.Development.json` đã bật `DevPaymentEnabled`, tài khoản ngân hàng giả `0000000000` và `SePay:Environment = Test`, nên máy mới kéo code về vẫn thử được hai dòng đầu.

**Mã QR không cần SePay setup.** Backend chỉ ghép một link ảnh `https://qr.sepay.vn/img?acc=…&bank=…&amount=…&des=…`; trình duyệt tải ảnh từ dịch vụ QR công khai của SePay (cần internet, không cần khóa). SePay chỉ tham gia ở bước sau, khi có tiền vào tài khoản thật và nó gọi webhook về API.

## 2. Chuẩn bị chung

1. SQL chạy bằng Docker (xem README mục 4), rồi cập nhật database để có bảng thanh toán:

```powershell
dotnet ef database update --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api -- --environment Development
```

2. Mua mới gói cần Owner đã **eKYC trong 90 ngày**, nên cần dịch vụ eKYC chạy (`Ekyc:BaseUrl`, mặc định `http://127.0.0.1:8879`). Không cần cho nâng hạng, gia hạn, mua thêm suất.
3. Chạy API: cổng `5000`, môi trường Development. Chạy FE `npm run dev` (cổng `5173`).

## 3. Chuyển khoản thật qua SePay

### 3.1. Tài khoản nhận tiền

Chỉ dùng tài khoản **của bạn dùng để thử**. Đặt bằng user-secrets, **không ghi vào appsettings, không commit**:

```powershell
dotnet user-secrets set "Billing:Bank:BankCode" "MBBank" --project src/LegacyVault.Api
dotnet user-secrets set "Billing:Bank:AccountNumber" "<số tài khoản>" --project src/LegacyVault.Api
dotnet user-secrets set "Billing:Bank:AccountName" "<TÊN CHỦ TÀI KHOẢN>" --project src/LegacyVault.Api
```

`BankCode` theo SePay/VietQR (ví dụ `MBBank`). Số tài khoản phải **trùng** tài khoản đã liên kết trong SePay, nếu khác thì giao dịch bị đưa vào đối chiếu (`WRONG_ACCOUNT`) và không được cấp quyền.

### 3.2. Khóa webhook

Tự đặt một chuỗi ngẫu nhiên dài, dùng cùng một chuỗi ở API và SePay:

```powershell
dotnet user-secrets set "Billing:SePay:WebhookApiKey" "<chuỗi-ngẫu-nhiên>" --project src/LegacyVault.Api
```

Chưa đặt khóa này thì webhook luôn trả `401` (chủ ý, để không cấp quyền nhầm).

### 3.3. Cho SePay gọi vào máy bạn (ngrok)

SePay cần một địa chỉ công khai. Cài ngrok, đăng nhập một lần (`ngrok config add-authtoken <token>`), rồi mở lúc API đang chạy:

```powershell
ngrok http 5000
```

Lấy địa chỉ `https://xxxx.ngrok-free.app` ngrok in ra. **Mỗi lần mở lại ngrok bản miễn phí, địa chỉ đổi**, phải cập nhật lại trong SePay.

### 3.4. Khai báo webhook trong SePay

Trong trang quản trị SePay, mục Webhooks (tên menu có thể khác theo giao diện), thêm webhook mới:

| Ô | Giá trị |
|---|---|
| URL | `https://xxxx.ngrok-free.app/api/v1/billing/sepay/webhook` |
| Kiểu xác thực | API Key (SePay gửi header `Authorization: Apikey <khóa>`) |
| API Key | chuỗi ở mục 3.2 |
| Tài khoản | tài khoản ở mục 3.1 |
| Sự kiện | Có tiền vào |

### 3.5. Thử

1. Đăng nhập Owner trên FE, vào **Gói dịch vụ**, tạo đơn.
2. Chuyển khoản **đúng số tiền** và **đúng nội dung** (mã thanh toán dạng `LV…` hiện trên màn thanh toán) trong vòng **15 phút**.
3. Màn thanh toán tự chuyển sang "Đã thanh toán" sau vài giây. Xem thêm ở **Lịch sử thanh toán** và nhật ký hoạt động.

Chuyển sai số tiền, sai tài khoản hoặc sai mã thì **không cấp quyền**; giao dịch nằm ở màn đối chiếu của Admin.

## 4. Thử webhook không cần tiền thật

Mở `http/billing-webhook.http`, điền `@apiKey` (đúng khóa mục 3.2, **xóa trước khi lưu hay commit**), `@paymentCode` và `@amount` lấy từ đơn vừa tạo, rồi gửi. File có sẵn các ca: khớp đơn, sai số tiền, sai tài khoản, thiếu mã, tiền ra, thiếu số tiền, sai khóa, và hai lệnh để Admin xem giao dịch (gửi lại cùng `id` để thử webhook gửi lặp).

## 5. Lỗi thường gặp

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| Báo giá hoặc tạo đơn trả lỗi chung (500) | Chưa có `Billing:Bank:BankCode` hoặc `AccountNumber` (môi trường không phải Development thì không có số giả) |
| Nút "Thanh toán thử" không hiện hoặc báo không tìm thấy | `DevPaymentEnabled` tắt, hoặc API không chạy ở Development |
| Webhook trả `401` | Chưa đặt hoặc sai `WebhookApiKey`, thiếu tiền tố `Apikey ` |
| Chuyển khoản xong mà đơn không đổi | Ngrok đã đổi địa chỉ, ngrok tắt, URL trong SePay sai, hoặc đơn quá 15 phút |
| Giao dịch vào màn đối chiếu | Sai số tiền (`AMOUNT_MISMATCH`), sai tài khoản (`WRONG_ACCOUNT`), thiếu mã (`CODE_MISSING`) |
| Lỗi 400 "Dữ liệu chưa hợp lệ" khi báo giá | API đang chạy bản build cũ: dừng API, build lại, chạy lại |
| Build báo không copy được DLL | API đang chạy giữ file: dừng API rồi build |

## 6. Không được làm

- Không ghi số tài khoản thật, khóa webhook hay token ngrok vào file nào trong repo, ảnh chụp màn hình hay tin nhắn nhóm.
- Không đặt `DevPaymentEnabled = true` ở `appsettings.json` hay môi trường thật (nút thanh toán thử sẽ cấp gói miễn phí).
- Không dùng chung một khóa webhook cho Test và Live (PAY-01).
