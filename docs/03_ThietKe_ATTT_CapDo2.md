# THIẾT KẾ AN TOÀN THÔNG TIN CẤP ĐỘ 2 — BlueMeal

> Phạm vi: tầng phần mềm của hệ thống BlueMeal (ứng dụng web, API, cơ sở dữ liệu, sao lưu) theo yêu cầu cơ bản đối với hệ thống
> thông tin cấp độ 2. Căn cứ (cập nhật 12/11/2026): Luật An ninh mạng 2025, Nghị định 331/2026/NĐ-CP, **TCVN 14423:2026** §4;
> bảo vệ dữ liệu cá nhân theo Luật 91/2025/QH15 và Nghị định 356/2025/NĐ-CP. Các văn bản này thay NĐ 85/2016, TT 12/2022, TCVN 11930:2017 và NĐ 13/2023 mà SPEC viện dẫn.
> Đối chiếu từng nhóm yêu cầu của TCVN 14423:2026, TT 39/2017/TT-BTTTT và TT 13/2025/TT-BYT: `10_DoiChieu_TieuChuan_PhapLy.md`.
> Hồ sơ đề xuất cấp độ: `11_HoSo_DeXuat_CapDo2.md`.
> Các yêu cầu về hạ tầng vật lý, mạng của đơn vị vận hành (tường lửa biên, phòng máy, quy chế) nằm ngoài phạm vi tài liệu này và được nêu ở mục 9.
>
> Mỗi yêu cầu được ánh xạ: **Yêu cầu → Biện pháp kỹ thuật → Vị trí trong mã nguồn/cấu hình → Cách kiểm thử**. Đường dẫn tính từ thư mục gốc repo.

## 1. Kiến trúc an toàn tổng quát

```
Internet/LAN ──TLS 1.2+──► [web] nginx + ModSecurity v3 + OWASP CRS 4 (PL1)   ← chỉ container này mở cổng ra ngoài
                              │  security headers, CSP nonce, rate limit, HSTS, giới hạn kích thước request
                              ▼  (mạng docker "frontend")
                           [api] ASP.NET Core 8 — JWT 15 phút + refresh token xoay vòng (cookie HttpOnly, SameSite=Strict, CSRF header)
                              │  deny-by-default permission middleware, data scope, FluentValidation, ProblemDetails
                              ▼  (mạng docker "backend", không ra ngoài)
              [postgres] 16 (role ít quyền bluemeal_app, audit_log chỉ INSERT)   [redis] 7.2   [worker] Quartz.NET
              [backup] pg_dump + file đính kèm → AES-256 (openssl PBKDF2 200k) + SHA-256, lưu 7/4/6, mirror NAS
```

Nguyên tắc: tối thiểu đặc quyền (DB, container non-root), phòng thủ nhiều lớp (WAF → API → DB), mặc định từ chối, không bí mật trong mã nguồn
(`deploy/.env` sinh bởi `scripts/gen-secrets.*`, bị `.gitignore`), không telemetry/SaaS bên ngoài.

## 2. Xác thực (SPEC §10.1)

