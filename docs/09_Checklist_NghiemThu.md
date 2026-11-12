# CHECKLIST NGHIỆM THU — BlueMeal v1.0.0

Đối chiếu SPEC §15 (Chương V E-HSMT). Mỗi dòng ghi bằng chứng kiểm chứng được trong bộ bàn giao: tài liệu, mã nguồn, test tự động, báo cáo.

Trạng thái:
- **Đạt**: đã có và đã kiểm thử.
- **Đạt (cần xác nhận hiện trường)**: phần mềm và tài liệu đã sẵn sàng, việc còn lại phải làm cùng chủ đầu tư.
- **Chưa đạt**: còn tồn tại, có mô tả rõ.

| # | Hạng mục | Bằng chứng | Trạng thái |
|---|---|---|---|
| 1 | Khảo sát và thiết kế: SRS/BRD, thiết kế hệ thống, thiết kế ATTT cấp độ 2 | `01_SRS_DacTaYeuCau.md`, `02_ThietKe_HeThong.md` (kiến trúc, ERD, API, màn hình, triển khai), `03_ThietKe_ATTT_CapDo2.md` (căn cứ cập nhật theo Luật An ninh mạng 2025, NĐ 331/2026, TCVN 14423:2026, Luật 91/2025) | Đạt |
| 2 | Danh mục chung (quốc gia, đơn vị hành chính, đơn vị đo) | Màn hình Danh mục chung; mẫu nhập `tools/import-templates/mau-nhap-{countries,provinces,wards,units}.xlsx`; E2E `p2-catalogs` | Đạt |
| 3 | Nhân viên, phân quyền nhân viên, đối tượng | Danh mục nhân viên, bộ phận, nhóm đối tượng; ma trận quyền vai trò × chức năng; test ma trận quyền (integration) | Đạt |
| 4 | Loại đối tác / đối tác (tên, liên hệ, hợp đồng…) | Danh mục loại đối tác, đối tác; mẫu nhập `mau-nhap-partner-types.xlsx`, `mau-nhap-partners.xlsx` | Đạt |
| 5 | Loại khách hàng / khách hàng (đơn vị đặt suất ăn) | Danh mục khách hàng (ảnh `images/customers.png`); vai trò Khách hàng chỉ xem dữ liệu của mình (`06_HDSD_KhachHang.md`) | Đạt |
| 6 | Thông tin đơn vị, nhóm tài khoản, tài khoản (thêm/sửa/khóa), phiên làm việc, danh sách chức năng, phân quyền, phiên bản hệ thống, đăng nhập/đăng xuất/quên mật khẩu tự phục vụ | Màn hình Quản trị (người dùng, vai trò, phiên, nhật ký, cấu hình, phiên bản); E2E `p1-admin`; integration: lockout, MFA, quên mật khẩu qua Mailpit, giới hạn phiên | Đạt |
| 7 | Kho; phân nhóm thực phẩm; thực phẩm; ATTP và chất lượng; nguyên liệu; món ăn/thành phẩm; công thức; khuyến nghị; nhóm báo cáo | E2E `p2-catalogs`, `p3-recipes`, `p5-stock`; test NutritionEngine, CostEngine, WarningEngine (≥ 30 case tính tay) | Đạt |
| 8 | Kế hoạch thực đơn tuần/tháng; danh sách thực đơn; tạo tự động cân đối dinh dưỡng; tạo thủ công; phê duyệt trước khi áp dụng | E2E `p4-menus` (tạo tự động → sửa → gửi → duyệt → công bố); test MenuGenerator (sinh 1 tháng < 5 s) | Đạt |
| 9 | Tính và phân tích thành phần dinh dưỡng | Màn hình Phân tích dinh dưỡng (món / thực đơn / khoảng ngày, % đáp ứng, P:L:G, so sánh phương án); E2E `p6-reports` | Đạt |
| 10 | Cảnh báo chỉ tiêu | Trung tâm cảnh báo, cấu hình ngưỡng; job kiểm tra hết hạn; test WarningEngine | Đạt |
| 11 | Tính chi phí suất ăn | CostEngine, báo cáo chi phí; test đơn vị | Đạt |
| 12 | Báo cáo nhập/xuất thực phẩm; kiểm kê kho; khuyến nghị dinh dưỡng; xuất Excel | 16 báo cáo RPT-01…RPT-16 xuất Excel có tiêu đề và chữ ký (`02_ThietKe_HeThong.md` §6.2); integration `ReportTests` | Đạt |
| 13 | Tích hợp API với cổng dữ liệu Viện Dinh dưỡng | `VddApiNutritionDataProvider` + ánh xạ cấu hình JSON, mock `tools/mock-vdd-api`, màn hình đồng bộ/xung đột, API công khai JSON/XML/FHIR (`api/openapi.json`); E2E `p7-integration`. Đủ 7 yêu cầu SPEC §7.10. SPEC xác định đặc tả API thật **chưa có**, nên phạm vi là khung tích hợp + mock + ánh xạ cấu hình. Khi Viện cung cấp đặc tả, chỉ cần chỉnh `mapping_json` (A-25), không sửa code | Đạt |
| 14 | ATTT: MFA, kiểm soát truy cập, nhật ký tập trung, mã hóa lưu trữ và truyền, chống tấn công ứng dụng, bảo vệ dữ liệu cá nhân, sao lưu tự động/bất thường và phục hồi | `03_ThietKe_ATTT_CapDo2.md`; MFA bắt buộc cho mọi tài khoản (mặc định); nhật ký tập trung gửi syslog/SIEM từ ứng dụng, nginx/ModSecurity, PostgreSQL (sink ứng dụng: integration `SyslogSinkTests` UDP/TCP; overlay `deploy/docker-compose.syslog.yml` đã kiểm tra cấu hình — kết nối máy thu thật của Viện xác nhận khi cài đặt); ZAP baseline 0 High / 0 Medium (`test-reports/zap/`); diễn tập khôi phục PASSED (`test-reports/restore-drill.txt`); E2E `p8-security` (chạy cả trên bản production HTTPS) | Đạt (cần xác nhận hiện trường: kết nối máy thu nhật ký của Viện) |
| 15 | Kiểm thử chức năng, hiệu năng, bảo mật có báo cáo | `04_KichBan_KiemThu.md` §5; unit 126, integration 92, E2E 54/54 (`test-reports/e2e/`), Lighthouse ≥ 85 (`test-reports/lighthouse/`), ZAP, k6 (`test-reports/k6-load-test.md`) | Đạt |
| 16 | Cài đặt trên hạ tầng của chủ đầu tư; chuyển đổi/nhập dữ liệu ban đầu (Excel import) | `05_HuongDan_CaiDat.md` (Ubuntu / Windows Server, có Internet / offline); gói offline `scripts/save-images` + `load-images` đã thử (5 image, kiểm tra SHA-256, không chứa bí mật); overlay production đã chạy thử (FS chỉ đọc, non-root, TLS, 2 API); nhập Excel cho 22 danh mục, công thức món ăn và tồn kho đầu kỳ, kèm 24 file mẫu (`tools/import-templates/`) | Đạt (cần xác nhận hiện trường) |
| 17 | Song ngữ Việt/Anh; ≥ 1.000 người dùng đồng thời; Chrome, Cốc Cốc, Edge, Safari; Windows, Linux, iOS, Android | Song ngữ: 1 463 khóa vi/en đồng bộ (`npm run i18n:check`). Responsive: E2E project `mobile` (Pixel 7). Trình duyệt: E2E Chromium; Cốc Cốc/Edge cùng nhân Chromium; Safari/iOS: E2E trên WebKit (Desktop Safari + iPhone 14) 21/21 đạt (`PW_WEBKIT=1`, `04` §6). **1 000 người dùng đồng thời**: k6 1 000 VU giữ 10 phút, p95 đọc 628 ms, 0 % lỗi (`test-reports/k6-load-test.md`) | Đạt |
| 18 | Đào tạo theo vai trò, tài liệu HDSD, biên bản đào tạo | `06_HDSD_00_ChungChoMoiNguoi.md` + 7 HDSD theo vai trò có ảnh chụp tự động (`images/`); `07_TaiLieu_DaoTao.md` (giáo án, bài tập, mẫu biên bản §5) | Đạt (buổi đào tạo và ký biên bản thực hiện tại hiện trường) |
| 19 | Bàn giao mã nguồn, CSDL, tài liệu; danh sách license bên thứ ba | Kho mã nguồn (README), migration EF Core + `deploy/postgres/init.sql`, tài liệu `docs/`, `THIRD_PARTY_LICENSES.md` (NuGet, npm 165 gói, image Docker) | Đạt |
| 20 | Tiêu chuẩn kỹ thuật theo TT 39/2017/TT-BTTTT và TT 13/2025/TT-BYT (HSMT §1.3.3); bảo vệ dữ liệu cá nhân theo Luật 91/2025 | `10_DoiChieu_TieuChuan_PhapLy.md` §2, §3, §5: đối chiếu từng tiêu chuẩn. Đáp ứng các tiêu chuẩn kết nối, tích hợp, truy cập, an toàn áp dụng cho hệ thống: xuất CSV UTF-8 (danh mục, báo cáo, nhật ký), XSD cho XML của API công khai (`api/public-api.xsd`, `GET /api/v1/public/schema.xsd`), thông báo xử lý dữ liệu cá nhân và ghi nhận đồng ý; integration `ReportTests` (CSV), `IntegrationTests` (XML hợp lệ theo XSD), `PrivacyAndInactiveAccountTests`; E2E `p1-admin` | Đạt (Viện rà soát nội dung thông báo dữ liệu cá nhân mẫu) |
| 21 | An ninh mạng cấp độ 2 theo NĐ 331/2026 và TCVN 14423:2026 | `10_DoiChieu_TieuChuan_PhapLy.md` §4 (15 nhóm yêu cầu, tách phần mềm / đơn vị vận hành); dự thảo hồ sơ đề xuất cấp độ `11_HoSo_DeXuat_CapDo2.md` | Đạt (cần xác nhận hiện trường: Viện điền thông tin, thẩm định và phê duyệt) |

