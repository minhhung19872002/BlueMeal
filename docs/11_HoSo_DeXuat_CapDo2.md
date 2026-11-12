# HỒ SƠ ĐỀ XUẤT CẤP ĐỘ AN NINH MẠNG — HỆ THỐNG BlueMeal (BẢN DỰ THẢO)

*Nhà thầu soạn dự thảo để Viện Dinh dưỡng hoàn thiện và trình thẩm định. Lập ngày 12/11/2026, khớp BlueMeal v1.0.0.*

Căn cứ:
- Luật An ninh mạng 2025.
- Nghị định 331/2026/NĐ-CP. Hồ sơ đề xuất cấp độ gồm các thành phần nêu tại Đ21. Với cấp độ 1–2, đơn vị chuyên trách an ninh mạng của chủ quản thẩm định và chủ quản phê duyệt (Đ18 khoản 1).
- TCVN 14423:2026.

Các mục ghi **[VIỆN ĐIỀN: …]** là thông tin chỉ chủ quản có. Trước khi trình, bộ phận an ninh mạng của Viện đối chiếu lại mẫu biểu và thành phần hồ sơ với văn bản gốc của Nghị định 331/2026/NĐ-CP.

| Thành phần hồ sơ (NĐ 331/2026 Đ21) | Nội dung trong bộ bàn giao |
|---|---|
| 1. Tài liệu mô tả tổng quan hệ thống | Mục 1 dưới đây |
| 2. Tài liệu thiết kế hệ thống | Mục 2: `01_SRS_DacTaYeuCau.md`, `02_ThietKe_HeThong.md`, `03_ThietKe_ATTT_CapDo2.md` |
| 3. Thuyết minh đề xuất cấp độ | Mục 3 |
| 4. Phương án bảo đảm an ninh mạng | Mục 4 và `10_DoiChieu_TieuChuan_PhapLy.md` §4 |
| 5. Ý kiến của chuyên gia/tổ chức chuyên môn | Không áp dụng: chỉ bắt buộc với cấp độ 4 và 5 |

## 1. Mô tả tổng quan hệ thống

| Thông tin | Nội dung |
|---|---|
| Tên hệ thống | Phần mềm quản lý bữa ăn ca thông minh **BlueMeal** |
| Chủ quản hệ thống | Viện Dinh dưỡng — Bộ Y tế |
| Đơn vị vận hành | [VIỆN ĐIỀN: phòng/trung tâm phụ trách CNTT] |
| Đơn vị chuyên trách an ninh mạng (thẩm định) | [VIỆN ĐIỀN] |
| Người phụ trách vận hành / người dự phòng | [VIỆN ĐIỀN: họ tên, chức vụ, liên hệ] |
| Nhà thầu phát triển, bảo hành | [VIỆN ĐIỀN: tên nhà thầu theo hợp đồng gói IB2600574672] |
| Mục tiêu | Quản lý bữa ăn ca: danh mục thực phẩm, món ăn, công thức; lập thực đơn cân đối dinh dưỡng; phê duyệt; quản lý kho, ATTP; tính chi phí; báo cáo; liên thông dữ liệu với cổng dữ liệu của Viện |
| Phạm vi phục vụ | Nội bộ Viện và các đơn vị khách hàng đặt suất ăn (mỗi đơn vị chỉ xem dữ liệu của mình) |
| Người dùng | [VIỆN ĐIỀN: số tài khoản dự kiến]; thiết kế cho ≥ 1.000 người dùng đồng thời. 8 nhóm quyền mặc định: quản trị, quản lý, dinh dưỡng, người duyệt thực đơn, bếp trưởng, thủ kho, kế toán, đại diện khách hàng |
| Vị trí lắp đặt | [VIỆN ĐIỀN: phòng máy chủ, địa chỉ]; triển khai tại chỗ (on-premise), không dùng dịch vụ đám mây bên ngoài |

### 1.1 Thành phần

