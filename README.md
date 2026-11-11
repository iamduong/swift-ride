# SwiftRide

SwiftRide là ứng dụng đặt xe máy được xây dựng theo kiến trúc microservices. Hệ thống hỗ trợ đăng ký và đăng nhập, tạo chuyến, tính tuyến đường và giá, ghép tài xế gần nhất, theo dõi vòng đời chuyến đi và thanh toán bằng mã QR.

## Kiến trúc hệ thống

```text
Next.js Client :3000
        |
        v
API Gateway :8000 (YARP)
        |
        +-- /ts --> Trip Service :5555 ------------> SQL Server
        +-- /mp --> Matching & Pricing :6666 ------> MongoDB
        +-- /pm --> Payment Service :7777 ---------> PostgreSQL
                          |
                     RabbitMQ
```

### Thành phần

- **Trip Service**: quản lý tài khoản, hồ sơ tài xế và vòng đời chuyến đi theo state machine; tạo và xác nhận chuyến, điều phối tài xế, xử lý chấp nhận, từ chối, đón khách, trả khách, hủy chuyến và cập nhật trạng thái thanh toán. Service dùng SQL Server, JWT và SSE.
- **Matching & Pricing Service**: quản lý vị trí và trạng thái sẵn sàng của tài xế; tìm tài xế khả dụng gần điểm đón, gọi OSRM để tính tuyến đường, khoảng cách và thời gian, sau đó tính giá theo quãng đường, thời gian và điều kiện thời tiết. Service dùng MongoDB.
- **Payment Service**: tạo bản ghi thanh toán và mã QR sau khi chuyến hoàn thành; tiếp nhận IPN từ SePay, xác nhận giao dịch và phát sự kiện thanh toán thành công. Service dùng PostgreSQL.
- **API Gateway**: cung cấp một địa chỉ API thống nhất và định tuyến request đến các service bằng YARP Reverse Proxy.
- **RabbitMQ**: truyền sự kiện bất đồng bộ giữa các service, ví dụ tài xế online, nhận chuyến, hoàn thành chuyến, tạo payment và xác nhận thanh toán.
- **Client**: ứng dụng Next.js cho hành khách và tài xế; sử dụng MapLibre để hiển thị bản đồ và SSE để nhận đề nghị chuyến theo thời gian thực.

## Công nghệ sử dụng

- .NET 10, ASP.NET Core và Entity Framework Core
- Next.js 16, React 19 và TypeScript
- YARP Reverse Proxy
- MassTransit và RabbitMQ
- SQL Server, MongoDB và PostgreSQL
- OSRM và MapLibre GL
- Docker và Docker Compose

## Luồng nghiệp vụ chính

```text
Đăng nhập
  -> Chọn điểm đón và điểm đến
  -> Tạo chuyến và lấy báo giá
  -> Xác nhận đặt xe
  -> Tìm và gửi đề nghị cho tài xế
  -> Tài xế chấp nhận
  -> Đón khách
  -> Trả khách
  -> Tạo mã QR thanh toán
  -> Xác nhận thanh toán
  -> Hoàn thành chuyến
```

Trạng thái chuyến đi:

```text
Requested
  -> DriverAccepted
  -> EnRoute
  -> PickedUp
  -> DroppedOff
  -> PaymentPending
  -> Paid
```

Chuyến cũng có thể chuyển sang `Cancelled` hoặc `Failed` khi bị hủy hoặc không tìm được tài xế.

## Cấu trúc thư mục

```text
swift-ride/
|-- client/                         # Next.js frontend
|-- src/
|   |-- ApiGateway/                 # YARP API Gateway
|   |-- Trip_Service/               # Tài khoản và chuyến đi
|   |-- MatchingPricing_Service/    # Ghép tài xế và tính giá
|   `-- Payment_Service/            # Thanh toán
|-- docker-compose.yaml
|-- Dockerfile.backend
|-- .env.example
`-- API.md                          # Danh sách API chi tiết
```

## Yêu cầu môi trường

Cách chạy được khuyến nghị chỉ yêu cầu:

- Git
- Docker Desktop với Docker Compose v2

Nếu chạy source trực tiếp để phát triển, máy cần thêm .NET 10 SDK và Node.js 22.

## Chạy toàn bộ hệ thống bằng Docker

### 1. Tạo file môi trường

Tại thư mục gốc dự án:

```powershell
Copy-Item .env.example .env
```

Các giá trị trong `.env.example` dùng được cho môi trường local. Không sử dụng các mật khẩu mẫu khi triển khai production.

### 2. Build và khởi động

