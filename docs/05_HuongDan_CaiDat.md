# HƯỚNG DẪN CÀI ĐẶT, CẤU HÌNH VÀ VẬN HÀNH KỸ THUẬT — BlueMeal v1.0

Tài liệu dành cho cán bộ kỹ thuật của chủ đầu tư. Mọi lệnh có bản PowerShell (`.ps1`, Windows) và shell (`.sh`, Linux).

## 1. Yêu cầu hệ thống

| Thành phần | Tối thiểu | Khuyến nghị (≥ 1.000 người dùng đồng thời) |
|---|---|---|
| CPU / RAM | 4 vCPU / 8 GB | 12–16 vCPU / 16 GB (ước tính từ đo chi phí từng request — `docs/test-reports/k6-load-test.md` mục 2; cấu hình mẫu `deploy/postgres/postgresql.conf` cho 16 GB RAM) |
| Ổ đĩa | 100 GB SSD | 200 GB SSD + ổ/NAS riêng cho bản sao lưu |
| Hệ điều hành | Linux x64 (Ubuntu 22.04/24.04, RHEL 9…) hoặc Windows Server 2022 / Windows 11 có Docker Desktop (WSL2) | Linux |
| Phần mềm | Docker Engine ≥ 24 và Docker Compose v2 | — |
| Mạng | Mở cổng 443 (và 80 để chuyển hướng) vào máy chủ; không cần Internet khi dùng gói offline | Tường lửa chỉ mở 80/443 |

Các container: `web` (nginx + ModSecurity WAF, cổng duy nhất mở ra ngoài), `api` (2 bản sao), `worker` (job nền Quartz), `postgres` 16, `redis` 7.2, `backup`.

## 2. Cài đặt mới

### 2.1 Lấy mã nguồn hoặc gói cài đặt

- Có Internet: sao chép thư mục mã nguồn `BlueMeal/` lên máy chủ.
- Không có Internet: dùng **gói offline** (mục 6).

### 2.2 Sinh cấu hình và khóa bí mật

```powershell
# Windows
.\scripts\gen-secrets.ps1
```
```bash
# Linux
./scripts/gen-secrets.sh
```

Script tạo `deploy/.env` từ `deploy/.env.example` với **mật khẩu và khóa ngẫu nhiên** (PostgreSQL, Redis, JWT, khóa mã hóa cột, khóa HMAC, passphrase sao lưu, mật khẩu admin ban đầu).
Sau đó mở `deploy/.env` và sửa:

| Biến | Ý nghĩa |
|---|---|
| `BLUEMEAL_PUBLIC_URL` | Địa chỉ truy cập (dùng trong email quên mật khẩu), ví dụ `https://suatan.congty.vn` |
| `SERVER_NAME` | Tên miền nginx (để `_` nếu truy cập bằng IP) |
| `SEED_SAMPLE_DATA` | `true` để có dữ liệu mẫu cho đào tạo; **`false` khi vận hành chính thức** |
| `BACKUP_PASSPHRASE` | Passphrase mã hóa bản sao lưu — **ghi lại và cất giữ tách biệt** (thiếu passphrase thì không khôi phục được) |
| `BACKUP_CRON`, `BACKUP_KEEP_*` | Lịch và số bản giữ lại (mặc định 01:00 hằng ngày, 7 ngày / 4 tuần / 6 tháng) |
| `BLUEMEAL_ADMIN_PASSWORD` | Mật khẩu ban đầu tài khoản `admin` — sinh ngẫu nhiên và in ra màn hình khi chạy `gen-secrets` (phải đổi và bật MFA ở lần đăng nhập đầu) |

> `deploy/.env` chứa bí mật: chỉ quản trị viên được đọc (`chmod 600 deploy/.env` trên Linux). Không đưa file này vào kho mã nguồn.

### 2.3 Chứng chỉ TLS

Đặt chứng chỉ và khóa riêng vào `deploy/nginx/ssl/server.crt` và `deploy/nginx/ssl/server.key` (PEM; chứng chỉ có kèm chuỗi CA trung gian).
Nếu chưa có, tạo tạm chứng chỉ tự ký:

```bash
mkdir -p deploy/nginx/ssl
openssl req -x509 -nodes -newkey rsa:2048 -days 365 -subj "/CN=suatan.congty.vn" \
  -keyout deploy/nginx/ssl/server.key -out deploy/nginx/ssl/server.crt
```