| Thành phần | Mô tả |
|---|---|
| Máy chủ | [VIỆN ĐIỀN: tên, IP, cấu hình]. Khuyến nghị 12–16 vCPU, 16 GB RAM, 200 GB SSD; Linux x64 (`05` §1) |
| Lưu trữ sao lưu | [VIỆN ĐIỀN: NAS/ổ riêng, đường dẫn mirror] |
| Phần mềm nền | Docker Engine ≥ 24, Docker Compose v2 |
| Thành phần ứng dụng (container) | `web` (nginx + ModSecurity WAF, cổng duy nhất mở ra ngoài), `api` ×2 (ASP.NET Core 8), `worker` (job nền), `postgres` 16, `redis` 7.2, `backup` |
| Phần mềm bên thứ ba | `THIRD_PARTY_LICENSES.md` |

### 1.2 Sơ đồ logic và kết nối

Xem `02_ThietKe_HeThong.md` §1 và `03_ThietKe_ATTT_CapDo2.md` §1. Kết nối ra ngoài máy chủ:

| Kết nối | Chiều | Giao thức | Mục đích |
|---|---|---|---|
| Người dùng → `web` | Vào | HTTPS (TLS 1.2+) | Giao diện và API |
| Hệ thống khác → API công khai | Vào | HTTPS, OAuth2 client credentials | Đọc thực đơn, dinh dưỡng, thực phẩm (JSON/XML/FHIR); không có dữ liệu cá nhân |
| `worker` → cổng dữ liệu Viện Dinh dưỡng | Ra | HTTPS | Đồng bộ dữ liệu thành phần dinh dưỡng |
| `api` → máy chủ thư | Ra | SMTP (STARTTLS/SSL) | OTP, đặt lại mật khẩu, thông báo |
| Mọi thành phần → máy thu log/SIEM | Ra | Syslog RFC 5424 (UDP/TCP) | Nhật ký tập trung |
| Quản trị viên → máy chủ | Vào | SSH qua VPN | Quản trị hệ điều hành (do Viện quản lý) |

### 1.3 Dữ liệu xử lý

| Loại | Ví dụ | Mức nhạy cảm (TCVN 14423 §4.4.2.1) |
|---|---|---|
| Dữ liệu cá nhân nhân viên | Họ tên, đơn vị, SĐT, email, CCCD (tùy chọn), tài khoản | Hạn chế truy cập — mã hóa AES-256-GCM, che khi không có quyền `pii.view` |
| Thông tin xác thực | Mật khẩu, bí mật MFA, mã khôi phục, khóa tích hợp | Hạn chế truy cập — băm PBKDF2/mã hóa |
| Dữ liệu nghiệp vụ | Thực đơn, công thức, kho, chi phí, ATTP, đối tác, khách hàng | Nội bộ |
| Thực đơn đã công bố, thành phần dinh dưỡng | Thông tin cung cấp cho khách hàng và hệ thống khác | Công khai trong phạm vi được cấp quyền |

Hệ thống **không** xử lý bí mật nhà nước, dữ liệu sức khỏe hay dữ liệu bệnh nhân.

## 2. Tài liệu thiết kế

| Tài liệu | Nội dung |
|---|---|
| `01_SRS_DacTaYeuCau.md` | Đặc tả yêu cầu, vai trò, ca sử dụng |
| `02_ThietKe_HeThong.md` | Kiến trúc, phân hệ, luồng dữ liệu, ERD, API, màn hình, job nền, triển khai |
| `03_ThietKe_ATTT_CapDo2.md` | Thiết kế an ninh mạng: yêu cầu → biện pháp → vị trí mã nguồn → kiểm thử |
| `04_KichBan_KiemThu.md`, `test-reports/` | Kịch bản và kết quả kiểm thử chức năng, hiệu năng, bảo mật |
| `05_HuongDan_CaiDat.md` | Cài đặt, cấu hình, sao lưu – khôi phục, nhật ký |
| `api/openapi.json` | Đặc tả API |

## 3. Thuyết minh đề xuất cấp độ

**Đề xuất: cấp độ 2.**