```powershell
docker compose up --build -d
```

### 3. Kiểm tra trạng thái

```powershell
docker compose ps
```

Theo dõi log:

```powershell
docker compose logs -f
```

### 4. Truy cập ứng dụng

| Thành phần | Địa chỉ |
|---|---|
| Web client | <http://localhost:3000> |
| API Gateway | <http://localhost:8000> |
| RabbitMQ Management | <http://localhost:15672> |
| SQL Server | `localhost:1433` |
| MongoDB | `localhost:27017` |
| PostgreSQL | `localhost:5433` mặc định |

Tài khoản RabbitMQ local được khai báo bằng `RABBITMQ_USERNAME` và `RABBITMQ_PASSWORD` trong file `.env`.

## Định tuyến API Gateway

Frontend chỉ cần gọi `http://localhost:8000`:

| Tiền tố | Service đích |
|---|---|
| `/ts/*` | Trip Service |
| `/mp/*` | Matching & Pricing Service |
| `/pm/*` | Payment Service |

Ví dụ:

```http
POST http://localhost:8000/ts/auth/login
POST http://localhost:8000/ts/trips
POST http://localhost:8000/mp/quote
GET  http://localhost:8000/pm/payment/{uuid}
```

Danh sách endpoint đầy đủ nằm trong [API.md](API.md).

## Chạy ở chế độ phát triển

Có thể chạy database và RabbitMQ bằng Docker, còn ứng dụng chạy trực tiếp để dễ debug:

```powershell
docker compose up -d sqlserver mongodb postgres rabbitmq
```

Sau khi thiết lập đầy đủ connection string và RabbitMQ credentials cho môi trường Development, chạy từng backend trong terminal riêng:

```powershell
dotnet run --project src/Trip_Service/TripService.csproj
dotnet run --project src/MatchingPricing_Service/MatchingPricingService.csproj
dotnet run --project src/Payment_Service/Payment_Service.csproj
dotnet run --project src/ApiGateway/ApiGateway.csproj
```

Chạy frontend:

```powershell
Set-Location client
npm.cmd install
npm.cmd run dev
```

Trên Windows, dùng `npm.cmd` nếu PowerShell chặn thực thi `npm.ps1`.

## Các lệnh Docker thường dùng

Dừng nhưng giữ container:

```powershell
docker compose stop
```

Chạy lại container đã dừng:

```powershell
docker compose start
```

Dừng và xóa container cùng network:

```powershell
docker compose down
```

Build lại sau khi cập nhật source code:

```powershell
docker compose up --build -d
```

Xem log của một service:

```powershell
docker compose logs -f trip-service
docker compose logs -f matching-pricing-service
docker compose logs -f payment-service
docker compose logs -f api-gateway
docker compose logs -f client
```

Xóa cả container và dữ liệu database:

```powershell
docker compose down -v
```

> Cảnh báo: tùy chọn `-v` xóa các Docker volume chứa dữ liệu SQL Server, MongoDB, PostgreSQL và RabbitMQ.

## Giao tiếp giữa các service

Hệ thống sử dụng hai cơ chế:

- **HTTP đồng bộ** cho nghiệp vụ cần kết quả ngay, chẳng hạn Trip Service yêu cầu Matching & Pricing Service tính báo giá hoặc tìm tài xế.
- **RabbitMQ bất đồng bộ** cho sự kiện liên service, chẳng hạn `DriverOnline`, `TripAccepted`, `TripDroppedOff`, `RideCompleted`, `PaymentRequested` và `PaymentSettled`.

Đề nghị chuyến được đẩy từ Trip Service xuống trình duyệt tài xế bằng **Server-Sent Events**, không đi qua RabbitMQ.

## Dữ liệu và cấu hình

| Service | Database | Dữ liệu chính |
|---|---|---|
| Trip Service | SQL Server | Account, DriverProfile, Trip, TripDriverRejection |
| Matching & Pricing | MongoDB | Vị trí, trạng thái online và availability của tài xế |
| Payment Service | PostgreSQL | Payment, mã chuyển khoản, QR và trạng thái giao dịch |

Các secret và mật khẩu được đọc từ `.env`. File `.env` đã bị Git bỏ qua; chỉ `.env.example` được commit làm mẫu.

## Lưu ý

- OSRM public API cần kết nối Internet để tính tuyến đường, khoảng cách và thời gian.
- Image Docker là bản build tại một thời điểm. Sau khi `git pull` hoặc sửa source, cần chạy lại với `--build`.
- Các cấu hình và mật khẩu mẫu chỉ dành cho môi trường phát triển local.
