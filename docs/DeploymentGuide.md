# 🚀 Hướng dẫn Đồng bộ hóa (Sync) Netcode lên Edgegap Cloud

Sau khi đã thêm `NetworkBootstrapper.cs`, hãy thực hiện các bước sau để cập nhật "bộ não" cho Server của bạn:

---

### Bước 1: Build Unity (Dedicated Server / Linux)
1. Trong Unity Editor, vào **File > Build Settings**.
2. Chọn Platform: **Dedicated Server** (Nếu không có, hãy cài đặt module *Linux Build Support*).
3. Target Platform: **Linux**.
4. Architecture: **x86_64**.
5. Nhấn **Build** và chọn đường dẫn: `C:\Users\Admin\EdgeParty\Builds\Linux\`.
6. **QUAN TRỌNG**: Đặt tên file thực thi là `EdgePartyServer.x86_64`.

---

### Bước 2: Build & Push Docker (CLI)
Mở PowerShell hoặc Command Prompt tại thư mục gốc của dự án (`C:\Users\Admin\EdgeParty`) và chạy các lệnh sau:

```powershell
# 1. Build Image cục bộ
docker build -t edgeparty:latest .

# 2. Gán nhãn (Tag) cho registry của Edgegap (Sử dụng đường dẫn chính xác của bạn)
docker tag edgeparty:latest registry.edgegap.com/uit-anhgam-cpkdvo5h5bd5/edge-party-server:latest

# 3. Đẩy lên Cloud (Sync)
docker push registry.edgegap.com/uit-anhgam-cpkdvo5h5bd5/edge-party-server:latest
```

> [!NOTE]
> Nếu bạn chưa đăng nhập Docker vào Edgegap, hãy dùng lệnh `docker login registry.edgegap.com` trước.

---

### Bước 3: Kiểm tra Dashboard Edgegap
1. Truy cập [Dashboard Edgegap](https://dashboard.edgegap.com).
2. Vào ứng dụng **EDGEPARTY**.
3. Tạo một **Deployment** mới từ tag `latest`.
4. Copy **Public IP** và **Port** (Vd: `185.12.34.56` và `27891`).

---

### Bước 4: Test Kết nối (Client)
1. Chạy game trong Unity Editor (hoặc Build Client Windows).
2. Nhập IP và Port bạn vừa copy vào HUD.
3. Nhấn **CONNECT TO SERVER (CLIENT)**.

> [!TIP]
> Bạn có thể kiểm tra Log của Server trực tiếp trên Dashboard Edgegap để xem các dòng debug từ Netcode.