| Tiêu chí (NĐ 331/2026 Đ12) | Đánh giá BlueMeal |
|---|---|
| Phục vụ hoạt động nội bộ của cơ quan, tổ chức | Có: quản lý bếp ăn ca của Viện. Đơn vị khách hàng truy cập trong phạm vi hợp đồng đặt suất ăn |
| Có xử lý thông tin riêng, dữ liệu cá nhân | Có: dữ liệu cá nhân của nhân viên và đầu mối đối tác/khách hàng (mục 1.3) |
| Không xử lý bí mật nhà nước | Đúng |
| Dịch vụ trực tuyến (nếu có): dưới 100.000 chủ thể dữ liệu cơ bản | Chỉ có API công khai cho hệ thống khác, không có dữ liệu cá nhân. Số chủ thể dữ liệu cá nhân [VIỆN ĐIỀN: ước tính số nhân viên + đầu mối] nhỏ hơn rất nhiều ngưỡng 100.000 |

**Lý do không đề xuất cấp độ 1:** hệ thống xử lý dữ liệu cá nhân.
**Lý do không đề xuất cấp độ 3 trở lên:** hệ thống không phục vụ người dân/doanh nghiệp trên diện rộng, không xử lý bí mật nhà nước, không là hạ tầng dùng chung hay hệ thống điều khiển. Sự cố chỉ ảnh hưởng tới hoạt động bếp ăn nội bộ, có phương án thủ công thay thế ngắn hạn và khôi phục từ bản sao lưu (RPO ≤ 24 giờ).
[VIỆN ĐIỀN: đối chiếu thêm với tiêu chí cấp độ 3 trong Nghị định 331/2026/NĐ-CP nếu bộ phận thẩm định yêu cầu.]

## 4. Phương án bảo đảm an ninh mạng

Phương án gồm hai phần: biện pháp kỹ thuật đã có trong phần mềm và biện pháp quản lý, vận hành do Viện thực hiện.
Đối chiếu đầy đủ với 15 nhóm yêu cầu của TCVN 14423:2026 §4 nằm ở `10_DoiChieu_TieuChuan_PhapLy.md` §4.2.

### 4.1 Biện pháp kỹ thuật đã có trong phần mềm

| Nhóm | Biện pháp | Chi tiết |
|---|---|---|
| Xác thực | Mật khẩu ≥ 10 ký tự đủ 4 loại ký tự, không trùng 5 mật khẩu gần nhất, hết hạn 90 ngày; MFA TOTP bắt buộc cho mọi tài khoản; khóa sau 5 lần sai; CAPTCHA sau 3 lần sai | `03` §2 |
| Phiên | Hết phiên khi không hoạt động (đặt 15 phút khi vận hành), giới hạn phiên đồng thời, thu hồi phiên khi đổi mật khẩu/khóa tài khoản | `03` §2 |
| Kiểm soát truy cập | RBAC mặc định từ chối trên mọi API, phạm vi dữ liệu theo kho/khách hàng, chống IDOR | `03` §3 |
| Nhật ký | `audit_log` chỉ thêm, lưu ≥ 12 tháng; log ứng dụng, nginx/WAF, PostgreSQL gửi tập trung về syslog/SIEM | `03` §4 |
| Mã hóa | TLS 1.2+/HSTS; AES-256-GCM cho dữ liệu cá nhân và bí mật; bản sao lưu mã hóa AES-256 | `03` §5 |
| Chống tấn công | WAF ModSecurity + OWASP CRS; rate limit; CSP và header bảo mật; chống CSRF, SQLi, XSS; kiểm tra tệp tải lên | `03` §6 |
| Dữ liệu cá nhân | Che dữ liệu, nhật ký truy cập, xuất dữ liệu và ẩn danh hóa theo yêu cầu | `03` §7 |
| Sao lưu | Tự động hằng ngày, giữ 7/4/6, mirror NAS, diễn tập khôi phục tự động | `03` §8 |
| Kiểm thử | Unit 134, integration 96, E2E 68/68, ZAP baseline 0 High/0 Medium, diễn tập khôi phục PASSED | `04`, `09` |