nginx chỉ chấp nhận TLS 1.2/1.3 và tự chuyển hướng HTTP → HTTPS ở cấu hình production; HSTS bật trên HTTPS.

### 2.4 Khởi động (production)

```bash
docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d --build
docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml ps
```

Lần đầu khởi động, `api` tự **tạo/cập nhật cơ sở dữ liệu** (migration chạy bằng tài khoản owner, dưới khóa advisory để nhiều bản sao không chạy chồng) và nạp dữ liệu nền
(quyền, vai trò, đơn vị đo, chất dinh dưỡng, nhóm thực phẩm, báo cáo…). Kiểm tra sẵn sàng: `https://<máy chủ>/health/ready` trả `Healthy`.

### 2.5 Đăng nhập lần đầu

1. Mở `https://<máy chủ>`, đăng nhập `admin` với `BLUEMEAL_ADMIN_PASSWORD`.
2. Hệ thống buộc **đổi mật khẩu** và **bật MFA**: quét mã QR bằng Google Authenticator / Microsoft Authenticator, nhập mã 6 số, **lưu 10 mã khôi phục**.
3. Vào *Quản trị → Thông tin đơn vị* nhập tên, địa chỉ, logo; *Cấu hình hệ thống → Email (SMTP)* để bật email quên mật khẩu và thông báo.
4. Tạo nhóm quyền/tài khoản cho người dùng (`06_HDSD_QuanTri.md`).

## 3. Cấu hình

| Nơi cấu hình | Nội dung |
|---|---|
| `deploy/.env` | Bí mật, tên miền, lịch sao lưu, kích thước pool kết nối (`DB_POOL_SIZE`) |
| Giao diện *Quản trị → Cấu hình hệ thống* | Chính sách mật khẩu, phiên (thời gian chờ 30 phút, số phiên đồng thời), MFA bắt buộc theo vai trò, ngưỡng cảnh báo dinh dưỡng, phương pháp giá kho (bình quân / FIFO), cho phép xuất âm, chi phí chung, SMTP, thời gian lưu mẫu (≥ 24 giờ), trọng số tạo thực đơn, số cấp duyệt (1/2), thời gian lưu nhật ký (≥ 12 tháng), Swagger |
| *Quản trị → Tích hợp Viện Dinh dưỡng* | Địa chỉ API, kiểu xác thực, credential, lịch đồng bộ (mặc định 02:00), mapping JSON; client cho API công khai |
| `deploy/postgres/postgresql.conf` | Tuning PostgreSQL cho 8 vCPU/16 GB — chỉnh `shared_buffers` (~25 % RAM), `effective_cache_size` (~75 % RAM) theo máy thực tế |
| `deploy/nginx/*.conf` | Header bảo mật, giới hạn request (`BM_EDGE_RATE`), kích thước upload (25 MB) |
| `deploy/nginx/modsecurity/bluemeal-before-crs.conf` | Loại trừ false positive của WAF (chỉ thêm khi đã xác minh) |

Lịch các job nền (múi giờ Asia/Ho_Chi_Minh): cảnh báo hằng ngày 06:00, đồng bộ VDD 02:00, dọn dẹp 03:30, sao lưu 01:00 (container `backup`).
Có thể đổi lịch job ứng dụng bằng biến môi trường `Jobs__Cron__daily-alerts`, `Jobs__Cron__vdd-sync`, `Jobs__Cron__housekeeping` (cú pháp cron 5 trường).

## 4. Nâng cấp phiên bản

1. **Sao lưu bất thường** trước khi nâng cấp: *Quản trị → Sao lưu → Sao lưu ngay* (hoặc `docker compose ... exec backup su-exec postgres sh -c '. /tmp/backup.env && backup.sh Manual truoc-nang-cap'`).
2. Chép mã nguồn/gói mới, giữ nguyên `deploy/.env` và `deploy/nginx/ssl/`.
3. `docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d --build` (hoặc `load-images` rồi `up -d` với gói offline).
4. Migration chạy tự động khi `api` khởi động; theo dõi `docker compose ... logs -f api`.
5. Kiểm tra *Quản trị → Phiên bản hệ thống* hiển thị phiên bản mới và changelog.

Nâng cấp chỉ thêm cột/bảng theo chiều tiến; nếu cần quay lại phiên bản cũ thì khôi phục bản sao lưu ở bước 1.

## 5. Sao lưu và khôi phục

### 5.1 Sao lưu

