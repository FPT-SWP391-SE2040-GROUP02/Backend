# Cloudflare R2: cài đặt và thử cho người test và AI

> File này cho **người hoặc AI** cần chạy LegacyVault Backend với nội dung tài sản lưu trên Cloudflare R2 (mặc định lưu ở đĩa). Làm theo thứ tự là chạy và thử được. **Mục 2 có sẵn khóa test dùng chung của nhóm** (do leader cấp, cố ý để trong file này để mọi người và AI dùng ngay).

## 0. Luật an toàn (AI bắt buộc đọc)

- Khóa ở mục 2 là khóa **test dùng chung**, đã được leader đồng ý để trong repo. **Chỉ dùng cho bucket test và dữ liệu giả.** Không dùng cho dữ liệu thật, không đem sang dự án hay nơi khác.
- Chỉ đặt khóa vào **user-secrets** hoặc biến môi trường (mục 2). Không chép vào `appsettings*.json`, `.http`, README hay file khác trong repo, không in ra log, không đưa vào thông báo lỗi.
- Nếu repo trở thành công khai, hoặc có người ngoài nhóm đọc được: **báo leader đổi khóa ngay** (tạo token mới, thu hồi token cũ). Nên đổi khóa sau mỗi đợt test.
- Bucket này **dùng chung cả nhóm** và API hiện chưa có endpoint xóa tài sản, nên object thử sẽ nằm lại. Chỉ tạo ít tài sản nhỏ, giả, và dọn bằng dashboard Cloudflare khi xong.
- Việc commit và push do người làm thực hiện (AGENTS.md Phần 3).

## 1. Chuẩn bị trên Cloudflare (chỉ khi KHÔNG dùng bucket chung của nhóm)

Dùng bucket chung ở mục 2 thì **bỏ qua mục này**. Muốn bucket riêng của bạn thì làm:

1. Vào **R2 Object Storage** → **Create bucket**, tên ví dụ `legacyvault-test-<ten-ban>`. Giữ bucket **private**: không bật Public access, không gắn domain công khai.
2. **R2** → **Manage API tokens** → **Create API token**, quyền **Object Read & Write**, giới hạn **đúng bucket** vừa tạo (không dùng token toàn tài khoản).
3. Ghi lại 4 giá trị (chỉ giữ trong máy của bạn): **Account ID** (góc phải trang R2), **tên bucket**, **Access Key ID**, **Secret Access Key** (chỉ hiện một lần lúc tạo token).

## 2. Đặt cấu hình bằng user-secrets

### 2a. Khóa test dùng chung của nhóm (leader cấp)

Chạy ở thư mục gốc repo Backend, dán nguyên khối:

```powershell
dotnet user-secrets set "AssetStorage:Provider" "R2" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:AccountId" "562cae34066c8f4099981c65baf4ed31" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:BucketName" "legacyvault-prototype-private" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:AccessKeyId" "fcc10274589cbcb2c94ce5a6421fbcd7" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:SecretAccessKey" "63350cda2dd02ecb408a8a0f398270fb30f85626b42cd8508543aa5ed3753f52" --project src/LegacyVault.Api
```

Biến môi trường tương đương: `AssetStorage__Provider=R2`, `AssetStorage__R2__AccountId`, `AssetStorage__R2__BucketName`, `AssetStorage__R2__AccessKeyId`, `AssetStorage__R2__SecretAccessKey` (cùng các giá trị trên).

### 2b. Dùng bucket riêng của bạn

Thay `<...>` bằng giá trị thật từ mục 1:

```powershell
dotnet user-secrets set "AssetStorage:Provider" "R2" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:AccountId" "<account-id>" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:BucketName" "<ten-bucket>" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:AccessKeyId" "<access-key-id>" --project src/LegacyVault.Api
dotnet user-secrets set "AssetStorage:R2:SecretAccessKey" "<secret-access-key>" --project src/LegacyVault.Api
```

Kiểm tra đã đặt đủ mà **không lộ giá trị** (chỉ in tên khóa):

```powershell
dotnet user-secrets list --project src/LegacyVault.Api | ForEach-Object { ($_ -split ' = ')[0] }
```

Phải thấy đủ 5 khóa `AssetStorage:*` ở trên. Cách khác (máy chủ hoặc CI): biến môi trường `AssetStorage__Provider`, `AssetStorage__R2__AccountId`, `AssetStorage__R2__BucketName`, `AssetStorage__R2__AccessKeyId`, `AssetStorage__R2__SecretAccessKey`.

Khóa mã hóa nội dung (KEK) đã có sẵn khóa mẫu `dev-local` trong `appsettings.Development.json`, chỉ dùng local. R2 chỉ giữ dữ liệu đã mã hóa AES-256-GCM, nên không có KEK thì không đọc được nội dung.

## 3. Chạy database và API

```powershell
docker compose up -d
dotnet ef database update --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api -- --environment Development
dotnet run --project src/LegacyVault.Api --launch-profile http
```