### 4.2 Biện pháp quản lý, vận hành do Viện thực hiện

| TCVN 14423:2026 | Việc cần làm | Người/bộ phận | Thời hạn |
|---|---|---|---|
| §4.1 | Ban hành quy trình quản lý rủi ro an ninh mạng, rà soát hằng năm | [VIỆN ĐIỀN] | [VIỆN ĐIỀN] |
| §4.2, §4.3 | Đưa máy chủ, NAS và BlueMeal vào danh mục tài sản phần cứng/phần mềm; ký cam kết bảo mật với nhà thầu | [VIỆN ĐIỀN] | Trước vận hành |
| §4.4 | Phân loại tài sản thông tin theo mục 1.3; rà soát phân quyền hằng năm; mã hóa ổ đĩa máy chủ | [VIỆN ĐIỀN] | Trước vận hành |
| §4.5 | Cấu hình an toàn máy chủ; đặt hết phiên 15 phút trong BlueMeal | [VIỆN ĐIỀN] | Khi cài đặt |
| §4.6 | Mỗi người một tài khoản; tài khoản không hoạt động 45 ngày bị hệ thống tự khóa, quản trị xem lại; thu hồi quyền khi nghỉ việc; rà soát hằng năm (`08` §4) | Quản trị BlueMeal | Hằng tháng / hằng năm |
| §4.7 | Vá hệ điều hành/Docker hằng tháng; quét lỗ hổng hằng năm; cập nhật bản vá BlueMeal (`08` §5) | [VIỆN ĐIỀN] + nhà thầu | Hằng tháng / hằng năm |
| §4.8 | Máy thu log/SIEM ([VIỆN ĐIỀN: địa chỉ]); đồng bộ NTP; rà soát nhật ký | [VIỆN ĐIỀN] | Khi cài đặt / hằng năm |
| §4.9, §4.10 | Trình duyệt được phép và cập nhật; phần mềm phòng chống mã độc cho máy chủ, máy trạm | [VIỆN ĐIỀN] | Thường xuyên |
| §4.11 | Lưu bản sao ở hạ tầng tách biệt; người giữ `BACKUP_PASSPHRASE` tách khỏi quản trị ứng dụng; diễn tập khôi phục hằng quý | [VIỆN ĐIỀN] | Hằng quý |
| §4.12 | Sơ đồ mạng, phân vùng; tường lửa có IPS chỉ mở 80/443; VPN + xác thực cho quản trị từ xa | [VIỆN ĐIỀN] | Trước vận hành |
| §4.13 | Phân công người phụ trách vận hành và an ninh mạng; đào tạo nhận thức hằng năm (`07`) | [VIỆN ĐIỀN] | Hằng năm |
| §4.14 | Đưa nhà thầu vào danh sách nhà cung cấp, xác định phạm vi trách nhiệm bảo hành (`08` §6) | [VIỆN ĐIỀN] | Trước vận hành |
| §4.15 | Người chủ chốt và người dự phòng ứng phó sự cố; đầu mối với cơ quan chức năng; quy trình `08` §3 | [VIỆN ĐIỀN] | Trước vận hành |
| Phụ lục A | An toàn vật lý phòng máy chủ | [VIỆN ĐIỀN] | — |

## 5. Trình tự thẩm định và phê duyệt (đề xuất)

1. Đơn vị vận hành hoàn thiện các mục [VIỆN ĐIỀN] và lập văn bản đề nghị thẩm định.
2. Đơn vị chuyên trách an ninh mạng của Viện thẩm định: đối chiếu mục 3 với tiêu chí cấp độ và mục 4 với TCVN 14423:2026 §4.
3. Lãnh đạo Viện ký quyết định phê duyệt cấp độ 2 và phương án bảo đảm an ninh mạng.
4. Triển khai phương án; kiểm tra, đánh giá định kỳ theo quy định. Khi hệ thống thay đổi (thêm kết nối ra ngoài, mở cho Internet, tăng loại dữ liệu), đánh giá lại cấp độ.
