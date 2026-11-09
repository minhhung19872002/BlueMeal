# NHẬT KÝ THAY ĐỔI (CHANGELOG)

Định dạng theo [Keep a Changelog](https://keepachangelog.com/vi/1.1.0/); phiên bản theo [SemVer](https://semver.org/).
Mỗi mục `## [x.y.z] - YYYY-MM-DD` được nạp vào màn hình **Phiên bản hệ thống** (bảng `system_version`).

## [1.0.0] - 2026-11-09
### Phát hành chính thức
- Kiểm thử tải đạt ngưỡng SPEC §11: **1 000 VU giữ 10 phút, p95 đọc 628 ms, lỗi 0 %**, 317 423 request (`docs/test-reports/k6-load-test.md`).
- Checklist nghiệm thu `docs/09_Checklist_NghiemThu.md`: các hạng mục phần mềm đều đạt. Còn các việc làm tại hiện trường: cài đặt, đào tạo, kết nối API thật của Viện Dinh dưỡng.

## [1.0.0-rc.1] - 2026-11-09
### P10 – Tài liệu, đóng gói, bản phát hành ứng viên
- Checklist nghiệm thu `docs/09_Checklist_NghiemThu.md`: đối chiếu từng mục SPEC §15 với bằng chứng và trạng thái. Còn tồn tại: hiệu năng 1 000 người dùng đồng thời cần đo trên hạ tầng thật; API thật của Viện Dinh dưỡng; các việc tại hiện trường (A-48).
- Chạy thử overlay production: FS chỉ đọc, user không phải root, bỏ mọi capability, TLS, 2 API. E2E P1 + P8 đạt trên HTTPS.
- Khóa Data Protection dùng chung qua Redis cho mọi replica (A-47). Bảng Quartz bổ sung cột tùy chọn của 3.x, hết cảnh báo khi worker khởi động.
- Gói triển khai offline: `save-images` không còn chép `deploy/.env` và khóa/chứng chỉ TLS vào gói. Đã thử `save-images` → `load-images` (5 image, kiểm tra SHA-256).
- Danh sách giấy phép npm (165 gói production, đều MIT/ISC/BSD/Apache) trong `THIRD_PARTY_LICENSES.md` và `docs/third-party/npm-licenses.json`.
### P9 – Hiệu năng (tiếp)
- Pool kết nối PostgreSQL: `DB_POOL_SIZE` 40 mỗi tiến trình (trước là 100, gây lỗi `too many clients` khi chạy 1 000 VU). Bật `No Reset On Close` và auto-prepare của Npgsql.
- StackExchange.Redis dùng `SocketManager.ThreadPool`: luồng scheduler riêng của thư viện quay vòng chờ, chiếm khoảng 30 % thời gian luồng của API (đo bằng dotnet-trace).
- Kết quả k6 trên máy phát triển: 400 VU đạt (p95 516 ms, 0 % lỗi). 1 000 VU trong 10 phút: 0 % lỗi, 185 413 request, nhưng p95 đọc 4,2 s do máy bão hòa CPU (`docs/test-reports/k6-load-test.md`).
- E2E đầy đủ **54/54 đạt** (44 desktop + 10 mobile). Sửa build frontend: tsconfig của e2e thiếu thư viện DOM.

## [0.10.0] - 2026-11-09
### P9 – Hiệu năng, giao diện điện thoại, tài liệu (SPEC §9, §11, §13) — *chưa hoàn tất*
- Kiểm thử tải k6: `tools/k6/load-test.js`. Mỗi VU là một người dùng đăng nhập một lần, nghỉ 2–5 giây giữa các bước. Thêm sub-metric theo từng endpoint. Đăng nhập lỗi thì chờ lũy thừa trước khi thử lại, và kịch bản tự dừng khi quá 50 % lượt đăng nhập lỗi. Script `scripts/load-test.(sh|ps1)` chạy trên overlay `docker-compose.perf.yml` (2 API). Tài khoản `loadtest*` được tạo bằng `--seed-loadtest=N` và tự xóa sau khi chạy. Thêm công cụ đo chi phí từng request `tools/k6/cost-probe.js` và `one-endpoint.js`.
- Sửa: giới hạn tần suất theo người dùng dùng claim `sub` (bị JWT đổi tên) nên mọi người dùng chung một hạn mức → chuyển sang `RateLimitKeys.ForRequest`.
- `PasswordHashGate`: giới hạn số lượt băm PBKDF2 chạy đồng thời trên mỗi API (`Auth:MaxConcurrentPasswordHashes`), để đợt đăng nhập đầu ca không làm chậm người đã đăng nhập (A-46).
- Seeder tải: đặt lại mật khẩu cho tài khoản còn sót từ lần trước (tránh CAPTCHA theo IP chặn cả lần chạy), xóa phiên của tài khoản thử khi gỡ.
- Kết quả tải: 300 VU đạt (p95 158 ms, 0 % lỗi). **1 000 VU chưa có kết quả hợp lệ** trên máy phát triển. Khuyến nghị phần cứng cho 1 000 người dùng đồng thời nâng lên 12–16 vCPU, giới hạn CPU mỗi API ở bản production nâng lên 5 (`docs/test-reports/k6-load-test.md`).
- Lighthouse `npm run lighthouse`: 8 trang chính đều ≥ 85 ở Performance, Accessibility và Best practices (`docs/test-reports/lighthouse/`).
- E2E `p9-responsive` (@mobile, Pixel 7): các màn kho, kiểm thực, lưu mẫu, thực đơn công bố, phê duyệt không bị cuộn ngang, form lưu mẫu dùng được trên điện thoại. E2E `zz-docs-screenshots`: ảnh cho HDSD.
- Mẫu nhập Excel cho 22 danh mục `tools/import-templates/mau-nhap-*.xlsx`, sinh bằng integration test.
- Tài liệu: SRS (01), thiết kế hệ thống bổ sung API/màn hình/báo cáo/job/triển khai (02), kịch bản kiểm thử (04), hướng dẫn cài đặt (05), HDSD chung + 7 vai trò (06), tài liệu đào tạo (07), quy trình vận hành – bảo hành (08).
- `gen-secrets` ở chế độ production sinh mật khẩu admin ngẫu nhiên thay cho mật khẩu mặc định đã công bố.
- Kiểm thử: unit 117, integration 81. Bộ E2E đầy đủ (54 test) chưa chạy lại sau P9 vì lần chạy bị ngắt do máy thiếu RAM.

## [0.9.0] - 2026-11-09
### P8 – Gia cố an toàn thông tin cấp độ 2 (SPEC §10)
- Tài liệu `docs/03_ThietKe_ATTT_CapDo2.md`: ánh xạ từng yêu cầu → biện pháp kỹ thuật → vị trí mã nguồn → cách kiểm thử.
- Sao lưu: màn hình lịch sử (định kỳ + bất thường), nút sao lưu bất thường chạy qua Quartz (worker → sidecar backup), tải bản sao lưu sau khi xác thực lại bằng mật khẩu + MFA (token tải dùng một lần, 2 phút), ghi audit `Backup`.
- Diễn tập khôi phục tự động `scripts/test-backup-restore.(sh|ps1)`: backup → restore vào DB tạm → so số bản ghi 12 bảng chính (báo cáo `docs/test-reports/restore-drill.txt` — PASSED).
- OWASP ZAP baseline `scripts/zap-baseline.(sh|ps1)` (quy tắc bỏ qua có lý do trong `deploy/zap/baseline-rules.tsv`): **0 High, 0 Medium** (báo cáo `docs/test-reports/zap/`).
- Header bảo mật gom vào `deploy/nginx/security-headers.conf`, áp cho mọi `location` (trước đây file tĩnh thiếu Permissions-Policy); thêm COEP `require-corp`, CORP `same-origin`; nginx ẩn bản header trùng từ API.
- Job housekeeping 03:30 (trước đây chỉ có lịch, chưa có cài đặt): xóa file xuất báo cáo hết hạn, thông báo > 180 ngày, token một lần hết hạn; tạo trước / xóa phân vùng `audit_log` quá hạn lưu (≥ 12 tháng) bằng kết nối owner — role ứng dụng vẫn không có quyền xóa nhật ký.
- Xác thực lại (`ReauthenticateAsync`) chống dùng lại mã TOTP; audit đúng loại hành động: `Export` khi xuất báo cáo, `SyncRun` khi đồng bộ VDD, `ConfigChange` khi đổi ngưỡng cảnh báo.
- Sửa: thư mục `requests` của sidecar backup dùng `0777` thay `1777` (sticky bit khiến sidecar không nhận được yêu cầu do worker tạo).
- Kiểm thử bổ sung: giới hạn phiên đồng thời, hết phiên khi không hoạt động, mật khẩu hết hạn → phiên hạn chế, chống replay TOTP khi đăng nhập, upload sai chữ ký nội dung bị từ chối, xuất/ẩn danh dữ liệu cá nhân có ghi nhật ký; E2E P8 (header, WAF chặn SQLi, sao lưu bất thường). Tổng: unit 111, integration 80.

## [0.8.0] - 2026-11-09
### P7 – Tích hợp dữ liệu Viện Dinh dưỡng và API công khai (SPEC §7.10)
- `INutritionDataProvider` + `VddApiNutritionDataProvider`: HttpClient, Polly (retry lũy thừa có jitter, circuit breaker, timeout theo kết nối), xác thực API key / Basic / OAuth2 client credentials (token được cache); credential lưu mã hóa, chỉ ghi (API không trả lại secret).
- Tầng ánh xạ cấu hình bằng `mapping_json`: đường dẫn API, tham số truy vấn, tên trường, bảng mã chất (INFOODS → mã nội bộ), mã nhóm, hệ số quy đổi đơn vị, tiền tố mã — đổi đặc tả chỉ cần sửa JSON.
- Mock cổng VDD `tools/mock-vdd-api` (docker dev cổng 5090, README mô tả hợp đồng giả định), tự cấu hình kết nối `VDD-MOCK` trong môi trường dev.
- Đồng bộ tăng dần theo `modifiedSince`, upsert theo mã cho chất dinh dưỡng, nhóm thực phẩm, thực phẩm (khóa sửa mặc định), khuyến nghị; bản ghi sửa tay mà nguồn cũng đổi tạo `sync_conflict`; màn hình xung đột so sánh song song từng trường, giữ bản đang dùng / lấy dữ liệu nguồn; lịch sử đồng bộ chi tiết từng request; job Quartz 02:00 + nút đồng bộ ngay; đẩy báo cáo tổng hợp (tùy chọn).
- API công khai `/api/v1/public/*`: token OAuth2 client credentials do BlueMeal phát hành (audience riêng), scope `menu.read` / `nutrition.read` / `food.read`, JSON hoặc XML theo `Accept`, FHIR R4 tối giản; quản lý client (secret hiển thị một lần, đổi secret, bật/tắt); tài liệu `docs/api/openapi.json` sinh tự động từ API.
- Kiểm thử: unit 111 (6 test mapping), integration 72 (4 test tích hợp: đồng bộ mock, xung đột và xử lý, retry, các kiểu xác thực, API công khai JSON/XML/FHIR, xuất OpenAPI), E2E P7.

## [0.7.0] - 2026-11-09
### P6 – Phân tích, cảnh báo, chi phí, báo cáo, dashboard (SPEC §7.5–7.7, §7.9)
- Phân tích dinh dưỡng cho 1 món / 1 thực đơn / khoảng ngày: bảng đầy đủ chất dinh dưỡng, % đáp ứng (thanh màu + biểu tượng), P:L:G, biểu đồ vi chất, xu hướng năng lượng theo ngày, trung bình tuần/tháng, so sánh 2 phương án, xuất Excel.
- Trung tâm cảnh báo `/alerts`: lọc theo mức/loại/trạng thái, tiếp nhận / đánh dấu đã xử lý kèm ghi chú, chạy kiểm tra ngay; màn cấu hình ngưỡng (quyền `alert.config`, có kiểm tra hợp lệ và ghi audit).
- Bảng tính chi phí suất ăn theo ngày/tuần/tháng × khách hàng: số suất, chi phí kế hoạch, chi phí thực tế (phiếu xuất đã ghi sổ + chi phí chung), doanh thu theo đơn giá thỏa thuận, chênh lệch, lãi gộp, biểu đồ kế hoạch – thực tế.
- 16 báo cáo RPT-01…RPT-16 dùng chung một khung: bộ lọc theo từng báo cáo, xem trên web (2.000 dòng đầu, có dòng tổng), xuất Excel ClosedXML có tiêu đề đơn vị, kỳ báo cáo, người lập, ngày lập, khối chữ ký và định dạng số Việt Nam; báo cáo trên 3.000 dòng tự chạy nền qua `export_job` (Quartz) và gửi thông báo khi xong; lưu bộ lọc yêu thích theo người dùng; tôn trọng phạm vi dữ liệu kho/khách hàng kể cả khi chạy nền.
- Dashboard trang chủ theo quyền: thực đơn chờ duyệt, thực đơn nháp, cảnh báo nghiêm trọng, số suất hôm nay theo ca, chi phí/suất tuần này so với ngân sách, % đáp ứng năng lượng 7 ngày, lô sắp hết hạn, mẫu lưu cần hủy.
- Sửa: mốc ngày giờ (+07) gửi xuống PostgreSQL được chuyển sang UTC (lọc theo ngày ở kiểm thực/lưu mẫu trước đây lỗi).
- Kiểm thử: integration 68 (6 test báo cáo: chạy + xuất đủ 16 báo cáo, số liệu RPT-02/04 khớp sổ cái, phạm vi kho, xuất nền, bộ lọc yêu thích, phân tích/chi phí/dashboard), E2E P6.

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
