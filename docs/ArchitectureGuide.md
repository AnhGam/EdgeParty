# Kiến Trúc Hệ Thống & Quy Chuẩn Phân Bổ Code (EdgeParty Repository)

Tài liệu này định nghĩa cấu trúc nền tảng (Base Architecture) và các quy chuẩn phân bổ mã nguồn cho dự án **EdgeParty**. Với đặc thù là một hệ thống Multiplayer dựa trên tương tác vật lý thời gian thực (Physics Brawler), kiến trúc hệ thống bắt buộc phải áp dụng mô hình **Server-Authoritative** kết hợp với **Separation of Concerns (Phân tách trách nhiệm)** để tối đa hóa khả năng bảo trì và loại bỏ sự chồng chéo (overlap) giữa các mô-đun.

---

## 1. Nguyên Lý Thiết Kế Kiến Trúc (Architecture Principles)

- **Server-Authoritative Model:** Máy chủ (Server) nắm quyền quyết định cuối cùng (Source of Truth) đối với toàn bộ trạng thái trong game (Vị trí, Vật lý, Sinh tồn, Điểm số). Client chỉ đóng vai trò thu thập Input (từ thiết bị ngoại vi) và hiển thị (Rendering/Interpolation) dựa trên luồng dữ liệu Server phản hồi.
- **Strict Separation of Concerns:** Trách nhiệm xử lý logic hệ thống (Core/Network) và logic trò chơi (Gameplay/UI) phải được cô lập thành các module độc lập. Giao tiếp giữa các module phải thông qua Event, Interface, hoặc NetworkVariables thay vì tham chiếu trực tiếp chéo chiều.
- **Dữ liệu 1 chiều đối với UI (One-way Data Flow for UI):** Thành phần UI tuyệt đối không can thiệp vào Logic Game hoặc Network State. UI đóng vai trò là "Observer" (Kẻ quan sát thụ động), chỉ phản ứng khi dữ liệu tại Server/Client Data Layer thay đổi.

---

## 2. Tiêu Chuẩn Cấu Trúc Thư Mục (Directory Responsibilities & Boundaries)

Để tránh hiện tượng "Overlap" (chồng chéo logic) giữa các phần của mã nguồn, mỗi thư mục dưới đây đã được vạch rõ phạm vi trách nhiệm (Responsibility Context). Việc tạo file mới phải tuân thủ nghiêm ngặt chỉ dẫn của từng khối module.