## Tồn tại và kế hoạch xử lý

1. **Hiệu năng (dòng 17)**: đã đạt trên máy phát triển. Khi nghiệm thu nên chạy lại `scripts/load-test.sh 1000 10m` trên hạ tầng thật để xác nhận.
2. **Sau bàn giao (không thuộc phạm vi SPEC §7.10)**: khi Viện Dinh dưỡng cung cấp đặc tả API chính thức, cấu hình kết nối và `mapping_json`, chạy đồng bộ thử.
3. **Hiện trường (dòng 16, 18)**: cài đặt, nhập dữ liệu ban đầu, đào tạo và ký biên bản theo `05`, `07`.
4. **Tiêu chuẩn, pháp lý (dòng 20; chi tiết `10` §6)**: đã bổ sung thông báo xử lý dữ liệu cá nhân và ghi nhận đồng ý (Luật 91/2025), xuất CSV UTF-8 và XSD cho XML của API công khai (TT 39/2017). Bộ phận pháp chế của Viện rà soát nội dung thông báo mẫu (*Cấu hình hệ thống → Bảo mật*) trước khi vận hành; nếu xác định không cần sự đồng ý thì tắt.
5. **Cấp độ 2 (dòng 21)**: Viện điền các mục [VIỆN ĐIỀN] trong `11`, thẩm định và phê duyệt. Khi vận hành chính thức:
   - Đặt hết phiên 15 phút.
   - Giữ *Khóa tài khoản không đăng nhập sau* = 45 ngày (mặc định; hệ thống tự khóa hằng đêm).
6. **Kiểm thử độc lập (HSMT)**: nhà thầu đã tự kiểm thử chức năng, hiệu năng, bảo mật (dòng 15). Kiểm thử bởi đơn vị kiểm thử phần mềm và đơn vị kiểm thử ATTT chuyên ngành do Viện chỉ định; nhà thầu phối hợp và khắc phục.
7. **Hồ sơ năng lực nhà thầu (HSMT §1.3.1)**: chứng chỉ ISO 13485/9001/27001 thuộc hồ sơ dự thầu, không thuộc sản phẩm phần mềm.