| Yêu cầu | Biện pháp kỹ thuật | Vị trí | Kiểm thử |
|---|---|---|---|
| Mật khẩu ≥ 10 ký tự, hoa/thường/số/đặc biệt (cấu hình) | `PasswordPolicySettings` + validator khi đặt/đổi mật khẩu | `backend/src/BlueMeal.Application/Settings/SettingModels.cs`, `backend/src/BlueMeal.Api/Auth/AuthService.cs` | `AuthTests.Password_policy_and_history_are_enforced` |
| Không trùng 5 mật khẩu gần nhất | Bảng `password_history` (hash), so khớp khi đổi | `AuthService` (đổi/đặt lại mật khẩu) | `AuthTests.Password_policy_and_history_are_enforced` |
| Hết hạn 90 ngày (cấu hình) | `ExpiryDays`; đăng nhập khi hết hạn → phiên *restricted* chỉ được đổi mật khẩu | `AuthService` (kiểm tra `PasswordChangedAt`), `PermissionEnforcementMiddleware` (`scope=restricted`) | `SessionAndPiiTests.Expired_password_gives_a_restricted_session` |
| Hash an toàn | ASP.NET Identity V3 **PBKDF2-HMAC-SHA512, 210.000 vòng** | `backend/src/BlueMeal.Infrastructure/DependencyInjection.cs` (`PasswordHasherOptions`) | rà soát cấu hình |
| MFA TOTP + 10 mã khôi phục, bắt buộc với **mọi tài khoản** (mặc định); nếu quản trị tắt thì vẫn luôn bắt buộc với SYS_ADMIN, MANAGER, MENU_APPROVER, ACCOUNTANT | Otp.NET (RFC 6238), QR tự sinh (QRCoder); mã khôi phục lưu hash; `MfaPolicySettings.RequireForAllUsers` = true mặc định, vai trò bắt buộc bổ sung cấu hình trong `MfaPolicySettings` (4 vai trò trên luôn bắt buộc); **mỗi mã TOTP chỉ dùng một lần** (khóa `totp-used:` trong cache) | `AuthService`, `ProfileController`, `MfaPolicySettings` | `AuthTests.Mfa_is_mandatory_for_every_account_by_default`, `AuthTests.Admin_first_login_is_restricted_until_password_changed_and_mfa_enabled`, `SessionAndPiiTests.A_totp_code_cannot_be_replayed_at_login`, E2E `p1-admin.spec.ts` |
| OTP qua email dự phòng | Mã 6 số, hạn 5 phút, lưu hash `one_time_token` | `AuthService`, Mailpit (dev) | kiểm tra thủ công qua Mailpit (chưa có test tự động) |
| Khóa tài khoản 5 lần sai / 15 phút | Identity Lockout | `Infrastructure/DependencyInjection.cs` | `AuthTests.Account_is_locked_after_5_failures_and_captcha_required_after_3` |
| CAPTCHA tự host sau 3 lần sai | SVG captcha sinh phía server, đáp án lưu cache; ngưỡng theo user (3) và theo IP (3×10, chống NAT) | `backend/src/BlueMeal.Api/Auth/CaptchaService.cs` | `AuthTests.Account_is_locked_after_5_failures_and_captcha_required_after_3` |
| Hết phiên khi không hoạt động 30 phút; giới hạn phiên đồng thời; thu hồi khi đổi mật khẩu / bị khóa | `user_session` (refresh token chỉ lưu SHA-256, xoay vòng mỗi lần refresh, phát hiện dùng lại → thu hồi cả phiên, ân hạn 30 giây); `SessionValidationMiddleware` kiểm tra `LastSeenAt`; `MaxConcurrentSessions` thu hồi phiên cũ nhất | `Auth/TokenService.cs`, `Auth/AuthService.cs`, `Infrastructure/Middlewares.cs` | `AuthTests.Refresh_token_rotates_and_reuse_revokes_the_session`, `AuthTests.Logout_and_admin_force_logout_revoke_sessions`, `SessionAndPiiTests.Idle_session_expires_after_the_configured_timeout`, `SessionAndPiiTests.Concurrent_session_limit_revokes_the_oldest_session` |
| Xác thực lại cho thao tác nhạy cảm | Tải bản sao lưu yêu cầu mật khẩu + mã MFA (một lần), token tải dùng một lần hạn 2 phút | `AuthService.ReauthenticateAsync`, `Controllers/Admin/BackupsController.cs` | `SecurityP8Tests.Manual_backup_and_download_with_reauthentication` |

## 3. Kiểm soát truy cập (SPEC §10.2)