- Tự động: 01:00 hằng ngày — `pg_dump -Fc` + thư mục file đính kèm → mã hóa AES-256 (openssl, PBKDF2 200.000 vòng) → `.tar.enc` + `.sha256` trong volume `backups`.
- Giữ 7 bản ngày, 4 bản tuần, 6 bản tháng. Đồng bộ ra NAS: mount thư mục NAS vào container `backup` (file override) và đặt `BACKUP_MIRROR_PATH`.
- Bất thường: nút *Sao lưu ngay* (quyền `system.backup`).
- Tải bản sao lưu: *Quản trị → Sao lưu → Tải về* — phải nhập lại mật khẩu và mã MFA.

### 5.2 Khôi phục

```powershell
# Windows — khôi phục vào CSDL đang chạy (ghi đè), kèm file đính kèm
.\deploy\backup\restore.ps1 -Archive bluemeal-20261109-010000-scheduled.tar.enc -WithFiles
```
```bash
# Linux
./deploy/backup/restore-host.sh bluemeal-20261109-010000-scheduled.tar.enc bluemeal --with-files
```

Script kiểm tra SHA-256, giải mã bằng `BACKUP_PASSPHRASE`, rồi `pg_restore --clean`. Khuyến nghị dừng `api` và `worker` trước khi khôi phục vào CSDL chính
(`docker compose ... stop api worker`), khởi động lại sau khi xong.

### 5.3 Diễn tập khôi phục (khuyến nghị hằng quý)

```bash
./scripts/test-backup-restore.sh --prod        # Windows: .\scripts\test-backup-restore.ps1 -Prod
```

Script tạo bản sao lưu, khôi phục vào CSDL tạm `bluemeal_restore_test`, so số bản ghi 12 bảng chính, xóa CSDL tạm và ghi biên bản `docs/test-reports/restore-drill.txt`.

## 6. Cài đặt không có Internet (gói offline)

Trên máy có Internet:

```bash
./scripts/save-images.sh 1.0.0 offline        # Windows: .\scripts\save-images.ps1 -Version 1.0.0 -Output offline
```

Kết quả thư mục `offline/` gồm `bluemeal-images-1.0.0.tar` (+ `.sha256`), thư mục `deploy/` (không có `.env`) và `scripts/`. Chép sang máy chủ rồi:

```bash
cd offline
./scripts/load-images.sh                       # kiểm tra SHA-256 và docker load
./scripts/gen-secrets.sh
docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d
```

## 7. Nhập dữ liệu ban đầu

Mẫu Excel cho từng danh mục tải tại màn hình danh mục (nút *Tải file mẫu*) hoặc thư mục `tools/import-templates/`. Thứ tự khuyến nghị:
đơn vị đo → kho → nhóm thực phẩm → thực phẩm (hoặc đồng bộ từ Viện Dinh dưỡng) → nguyên liệu → đối tác → khách hàng → đối tượng → khuyến nghị → món ăn → công thức.
Mỗi lần nhập có bước xem trước lỗi theo dòng trước khi ghi.

## 8. Giám sát và nhật ký

| Nguồn | Vị trí |
|---|---|
| Sức khỏe | `GET /health/live`, `GET /health/ready` (CSDL, Redis) |
| Log ứng dụng (JSON, xoay vòng theo ngày, 90 file) | volume `logs` (`/app/logs` trong `api`/`worker`); chuyển sang syslog/SIEM bằng bộ chuyển tiếp log đọc thư mục này |
| Log container | `docker compose ... logs <service>` (giới hạn 50 MB × 10 file) |
| Nhật ký nghiệp vụ / ATTT | *Quản trị → Nhật ký hệ thống*, báo cáo RPT-16 |
| Lịch sử job | *Quản trị → Sao lưu*, *Báo cáo → File đã xuất*, *Tích hợp → Lịch sử đồng bộ* |

