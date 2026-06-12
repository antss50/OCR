# LexVerse README

## 1. Cấu trúc thư mục và cách dùng từng phần

```text
LexVerse/
│
├── src/
│   ├── LexVerse.App/
│   ├── LexVerse.Core/
│   ├── LexVerse.Infrastructure/
│   ├── LexVerse.OCR/
│   ├── LexVerse.Translation/
│   └── LexVerse.Overlay/
│
├── tests/
│   ├── LexVerse.Core.Tests/
│   ├── LexVerse.OCR.Tests/
│   └── LexVerse.Translation.Tests/
│
├── samples/
│
├── docs/
│
├── assets/
│
├── scripts/
│
├── .gitignore
├── README.md
└── LexVerse.sln
```

### `src/LexVerse.App/`
Phần chạy chính của ứng dụng.

Dùng để:
- Khởi động app.
- Gọi các module OCR, Translation, Overlay.
- Chứa UI chính nếu có.

Không nên để logic xử lý nặng ở đây.

---

### `src/LexVerse.Core/`
Phần lõi của hệ thống.

Dùng để:
- Chứa interface.
- Chứa model dùng chung.
- Chứa logic chính không phụ thuộc UI, OCR engine, API dịch.

Ví dụ nội dung nên đặt ở đây:
- `IOcrService`
- `ITranslationService`
- `TextRegion`
- `TranslationResult`
- `AppSettings`

---

### `src/LexVerse.Infrastructure/`
Phần kết nối bên ngoài.

Dùng để:
- Đọc/ghi file config.
- Gọi API.
- Logging.
- Lưu cache.
- Xử lý setting hệ thống.

Không đặt logic nghiệp vụ chính ở đây.

---

### `src/LexVerse.OCR/`
Phần xử lý OCR.

Dùng để:
- Nhận ảnh màn hình hoặc vùng ảnh.
- Trích xuất text.
- Trả về danh sách vùng text.

Module này chỉ nên tập trung vào OCR, không dịch text và không vẽ overlay.

---

### `src/LexVerse.Translation/`
Phần xử lý dịch.

Dùng để:
- Nhận text từ OCR.
- Dịch sang ngôn ngữ đích.
- Trả về kết quả dịch.

Module này không xử lý capture màn hình và không vẽ UI.

---

### `src/LexVerse.Overlay/`
Phần hiển thị lớp overlay trên màn hình.

Dùng để:
- Vẽ text dịch lên màn hình.
- Quản lý vị trí text.
- Xử lý click-through nếu cần.
- Cập nhật overlay theo kết quả OCR và Translation.

Module này không tự OCR và không tự dịch.

---

### `tests/`
Chứa code test tự động.
Tức là code sẽ tự động được test sau khi build

Dùng để:
- Test logic trong `Core`.
- Test xử lý OCR giả lập.
- Test xử lý Translation giả lập.

Không test trực tiếp UI nặng nếu chưa cần.

---

### `samples/`
Chứa code test.
Test những thứ cần chạy thật,

Ví dụ:
    chạy in chữ ra Overlay để xem in ra có đúng không (cái này test tự động thì sao mà biết đúng sai)

---

### `docs/`
Chứa tài liệu dự án.

Dùng để:
- Ghi luồng xử lý.
- Ghi quyết định kỹ thuật.
- Ghi hướng dẫn setup.
- Ghi ghi chú họp team.

---

### `assets/`
Chứa tài nguyên tĩnh.

Dùng để:
- Icon.
- Ảnh demo.
- Font nếu được phép dùng.
- File mẫu test thủ công.

Không lưu file build, file tạm, hoặc dữ liệu quá nặng.

---

### `scripts/`
Chứa script hỗ trợ.

Dùng để:
- Script build.
- Script chạy test.
- Script setup môi trường.

---

## 2. Luật dùng Git chung

### Branch

Không code trực tiếp trên `main`.

Branch chính:

```text
main      : bản ổn định
develop   : bản đang phát triển chung
feature/* : code tính năng mới
fix/*     : sửa lỗi
```

Cách đặt tên branch:

```text
feature/ocr-module
feature/translation-service
feature/overlay-window
fix/ocr-empty-result
fix/overlay-position
```

---

### Commit

Commit phải ngắn gọn, rõ việc đã làm.

Format:

```text
[type] đối tượng: nội dung
```

Các type dùng chung:

```text
[feat] : thêm tính năng
[fix] : sửa lỗi
[refactor] : sửa code nhưng không đổi chức năng
[test] : thêm hoặc sửa test
[docs] : sửa tài liệu
[chore] : việc phụ như config, rename, cleanup
```

Ví dụ:

```text
[feat] overlay: thêm tính năng kéo thả vùng dịch
[fix] translate: sửa lỗi kết quả dịch rỗng
[refactor] app ui/ux: tách logic giao diện khỏi xử lý OCR
[test] ocr: thêm test cho hàm nhận diện vùng chữ
[docs] testing translate: cập nhật hướng dẫn test module dịch
[chore] config overlay: đổi tên file cấu hình overlay
```

---

### Pull code trước khi làm 1 tính năng gì đó mới (hoặc có cập nhật gấp)

Trước khi code:

```bash
git checkout develop
git pull origin develop
```

Sau đó mới tạo branch mới (làm 1 tính năng gì đó mớis):

```bash
git checkout -b feature/ten-chuc-nang
```

---

### Push code

Sau khi code xong:

```bash
git add .
git commit -m "type: nội dung"
git push origin ten-branch
```

Sau đó tạo Pull Request vào `develop`.

---

### Pull Request

Mỗi Pull Request chỉ nên làm một việc chính.

Trước khi tạo Pull Request cần kiểm tra:

- Code chạy được.
- Không lỗi build.
- Không commit file rác.
- Không sửa lung tung phần của người khác.
- Tên Pull Request rõ ràng.

Không merge Pull Request của chính mình nếu chưa có người khác xem.

---

### Resolve conflict

Khi bị conflict:

- Không xóa code của người khác nếu chưa hiểu.
- Đọc kỹ file bị conflict.
- Hỏi người đang phụ trách phần đó nếu không chắc.
- Resolve xong phải chạy lại project.

---

### Không commit các file sau

Không commit:

```text
bin/
obj/
.vs/
*.user
*.suo
*.log
.env
appsettings.Local.json
```

Không commit API key, token, password, hoặc dữ liệu cá nhân.

---

### Quy tắc làm việc chung

- Một người chỉ nên phụ trách một module chính trong một thời điểm.
- Trước khi sửa file lớn của người khác, cần báo trong nhóm.
- Code xong phần nào thì push sớm phần đó.
- Không để code local quá lâu rồi mới push.
- Không đổi cấu trúc thư mục nếu chưa thống nhất.
- Không đổi tên class, interface, project nếu chưa báo team.
- Ưu tiên code dễ đọc hơn code quá phức tạp.
- Tách logic ra khỏi UI để dễ test và dễ sửa.
