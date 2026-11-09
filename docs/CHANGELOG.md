# NHẬT KÝ THAY ĐỔI (CHANGELOG)

Định dạng theo [Keep a Changelog](https://keepachangelog.com/vi/1.1.0/); phiên bản theo [SemVer](https://semver.org/).
Mỗi mục `## [x.y.z] - YYYY-MM-DD` được nạp vào màn hình **Phiên bản hệ thống** (bảng `system_version`).

## [0.1.0] - 2026-11-09
### P0 – Khung dự án
- Solution .NET 8 bốn lớp (Domain, Application, Infrastructure, Api) và 2 project kiểm thử.
- Mô hình dữ liệu đầy đủ theo SPEC mục 5 (schema `app`, snake_case, xóa mềm, `xmin`), migration đầu tiên; bảng `audit_log` phân vùng theo tháng, chỉ thêm (append-only).
- Serilog (Console + file JSON xoay vòng), lỗi RFC 7807 song ngữ, health check `/health/live`, `/health/ready`.
- Phân quyền theo chức năng `module.action`, mặc định từ chối nếu endpoint thiếu khai báo quyền.
- Job nền Quartz.NET (thay Hangfire do giấy phép — A-34).
- Frontend React 18 + Vite + Ant Design 5, song ngữ Việt/Anh, trang đăng nhập.
- Docker Compose (base/dev/prod), nginx + ModSecurity OWASP CRS, PostgreSQL 16, Redis 7.2, container sao lưu.
