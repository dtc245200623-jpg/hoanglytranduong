# HỆ THỐNG THƯ VIỆN VÀ ĐẶT MƯỢN SÁCH PHÂN TÁN (ĐỀ TÀI 20)
> **Học phần:** Phát triển ứng dụng phân tán (PTUDPT)  
> **Nền tảng & Công nghệ:** C# .NET 10 | ASP.NET Core Minimal API | RabbitMQ | PostgreSQL | EF Core | Frontend Web SPA (Nginx) | Docker & Docker Compose

---

## 1. Giới thiệu & Yêu cầu đề tài

Hệ thống thư viện phân tán cho phép người dùng tra cứu tài liệu sách, kiểm tra số lượng bản sao khả dụng thời gian thực, gửi yêu cầu đặt mượn sách và theo dõi toàn bộ trạng thái xử lý theo mô hình **bất đồng bộ (Asynchronous Event-Driven)**.

### Sơ đồ Kiến trúc Hệ thống

```
+-------------------------------------------------------------+
|                      CLIENT / FRONTEND                      |
|           (Web App SPA độc lập: HTML5, CSS3, JS)            |
+------------------------------+------------------------------+
                               | (HTTP REST API / CORS)
                               v
+-------------------------------------------------------------+
|                     LIBRARY BACKEND API                     |
|           (ASP.NET Core Minimal API - .NET 10)              |
+---------------+-----------------------------+---------------+
                |                             |
  (Tra cứu/Ghi  |                             | (Đẩy tin nhắn đặt mượn:
   Pending)     |                             |  Exchange 'library.events')
                v                             v
+-------------------------------+   +-------------------------+
|     POSTGRESQL DATABASE       |   |     RABBITMQ BROKER     |
|         (Port 5432)           |   | Queue:                  |
| - Books & BookCopies          |   | 'reservation.requests'  |
| - Users & Reservations        |   | DLQ: 'reservation.dlq'  |
+---------------+---------------+   +------------+------------+
                ^                                |
                | (Kiểm tra bản khả dụng         | (Lắng nghe & tiêu thụ
                |  & Cập nhật Reserved/Rejected) |  Prefetch = 1)
                +--------------------------------+
                               |
                +--------------+---------------+
                |      RESERVATION WORKER      |
                |  (.NET 10 BackgroundService) |
                +------------------------------+
```

---

## 2. Cấu trúc Dự án (BE và FE Tách Riêng Biệt)

```text
quản lý thư viện/
├── backend/                                # [BACKEND] Mã nguồn C# .NET 10
│   ├── LibraryManagement.slnx             # Solution .NET 10
│   ├── Dockerfile.api                     # Dockerfile riêng cho API
│   ├── Dockerfile.worker                  # Dockerfile riêng cho Worker
│   └── src/
│       ├── LibraryManagement.Shared/      # Enums, Contracts (RabbitMQ message), DTOs
│       ├── LibraryManagement.Infrastructure/ # EF Core DbContext, Entities, Seeder, RabbitMQ Producer & Connection Manager
│       ├── LibraryManagement.API/         # Minimal API .NET 10, OpenAPI, Health Check, CORS
│       └── LibraryManagement.Worker/      # Reservation Worker Service (Lắng nghe RabbitMQ, Idempotent, Retry & DLQ)
│
├── frontend/                               # [FRONTEND] Ứng dụng Web độc lập
│   ├── index.html                         # Giao diện Dashboard tra cứu & đặt mượn
│   ├── app.js                             # Xử lý logic gọi API, polling realtime
│   ├── style.css                          # Giao diện hiện đại (Google Inter, Badges, Animations)
│   ├── nginx.conf                         # Web Server Nginx cấu hình Reverse Proxy & CORS
│   └── Dockerfile                         # Dockerfile riêng cho Frontend (Nginx Alpine)
│
├── docker-compose.yml                     # Khởi chạy toàn bộ hệ thống (DB, RabbitMQ, API, Worker, FE)
├── .dockerignore
├── .gitignore
└── README.md
```

---

## 3. Các Chức Năng Chính & API Đã Triển Khai

