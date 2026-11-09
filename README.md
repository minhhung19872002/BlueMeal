# BlueMeal — Phần mềm bữa ăn ca thông minh

Phần mềm web quản lý bữa ăn ca, cân đối dinh dưỡng cho người lao động. Xây dựng cho **Viện Dinh dưỡng – Bộ Y tế**, gói thầu *"Xây dựng phần mềm bữa ăn ca thông minh"* (IB2600574672).

- Đặc tả: [`SPEC.md`](SPEC.md) · Giả định: [`docs/ASSUMPTIONS.md`](docs/ASSUMPTIONS.md) · Nhật ký thay đổi: [`docs/CHANGELOG.md`](docs/CHANGELOG.md)
- Thiết kế hệ thống: [`docs/02_ThietKe_HeThong.md`](docs/02_ThietKe_HeThong.md) · Giấy phép bên thứ ba: [`docs/THIRD_PARTY_LICENSES.md`](docs/THIRD_PARTY_LICENSES.md)
- Hướng dẫn cài đặt đầy đủ: [`docs/05_HuongDan_CaiDat.md`](docs/05_HuongDan_CaiDat.md)

## Công nghệ

| Lớp | Thành phần |
|---|---|
| Backend | .NET 8, ASP.NET Core Web API, EF Core 8 + Npgsql, ASP.NET Core Identity + JWT, Quartz.NET, ClosedXML, Serilog |
| Frontend | React 18 + TypeScript + Vite, Ant Design 5, React Router, TanStack Query, Zustand, react-i18next, Recharts |
| Dữ liệu – hạ tầng | PostgreSQL 16, Redis 7.2, nginx + ModSecurity (OWASP CRS), Docker Compose |

## Chạy nhanh (môi trường phát triển)

Yêu cầu: **Docker Desktop** (Windows/macOS) hoặc Docker Engine + Compose v2 (Linux). Không cần cài .NET/Node nếu chỉ chạy bằng Docker.

**Windows (PowerShell):**

```powershell
.\scripts\dev-up.ps1
```

**Linux/macOS:**

```sh
./scripts/dev-up.sh
```

Script sẽ tự sinh `deploy/.env` với secret ngẫu nhiên, build image, khởi động toàn bộ dịch vụ, chạy migration + seed và in ra:

| Dịch vụ | Địa chỉ |
|---|---|
| Ứng dụng web (qua WAF) | http://localhost:8080 (https://localhost:8443 — chứng chỉ tự ký) |
| API + Swagger | http://localhost:5080/swagger |
| Mailpit (hộp thư thử nghiệm) | http://localhost:8025 |
| Mock cổng Viện Dinh dưỡng (`tools/mock-vdd-api`) | http://localhost:5090 |

Tài khoản mặc định: **`admin` / `BlueMeal@2026!`** (dev). Lần đăng nhập đầu **bắt buộc đổi mật khẩu và bật xác thực 2 lớp** (Google/Microsoft Authenticator).

> Dữ liệu seed sẵn (thực phẩm, món ăn, khuyến nghị…) là **[DỮ LIỆU MẪU]** phục vụ demo/đào tạo, **không phải số liệu chính thức**. Tắt bằng `SEED_SAMPLE_DATA=false` hoặc xóa nhanh trong màn hình Cấu hình hệ thống.

## Phát triển từ mã nguồn

```sh
# Cơ sở dữ liệu + Redis + Mailpit trong Docker
docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.dev.yml up -d postgres redis mailpit
# API (cần .NET SDK 8+)
./scripts/run-api-local.sh            # http://localhost:5080
# Frontend (cần Node.js 22+)
cd frontend && npm ci && npm run dev  # http://localhost:5173 (proxy /api → 5080)
```

Cổng dev mặc định: PostgreSQL `5442`, Redis `6395` (đổi bằng biến `DEV_PG_PORT`, `DEV_REDIS_PORT`).

## Kiểm thử

```powershell
.\scripts\test-all.ps1        # build, unit + integration test, quét lỗ hổng NuGet/npm, lint, kiểm tra i18n
```

Kết quả lưu tại `docs/test-reports/`.

## Các script

| Script (`.ps1` và `.sh`) | Mục đích |
|---|---|
| `scripts/dev-up` | Khởi động môi trường dev |
| `scripts/gen-secrets` | Sinh `deploy/.env` với secret ngẫu nhiên |
| `scripts/migrate` | Chạy migration cơ sở dữ liệu |
| `scripts/seed` | Nạp dữ liệu danh mục (và dữ liệu mẫu) |
| `scripts/test-all` | Chạy toàn bộ kiểm thử (CI) |
| `scripts/save-images`, `scripts/load-images` | Đóng gói/nạp image cho máy chủ không có Internet |
| `deploy/backup/restore` | Khôi phục bản sao lưu |

## Cấu trúc thư mục

```
backend/   .NET solution (Domain, Application, Infrastructure, Api) + tests
frontend/  React SPA (features/<module>, shared, locales/vi|en) + e2e (Playwright)
deploy/    docker-compose (base/dev/prod), nginx + ModSecurity, postgres, backup
tools/     mock cổng dữ liệu Viện Dinh dưỡng, k6, ZAP, file Excel mẫu
scripts/   script tiện ích (PowerShell + sh)
docs/      tài liệu bàn giao
```

## Bản quyền

Toàn bộ mã nguồn, cơ sở dữ liệu và thiết kế giao diện thuộc sở hữu của chủ đầu tư sau bàn giao. Danh sách thành phần mã nguồn mở và giấy phép: [`docs/THIRD_PARTY_LICENSES.md`](docs/THIRD_PARTY_LICENSES.md).
