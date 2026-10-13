# Hướng dẫn Git và quy trình code chức năng

Repository chính thức: https://github.com/FPT-SWP391-SE2040-GROUP02/Backend

Các lệnh bên dưới chạy bằng PowerShell, tại thư mục chứa `LegacyVault.sln`. Tên `feature/auth` chỉ là ví dụ; thay bằng chức năng bạn phụ trách.

## 1. Nhóm sử dụng các nhánh như thế nào?

| Nhánh | Mục đích | Cách sử dụng |
|---|---|---|
| `main` | Bản ổn định của nhóm | Nhận PR từ `develop` khi đã kiểm tra |
| `develop` | Tích hợp các chức năng đang phát triển | Nhận PR từ các nhánh chức năng |
| `feature/ten-chuc-nang` | Làm một chức năng | Tạo từ `develop`, code và push vào nhánh này |
| `fix/ten-loi` | Sửa một lỗi trong quá trình phát triển | Tạo từ `develop`, mở PR về `develop` |
| `docs/ten-tai-lieu` | Viết hoặc sửa tài liệu | Tạo từ `develop`, mở PR về `develop` |

```text
develop mới nhất
    ↓ tạo nhánh
feature/auth → code → kiểm tra → commit → push
    ↓ Pull Request, review và CI
develop
    ↓ Pull Request khi nhóm chốt bản ổn định
main
```

Không code và push chức năng trực tiếp lên `main` hoặc `develop`. Quy tắc này cần được nhóm thực hiện; muốn GitHub bắt buộc tuân thủ thì người quản lý repo cấu hình branch protection/ruleset.

Đặt tên nhánh không dấu, chữ thường và dùng dấu gạch nối, ví dụ `feature/vault-management`, `fix/login-validation`, `docs/git-workflow`. Nên chia công việc đủ nhỏ để một PR dễ review.

## 2. Thành viên mới: clone và setup một lần

Tại thư mục bạn muốn lưu source:

```powershell
git clone https://github.com/FPT-SWP391-SE2040-GROUP02/Backend.git
cd Backend
git fetch origin
git switch --track origin/develop
```

Nếu đã có nhánh `develop` trên máy, dùng `git switch develop` thay cho lệnh cuối. Không chạy lại `git init` hoặc `git remote add origin` sau khi clone: Git đã cấu hình sẵn remote.

Kiểm tra thông tin tác giả commit:

```powershell
git config user.name
git config user.email
```

Nếu thiếu hoặc sai, thay nội dung trong dấu nháy bằng tên/email của bạn:

```powershell
git config user.name "Tên của bạn"
git config user.email "Email liên kết GitHub của bạn"
```

Cấu hình này chỉ áp dụng cho repository hiện tại. Email liên kết GitHub hoặc email noreply trong tài khoản giúp GitHub gắn commit đúng tài khoản.

Mở Docker Desktop và setup môi trường theo [README](../README.md):

```powershell
docker compose up -d --wait
```

Docker tự khởi động SQL Server và tạo database trống nếu chưa có; không cần `.env`, tự đặt password hoặc chạy script setup. Mỗi thành viên có database local riêng, dùng thông tin kết nối mẫu trong README. API vẫn cần .NET SDK/Visual Studio để chạy.

## 3. Khi bắt đầu một chức năng mới

Đầu tiên kiểm tra nhánh và công việc đang có:

```powershell
git status
git branch --show-current
```

Nếu còn file sửa chưa commit, xử lý theo mục 6 trước khi tiếp tục. Khi working tree sạch:

```powershell
git switch develop
git pull --ff-only origin develop
git switch -c feature/auth
```

- `switch develop`: chuyển sang nhánh tích hợp.
- `pull --ff-only`: lấy bản mới và chỉ cập nhật khi không cần tạo merge commit. Nếu lệnh lỗi, dừng xử lý lỗi trước khi chạy lệnh tiếp theo.
- `switch -c`: tạo nhánh mới và chuyển sang nhánh đó.

Kiểm tra thấy đúng tên `feature/auth` rồi mới code:

```powershell
git branch --show-current
docker compose up -d --wait
dotnet run --project src/LegacyVault.Api --launch-profile http
```

Có thể chạy API bằng Visual Studio thay cho `dotnet run`. Không chạy cả hai cùng cổng 5000.

## 4. Trong lúc code và trước khi commit

Code theo cấu trúc Controller → Service → Repository → Database trong README. Kiểm tra thay đổi thường xuyên:

```powershell
git status
git diff
```

Trước khi gửi một phần công việc đã hoàn thiện:

```powershell
dotnet build LegacyVault.sln
dotnet test LegacyVault.sln
```

Chạy thêm các ca kiểm thử phù hợp chức năng. Test SQL thật cần cấu hình biến môi trường như README; nếu hiện `Skipped` thì test đó chưa chạy. Thay đổi tài liệu thuần túy chỉ cần kiểm tra nội dung/link và `git diff --check`, không cần chạy lại bộ test .NET.