```text
Assets/Scripts/
│
├── ConnectionManagement/
│   # Phạm vi: Logic làm việc với Transport Layer, State Machine của quá trình kết nối và Dịch vụ Cloud.
│   # Không chứa: Bất kỳ file nào liên quan đến sinh/diệt nhân vật hay luật chơi.
│   # Boundary Rule: Cung cấp API (ví dụ: IsConnected, IP, Port) cho Core xử lý.
│
├── Core/
│   # Phạm vi: Chứa các Singleton/Managers điều phối toàn bộ vòng đời (Lifecycle) của Application từ khi bật app đến khi tắt máy.
│   # Chức năng chính: Load Scene, Bootstrap (khởi tạo dependencies), Quản lý Flow (Menu -> Lobby -> Match).
│   # Boundary Rule: Đứng ở tầng cao nhất, được phép gọi xuống ConnectionManagement và GameModes, nhưng Gameplay tuyệt đối không được tham chiếu ngược (Circular Dependency) lên Core bằng các hardcode.
│
├── Gameplay/
│   # Chứa các Domain Logic thuần túy liên quan đến lối chơi. Toàn bộ tính toán In-match nằm ở bộ phân tầng này.
│   │
│   ├── Character/
│   │   # Phạm vi: Dữ liệu mô tả thực thể người chơi và xử lý Input/Movement cơ bản. Định nghĩa "Nhân vật là gì" và "Làm thế nào để di chuyển".
│   │   # Boundary Rule: Nhận sát thương thông qua việc nhận Interface từ Combat, không tự tính toán công thức trừ điểm hay trừ máu trong file Movement.
│   │
│   ├── Combat/
│   │   # Phạm vi: Hệ thống tính toán va chạm vật lý, Hitbox, Damage và Knockback.
│   │   # Boundary Rule: Đóng vai trò làm "Trọng tài" xử lý tương tác giữa Character A và Character B (hoặc Weapon). Không lưu trữ thông số HP của nhân vật (HP nằm trên Character State), Core Combat chỉ tính toán và đẩy request thay đổi State vào Character.
│   │
│   ├── Weapons/
│   │   # Phạm vi: Logic cấp phát (Spawning), định nghĩa các loai vật phẩm có thể tương tác hoặc cầm nắm.
│   │   # Boundary Rule: Vũ khí mang các thông số sát thương và hành vi, chuyển giao dữ liệu này cho module Combat khi có va chạm xảy ra.
│   │
│   └── GameModes/
│       # Phạm vi: Quản lý điều kiện thắng/thua, đếm ngược thời gian trận đấu, tính điểm, cơ chế hồi sinh (Respawn).
│       # Boundary Rule: Đóng vai trò là "Rule Engine". Phản ứng với các Event từ Character (như OnPlayerDeath) để cộng điểm, thay vì để thuật toán của Character tự cộng điểm cho nhau.
│
├── UI/
│   # Thiết kế chuẩn Model-View-Presenter (MVP). Giao diện là lớp "View", phản ứng lại thay đổi State.
│   │
│   ├── Menus/
│   │   # Phạm vi: Quản lý giao diện Out-Game (Đăng nhập, Tùy chỉnh, Lobby chờ).
│   │
│   └── InGame/
│       # Phạm vi: Quản lý HUD (Máu, Radar, Bảng Điểm) hiển thị trong thời gian thực.
│       # Boundary Rule: Data-Binding trực tiếp vào NetworkVariables của Character hoặc GameMode. Nghiêm cấm đặt hàm xử lý logic game (như Shoot(), DropItem()) bên trong các script này.
│
├── Infrastructure/
│   # Phạm vi: Tầng dưới cùng (Low-level Base). Cung cấp cơ sở hạ tầng kiến trúc nội bộ.
│   # Chức năng chính: Custom Network Variables, Dependency Injection Setup, Database Schema (Nếu có), Object Pooling Framework.
│   # Boundary Rule: Hoàn toàn không biết tới sự tồn tại của Gameplay hay UI. Có thể được sử dụng trong mọi dự án khác.
│
└── Utils/
    # Phạm vi: Tập hợp các Helper Classes, Extension Methods và Math/Physics Utilities thuần túy.
    # Chức năng chính: Tính toán hướng tịnh tiến, xử lý mảng (arrays), Log formatter. Stateless (Không lưu trữ trạng thái vật lý game).
```

---

## 3. Quản Lý Mô Hình Giao Tiếp & Giải Quyết Overlap

Để đảm bảo các thư mục không bị giẫm chân lên nhau (Overlap Architecture), dự án sẽ áp dụng các bộ giao thức giải quyết luồng công việc sau:

### Tình Huống: Nhân vật A cầm vũ khí B đánh mục tiêu C
Đây là tình huống dễ gây rối loạn cấu trúc thư mục nhất. Các lớp mã nguồn phải được phân bổ như sau:

1. **Khởi Tạo Yêu Cầu (Client -> Server):**
   * Module `Gameplay/Character` đọc Input (Nút đánh).
   * Qua RPC, truyền tin hiệu thao tác cho Server.
2. **Logic Vũ Khí (Xác Định Tài Sản):**
   * Tại Server, module `Gameplay/Weapons` chịu trách nhiệm cung cấp thông số (Góc đánh, Sát thương cơ bản, Trạng thái đang kích hoạt Hitbox).
3. **Phân Giải Va Chạm (Resolution Layer):**
   * Module `Gameplay/Combat` nắm bắt khung va chạm (Collision/Trigger Physics) tại thời điểm thực thi.
   * Nó đối chiếu thông tin vũ khí (`Weapons`) với thông tin phòng thủ (`Character`).
   * Module `Combat` tính toán lượng sát thương cuối cùng sẽ bị trừ, và tính toán Vector Lực ném văng (Knockback Force).
4. **Cập Nhật Trạng Thái (State Mutator):**
   * Module `Combat` gọi phương thức trừ máu và AddForce ngược lại cho module `Gameplay/Character` của nạn nhân (C).
5. **Giám Sát Luật Chơi (Rule Observer):**
   * Khi nạn nhân C hết máu, nó phát ra một Event `OnDeath`.
   * Module `Gameplay/GameModes` (như DeatchmatchMode) lắng nghe event này, ghi nhận người chơi A có 1 điểm hạ gục (Kill). Hệ thống UI tự động cập nhật View.

**Tóm lại:** Với cách tiếp cận trên:
- Thư mục Combat không cần biết cách vận hành Vũ khí.
- Thư mục Character không cần biết cách tính điểm.
- Hạn chế tối đa sự dính chéo rườm rà (Spaghetti Code), chuẩn hóa quy trình Scale dự án về lâu dài.