| Yêu cầu | Biện pháp | Vị trí | Kiểm thử |
|---|---|---|---|
| RBAC, kiểm tra quyền ở server cho **mọi** request, mặc định từ chối | `[HasPermission]` trên từng action; `PermissionEnforcementMiddleware` trả 403 cho endpoint không khai báo quyền; quyền nạp từ DB + cache (`PermissionService`) | `backend/src/BlueMeal.Api/Authorization/PermissionAuthorization.cs`, `Infrastructure/Identity/PermissionService.cs` | `EndpointPermissionTests` (quét toàn bộ controller: mọi endpoint có quyền/`[PublicScope]`/`[AllowAnonymous]`, mã quyền hợp lệ), `PermissionMatrixTests.Role_can_only_reach_endpoints_it_is_granted`, `Write_endpoints_are_denied_without_permission`, `Anonymous_requests_are_rejected` |
| Phạm vi dữ liệu (kho, khách hàng) | `ICurrentUser.WarehouseScope/CustomerScope`; mọi truy vấn danh sách và theo id lọc theo phạm vi; job nền chạy bằng quyền + phạm vi của người yêu cầu | `Controllers/Stock/*`, `Controllers/Menus/*`, `Reports/ReportFramework.cs` (`ScopeWarehouse`), `Infrastructure/CurrentUser.cs` (`OverrideAccess`) | `CatalogP2Tests.Warehouse_scope_hides_other_warehouses_and_blocks_idor`, `ReportTests.Warehouse_scoped_user_only_sees_own_warehouse_in_reports` |
| Chống IDOR | Truy vấn theo id luôn qua `Scoped()`/`EnsureWarehouse()` → 404 nếu ngoài phạm vi | như trên | như trên |
| API cho hệ thống khác | OAuth2 client credentials do BlueMeal phát hành (audience riêng `bluemeal-public`, khóa dẫn xuất riêng), scope `menu.read`/`nutrition.read`/`food.read`, secret lưu hash | `Controllers/PublicApi/PublicApi.cs` | `IntegrationTests.Public_api_with_client_credentials_json_xml_and_fhir` |

## 4. Nhật ký tập trung (SPEC §10.3)

| Yêu cầu | Biện pháp | Vị trí | Kiểm thử |
|---|---|---|---|
| Ghi thời điểm, user, IP, user agent, hành động, đối tượng, giá trị trước/sau (JSON diff, che trường nhạy cảm), kết quả, traceId | `AuditSaveChangesInterceptor` (Create/Update/Delete tự động, che `[Sensitive]`), `AuditWriter` cho Login/Logout/LoginFailed/Export/Import/PermissionChange/ConfigChange/Backup/Restore/SyncRun/PiiAccess… | `Infrastructure/Persistence/AuditSaveChangesInterceptor.cs`, `Infrastructure/Services/CoreServices.cs` | `AuditLogTests.Changes_are_audited_with_json_diff_and_masked_sensitive_fields` |
| Chỉ thêm, không sửa/xóa qua ứng dụng | Role `bluemeal_app` chỉ có `SELECT, INSERT` trên `audit_log` (REVOKE UPDATE/DELETE/TRUNCATE); bảng phân vùng theo tháng | `Infrastructure/Persistence/Migrations/AuditLogSql.cs`, `deploy/postgres/init.sql` | `AuditLogTests.Audit_log_is_append_only_for_application_role` |
| Lưu ≥ 12 tháng (cấu hình) | `AuditSettings.RetentionMonths` (tối thiểu 12); job housekeeping 03:30 tạo trước phân vùng và xóa phân vùng quá hạn bằng **kết nối owner** (role app không có quyền) | `Services/BackupService.cs` (`HousekeepingJob`) | `SecurityP8Tests.Housekeeping…` |
| Log ứng dụng JSON xoay vòng, gửi tập trung sang syslog/SIEM | Serilog Compact JSON ra `logs/` (xoay theo ngày, 90 file, 200 MB/file); sink syslog tích hợp gửi từng sự kiện dạng RFC 5424 (thân là JSON) qua UDP hoặc TCP (khung RFC 6587) tới máy thu log/SIEM — bật bằng `SYSLOG_HOST`; gửi qua hàng đợi nền (10.000 sự kiện) nên máy thu chậm/mất kết nối không làm chậm ứng dụng, file JSON cục bộ vẫn luôn được ghi. Overlay `docker-compose.syslog.yml` đưa log của các thành phần còn lại về **cùng máy thu** qua log driver syslog của Docker (RFC 5424, APP-NAME = tên container, chế độ non-blocking): nginx access/error log và audit log JSON của ModSecurity (`web`), kết nối/ngắt kết nối, lỗi xác thực, lỗi và câu lệnh chậm > 1 s của PostgreSQL, Redis, sidecar sao lưu | `backend/src/BlueMeal.Api/Program.cs`, `Infrastructure/SyslogSink.cs`, `deploy/docker-compose.syslog.yml` | `SyslogSinkTests`; thử thực tế 2026-11-12: request XSS bị ModSecurity chặn (403) và lỗi `FATAL` của PostgreSQL đều đến máy thu, `docker logs` vẫn đọc được |
| Màn hình tra cứu nhật ký, lọc, xuất Excel | *Quản trị → Nhật ký hệ thống*; báo cáo RPT-16 | `Controllers/Admin/SystemAdminControllers.cs` (`AuditLogsController`), `Reports/CostSafetySystemReports.cs` | E2E `p1-admin.spec.ts`, `ReportTests` |

