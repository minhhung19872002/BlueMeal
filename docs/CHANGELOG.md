# NHẬT KÝ THAY ĐỔI (CHANGELOG)

Định dạng theo [Keep a Changelog](https://keepachangelog.com/vi/1.1.0/); phiên bản theo [SemVer](https://semver.org/).
Mỗi mục `## [x.y.z] - YYYY-MM-DD` được nạp vào màn hình **Phiên bản hệ thống** (bảng `system_version`).

## [0.6.0] - 2026-11-09
### P5 – Kho và An toàn thực phẩm (SPEC §5.5, §5.6, §6.5, §7.8)
- InventoryEngine thuần C#: bình quân gia quyền di động (mặc định) hoặc FIFO theo lô, gợi ý lô FEFO, chặn xuất âm (cấu hình được), chênh lệch kiểm kê, nhu cầu nguyên liệu từ thực đơn, đề xuất mua `max(0, nhu cầu − tồn khả dụng + tồn tối thiểu)`, quy tắc khóa sổ; 19 unit test số liệu tính tay.
- Phiếu nhập (PN-) có kiểm tra chất lượng đầu vào từng dòng (số lô, NSX, HSD, nhiệt độ), nhập mã vạch, in phiếu; ghi sổ tự lập biên bản kiểm thực bước 1. Dòng Không đạt chặn ghi sổ.
- Phiếu xuất (PX-): thủ công, chuyển kho, hoặc sinh phiếu xuất dự kiến từ thực đơn đã duyệt (định lượng × số suất, lô FEFO); đơn giá xuất theo phương pháp giá kho.
- Kiểm kê (KK-): chụp tồn sổ sách theo lô, nhập số thực tế trên mobile, duyệt tự sinh và ghi sổ phiếu điều chỉnh tăng/giảm.
- Sổ cái kho bất biến + bảng tồn hiện thời cập nhật trong cùng transaction (khóa theo kho); tồn tại thời điểm, thẻ kho, danh sách lô sắp hết hạn; khóa sổ theo tháng.
- Đề xuất mua hàng (DX-) từ thực đơn đã duyệt, duyệt và tạo phiếu nhập theo nhà cung cấp.
- Kiểm thực 3 bước với tiêu chí cấu hình được và biểu mẫu in; sổ lưu mẫu thức ăn (LM-) với thời gian lưu tối thiểu và khối lượng tối thiểu cấu hình được.
- Job cảnh báo hằng ngày 06:00 (Quartz): W-CERT-EXPIRY, W-STOCK-EXPIRY, W-STOCK-MIN, W-SAMPLE-DISPOSE — chống trùng, tự đóng khi hết điều kiện, thông báo cho người có quyền `alert.view`.
- Header `Idempotency-Key` cho các thao tác tạo/ghi sổ chứng từ.
- Kiểm thử: unit 105, integration 62 (9 test kho/ATTP), E2E P5 (nhập → ghi sổ → tồn/thẻ kho, kiểm kê → duyệt, lưu mẫu).

## [0.5.0] - 2026-11-09
### P4 – Thực đơn & phê duyệt (SPEC §6.4, §7.4)
- MenuGenerator: K=200 ứng viên ngẫu nhiên có trọng số + tìm kiếm cục bộ 500 vòng trên top 10, trọng số w1..w7, tối đa 1 món chiên, không lặp món N ngày, seed tái lập, 2 phương án thay thế, giải thích song ngữ; yêu cầu lớn (> 3 ca) chạy nền qua Quartz.
- Kế hoạch thực đơn tuần/tháng với lịch ngày × ca, kéo-thả món giữa các ngày, nhân bản tuần, áp dụng cho nhiều khách hàng.
- Soạn thực đơn thủ công: chọn món theo loại, hệ số khẩu phần, dinh dưỡng – chi phí – cảnh báo realtime, gợi ý đổi món cải thiện điểm.
- Quy trình duyệt 1 hoặc 2 cấp: nháp → chờ duyệt → duyệt → công bố; trả lại/từ chối bắt buộc ý kiến; cảnh báo nghiêm trọng cần lý do + quyền `menu.override_critical`; thu hồi, hủy; chốt phiên bản công thức khi duyệt; duyệt/công bố hàng loạt; thông báo cho người lập.
- Thực đơn công bố theo tuần (in A4, không hiển thị chi phí); đặt suất ăn theo khách hàng + tổng hợp + cập nhật số suất vào thực đơn.
- Khi không chọn cơ cấu bữa, bộ sinh dùng cơ cấu đang hoạt động đầu tiên.
- Kiểm thử: unit 86, integration 53, E2E P4 (tạo tự động → sửa → gửi duyệt → duyệt → công bố).

## [0.4.0] - 2026-11-09
### P3 – Món ăn, công thức, khuyến nghị (SPEC §6.1–6.3, §7.3.6–7.3.9)
- NutritionEngine, CostEngine, WarningEngine viết thuần C# (decimal, làm tròn khi hiển thị); 57 unit test với số liệu tính tay, độ phủ dòng 98,8%.
- Món ăn (ảnh, loại món, phương pháp chế biến, xem nhanh dinh dưỡng & chi phí 1 suất); công thức có phiên bản (nháp → kích hoạt → lưu trữ), nhân bản, so sánh 2 phiên bản, quy đổi theo số suất, tính dinh dưỡng – chi phí – cơ cấu P:L:G **realtime** khi soạn.
- Đơn giá theo cấu hình: giá tham chiếu / nhập gần nhất / bình quân gia quyền (mặc định) / hợp đồng; chi phí chung theo % hoặc cố định/suất.
- Khuyến nghị kèm mục tiêu theo từng chất (min/max/bữa, mức cảnh báo); nhóm báo cáo + gán 16 báo cáo.
- Sửa: validator của API trước đây chưa được đăng ký; id sinh ở client được EF coi là bản ghi tồn tại (ValueGeneratedNever).

## [0.3.0] - 2026-11-09
### P2 – Danh mục nền (SPEC §7.2, §7.3.1–7.3.5)
- Đối tượng; loại đối tác/đối tác (hợp đồng + đơn giá hợp đồng, giấy chứng nhận ATTP có tệp, đánh giá, lịch sử nhập); loại khách hàng/khách hàng (liên hệ, đơn giá suất ăn, đặt suất, thực đơn công bố) với phạm vi dữ liệu theo khách hàng.
- Kho (phạm vi theo thủ kho), nhóm thực phẩm dạng cây, chất dinh dưỡng, thực phẩm (lưới sửa nhanh giá trị/100 g, khóa dữ liệu từ API, nhập/xuất Excel mỗi chất một cột), ATTP – chất lượng, nguyên liệu (lịch sử giá, tồn), ca ăn, cơ cấu bữa.
- Dữ liệu MẪU gắn nhãn [DỮ LIỆU MẪU]: 81 thực phẩm, 42 nguyên liệu, 30 món kèm công thức, 3 khuyến nghị; xóa nhanh bằng 1 thao tác.
- Nhập 500 thực phẩm từ Excel < 30 s (integration test); tìm kiếm không dấu.

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