Nếu thay entity/mapping, tạo và chạy migration theo README; commit cả migration và model snapshot cùng code. Không tạo lại migration của người khác sau khi pull.

## 5. Commit và push code chức năng

### Bước 1: chọn file đưa vào commit

```powershell
git status
git add .
git diff --cached --stat
git diff --cached
```

`git add .` chọn mọi thay đổi không bị ignore trong thư mục hiện tại. Chỉ dùng khi mọi thay đổi đều thuộc công việc cần commit; nếu có việc khác xen lẫn thì dùng `git add đường-dẫn-file` cho từng file cần chọn.

Kiểm tra không có `.env`, `.vs`, `bin`, `obj`, token hoặc dữ liệu thật. Cấu hình SQL development mẫu trong Compose/appsettings.Development.json được commit để mọi người chạy ngay. Nếu chọn nhầm file, bỏ khỏi vùng chuẩn bị commit bằng:

```powershell
git restore --staged đường-dẫn-file
```

Lệnh này giữ nội dung file trên máy. Nó dùng được sau khi repo đã có commit, như repository hiện tại của nhóm.

### Bước 2: tạo commit bằng tiếng Việt

```powershell
git commit -m "feat: thêm chức năng đăng nhập"
```

Ví dụ các loại commit:

| Loại | Ví dụ |
|---|---|
| Chức năng | `feat: thêm chức năng tạo kho` |
| Sửa lỗi | `fix: kiểm tra email trước khi đăng nhập` |
| Tài liệu | `docs: bổ sung hướng dẫn Git cho nhóm` |
| Cấu hình | `chore: cập nhật cấu hình môi trường development` |
| Kiểm thử | `test: bổ sung kiểm thử quyền truy cập kho` |

Commit lưu phiên bản trên máy; chưa đưa code lên GitHub. Tên commit cần mô tả phần đã làm thực tế, không ghi hoàn thành cả chức năng khi mới làm một phần.

### Bước 3: đẩy nhánh lên GitHub

Lần đầu push nhánh:

```powershell
git push -u origin feature/auth
```

Các lần sau trên cùng nhánh:

```powershell
git push
```

`-u` thiết lập nhánh remote theo dõi. Push nhánh chức năng không tự đưa code vào `develop` hoặc `main`. Nếu cần đăng nhập, dùng tài khoản GitHub được cấp quyền vào repo nhóm.

## 6. Chuyển nhánh khi đang làm dở

### Khi không có thay đổi chưa commit

```powershell
git switch develop
git switch feature/auth
```

Hai lệnh trên minh họa chuyển qua rồi trở lại; bạn chỉ chạy lệnh tương ứng với nhánh muốn đến.

`git switch feature/auth` dùng cho nhánh đã tồn tại. Không thêm `-c` mỗi lần quay lại, vì `-c` là tạo mới. Nếu nhánh chỉ có trên GitHub:

```powershell
git fetch origin
git switch --track origin/feature/auth
```

### Khi còn code dở: lưu tạm trước khi chuyển

Ví dụ đang ở `feature/auth`, cần qua `develop`:

```powershell
git status
git stash push -u -m "Tạm lưu phần đăng nhập đang làm"
git switch develop
```

`-u` lưu cả file mới chưa được Git theo dõi, nhưng không lưu file bị ignore như `.env`. Stash chỉ nằm trên máy, không được push lên GitHub và không phải bản backup từ xa.

Khi quay lại:

```powershell
git switch feature/auth
git stash list
```

Chọn đúng stash theo lời nhắn; nếu nó là mục mới nhất:

```powershell
git stash apply 'stash@{0}'
git status
```

Kiểm tra code đã khôi phục đúng rồi mới xóa bản lưu tạm đó:

```powershell
git stash drop 'stash@{0}'
```

`apply` giữ stash để kiểm tra trước; nếu có conflict thì xử lý rồi mới drop. Nếu có nhiều stash, thay chỉ số bằng đúng mục cần lấy, không mặc định mục 0 luôn là code của nhánh đang làm.

Nếu code hiện tại đã là một phần công việc hoàn chỉnh, có thể commit trên nhánh chức năng trước khi chuyển, thay vì stash.

## 7. Lấy code mới từ develop vào nhánh đang làm

Trong khi bạn code, các thành viên khác có thể đã merge chức năng vào `develop`. Sau khi commit hoặc stash phần đang sửa, cập nhật nhánh của bạn:

```powershell
git switch feature/auth
git fetch origin
git merge origin/develop
```

Cách này đưa thay đổi mới của nhóm vào nhánh chức năng; không thay đổi nhánh `develop` trên GitHub. Nếu Git mở trình soạn thảo cho merge message, lưu và đóng để hoàn tất.

Sau khi merge thành công, chạy lại kiểm tra liên quan rồi `git push`.

### Nếu có conflict

