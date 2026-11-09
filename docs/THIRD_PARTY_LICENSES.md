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
| MailKit / MimeKit | 4.18.2 | MIT | ✔ | Gửi email |
| xunit / xunit.runner.visualstudio | 2.9.3 / 2.8.2 | Apache-2.0 | dev | Unit test |
| FluentAssertions | **7.2.2** | Apache-2.0 | dev | Assertion (**không dùng bản 8.x** vì giấy phép thương mại Xceed) |
| Microsoft.NET.Test.Sdk / Mvc.Testing | 17.14.1 / 8.0.33 | MIT | dev | Test |
| coverlet.collector | 6.0.4 | MIT | dev | Đo độ phủ |
| Testcontainers.PostgreSql / .Redis | 4.16.0 | MIT | dev | Integration test với DB thật |

## 2. Frontend — npm

*(Bổ sung đầy đủ ở Phase P0 sau khi khởi tạo frontend; danh sách cuối sinh tự động bằng `npx license-checker` ở P10.)*

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

## 5. Đã cân nhắc và loại bỏ

| Thành phần | Giấy phép | Lý do loại |
|---|---|---|
| Hangfire / Hangfire.PostgreSql | LGPL-3.0 (bản Pro/Ace thương mại) | Ngoài danh sách giấy phép cho phép → thay bằng Quartz.NET (A-34) |
| FluentAssertions ≥ 8.0 | Thương mại (Xceed) | Dùng 7.2.2 (Apache-2.0) |
| Redis ≥ 7.4 | RSALv2 / SSPL / AGPL | Dùng 7.2 (BSD-3) |
