# HƯỚNG DẪN CỘNG TÁC UNITY QUA GITHUB 

Tài liệu này hướng dẫn cách thiết lập và làm việc nhóm để đảm bảo project không bị lỗi, mất dữ liệu hoặc xung đột giữa các thành viên.

---

## 1. CÀI ĐẶT CÔNG CỤ (BẮT BUỘC)

### 1.1. Unity Editor & Modules
Tất cả thành viên **PHẢI** sử dụng đúng phiên bản: **2022.3.25f1**.
**Quan trọng**: Mở **Unity Hub** -> Tab **Installs** -> Nhấn bánh răng cạnh bản 2022.3.25f1 -> **Add Modules**. Cài đặt đủ 3 cái sau:
*   **Linux Build Support (IL2CPP)**
*   **Linux Build Support (Mono)**
*   **Linux Dedicated Server Build Support**
*(Nếu không cài bản Linux này, code Edgegap sẽ báo lỗi Missing Namespace).*

### 1.2. Git LFS (Large File Storage)
Vì Project Game có nhiều file nặng (Model 3D, Texture), bạn cần cài Git LFS để GitHub không bị treo hoặc báo lỗi dung lượng.
*   **Cách 1 (Khuyên dùng)**: Tải và cài đặt **GitHub Desktop**. Phần mềm này đã tích hợp sẵn Git LFS và rất dễ dùng cho người mới.
*   **Cách 2 (Dùng lệnh)**: Tải tại [git-lfs.github.com](https://git-lfs.github.com/) -> Chạy lệnh `git lfs install` trong CMD/Terminal.

### 1.3. Edgegap SDK & Plugin
Project có sử dụng Edgegap SDK. Khi bạn Pull về mà Unity báo lỗi đỏ, hãy kiểm tra:
*   **Matchmaking SDK**: Nếu Unity không tự nhận diện gói Git, hãy vào `Package Manager` -> `Add package from git URL` -> Dán link GitHub của Edgegap [https://github.com/edgegap/edgegap-unity-matchmaking-sdk.git] vào.
*   **Lỗi Namespace (Visual Studio)**: Nếu VS báo "The type or namespace name 'Edgegap' could not be found":
    1. Vào Unity -> **Edit / Preferences / External Tools**.
    2. Tại dòng **Generate .csproj files for**, tích chọn các ô sau:
        *   ✅ **Embedded packages**
        *   ✅ **Local packages**
        *   ✅ **Registry packages**
        *   ✅ **Git packages** (Bắt buộc để nhận diện Edgegap SDK)
        *   ✅ **Built-in packages**
        *   ✅ **Player projects**
    3. Nhấn **Regenerate project files**.
    4. Đây là thiết lập cá nhân, mỗi người phải tự làm một lần trên máy mình.

---

## 2. QUY TRÌNH CLONE (TẢI DỰ ÁN VỀ MÁY)

Sau khi được thêm vào danh sách **Collaborators** trên GitHub:

1.  Mở **GitHub Desktop**.
2.  Chọn **File** -> **Clone Repository**.
3.  Chọn tab **GitHub.com**, tìm tên dự án của nhóm và nhấn **Clone**.
4.  Chọn thư mục lưu trữ trên máy bạn (Nên lưu ở ổ SSD để Unity chạy nhanh hơn).

---

## 3. CÁCH MỞ DỰ ÁN TRONG UNITY

1.  Mở **Unity Hub**.
2.  Nhấn nút **Add** (hoặc mũi tên bên cạnh nút Open -> **Add project from disk**).
3.  Trỏ đến thư mục bạn vừa Clone về.
4.  Đợi Unity khởi tạo (Lần đầu mở sẽ mất 5-10 phút vì Unity phải tải lại thư mục *Library* từ máy bạn).

---

## 4. QUY TRÌNH LÀM VIỆC 

Để tránh lỗi xung đột (Merge Conflict), hãy tuân thủ 3 bước:

1.  **PULL (Trước khi làm)**: Mở GitHub Desktop, nhấn **Fetch origin** rồi nhấn **Pull**. Hoặc có thể xài lệnh trong terminal
2.  **WORK (Trong khi làm)**: 
    *   Hạn chế sửa chung một Scene.
    *   Dùng **Prefab** cho mọi thứ (Nhân vật, Boss, Map) để khi gộp lại không bị lỗi file Scene.
3.  **PUSH (Sau khi làm xong)**:
    *   Quay lại GitHub Desktop.
    *   Viết chú thích (Summary) ngắn gọn (VD:    "Xong core di chuyển").
    *   **Commit** và **Push**

---

## 5. LƯU Ý KHI DÙNG VISUAL STUDIO 

Khi mở Visual Studio, bạn sẽ thấy rất nhiều dự án (60+ cái). Đừng hoảng sợ, đây là cách Unity quản lý thư viện.

1.  **Chỉ quan tâm đến `Assembly-CSharp`**: Đây là nơi chứa code bạn viết trong thư mục `Assets`. Hãy nhấn mũi tên để mở nó ra.
2.  **`Assembly-CSharp.Player`?**: 
    *   Đây là "bản sao" của code bạn nhưng ở góc nhìn của máy người chơi (Player). 
    * Tuy thực chất cả 2 cùng link vào 1 file cs nhưng luôn mở và sửa file trong mục **`Assembly-CSharp`** (cái không có chữ .Player) cho đỡ rối.
3.  **Dùng thanh tìm kiếm**: Nhấn `Ctrl + ;` để tìm file nhanh nhất thay vì kéo chuột tìm trong danh sách dài.

---

**Chúc nhóm làm đồ án thành công!**