| STT | Endpoint | Phương thức | Mô tả chức năng |
| :---: | :--- | :---: | :--- |
| **1** | `/books` | `GET` | Tra cứu danh sách sách kèm số bản khả dụng và bộ lọc tìm kiếm |
| **2** | `/books/{id}` | `GET` | Chi tiết cuốn sách và thông tin từng bản sao (Barcode, trạng thái) |
| **3** | `/reservations` | `POST` | Gửi yêu cầu đặt mượn sách &rarr; Lưu trạng thái `Pending` &rarr; Đẩy vào RabbitMQ |
| **4** | `/reservations/{id}` | `GET` | Tra cứu trạng thái yêu cầu đặt mượn |
| **5** | `/users/{userId}/reservations` | `GET` | Tra cứu danh sách sách đã đặt/đang mượn của một người dùng |
| **6** | `/reservations` | `GET` | Lấy toàn bộ đơn đặt (Dành cho thủ thư) |
| **7** | `/reservations/{id}/borrow` | `POST` | Thủ thư giao sách: chuyển từ `Reserved` &rarr; `Borrowed` |
| **8** | `/reservations/{id}/return` | `POST` | Độc giả trả sách: chuyển `Borrowed` &rarr; `Returned`, tự động hoàn lại số bản khả dụng trong kho |
| **9** | `/users` | `GET` | Lấy danh sách bạn đọc trong thư viện |
| **10** | `/health` | `GET` | Kiểm tra trạng thái sức khỏe của API, PostgreSQL và RabbitMQ Broker |

---

## 4. Các Yêu Cầu Kỹ Thuật Nâng Cao (Điểm Cộng BTL)

1. **Xử lý Bất Đồng Bộ Qua RabbitMQ**:
   - Tách rời hoàn toàn tải của API và tác vụ nghiệp vụ kiểm tra kho sách.
   - Message được serialize dạng JSON, gắn kèm `CorrelationId` để theo dõi luồng phân tán.
2. **Idempotent Consumer (Chống Xử Lý Lặp)**:
   - Worker kiểm tra trạng thái đơn đặt mượn trong Database trước khi xử lý. Nếu đơn đã ở trạng thái khác `Pending`, Worker sẽ bỏ qua mà không gây trừ trùng số bản sách.
3. **Cơ chế Retry và Dead-Letter Queue (DLQ)**:
   - Thiết lập Dead-Letter Exchange `library.dlx` và Dead-Letter Queue `reservation.dlq`.
   - Nếu gặp lỗi kết nối tạm thời, Worker thử lại tối đa 3 lần. Sau 3 lần thất bại, message tự động được định tuyến vào DLQ để kiểm toán.
4. **Xử lý Tranh Chấp Concurrency (Tránh Overselling)**:
   - Khi có nhiều độc giả gửi yêu cầu mượn cùng lúc cho một cuốn sách chỉ còn 1 bản, Database Transaction và hàng đợi RabbitMQ đảm bảo duy nhất 1 yêu cầu được `Reserved`, yêu cầu còn lại sẽ chuyển sang `Rejected` kèm thông báo rõ ràng.
5. **Cơ chế Phục Hồi (Resilience)**:
   - Tự động kết nối lại (Automatic Recovery) với RabbitMQ và Database khi broker/database bị restart.
6. **Logging Có Cấu Trúc (Structured Logging)**:
   - Ghi nhận chi tiết từng bước: `[API] Published` &rarr; `[Worker] Received` &rarr; `[Worker - SUCCESS / REJECTED]`.

---

## 5. Hướng Dẫn Cài Đặt & Khởi Chạy

### Cách 1: Khởi chạy toàn bộ hệ thống bằng Docker Compose (Khuyên dùng khi chấm thi)

Chỉ cần một lệnh duy nhất:

```bash
docker compose up --build -d
```

Hệ thống sẽ tự động khởi động 5 containers:
- **Frontend Web UI**: `http://localhost:3000`
- **Backend Minimal API**: `http://localhost:5000` (OpenAPI: `http://localhost:5000/openapi/v1.json`)
- **RabbitMQ Management Dashboard**: `http://localhost:15672` (Tài khoản: `guest` / Mật khẩu: `guest`)
- **PostgreSQL Database**: `localhost:5432` (Database: `library_db`, User: `postgres`, Pass: `postgres`)
- **Reservation Worker Service**: Chạy nền tự động tiêu thụ queue.