## 5. Mã hóa (SPEC §10.4)

| Yêu cầu | Biện pháp | Vị trí | Kiểm thử |
|---|---|---|---|
| TLS 1.2+, HSTS, chuyển HTTP → HTTPS | nginx `ssl_protocols TLSv1.2 TLSv1.3`, HSTS 1 năm trên HTTPS, `NGINX_ALWAYS_TLS_REDIRECT` trong prod | `deploy/nginx/default.conf.template`, `deploy/docker-compose.prod.yml` | `curl -I`, ZAP |
| Mạng nội bộ container riêng | 2 mạng docker: `frontend` (web) và `backend` (api, db, redis, worker, backup) — DB/Redis không mở cổng trong prod | `deploy/docker-compose*.yml` | rà soát compose |
| Mã hóa cột AES-256-GCM (SĐT, email, CCCD, credential tích hợp, secret MFA) + cột hash HMAC để tìm chính xác | `[Encrypted]` → ValueConverter; định dạng `v1:{keyId}:base64`; nhiều khóa để **xoay khóa**; HMAC-SHA256 cho `*_hash` | `Infrastructure/Security/Encryption.cs`, `Domain/Common/Entity.cs`, `AuditSaveChangesInterceptor` | `TextAndCryptoTests.Round_trips_and_is_randomized`, `Supports_key_rotation`, `Tampering_is_detected`, `Hash_is_deterministic_and_case_insensitive`; `CatalogTests.Employee_pii_is_encrypted_at_rest_masked_without_pii_view_and_searchable_by_hash` |
| Khóa từ secret/biến môi trường | `BLUEMEAL_ENC_KEYS`, `BLUEMEAL_HMAC_KEY`, `BLUEMEAL_JWT_KEY` (sinh bởi `scripts/gen-secrets.*`) | `backend/src/BlueMeal.Api/ApiSetup.cs` | — |
| Mã hóa bản sao lưu | `openssl enc -aes-256-cbc -pbkdf2 -iter 200000` + SHA-256 (A-29); ứng dụng không giữ passphrase | `deploy/backup/backup.sh`, `restore.sh` | `scripts/test-backup-restore.(sh|ps1)` |
| File đính kèm: volume riêng, tên ngẫu nhiên, kiểm tra MIME/kích thước, chỉ phục vụ qua API có quyền | `FileService`: whitelist loại file theo mục đích + kiểm tra chữ ký nội dung (magic bytes), giới hạn kích thước, lưu tên ngẫu nhiên, tải qua `/api/v1/files/{id}` có kiểm tra quyền theo đối tượng sở hữu | `Controllers/Admin/SystemAdminControllers.cs` (`FileService`, `FilesController`) | `SessionAndPiiTests.Upload_rejects_content_that_does_not_match_the_extension`, `CatalogTests.Import_rejects_non_excel_content` |

## 6. Chống tấn công ứng dụng (SPEC §10.5)