## 9. Xử lý sự cố thường gặp

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| Trang báo 502/503 sau khi khởi động | `api` chưa sẵn sàng (đang migration) | Chờ 1–2 phút; xem `logs api`; kiểm tra `/health/ready` |
| Không đăng nhập được, báo khóa tài khoản | Sai mật khẩu 5 lần | Chờ 15 phút hoặc quản trị mở khóa (*Tài khoản người dùng*) |
| Mất thiết bị MFA | — | Dùng mã khôi phục; hoặc quản trị *đặt lại MFA* cho tài khoản |
| Không nhận email quên mật khẩu | SMTP chưa cấu hình/sai | *Cấu hình hệ thống → Email (SMTP) → Gửi thử*; tạm thời quản trị đặt lại mật khẩu |
| Request bị chặn 403 dù có quyền (nội dung đặc biệt) | WAF ModSecurity nhận nhầm | Xem `logs web` (ModSecurity rule id); bổ sung loại trừ hẹp vào `bluemeal-before-crs.conf`, build lại `web` |
| Lỗi 409 khi lưu | Người khác vừa sửa cùng bản ghi | Tải lại và nhập lại thay đổi |
| Sao lưu bất thường mãi "Đang chạy" | Container `backup` dừng hoặc thiếu `BACKUP_PASSPHRASE` | `logs backup`; khởi động lại `backup`; job tự báo lỗi sau thời gian chờ |
| Đồng bộ VDD lỗi | Sai credential/URL, nguồn tạm ngừng | *Tích hợp → Kiểm tra kết nối*, xem *Lịch sử đồng bộ* (chi tiết từng request) |
| Đầy ổ đĩa | Bản sao lưu/log | Kiểm tra volume `backups`, `logs`; giảm `BACKUP_KEEP_*`, chuyển bản cũ sang NAS |

## 10. Gỡ cài đặt

`docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml down` (giữ dữ liệu).
Thêm `-v` sẽ **xóa toàn bộ dữ liệu** (CSDL, file, bản sao lưu) — chỉ làm khi đã chuyển bản sao lưu ra ngoài.

## 11. Triển khai sau reverse proxy dùng chung và CI/CD (máy chủ BlueStar)

Bản demo `https://bluemeal.bluestar.com.vn` chạy trên VM dùng chung `14.225.83.93`. Cổng 80/443 ở đó thuộc Caddy chung (`~/proxy`), nên BlueMeal dùng thêm overlay `deploy/docker-compose.proxy.yml`:

- `web` không mở cổng ra máy chủ. Caddy tham gia mạng `bluemeal_frontend`, chuyển tiếp tới `bluemeal-web-1:8080`, cấp chứng chỉ Let's Encrypt và thêm HSTS (`~/proxy/sites/bluemeal.caddy`).
- nginx chỉ tin `X-Forwarded-For` / `X-Forwarded-Proto` khi yêu cầu đến từ `TRUSTED_PROXY_CIDR` (mặc định `172.16.0.0/12`), nhờ vậy giới hạn tần suất tính theo IP thật và API thấy `https`.
- PostgreSQL và API được giảm tài nguyên cho máy nhỏ (biến `PG_*`, `API_REPLICAS`, `API_CPUS`, `API_MEMORY` trong `deploy/.env`).
- Lệnh tắt trên VM: `~/BlueMeal/bm.sh <lệnh compose>` (base + prod + proxy), ví dụ `./bm.sh ps`, `./bm.sh logs -f api`.

**CI/CD** (`.github/workflows/ci-cd.yml`):

- Mọi push và PR đều chạy CI trên runner của GitHub: build backend, unit test, integration test (Testcontainers), quét lỗ hổng NuGet; ở frontend kiểm tra i18n, lint, Vitest, build và `npm audit`.
- Push lên `main` mà CI đạt thì chạy job `deploy` (environment `production`): runner SSH vào VM bằng khóa riêng. Trong `~/.ssh/authorized_keys`, khóa này bị khóa vào `restrict,command="/home/hung/apps/bluemeal-deploy.sh"`, nên chỉ chạy được script triển khai với tham số là SHA. Script lấy commit → build → sao lưu trước triển khai (`predeploy-<sha>`) → migration → `up --wait` → kiểm tra `/health/ready`.
- `~/apps/bluemeal-deploy.sh` là **bản sao** của `deploy/vm/bluemeal-deploy.sh`. Khi sửa script trong repo, phải chép lại lên VM bằng tay. Nhờ vậy một commit không thể đổi lệnh mà khóa triển khai được phép chạy.
- Secret của repository: `SSH_HOST`, `SSH_USER`, `SSH_PRIVATE_KEY`, `SSH_KNOWN_HOSTS` (lấy bằng `ssh-keyscan -t ed25519 <host>`).
- Repo công khai nên **không dùng self-hosted runner** (vì PR từ fork có thể chạy mã trên VM).
- Triển khai tay hoặc quay về một commit cũ: `~/apps/bluemeal-deploy.sh <sha>`. Commit đang chạy được ghi ở `~/apps/bluemeal.REVISION`.