1. Chạy `git status` để biết file xung đột.
2. Mở file hoặc dùng công cụ merge của IDE.
3. Đọc cả hai phía, chọn hoặc kết hợp code đúng; xóa các dấu `<<<<<<<`, `=======`, `>>>>>>>`.
4. Build/test phần thay đổi, rồi đánh dấu đã xử lý:

```powershell
git add đường-dẫn-file-đã-sửa
git status
git commit -m "merge: đồng bộ develop và xử lý xung đột"
git push
```

Nếu muốn hủy lần merge đang xung đột và quay về trước lúc merge:

```powershell
git merge --abort
```

Nên bắt đầu merge với working tree sạch để có thể quay lại rõ ràng. Không dùng `git reset --hard` hoặc `git push --force` để xử lý lỗi thông thường.

## 8. Tạo Pull Request vào develop

Sau khi push:

1. Mở repository GitHub, chọn **Pull requests → New pull request**.
2. Chọn **base: `develop`**, **compare: `feature/auth`**.
3. Kiểm tra diff: chỉ chứa thay đổi của chức năng, không có file bí mật hoặc việc khác.
4. Điền tên PR bằng tiếng Việt, mô tả thay đổi, cách kiểm tra và migration nếu có.
5. Nhờ thành viên khác review; chờ CI chạy thành công và xử lý góp ý trước khi merge.

PR chưa sẵn sàng có thể để Draft. Khi có góp ý, sửa trên chính nhánh `feature/auth`, commit và push tiếp; PR tự cập nhật, không cần tạo PR mới.

Workflow hiện tại chạy khi mở/cập nhật PR (kể cả PR vào `develop`) và khi push `main`; push riêng nhánh feature/develop không tự chạy workflow nếu chưa có PR phù hợp.

Sau khi merge PR, code mới có trong `develop`. Chỉ khi nhóm chốt phiên bản ổn định mới tạo PR **base: `main`**, **compare: `develop`**.

## 9. Sau khi chức năng đã merge

Đảm bảo mọi thay đổi cần giữ đã commit/push, rồi cập nhật nhánh tích hợp:

```powershell
git switch develop
git pull --ff-only origin develop
```

Nếu code mới có migration, áp dụng `database update` theo README. Khi bắt đầu chức năng tiếp theo, tạo nhánh mới từ `develop` vừa cập nhật:

```powershell
git switch -c feature/vault-management
```

Nhánh cũ có thể giữ lại. Chỉ xóa khi chắc chắn công việc đã được merge và không còn thay đổi cần giữ. Không tiếp tục dùng nhánh đã merge cho một chức năng khác.

## 10. Các lệnh hay dùng và lỗi thường gặp

| Lệnh / tình huống | Ý nghĩa / xử lý |
|---|---|
| `git status` | Nhánh hiện tại, file sửa, file staged và trạng thái merge |
| `git branch --show-current` | Tên nhánh hiện tại |
| `git branch -a` | Xem nhánh local và các remote-tracking branch đã fetch |
| `git remote -v` | Kiểm tra URL GitHub; fetch/push là hai mục bình thường |
| `git log --oneline -5` | Xem năm commit gần nhất |
| `git fetch origin` | Cập nhật thông tin remote, chưa tự merge code vào nhánh đang làm |
| Màn hình Git hiện `(END)` | Bấm `q` để thoát trình xem |
| `branch already exists` | Dùng `git switch tên-nhánh` thay vì tạo lại bằng `-c` |
| `Your local changes would be overwritten` | Commit hoặc stash đúng công việc trước khi switch/pull/merge |
| `non-fast-forward` / push rejected | Chạy fetch, kiểm tra thay đổi remote; không force push. Nếu cùng làm một nhánh, phối hợp và merge đúng remote branch trước khi push lại |
| `pull --ff-only` thất bại | Nhánh local và remote có lịch sử khác nhau; kiểm tra commit local và trao đổi với nhóm trước khi đồng bộ |
| `Authentication failed` / không có quyền | Kiểm tra tài khoản đăng nhập và quyền collaborator của repository |
| `nothing to commit` | Không có thay đổi đã chọn để commit; kiểm tra `git status` |

## 11. Bảng nhớ nhanh

| Lúc nào | Làm gì |
|---|---|
| Nhận task mới | Working tree sạch → cập nhật develop → tạo nhánh feature |
| Bắt đầu ngày làm việc | Chuyển đúng nhánh → mở Docker/database → chạy API |
| Đang code dở cần chuyển nhánh | Commit phần hoàn chỉnh hoặc stash trước |
| Chuẩn bị chia sẻ code | Kiểm tra → chọn file → xem staged diff → commit tiếng Việt → push nhánh |
| Muốn nhập code vào nhóm | PR từ feature vào develop → CI + review → merge |
| Đã merge xong | Cập nhật develop → tạo nhánh mới cho task tiếp theo |

Lệnh trong hướng dẫn là các bước để bạn thực hiện. Tạo file tài liệu này không tự commit, push hoặc merge code của bạn.