| Yêu cầu | Biện pháp | Vị trí | Kiểm thử |
|---|---|---|---|
| WAF ModSecurity + OWASP CRS PL1, có file loại trừ false positive | Image `owasp/modsecurity-crs:4.25.2-nginx…-lts`; loại trừ trong `bluemeal-before-crs.conf` (chỉ cho các tham số JSON đã kiểm chứng) | `frontend/Dockerfile`, `deploy/nginx/modsecurity/bluemeal-before-crs.conf` | E2E `p8-security.spec.ts` (SQLi bị chặn 403) |
| Rate limit theo IP và theo user | nginx `limit_req` theo IP; ASP.NET RateLimiter: policy `auth` (đăng nhập, quên mật khẩu, token public, tải sao lưu) theo IP, giới hạn chung theo user/IP | `deploy/nginx/default.conf.template`, `backend/src/BlueMeal.Api/Program.cs` | rà soát cấu hình (bộ test tích hợp nâng ngưỡng để không bị giới hạn); kiểm tra thủ công bằng gửi liên tiếp yêu cầu đăng nhập |
| Security headers: CSP chặt (nonce), nosniff, X-Frame-Options DENY, Referrer-Policy, Permissions-Policy, COOP/COEP/CORP, HSTS | Một file include dùng cho mọi `location` | `deploy/nginx/security-headers.conf`, `deploy/nginx/app.conf` | E2E `p8-security.spec.ts`, ZAP |
| CSRF cho cookie refresh token | Cookie `bm_rt` HttpOnly, SameSite=Strict, Path `/api/v1/auth`; double-submit `bm_csrf` + header `X-BlueMeal-CSRF` | `Controllers/AuthController.cs` | `AuthTests.Refresh_requires_csrf_header`, `AuthTests.Refresh_cookie_is_httponly_samesite_strict` |
| Chặn SQL injection | Chỉ EF Core/LINQ; SQL thô duy nhất đều tham số hóa (`ExecuteSqlRawAsync(sql, params)`, `NpgsqlParameter`) | `Services/InventoryService.cs`, `Infrastructure/Services/CoreServices.cs`, `Controllers/Admin/SampleDataController.cs` | rà soát mã + ZAP + WAF |
| Chặn XSS | React escape mặc định; ESLint cấm `dangerouslySetInnerHTML`; CSP không `unsafe-inline` cho script | `frontend/eslint.config.js`, `deploy/nginx/app.conf` | `npm run lint` trong `scripts/test-all` |
| Validate input, giới hạn kích thước request, chống mass assignment | FluentValidation cho mọi DTO; Kestrel `MaxRequestBodySize` 25 MB + nginx `client_max_body_size`; chỉ nhận DTO, Mapster bỏ qua Id/Version/audit | `**/*Validator`, `Program.cs`, `Catalog/CatalogDtos.cs` | integration tests (400 với dữ liệu sai) |
| Idempotency cho chứng từ kho | Header `Idempotency-Key` (A-42) | `Infrastructure/IdempotencyFilter.cs` | `StockTests.Idempotency_key_replays_the_first_response` |
| Quét thư viện có lỗ hổng | `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev --audit-level=moderate` | `scripts/test-all.(sh|ps1)` | báo cáo `docs/test-reports/nuget-vulnerable.txt` |
| **OWASP ZAP baseline sạch High/Medium** | `scripts/zap-baseline.(sh|ps1)` — chạy ZAP (container dùng một lần) trên mạng `frontend`, thất bại nếu có cảnh báo mức Medium/High; file quy tắc `deploy/zap/baseline-rules.tsv` ghi lý do các mục bỏ qua | `scripts/zap-baseline.*` | báo cáo `docs/test-reports/zap/zap-baseline.html` (kết quả 09/11/2026: **0 High, 0 Medium**, 1 Low false positive, Informational) |

## 7. Bảo vệ dữ liệu cá nhân (SPEC §10.6)

| Yêu cầu | Biện pháp | Vị trí | Kiểm thử |
|---|---|---|---|
| Thu thập tối thiểu | Nhân viên chỉ gồm trường cần cho suất ăn/kho (họ tên, đơn vị, liên hệ, CCCD tùy chọn) | `Domain/Entities/SystemEntities.cs` (`Employee`) | rà soát |
| Che dữ liệu khi không có `pii.view` | API trả giá trị đã che (`09******89`, `n***@x.vn`) | `Controllers/Admin/EmployeesController.cs` | `CatalogTests.Employee_pii_is_encrypted_at_rest_masked_without_pii_view_and_searchable_by_hash` |
| Nhật ký truy cập dữ liệu cá nhân | `AuditAction.PiiAccess` khi xem chi tiết/xuất | như trên | `SessionAndPiiTests.Personal_data_export_and_anonymization_are_audited` |
| Xuất và xóa (ẩn danh hóa) dữ liệu của một nhân viên | `GET /employees/{id}/personal-data` (JSON), `POST /employees/{id}/anonymize` (không đảo ngược, khóa tài khoản liên kết) | như trên | `SessionAndPiiTests.Personal_data_export_and_anonymization_are_audited` |
| Không đưa dữ liệu ra ngoài trừ API tích hợp đã cấu hình | Không telemetry; API công khai chỉ trả thực đơn/dinh dưỡng/thực phẩm (không dữ liệu cá nhân); FHIR không có dữ liệu bệnh nhân (A-43) | `Controllers/PublicApi/PublicApi.cs` | `IntegrationTests.Public_api_with_client_credentials_json_xml_and_fhir` |

## 8. Sao lưu – phục hồi (SPEC §10.7)

