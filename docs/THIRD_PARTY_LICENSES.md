# DANH SÁCH THÀNH PHẦN BÊN THỨ BA VÀ GIẤY PHÉP

Theo SPEC mục 0.4: chỉ dùng phần mềm mã nguồn mở có giấy phép cho phép sử dụng thương mại (MIT / Apache-2.0 / BSD / PostgreSQL License). Không nhúng license key, telemetry hay dịch vụ SaaS.

Cột **Phân phối** cho biết thành phần có nằm trong sản phẩm bàn giao (✔) hay chỉ dùng khi phát triển/kiểm thử (dev).

## 1. Backend (.NET 8) — NuGet

| Thành phần | Phiên bản | Giấy phép | Phân phối | Mục đích |
|---|---|---|---|---|
| .NET 8 / ASP.NET Core 8 runtime | 8.0.x | MIT | ✔ | Nền tảng |
| Microsoft.EntityFrameworkCore (+Relational, Design) | 8.0.33 | MIT | ✔ | ORM |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.33 | MIT | ✔ | Quản lý người dùng, hash mật khẩu |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.33 | MIT | ✔ | JWT |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.11 | PostgreSQL License | ✔ | Provider PostgreSQL |
| EFCore.NamingConventions | 8.0.3 | Apache-2.0 | ✔ | Tên bảng/cột snake_case |
| FluentValidation (+DependencyInjectionExtensions) | 11.12.0 | Apache-2.0 | ✔ | Kiểm tra dữ liệu |
| Mapster | 7.4.0 | MIT | ✔ | Ánh xạ DTO |
| Serilog.AspNetCore / Sinks.Console / Sinks.File / Formatting.Compact | 8.0.3 / 6.1.1 / 6.0.0 / 3.0.0 | Apache-2.0 | ✔ | Ghi log |
| **Quartz / Quartz.Extensions.Hosting / Quartz.Serialization.SystemTextJson** | 3.22.9 | Apache-2.0 | ✔ | Job nền (thay Hangfire — A-34) |
| ClosedXML | 0.105.1 | MIT | ✔ | Xuất/nhập Excel |
| Otp.NET | 1.4.1 | MIT | ✔ | MFA TOTP |
| QRCoder | 1.8.1 | MIT | ✔ | Mã QR thiết lập MFA |
| StackExchange.Redis | 2.13.23 | MIT | ✔ | Cache, rate limit, thu hồi token |
| Swashbuckle.AspNetCore | 6.9.0 | MIT | ✔ | OpenAPI/Swagger |
| AspNetCore.HealthChecks.NpgSql / .Redis | 8.0.2 / 8.0.1 | Apache-2.0 | ✔ | Health check |
| Microsoft.Extensions.Http.Resilience (Polly) | 8.10.0 | MIT (Polly: BSD-3-Clause) | ✔ | Retry/circuit breaker gọi API VDD |
| Polly | 8.5.2 | BSD-3-Clause | ✔ | Pipeline retry + circuit breaker + timeout của `VddApiNutritionDataProvider` |
| Microsoft.AspNetCore.DataProtection.StackExchangeRedis | 8.0.33 | MIT | ✔ | Lưu khóa Data Protection dùng chung cho các replica trong Redis |
| MailKit / MimeKit | 4.18.2 | MIT | ✔ | Gửi email |
| xunit / xunit.runner.visualstudio | 2.9.3 / 2.8.2 | Apache-2.0 | dev | Unit test |
| FluentAssertions | **7.2.2** | Apache-2.0 | dev | Assertion (**không dùng bản 8.x** vì giấy phép thương mại Xceed) |
| Microsoft.NET.Test.Sdk / Mvc.Testing | 17.14.1 / 8.0.33 | MIT | dev | Test |
| coverlet.collector | 6.0.4 | MIT | dev | Đo độ phủ |
| Testcontainers.PostgreSql / .Redis | 4.16.0 | MIT | dev | Integration test với DB thật |

## 2. Frontend — npm

Thư viện được bundle vào bản build frontend (`dependencies` trong `frontend/package.json`):