Dừng hệ thống:
```bash
docker compose down
```

---

### Cách 2: Chạy Local Từng Phần (Không cần Docker)

#### Bước 1: Khởi chạy PostgreSQL và RabbitMQ
Có thể chạy nhanh qua docker:
```bash
docker run -d --name postgres -p 5432:5432 -e POSTGRES_DB=library_db -e POSTGRES_PASSWORD=postgres postgres:16-alpine
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management-alpine
```

#### Bước 2: Chạy Backend Library API (.NET 10)
```bash
cd backend/src/LibraryManagement.API
dotnet run
```
API sẽ lắng nghe tại `http://localhost:5000` (hoặc cổng cấu hình). Cơ sở dữ liệu mẫu gồm 6 đầu sách và 5 bạn đọc sẽ được tự động seed vào database.

#### Bước 3: Chạy Backend Reservation Worker (.NET 10)
Mở một cửa sổ Terminal mới:
```bash
cd backend/src/LibraryManagement.Worker
dotnet run
```

#### Bước 4: Chạy Frontend Web App
Vì Frontend là Web SPA thuần (HTML/CSS/JS) độc lập và API đã cấu hình CORS đầy đủ:
- Chỉ cần click đúp mở trực tiếp file `frontend/index.html` trên bất kỳ trình duyệt nào (Chrome, Edge, Firefox)!
- Hoặc phục vụ qua bất kỳ static web server nào (Live Server, Nginx, v.v.).

---

## 6. Kịch Bản Minh Họa Demo Khi Báo Cáo

1. **Tra cứu sách & số bản khả dụng**:
   - Mở giao diện `http://localhost:3000`.
   - Xem danh sách sách: mỗi cuốn hiển thị rõ tổng số bản và số bản còn khả dụng.
   - Bấm "Chi tiết" để xem danh sách từng mã bản sao cụ thể (`BC-0001-01`, `BC-0001-02`...).
2. **Quy trình Đặt Mượn Bất Đồng Bộ**:
   - Chọn độc giả (ví dụ: *Nguyễn Văn An*).
   - Chọn một cuốn sách đang còn bản khả dụng &rarr; Bấm **"Đặt mượn"**.
   - Quan sát thông báo: Đơn hàng tạo ở trạng thái `Pending`, được đẩy vào RabbitMQ.
   - Mở tab **"Hàng Đợi Đặt Mượn (RabbitMQ)"**: Quan sát sau 1-2 giây Worker tự động xử lý và cập nhật trạng thái đơn thành `Reserved` kèm mã bản sao đã gán.
   - Kiểm tra số bản khả dụng của sách đã tự động giảm đi 1.
3. **Quy trình Thủ Thư Giao Sách & Trả Sách**:
   - Chuyển sang tab **"Nghiệp Vụ Thủ Thư"**:
   - Thấy đơn đang ở cột *Chờ độc giả nhận sách (Reserved)* &rarr; Bấm **"Xác nhận giao sách"** &rarr; Chuyển thành `Borrowed`.
   - Khi độc giả mang trả sách &rarr; Bấm **"Xác nhận trả sách"** &rarr; Chuyển thành `Returned`. Số bản khả dụng trong kho lập tức được tăng lại 1 bản.
4. **Thử Nghiệm Tranh Chấp Tài Nguyên Phân Tán (Concurrency Demo)**:
   - Vào tab **"Demo Tranh Chấp Sách"**.
   - Chọn cuốn sách chỉ còn 1 bản (cuốn *"C# 12 and .NET 8 / 10 Modern Cross-Platform"*).
   - Bấm nút **"Bấm để gửi 2 Request Song Song Cùng Lúc!"**.
   - Cả 2 sinh viên gửi yêu cầu đồng thời.
   - Kết quả: **Chính xác 1 sinh viên nhận trạng thái `Reserved`** và **1 sinh viên nhận trạng thái `Rejected`** (với lý do: "Hiện tại tất cả các bản sao đã được giữ trước"). Không bao giờ xảy ra lỗi âm số lượng sách.

