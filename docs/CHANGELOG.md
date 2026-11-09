# NHẬT KÝ THAY ĐỔI (CHANGELOG)

Định dạng theo [Keep a Changelog](https://keepachangelog.com/vi/1.1.0/); phiên bản theo [SemVer](https://semver.org/).
Mỗi mục `## [x.y.z] - YYYY-MM-DD` được nạp vào màn hình **Phiên bản hệ thống** (bảng `system_version`).

## [0.2.0] - 2026-11-09
### P1 – Xác thực và quản trị (SPEC §7.1, §10.1–10.3)
- Khung danh mục dùng chung: phân trang server, tìm không dấu, lọc, sắp xếp, ẩn/hiện cột lưu theo người dùng, khóa/mở, xóa mềm, lịch sử thay đổi, nhập Excel (mẫu → xem trước → ghi) và xuất Excel.
- Thông tin đơn vị + logo; cơ cấu tổ chức dạng cây kéo-thả; quốc gia, 34 tỉnh/thành, xã/phường (nhập Excel), đơn vị đo + công cụ quy đổi.
- Nhân viên: mã hóa AES-256-GCM điện thoại/email/CCCD, tìm chính xác qua HMAC, che dữ liệu khi thiếu quyền `pii.view`, tạo tài khoản từ nhân viên, xuất và ẩn danh hóa dữ liệu cá nhân.
- Nhóm quyền + ma trận phân quyền module × hành động; tài khoản (khóa/mở, đặt lại mật khẩu, bắt buộc đổi mật khẩu, MFA, phạm vi dữ liệu kho/khách hàng); phiên làm việc + đăng xuất cưỡng bức; danh sách chức năng; cấu hình hệ thống; nhật ký hệ thống (tra cứu, xuất Excel RPT-16).
- Bảo mật: MFA TOTP bắt buộc theo vai trò + 10 mã khôi phục + OTP email; khóa tài khoản sau 5 lần sai; CAPTCHA tự host; chính sách mật khẩu + lịch sử 5 mật khẩu; refresh token xoay vòng và phát hiện dùng lại; quên mật khẩu qua email (Mailpit ở dev).
- Kiểm thử: 30 integration test (Testcontainers PostgreSQL) gồm ma trận quyền 8 vai trò, khóa tài khoản, MFA, audit log chỉ thêm; 5 E2E Playwright (desktop + mobile).

## [0.1.0] - 2026-11-09
### P0 – Khung dự án
- Solution .NET 8 bốn lớp (Domain, Application, Infrastructure, Api) và 2 project kiểm thử.
- Mô hình dữ liệu đầy đủ theo SPEC mục 5 (schema `app`, snake_case, xóa mềm, `xmin`), migration đầu tiên; bảng `audit_log` phân vùng theo tháng, chỉ thêm (append-only).
- Serilog (Console + file JSON xoay vòng), lỗi RFC 7807 song ngữ, health check `/health/live`, `/health/ready`.
- Phân quyền theo chức năng `module.action`, mặc định từ chối nếu endpoint thiếu khai báo quyền.
- Job nền Quartz.NET (thay Hangfire do giấy phép — A-34).
- Frontend React 18 + Vite + Ant Design 5, song ngữ Việt/Anh, trang đăng nhập.
- Docker Compose (base/dev/prod), nginx + ModSecurity OWASP CRS, PostgreSQL 16, Redis 7.2, container sao lưu.