| Thành phần | Phiên bản | Giấy phép | Phân phối |
|---|---|---|---|
| @ant-design/icons | 5.6.1 | MIT | ✔ |
| @hookform/resolvers | 5.9.2 | MIT | ✔ |
| @tanstack/react-query | 5.90.21 | MIT | ✔ |
| @zxing/browser | 0.1.5 | MIT | ✔ |
| @zxing/library | 0.21.3 | MIT | ✔ |
| antd | 5.29.3 | MIT | ✔ |
| axios | 1.21.0 | MIT | ✔ |
| dayjs | 1.11.18 | MIT | ✔ |
| i18next | 25.10.10 | MIT | ✔ |
| react | 18.3.1 | MIT | ✔ |
| react-dom | 18.3.1 | MIT | ✔ |
| react-hook-form | 7.65.0 | MIT | ✔ |
| react-i18next | 15.7.4 | MIT | ✔ |
| react-router | 7.18.5 | MIT | ✔ |
| recharts | 3.3.0 | MIT | ✔ |
| zod | 4.6.7 | MIT | ✔ |
| zustand | 5.0.8 | MIT | ✔ |

Toàn bộ cây phụ thuộc production (164 gói, quét bằng `npx license-checker-rseidelsohn --production`, ngày 2026-11-09): 148 MIT, 11 ISC, 2 BSD-3-Clause, 1 (Unlicense OR Apache-2.0), 1 Apache-2.0, 1 MIT AND ISC AND BSD-3-Clause. `@zxing/text-encoding` có hai giấy phép (Unlicense OR Apache-2.0), dùng theo Apache-2.0. Không có gói GPL/AGPL/thương mại. Danh sách đầy đủ từng gói: `docs/third-party/npm-licenses.json`.

Công cụ phát triển (Vite, TypeScript, ESLint, Vitest, Playwright, Lighthouse…) chỉ dùng khi build/kiểm thử, không nằm trong sản phẩm (mục 4).

## 3. Hạ tầng (Docker image)

| Thành phần | Image | Giấy phép | Phân phối |
|---|---|---|---|
| PostgreSQL 16 | `postgres:16-alpine` | PostgreSQL License | ✔ |
| Redis | `redis:7.2-alpine` | BSD-3-Clause (**không dùng ≥ 7.4**: RSALv2/SSPL; Redis 8: AGPL — A-35) | ✔ |
| Nginx + ModSecurity + OWASP CRS | `owasp/modsecurity-crs:nginx` | BSD-2 (nginx), Apache-2.0 (ModSecurity, CRS) | ✔ |
| .NET runtime | `mcr.microsoft.com/dotnet/aspnet:8.0` | MIT | ✔ |
| Alpine Linux (backup: `pg_dump`, `openssl`) | `postgres:16-alpine` | MIT/GPL (hệ điều hành, công cụ dòng lệnh độc lập) | ✔ |
| Mailpit | `axllent/mailpit` | MIT | dev |
| pgAdmin 4 | `dpage/pgadmin4` | PostgreSQL License | dev (tùy chọn) |

## 4. Công cụ kiểm thử (không phân phối)

| Công cụ | Giấy phép | Ghi chú |
|---|---|---|
| Playwright | Apache-2.0 | E2E, chụp ảnh màn hình HDSD |
| Vitest / React Testing Library | MIT | Unit test frontend |
| k6 | **AGPL-3.0** | Chỉ chạy kiểm thử tải nội bộ; không đóng gói vào sản phẩm (A-36) |
| OWASP ZAP | Apache-2.0 | Quét bảo mật baseline |
| Lighthouse | Apache-2.0 | Đo Performance/Accessibility/Best practices (`npm run lighthouse`), công cụ phát triển |

## 5. Đã cân nhắc và loại bỏ

| Thành phần | Giấy phép | Lý do loại |
|---|---|---|
| Hangfire / Hangfire.PostgreSql | LGPL-3.0 (bản Pro/Ace thương mại) | Ngoài danh sách giấy phép cho phép → thay bằng Quartz.NET (A-34) |
| FluentAssertions ≥ 8.0 | Thương mại (Xceed) | Dùng 7.2.2 (Apache-2.0) |
| Redis ≥ 7.4 | RSALv2 / SSPL / AGPL | Dùng 7.2 (BSD-3) |
