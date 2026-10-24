# LegacyVault Backend — Agent Operating Rules

Tài liệu này áp dụng cho mọi AI Agent và lập trình viên khi làm việc trong repository **Backend** (`.NET 10 Clean Architecture`).

---

## 1. Hai nguyên tắc vàng về Git (BẮT BUỘC)

1. **LUÔN PULL TRƯỚC KHI SỬA (Pull before Modify)**:
   - Trước khi bắt đầu bất kỳ phân tích, đề xuất hay chỉnh sửa code nào, **BẮT BUỘC PHẢI CHẠY `git pull origin develop`** để cập nhật toàn bộ thay đổi mới nhất từ các thành viên trong nhóm.
   - Tuyệt đối không làm việc trên mã nguồn cũ.

2. **LUÔN TẠO NHÁNH TRƯỚC KHI CODE (Branch before Code)**:
   - **CẤM TUYỆT ĐỐI** code hoặc chỉnh sửa trực tiếp trên nhánh `develop` hoặc `main`.
   - Trước khi sửa bất kỳ file nào, **BẮT BUỘC PHẢI TẠO NHÁNH MỚI** từ `develop`:
     - Tính năng mới: `feat/<jira-id>-<slug>` hoặc `feature/<slug>`
     - Sửa lỗi: `fix/<jira-id>-<slug>` hoặc `fix/<slug>`
     - Tài liệu: `docs/<slug>`
   - Mọi thay đổi đều được tích hợp vào `develop` thông qua Pull Request hoặc merge sau khi đã chạy đủ Quality Gates.

---

## 2. Kiến trúc & Tiêu chuẩn kỹ thuật

- **Kiến trúc**: Clean Architecture 4 tầng:
  `LegacyVault.Api` $\rightarrow$ `LegacyVault.Application` $\rightarrow$ `LegacyVault.Domain` $\leftarrow$ `LegacyVault.Infrastructure`.
- **Bảo mật & Mã hóa**:
  - Không bao giờ commit secret, private key, token vào Git.
  - PII (Số CCCD, Họ tên, Ngày sinh, Địa chỉ) bắt buộc mã hóa qua `DataProtector`.
  - File nội dung tài sản được bảo vệ bằng Envelope Encryption (`AES-256-GCM`).
- **Conventional Commits**:
  - Cú pháp: `<type>(<scope>): <mô tả ngắn>`
  - Ví dụ: `feat(auth): thêm endpoint xác thực mật khẩu`, `fix(email): auto-load .env config`

---

## 3. Ràng buộc chất lượng (Quality Gates)

Trước khi commit và mở PR, bắt buộc phải chạy và đạt exit code 0:
```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```