- Không dùng Docker thì đặt chuỗi kết nối tới SQL Server của bạn bằng biến môi trường `ConnectionStrings__Default`, rồi chạy hai lệnh còn lại.
- API chạy ở `http://localhost:5000`, Swagger ở `/swagger`.
- Thiếu một trong 4 giá trị R2 thì **API không khởi động** và báo `Thiếu AssetStorage:R2 ...`. Đó là cách nhanh nhất để biết cấu hình đã nhận chưa.

## 4. Thử R2 thật (smoke test)

Dùng `http/auth.http` (đăng ký, token xác minh email in ở console của API) rồi `http/vault-assets.http`:

1. Đăng nhập, tạo kho, **tạo tài sản dạng file** nhỏ (vài KB, nội dung giả như "hello r2"). API trả `201` và tài sản xuất hiện ở `GET .../assets`.
2. Kiểm tra object đã nằm trên bucket và **chỉ chứa bản mã hóa**. R2 tương thích S3, dùng AWS CLI với biến môi trường của bạn (đừng ghi khóa vào lệnh):

```powershell
$env:AWS_ACCESS_KEY_ID = "<access-key-id>"
$env:AWS_SECRET_ACCESS_KEY = "<secret-access-key>"
aws s3 ls s3://<ten-bucket> --recursive --endpoint-url https://<account-id>.r2.cloudflarestorage.com --region auto
```

   - Sau bước 1 phải có thêm đúng một object mới. Tải object đó về và mở thử: không được thấy chữ "hello r2" (nội dung là byte đã mã hóa).
3. (Tùy chọn, đọc lại nội dung) API hiện **chưa có** endpoint để Owner tải lại hoặc xóa tài sản. Chỉ luồng bàn giao mới đọc được nội dung: dựng một lượt bàn giao theo `docs/HANDOVER-RECEIVE-API.md`, `http/handover-dev.sql` và `http/handover-receive.http`, rồi `GET grants/{grantId}/assets/{itemId}/content`. Nội dung tải về phải **giống hệt** nội dung đã gửi.

Đạt khi bước 1 và 2 đúng (và bước 3 nếu có làm). Vì chưa có endpoint xóa, object thử **không tự mất**: xóa tay trong dashboard hoặc bằng `aws s3 rm` khi dọn dẹp (mục 7). Xong thì xóa biến môi trường: `Remove-Item Env:AWS_ACCESS_KEY_ID, Env:AWS_SECRET_ACCESS_KEY`.

## 5. Test tự động

- `dotnet test -c Release` **luôn ép lưu ở đĩa** (và dùng bản giả lập R2 trong bộ nhớ ở `R2AssetContentStoreTests`), nên không ghi vào bucket thật dù máy có user-secrets R2. Không cần cấu hình R2 để chạy test.
- Các test cần SQL Server thật bị bỏ qua (Skipped) nếu chưa đặt biến `LEGACYVAULT_TEST_CONNECTION`.
- Vì vậy R2 thật chỉ kiểm chứng bằng mục 4.

## 6. Lỗi thường gặp

| Triệu chứng | Nguyên nhân và cách xử lý |
|---|---|
| API không khởi động, báo thiếu `AssetStorage:R2` | Thiếu một trong 4 giá trị. Chạy lệnh liệt kê khóa ở mục 2. |
| `503 STORAGE_UNAVAILABLE` khi tạo tài sản (hoặc tải qua grant) | Sai Access Key, sai Account ID hoặc tên bucket, token không đủ quyền trên bucket, hoặc mất mạng. Kiểm tra lại token và bucket. |
| Tài sản cũ không tải được sau khi đổi sang R2 | Đổi `Provider` **không chuyển dữ liệu cũ**: tài sản lưu ở đĩa không đọc được khi chạy R2, và ngược lại. Dùng database mới hoặc tạo tài sản mới sau khi đổi. |
| Lỗi toàn vẹn nội dung khi tải | Object bị sửa hoặc KEK sai. Dùng đúng KEK `dev-local` đã tạo tài sản đó. Mất KEK là mất nội dung. |
| Cloudflare báo lỗi checksum | Code đã đặt checksum `WHEN_REQUIRED` cho R2. Nếu tự đổi phiên bản AWSSDK.S3 thì giữ nguyên tùy chọn này. |

## 7. Dọn dẹp và quay về lưu ở đĩa

```powershell
dotnet user-secrets remove "AssetStorage:Provider" --project src/LegacyVault.Api
```

Sau đó khởi động lại API (mặc định `Provider` = `Local`, thư mục `App_Data/asset-content`). Xóa các object thử trong bucket và **thu hồi API token** ở Cloudflare khi không dùng nữa.

## 8. Checklist cho AI trước khi báo "đã test R2"

- [ ] Đã xác nhận 5 khóa `AssetStorage:*` có trong user-secrets (chỉ nhìn tên khóa).
- [ ] API khởi động được với `Provider = R2`.
- [ ] Tạo tài sản file thành công (`201`) và có đúng một object mới trên bucket.
- [ ] Object trên bucket là bản mã hóa (không thấy nội dung gốc).
- [ ] Nếu có dựng luồng bàn giao: nội dung tải qua grant giống hệt nội dung đã gửi.
- [ ] Đã xóa object thử khỏi bucket khi dọn dẹp.
- [ ] Không có khóa nào xuất hiện trong output, log, diff hay file của repo.