| Yêu cầu | Biện pháp | Vị trí | Kiểm thử |
|---|---|---|---|
| `pg_dump -Fc` hằng ngày 01:00 + file đính kèm, mã hóa, checksum | Container `backup` (crond), `backup.sh` | `deploy/backup/` | `scripts/test-backup-restore.*` |
| Giữ 7 ngày / 4 tuần / 6 tháng, mirror NAS | Retention trong `backup.sh`, `BACKUP_MIRROR_PATH` | `deploy/backup/backup.sh`, `.env` | rà soát |
| Sao lưu bất thường bằng nút (quyền `system.backup`) qua Quartz | `POST /api/v1/backups` → job `backup` (worker) ghi `requests/<id>.req` → sidecar chạy, ghi `manifests/<id>.json` → cập nhật `backup_job` | `Services/BackupService.cs`, `Controllers/Admin/BackupsController.cs`, `deploy/backup/entrypoint.sh` | `SecurityP8Tests.Manual_backup_and_download_with_reauthentication`, E2E `p8-security.spec.ts` |
| Lịch sử + tải bản sao lưu cần nhập lại mật khẩu + MFA | Màn *Quản trị → Sao lưu*; token tải một lần 2 phút; ghi audit `Backup` | `frontend/src/features/admin/BackupsPage.tsx` | `SecurityP8Tests.Manual_backup_and_download_with_reauthentication` |
| `restore.ps1/.sh` + diễn tập khôi phục tự động | `deploy/backup/restore.sh` (kiểm SHA-256 → giải mã → `pg_restore --clean`), `restore.ps1`/`restore-host.sh`; `scripts/test-backup-restore.*`: backup → restore vào DB tạm → so số bản ghi 12 bảng chính | `deploy/backup/`, `scripts/` | báo cáo `docs/test-reports/restore-drill.txt` (09/11/2026: **PASSED**, mọi bảng khớp) |

## 9. Ngoài phạm vi phần mềm (khuyến nghị cho đơn vị vận hành)

- Tường lửa biên/IPS, phân vùng mạng, chỉ mở cổng 443 (và 80 để chuyển hướng) của container `web`.
- Chứng chỉ TLS hợp lệ (CA nội bộ hoặc công khai) thay chứng chỉ tự ký mặc định; gia hạn định kỳ.
- Quy chế quản lý tài khoản quản trị, phân công người giữ `BACKUP_PASSPHRASE` (tách biệt người quản trị ứng dụng), lưu bản sao lưu ngoài máy chủ.
- Cập nhật bản vá hệ điều hành máy chủ, Docker Engine; máy thu log tập trung/SIEM nhận log của BlueMeal (mục 4).
- Đồng bộ thời gian máy chủ bằng NTP (mã TOTP và nhật ký phụ thuộc đồng hồ máy chủ).
- Diễn tập khôi phục định kỳ (khuyến nghị hằng quý) bằng `scripts/test-backup-restore.*` và lưu biên bản.
- Cấu hình khi vận hành chính thức theo TCVN 14423:2026: hết phiên sau 15 phút không hoạt động.
- Tài khoản không đăng nhập quá *Khóa tài khoản không đăng nhập sau* (mặc định 45 ngày, 0 = tắt) bị job `inactive-accounts` khóa lúc 00:15 hằng ngày: đổi security stamp, thu hồi mọi phiên, ghi nhật ký `Lock` (`inactive_45d`). Thời hạn tính từ lần đăng nhập cuối, ngày tạo hoặc lần quản trị mở khóa gần nhất. Không bao giờ khóa tài khoản quản trị hệ thống cuối cùng còn hoạt động (`InactiveAccountsJob`; integration `PrivacyAndInactiveAccountTests`).
- Thông báo xử lý dữ liệu cá nhân (Luật 91/2025/QH15): người dùng phải đồng ý phiên bản hiện hành sau khi đăng nhập; lưu bảng `privacy_consent` (phiên bản, thời điểm, IP, trình duyệt) và nhật ký `Consent`. Đổi nội dung thông báo → tăng phiên bản → mọi người đồng ý lại.
- Các yêu cầu quản lý còn lại của TCVN 14423:2026 (rủi ro, tài sản, nhân sự, nhà cung cấp, ứng phó sự cố): `10_DoiChieu_TieuChuan_PhapLy.md` §4.
