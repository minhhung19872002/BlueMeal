# ĐỐI CHIẾU TIÊU CHUẨN KỸ THUẬT VÀ QUY ĐỊNH PHÁP LÝ — BlueMeal

*Phần mềm quản lý bữa ăn ca thông minh — Gói thầu IB2600574672 — Viện Dinh dưỡng (Bộ Y tế). Lập ngày 12/11/2026, khớp BlueMeal v1.0.0.*

Tài liệu này trả lời hai câu hỏi của chủ đầu tư:

1. HSMT §1.3.3 yêu cầu "sử dụng các tiêu chuẩn kỹ thuật kết nối, tích hợp dữ liệu, truy cập thông tin và an toàn thông tin theo Thông tư 39/2017/TT-BTTTT và Thông tư 13/2025/TT-BYT". BlueMeal áp dụng các tiêu chuẩn đó đến đâu?
2. Theo các văn bản đang có hiệu lực tại ngày lập tài liệu, BlueMeal đáp ứng yêu cầu an ninh mạng cấp độ 2 và bảo vệ dữ liệu cá nhân đến đâu?

Mỗi dòng có một trạng thái:

| Trạng thái | Nghĩa |
|---|---|
| **Đáp ứng** | Phần mềm đã có, có bằng chứng (mã nguồn, cấu hình, test, báo cáo) |
| **Một phần** | Đã có nhưng chưa đủ, phần còn thiếu ghi trong cột ghi chú |
| **Vận hành** | Thuộc trách nhiệm của đơn vị vận hành (hạ tầng, mạng, quy chế, con người); phần mềm hỗ trợ ở mức ghi chú |
| **Không áp dụng** | BlueMeal không có chức năng tương ứng (ví dụ GIS, Wi‑Fi, thư điện tử) |
| **Chưa đáp ứng** | Còn thiếu, có đề xuất ở mục 6 |

Chi tiết từng biện pháp kỹ thuật (vị trí mã nguồn, tên test) nằm ở `03_ThietKe_ATTT_CapDo2.md`. Tài liệu này chỉ dẫn chiếu sang đó, không lặp lại.

> **Lưu ý:** số điều khoản của các văn bản ban hành năm 2025–2026 được trích từ văn bản và bản tóm tắt công khai tại ngày lập tài liệu.
> Trước khi đưa vào hồ sơ chính thức, bộ phận pháp chế/an ninh mạng của Viện nên đối chiếu lại với văn bản gốc trên Công báo.

## 1. Căn cứ pháp lý: các văn bản đã thay thế

SPEC và bản thiết kế ATTT ban đầu viện dẫn một số văn bản đã hết hiệu lực trong năm 2026. Bảng dưới là căn cứ cập nhật mà bộ tài liệu bàn giao dùng từ nay.

| Văn bản cũ (SPEC/HSMT viện dẫn) | Văn bản đang có hiệu lực | Hiệu lực | Ảnh hưởng tới BlueMeal |
|---|---|---|---|
| Luật An toàn thông tin mạng 86/2015/QH13; Luật An ninh mạng 24/2018/QH14 | **Luật An ninh mạng 2025** (hợp nhất hai luật cũ) | 01/07/2026 | Đổi tên gọi "an toàn thông tin theo cấp độ" thành "an ninh mạng theo cấp độ" (5 cấp) |
| Nghị định 85/2016/NĐ-CP (bảo đảm ATTT theo cấp độ) | **Nghị định 331/2026/NĐ-CP** | 19/08/2026 | Tiêu chí cấp độ (Đ12), thẩm quyền thẩm định (Đ18), thành phần hồ sơ (Đ21), yêu cầu kỹ thuật (Đ30), chuyển tiếp (Đ39). Xem mục 4 và `11_HoSo_DeXuat_CapDo2.md` |
| TCVN 11930:2017 (yêu cầu cơ bản về ATTT theo cấp độ); TT 12/2022/TT-BTTTT | **TCVN 14423:2026** "An ninh mạng – Hệ thống thông tin – Yêu cầu cơ bản" (công bố theo QĐ 3243/QĐ-BKHCN ngày 24/07/2026; thay TCVN 11930:2017 và TCVN 14423:2025) | 24/07/2026 | Bộ yêu cầu dùng để đối chiếu ở mục 4 (§4 – cấp độ 2) |
| Nghị định 13/2023/NĐ-CP (bảo vệ dữ liệu cá nhân) | **Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15**; **Nghị định 356/2025/NĐ-CP** hướng dẫn | 01/01/2026 | Xem mục 5 |
| Thông tư 39/2017/TT-BTTTT (danh mục tiêu chuẩn kỹ thuật ứng dụng CNTT) | *Không đổi* — vẫn có hiệu lực | 01/07/2018 | Xem mục 2 |
| Thông tư 46/2018/TT-BYT; tiêu chí CNTT của TT 54/2017/TT-BYT | **Thông tư 13/2025/TT-BYT** (hướng dẫn triển khai hồ sơ bệnh án điện tử) | 21/07/2025 | Xem mục 3 |

**Chuyển tiếp (NĐ 331/2026, Đ39):** hệ thống xây dựng trước 01/07/2026 được hoàn thiện thẩm định theo quy định cũ trong 6 tháng và phải đáp ứng tiêu chuẩn mới trong 12 tháng.
BlueMeal hoàn thành sau mốc này nên **áp dụng thẳng NĐ 331/2026 và TCVN 14423:2026**, không dùng quy định cũ.

## 2. Thông tư 39/2017/TT-BTTTT

Thông tư liệt kê tiêu chuẩn theo bốn nhóm, mỗi tiêu chuẩn là *Bắt buộc* (B) hoặc *Khuyến nghị* (K).
Một tiêu chuẩn bắt buộc chỉ ràng buộc khi hệ thống có chức năng tương ứng: phần mềm không có bản đồ thì không phải áp dụng GML/WMS, không có thư điện tử thì không phải áp dụng POP3/IMAP.
Các tiêu chuẩn **Không áp dụng** được gom vào cuối mỗi nhóm.

### 2.1 Tiêu chuẩn kết nối

| Tiêu chuẩn | B/K | Trạng thái | BlueMeal áp dụng thế nào |
|---|---|---|---|
| HTTP 1.1 | B | Đáp ứng | nginx và Kestrel phục vụ HTTP/1.1; mọi API là HTTP |
| HTTP 2.0 | K | Chưa bật | Có thể bật `http2 on;` trên khối `listen … ssl` của nginx khi cần; hiện chưa bật để giữ cấu hình WAF đã kiểm thử |
| RESTful | K | Đáp ứng | Toàn bộ API là REST, mô tả bằng OpenAPI 3 (`api/openapi.json`) |
| SOAP 1.2 / WSDL 2.0 / UDDI v3 | B (một, hai hoặc cả ba) | Không áp dụng | Nhóm này ràng buộc dịch vụ web *dạng SOAP*. BlueMeal chỉ cung cấp dịch vụ web dạng RESTful (nhóm khuyến nghị). Nếu hệ thống của Viện cần gọi qua SOAP, bổ sung lớp SOAP/WSDL trên cùng dữ liệu của API công khai |
| OData v4 | K | Không dùng | Truy vấn danh sách dùng tham số lọc/sắp xếp/phân trang riêng (`02` §5.1) |
| SMTP/MIME | B | Đáp ứng | Gửi thư (OTP, đặt lại mật khẩu, thông báo) qua máy chủ SMTP của Viện bằng MailKit |
| DNS, TCP, UDP, IPv4 | B | Đáp ứng | Mạng Docker và hệ điều hành; nhật ký tập trung gửi qua UDP hoặc TCP |
| IPv6 | B (khi kết nối Internet) | Vận hành | BlueMeal triển khai trong mạng nội bộ. Nếu công bố ra Internet bằng IPv6, bật IPv6 ở thiết bị biên/reverse proxy hoặc thêm `listen [::]:443 ssl;` cho container `web` |
| NTP v3/v4 | B (một trong hai) | Vận hành | Container dùng đồng hồ của máy chủ; máy chủ phải đồng bộ NTP (bắt buộc cho mã TOTP và đối chiếu nhật ký — TCVN 14423 §4.8.2.1) |
| LDAP v3 | B | Không áp dụng | BlueMeal tự quản lý tài khoản, HSMT không yêu cầu tích hợp dịch vụ thư mục. Nếu Viện muốn đăng nhập bằng tài khoản AD/LDAP, đây là hạng mục bổ sung |

*Không áp dụng:* FTP, WebDAV, RTSP/RTP/RTCP (không truyền tệp qua FTP, không phát media), POP3/IMAP (không là hệ thống thư), IEEE 802.11, WAP (hạ tầng không dây), các chuẩn WS-* đi kèm SOAP.

### 2.2 Tiêu chuẩn tích hợp dữ liệu

| Tiêu chuẩn | B/K | Trạng thái | BlueMeal áp dụng thế nào |
|---|---|---|---|
| XML 1.0 | B (một trong hai) | Đáp ứng | API công khai trả XML khi gửi `Accept: application/xml` (`AddXmlSerializerFormatters`) |
| XML Schema 1.1 | B | Đáp ứng | Lược đồ XSD của mọi phản hồi XML của API công khai (`token`, `menus`, `menuNutrition`, `foods`, `nutritionSummary`) phát hành tại `GET /api/v1/public/schema.xsd` (không cần token) và tệp `api/public-api.xsd`. XSD sinh từ chính các kiểu dữ liệu mà API tuần tự hóa, nên không lệch với phản hồi; test tích hợp kiểm tra từng phản hồi XML hợp lệ theo XSD. Lược đồ dùng cú pháp XSD 1.0, tương thích với bộ xử lý XSD 1.1 |
| UTF-8 | B | Đáp ứng | Toàn bộ API, CSDL (PostgreSQL `UTF8`), tệp Excel, giao diện |
| JSON (RFC 7159) | K | Đáp ứng | Định dạng mặc định của mọi API; FHIR dùng `application/fhir+json` |
| UML 2.5, BPMN 2.0 | K | Đáp ứng | Sơ đồ kiến trúc, ERD, luồng nghiệp vụ trong `01`, `02` (Mermaid) |

*Không áp dụng:* ebXML (ISO/TS 15000), XSL (không biến đổi/hiển thị XML phía máy chủ), GML/WMS/WFS (không có GIS), RDF/OWL/XMI, ISO/IEC 11179, Dublin Core.

### 2.3 Tiêu chuẩn truy cập thông tin

| Tiêu chuẩn | B/K | Trạng thái | BlueMeal áp dụng thế nào |
|---|---|---|---|
| HTML 4.01 / XHTML 1.1 (B), HTML5 (K) | B, K | Đáp ứng | Giao diện là HTML5 (tương thích ngược các phần tử HTML 4.01) |
| CSS2/CSS3 | B (một trong ba) | Đáp ứng | CSS3 (Ant Design) |
| ECMA-262 (ECMAScript 6) | B | Đáp ứng | Mã giao diện viết TypeScript, biên dịch ra ES2015+ |
| WCAG 2.0 | K | Đáp ứng (kiểm tra tự động) | E2E `p10-accessibility` quét 14 màn hình chính bằng axe-core theo WCAG 2.0/2.1 mức A, AA, không còn vi phạm mức nghiêm trọng (serious, critical): độ tương phản chữ ≥ 4,5:1, nhãn cho mọi ô chọn, tên truy cập chứa chữ hiển thị. Lighthouse Accessibility 90–97 (`test-reports/lighthouse/`). Kiểm tra tự động chỉ phủ một phần tiêu chí WCAG; đánh giá thủ công bằng bàn phím, trình đọc màn hình do đơn vị kiểm thử độc lập thực hiện nếu Viện yêu cầu |
| .xlsx | K | Đáp ứng | 16 báo cáo xuất Excel; nhập liệu ban đầu bằng 24 mẫu `.xlsx` |
| .xls / .ods | B (một hoặc cả hai) | Đáp ứng | Nút *Xuất ODS* ở mọi danh mục, 16 báo cáo và nhật ký hệ thống (`?format=ods`): OpenDocument Spreadsheet 1.2 (ISO/IEC 26300), cùng bố cục với tệp Excel (khối tiêu đề, ô số/ngày có kiểu, dòng tổng bằng công thức `SUM`, khối chữ ký). Kiểm thử: unit `OdsWriterTests` (gói ODF, kiểu ô), integration `ReportTests`; mở bằng LibreOffice Calc, Excel |
| .csv | B | Đáp ứng | Nút *Xuất CSV* ở mọi danh mục, 16 báo cáo và nhật ký hệ thống (`?format=csv`). Định dạng RFC 4180: UTF-8 có BOM, dấu phẩy, xuống dòng CRLF, một dòng tiêu đề; số dùng dấu chấm thập phân, ngày ISO 8601. Ô bắt đầu bằng `=`, `+`, `@` được thêm dấu `'` để bảng tính không thực thi công thức |
| .pdf 1.4–1.7 | B (một trong ba) | Đáp ứng | Đính kèm chứng từ/hồ sơ ATTP dạng PDF (kiểm tra chữ ký `%PDF`); phiếu kho, thực đơn, biểu mẫu ATTP in hoặc lưu PDF từ trình duyệt |
| .htm | B | Đáp ứng | Giao diện web |
| JPEG, PNG | B (một đến bốn) | Đáp ứng | Ảnh món ăn, chứng từ (kiểm tra chữ ký nội dung) |
| ASCII, TCVN 6909:2001 (Unicode) | B | Đáp ứng | Tiếng Việt Unicode dựng sẵn trên toàn hệ thống |
| .zip / .gz | B (một hoặc cả hai) | Đáp ứng | Phản hồi HTTP nén gzip (hoặc brotli nếu trình duyệt hỗ trợ); tệp `.xlsx` bản chất là gói zip |

*Không áp dụng:* .txt/.rtf/.doc/.odt/.ppt/.odp (không soạn thảo văn bản, trình chiếu), GIF/TIFF/GeoTIFF, định dạng âm thanh/hình ảnh động, WML, RSS/ATOM (không cung cấp tin), JSR 168/286 và WSRP (không là cổng thông tin portlet).

### 2.4 Tiêu chuẩn an toàn thông tin

| Tiêu chuẩn | B/K | Trạng thái | BlueMeal áp dụng thế nào |
|---|---|---|---|
| TLS 1.2 | B | Đáp ứng | nginx chỉ bật TLS 1.2 và 1.3 (`deploy/nginx/default.conf.template`) |
| HTTPS | B | Đáp ứng | Production chuyển hướng HTTP → HTTPS, HSTS 1 năm |
| SMTPS | B | Đáp ứng | STARTTLS hoặc SSL (cổng 465) khi bật *Dùng SSL* trong cấu hình thư |
| ECDHE | K | Đáp ứng | Chỉ bộ mã ECDHE + AES-GCM/ChaCha20 (forward secrecy) |
| ECC, ECDSA, PKCS #1 | K | Đáp ứng | Hỗ trợ chứng chỉ ECDSA hoặc RSA do Viện cấp |
| RFC 5280 (X.509, CRL) | K | Vận hành | Chứng chỉ TLS do CA của Viện hoặc CA công khai cấp |
| TCVN 7816:2007 (AES) | K | Đáp ứng | AES-256-GCM cho cột dữ liệu nhạy cảm; AES-256 cho bản sao lưu |
| SHA-2 | K | Đáp ứng | HMAC-SHA256 (cột tìm kiếm), PBKDF2-HMAC-SHA512 (mật khẩu), SHA-256 (refresh token, checksum sao lưu) |
| SSH v2 | B | Vận hành | Quản trị máy chủ từ xa qua OpenSSH; BlueMeal không mở SSH |
| IPsec | B | Vận hành | VPN cho truy cập quản trị từ xa (TCVN 14423 §4.12.2.5) |
| WPA2 | B | Vận hành | Hạ tầng Wi‑Fi của Viện |
| S/MIME 3.2 | B | Không áp dụng | BlueMeal chỉ gửi thư thông báo hệ thống qua SMTP/TLS, không ký/mã hóa nội dung thư |
| RSA-KEM | B | Không áp dụng | Trao đổi khóa dùng ECDHE, không dùng cơ chế đóng gói khóa RSA |
| XML Encryption, XML Signature | B | Không áp dụng | Không ký/mã hóa tài liệu XML; XML chỉ truyền qua TLS |
| SAML 2.0 | K | Không dùng | Xác thực cho hệ thống khác dùng OAuth2 client credentials (`03` §3) |
| RFC 3161 (dấu thời gian) | K | Không dùng | Nhật ký dùng thời gian máy chủ đồng bộ NTP, bảng `audit_log` chỉ thêm |

*Không áp dụng:* OpenPGP, FTPS/SFTP, POP3S/IMAPS, DNSSEC (hạ tầng DNS), 3DES, XKMS, P3P, PKCS #7/#8/#10/#11/#12/#15, RFC 6960, ISO/IEC 18014, WS-Security, RFC 7970.

## 3. Thông tư 13/2025/TT-BYT

TT 13/2025/TT-BYT hướng dẫn triển khai **hồ sơ bệnh án điện tử (HSBA)** tại cơ sở khám bệnh, chữa bệnh. Văn bản thay TT 46/2018/TT-BYT và nhóm tiêu chí CNTT của TT 54/2017/TT-BYT. Trao đổi dữ liệu y tế theo định hướng **HL7 FHIR R4**; Bộ hướng dẫn triển khai FHIR lõi Việt Nam (*VN Core IG*) được công bố tại `fhir.hl7.org.vn`.

BlueMeal **không phải** hệ thống HSBA. Phần mềm không lưu thông tin người bệnh, chẩn đoán hay chỉ định điều trị (A-43). HSMT viện dẫn thông tư này cho phần trao đổi dữ liệu và an toàn thông tin. Nhà thầu áp dụng như sau:

| Nhóm yêu cầu | Trạng thái | BlueMeal |
|---|---|---|
| Chuẩn trao đổi dữ liệu y tế HL7 FHIR R4 | Đáp ứng (phạm vi thực đơn, dinh dưỡng) | Máy chủ FHIR R4 (4.0.1) chỉ đọc tại `/api/v1/public/fhir/r4`: `GET metadata` trả `CapabilityStatement` (không cần token, khai báo bảo mật OAuth); `NutritionOrder` (thực đơn đã công bố: danh sách món, năng lượng theo UCUM, đối tượng phục vụ) và `Observation` (giá trị dinh dưỡng một suất theo từng chất, `focus` trỏ về `NutritionOrder`), mỗi loại hỗ trợ đọc theo id và tìm theo `date`; lỗi trả `OperationOutcome`. Kiểm tra bằng HL7 FHIR Validator (HAPI `$validate`): 0 lỗi; còn cảnh báo về hệ mã nội bộ `urn:bluemeal:*` vì chưa có bộ mã chất dinh dưỡng quốc gia. Chưa gắn profile VN Core IG (IG chưa có profile cho dinh dưỡng tập thể) |
| Định danh, xác thực người dùng; phân quyền theo vai trò | Đáp ứng | MFA cho mọi tài khoản, RBAC mặc định từ chối (`03` §2, §3) |
| Nhật ký truy cập, thay đổi dữ liệu | Đáp ứng | `audit_log` chỉ thêm, lưu ≥ 12 tháng, gửi tập trung về syslog/SIEM (`03` §4) |
| Mã hóa truyền và lưu trữ | Đáp ứng | TLS 1.2+, AES-256-GCM cho dữ liệu nhạy cảm (`03` §5) |
| Sao lưu, phục hồi | Đáp ứng | Sao lưu tự động hằng ngày, mã hóa; diễn tập khôi phục PASSED (`03` §8) |
| Chữ ký số trên hồ sơ, lưu trữ HSBA, kết nối BHXH | Không áp dụng | Không có hồ sơ bệnh án |

**Liên thông với HIS/HSBA:** HIS đọc `metadata` để biết tài nguyên hỗ trợ, lấy token client credentials (scope `menu.read`, `nutrition.read`), rồi đọc `NutritionOrder`/`Observation` theo ngày. Khi VN Core IG có profile cho dinh dưỡng, nhà thầu gắn profile và mã chuẩn tương ứng.

## 4. TCVN 14423:2026 — yêu cầu cơ bản cấp độ 2 (§4)

### 4.1 Xác định cấp độ

Theo NĐ 331/2026/NĐ-CP Đ12, cấp độ 2 gồm hệ thống phục vụ hoạt động nội bộ, có xử lý thông tin riêng/dữ liệu cá nhân và không xử lý bí mật nhà nước.
BlueMeal phục vụ hoạt động bếp ăn ca của Viện (nội bộ) và xử lý dữ liệu cá nhân của nhân viên (họ tên, đơn vị, liên hệ, CCCD tùy chọn), không có bí mật nhà nước. Đơn vị khách hàng đặt suất ăn chỉ xem dữ liệu của mình, số chủ thể rất nhỏ so với ngưỡng 100.000.
Vì vậy nhà thầu đề xuất **cấp độ 2**. Thuyết minh đầy đủ nằm ở `11_HoSo_DeXuat_CapDo2.md`.

### 4.2 Đối chiếu 15 nhóm yêu cầu

TCVN 14423:2026 là yêu cầu cho **cả hệ thống thông tin** (con người, quy trình, hạ tầng, phần mềm). Phần lớn yêu cầu quản lý thuộc về chủ quản và đơn vị vận hành.
Cột "Phần mềm BlueMeal" ghi phần phần mềm tự đáp ứng hoặc hỗ trợ; cột "Đơn vị vận hành" ghi phần Viện phải thực hiện.

| § | Nhóm yêu cầu | Phần mềm BlueMeal | Đơn vị vận hành | Trạng thái |
|---|---|---|---|---|
| 4.1 | Quản lý rủi ro an ninh mạng | Tài liệu thiết kế ATTT (`03`) nêu các mối đe dọa đã xử lý: OWASP Top 10, IDOR, lạm dụng phiên | Ban hành quy trình quản lý rủi ro (xác định → phân tích → đánh giá → xử lý), rà soát hằng năm | Vận hành |
| 4.2 | Quản lý tài sản phần cứng | Yêu cầu phần cứng tối thiểu (`05` §1) | Danh sách máy chủ, thiết bị lưu trữ, NAS; xóa sạch dữ liệu khi chuyển giao thiết bị | Vận hành |
| 4.3 | Quản lý tài sản phần mềm; phát triển thuê khoán | Danh sách thành phần và giấy phép (`THIRD_PARTY_LICENSES.md`), phiên bản image cố định. **Bàn giao toàn bộ mã nguồn** (§4.3.2.3 b) | Đưa BlueMeal và thành phần vào danh mục phần mềm, rà soát hằng năm; biên bản/cam kết bảo mật với nhà thầu (§4.3.2.3 a) | Đáp ứng (phần mềm) |
| 4.4 | Quản lý tài sản thông tin; mã hóa dữ liệu | Phân quyền theo vai trò và phạm vi kho/khách hàng; ma trận quyền theo nhóm quyền xem được trên màn hình *Quản trị → Nhóm quyền* (§4.4.2.3); mọi thay đổi quyền ghi nhật ký. Mã hóa: mật khẩu băm PBKDF2, dữ liệu cá nhân và bí mật tích hợp AES-256-GCM, bản sao lưu AES-256 (§4.4.2.4). Dữ liệu nghiệp vụ (thực đơn, kho) không mã hóa cột vì truy xuất lớn; được kiểm soát bằng phân quyền, mạng nội bộ và mã hóa ổ đĩa | Phân loại mức nhạy cảm (công khai/nội bộ/hạn chế); rà soát phân quyền hằng năm (§4.4.2.1 d); mã hóa ổ đĩa máy chủ (LUKS/BitLocker) | Đáp ứng (phần mềm) |
| 4.5 | Cấu hình an toàn; tự động khóa phiên | Container non-root, FS chỉ đọc, chỉ container `web` mở cổng, DB/Redis không mở cổng trong production (`03` §1, §5). Hết phiên khi không hoạt động **15 phút** theo mặc định (cấu hình được 5–480 phút), khớp mức TCVN gợi ý ≤ 15 phút. Khóa tài khoản sau **5 lần** sai — khớp mức gợi ý "tối đa 05 lần". Quản trị mở khóa khẩn cấp từ màn hình người dùng (§4.5.2.2 b) | Tài liệu cấu hình chuẩn cho máy chủ; tắt dịch vụ không dùng; khóa màn hình máy trạm 15 phút. Giữ *Hết hạn khi không hoạt động* ≤ 15 phút | Đáp ứng (phần mềm) |
| 4.6 | Quản lý tài khoản và quyền truy cập | Tài khoản cá nhân, không dùng chung; tách vai trò quản trị/tác nghiệp; tài khoản kỹ thuật (OAuth2 client của hệ thống khác) tách riêng. Buộc đổi mật khẩu `admin` mặc định ở lần đăng nhập đầu. Mật khẩu ≥ 10 ký tự + MFA cho **mọi** tài khoản (TCVN: ≥ 8 ký tự khi có MFA). Hết hạn mật khẩu 90 ngày cho mọi tài khoản (TCVN gợi ý tài khoản quản trị 2 tháng/lần). Danh sách tài khoản có lần đăng nhập cuối, trạng thái, vai trò, đơn vị (§4.6.2.1). Nhật ký đăng nhập và thay đổi tài khoản | Rà soát danh sách tài khoản hằng năm; **khóa tài khoản không hoạt động sau 45 ngày** (§4.6.2.3) — BlueMeal tự khóa hằng đêm theo *Chính sách mật khẩu → Khóa tài khoản không đăng nhập sau (ngày)*, giữ lại tài khoản quản trị hệ thống cuối cùng; thu hồi quyền ngay khi nhân sự nghỉ việc | Đáp ứng — tự động khóa sau 45 ngày không đăng nhập (cấu hình được) |
| 4.7 | Quản lý lỗ hổng, bản vá | Quét thư viện NuGet/npm có lỗ hổng trong `scripts/test-all`; ZAP baseline 0 High/0 Medium; cập nhật image theo bản phát hành (`08` §5) | Quét lỗ hổng máy chủ hằng năm; vá hệ điều hành/Docker hằng tháng; kênh tiếp nhận báo cáo lỗ hổng | Đáp ứng (phần mềm) |
| 4.8 | Quản lý nhật ký an ninh mạng | Nhật ký truy cập (IP nguồn, tài khoản, thời điểm, hành vi) trong `audit_log` và log nginx; cảnh báo của ModSecurity (tên luật, mức độ, IP nguồn, thời điểm). Tất cả gửi tập trung về syslog/SIEM. Lưu: `audit_log` ≥ 12 tháng, log JSON 90 ngày (TCVN: ≥ 1 tháng) | Máy thu log/SIEM; đồng bộ NTP; rà soát nhật ký tối thiểu hằng năm | Đáp ứng |
| 4.9 | Bảo vệ trình duyệt, thư điện tử | Hỗ trợ Chrome/Cốc Cốc/Edge/Safari bản đang được hỗ trợ; CSP, HSTS, các header bảo mật | Danh sách trình duyệt được phép, cập nhật bản vá; lọc tên miền độc hại | Vận hành |
| 4.10 | Phòng chống mã độc | Tệp tải lên: chỉ nhận loại cho phép, kiểm tra chữ ký nội dung, giới hạn kích thước, lưu tên ngẫu nhiên, không thực thi | Phần mềm phòng chống mã độc cho máy chủ và máy trạm; tắt autorun cho thiết bị lưu trữ ngoài | Vận hành |
| 4.11 | Sao lưu, khôi phục | Sao lưu tự động hằng ngày CSDL + tệp đính kèm, mã hóa, checksum, giữ 7/4/6; sao lưu bất thường; diễn tập khôi phục tự động (`03` §8) | Quy định sao lưu (gồm cả tệp cấu hình `deploy/.env` và bản dự phòng hệ điều hành); **lưu bản sao ở hạ tầng tách biệt** (NAS/ngoài máy chủ, `BACKUP_MIRROR_PATH`); diễn tập định kỳ | Đáp ứng |
| 4.12 | Hạ tầng mạng; kiểm thử nghiệm thu; truy cập từ xa | WAF ModSecurity + OWASP CRS (§4.12.2.2); mạng Docker tách `frontend`/`backend`; tự đóng phiên khi không hoạt động (§4.12.2.4 d); kế hoạch, kịch bản và báo cáo kiểm thử (`04`, `09`) (§4.12.2.3) | Sơ đồ mạng, phân vùng (máy chủ/DMZ/nội bộ/biên), tường lửa có IPS; VPN + xác thực cho quản trị từ xa; giới hạn địa chỉ được quản trị | Một phần (phần mềm đáp ứng phần của mình) |
| 4.13 | Nhân sự vận hành, đào tạo | Tài liệu đào tạo theo vai trò (`07`), HDSD quản trị (`06_HDSD_QuanTri.md`), quy trình vận hành (`08`) | Phân công người phụ trách vận hành/an ninh mạng; đào tạo nhận thức hằng năm; thu hồi quyền khi nghỉ việc | Vận hành |
| 4.14 | Quản lý nhà cung cấp | Thông tin nhà thầu, phạm vi trách nhiệm bảo hành (`08`) | Đưa nhà thầu vào danh sách nhà cung cấp, rà soát hằng năm | Vận hành |
| 4.15 | Ứng phó sự cố | Quy trình xử lý sự cố, gồm sự cố ATTT (`08` §3); công cụ cô lập: thu hồi phiên, khóa tài khoản, tra cứu nhật ký theo traceId | Chỉ định người chủ chốt và người dự phòng; đầu mối liên hệ với cơ quan chức năng và Liên minh ứng cứu sự cố quốc gia; rà soát quy trình hằng năm | Vận hành (phần mềm hỗ trợ) |

Yêu cầu an toàn vật lý (Phụ lục A của TCVN 14423:2026) thuộc phòng máy chủ của Viện.

## 5. Bảo vệ dữ liệu cá nhân — Luật 91/2025/QH15 và NĐ 356/2025/NĐ-CP

Dữ liệu cá nhân trong BlueMeal:
- **Nhân viên:** họ tên, đơn vị, số điện thoại, email, CCCD (tùy chọn), tài khoản đăng nhập.
- **Đầu mối của đối tác và khách hàng:** họ tên, liên hệ.

BlueMeal không xử lý dữ liệu sức khỏe hay dữ liệu bệnh nhân. Viện là **bên kiểm soát và xử lý dữ liệu**; nhà thầu chỉ tham gia khi bảo hành và phải được Viện cho phép.

| Nghĩa vụ | Trạng thái | BlueMeal |
|---|---|---|
| Thu thập tối thiểu, đúng mục đích | Đáp ứng | Chỉ các trường cần cho suất ăn và kho (`03` §7) |
| Bảo vệ bằng biện pháp kỹ thuật | Đáp ứng | Mã hóa AES-256-GCM, che dữ liệu khi người dùng không có quyền `pii.view`, phân quyền, TLS, sao lưu mã hóa |
| Ghi nhật ký truy cập dữ liệu cá nhân | Đáp ứng | `PiiAccess` khi xem chi tiết hoặc xuất |
| Quyền của chủ thể: được biết, truy cập, chỉnh sửa, xóa | Đáp ứng | Xuất dữ liệu của một nhân viên (JSON); chỉnh sửa trên màn hình nhân viên; ẩn danh hóa không đảo ngược (`06_HDSD_QuanTri.md`) |
| Thông báo xử lý dữ liệu, sự đồng ý của chủ thể | Đáp ứng | Sau khi đăng nhập, người dùng phải đọc *Thông báo xử lý dữ liệu cá nhân* và bấm *Tôi đã đọc và đồng ý* mới dùng được hệ thống; từ chối thì đăng xuất. BlueMeal lưu phiên bản thông báo, thời điểm, IP, trình duyệt và ghi nhật ký `Consent`. Quản trị soạn nội dung (tiếng Việt, tiếng Anh) ở *Cấu hình hệ thống → Bảo mật*; khi đổi nội dung, phiên bản tăng và mọi người phải đồng ý lại. Có thể tắt nếu bộ phận pháp chế của Viện xác định dữ liệu nhân viên phục vụ quản lý lao động nội bộ không cần sự đồng ý. Nội dung mặc định là bản mẫu, Viện cần rà soát trước khi vận hành |
| Đánh giá tác động xử lý dữ liệu cá nhân (DPIA) | Vận hành | Hồ sơ do Viện lập. Nhà thầu cung cấp đầu vào: danh mục dữ liệu, luồng dữ liệu (`02` §3), biện pháp bảo vệ (`03`) |
| Thông báo vi phạm dữ liệu cá nhân cho cơ quan chuyên trách (trong 72 giờ) | Vận hành (phần mềm hỗ trợ) | Quy trình sự cố ATTT (`08` §3). Nhật ký tập trung giúp xác định phạm vi dữ liệu bị ảnh hưởng |
| Không chuyển dữ liệu ra ngoài khi không có căn cứ | Đáp ứng | Không telemetry, không dịch vụ SaaS; API công khai không có dữ liệu cá nhân |

## 6. Tổng hợp chênh lệch và đề xuất

| # | Chênh lệch | Căn cứ | Đề xuất | Bên thực hiện |
|---|---|---|---|---|
| 1 | ~~Chưa có thông báo xử lý dữ liệu cá nhân và ghi nhận đồng ý~~ — **đã khắc phục** | Luật 91/2025 | Đã có màn hình thông báo và ghi nhận đồng ý (mục 5). Viện rà soát nội dung mẫu trước khi vận hành | Viện (nội dung) |
| 2 | ~~Chưa tự động khóa tài khoản không hoạt động 45 ngày~~ — **đã khắc phục** | TCVN 14423 §4.6.2.3 | Job `inactive-accounts` chạy 00:15 hằng ngày, khóa tài khoản quá 45 ngày không đăng nhập (cấu hình được, 0 = tắt), thu hồi phiên, ghi nhật ký | — |
| 3 | ~~Thời gian hết phiên mặc định 30 phút~~ — **đã khắc phục** | TCVN 14423 §4.5.2.2 (gợi ý ≤ 15 phút) | Mặc định 15 phút; hệ thống đã lưu cấu hình cũ thì kiểm tra lại ở *Quản trị → Cấu hình hệ thống* (mục Phiên) | — |
| 4 | Chưa có chu kỳ mật khẩu riêng cho tài khoản quản trị | TCVN 14423 §4.6.2.2 (gợi ý 2 tháng/lần) | Mức gợi ý, bù bằng MFA bắt buộc. Nếu cần, giảm *Hết hạn sau* xuống 60 ngày cho toàn hệ thống | Viện (cấu hình) |
| 5 | ~~Chưa có xuất CSV~~ — **đã khắc phục** | TT 39/2017 (.csv bắt buộc) | Xuất CSV UTF-8 cho danh mục, báo cáo, nhật ký hệ thống (mục 2) | — |
| 6 | ~~Chưa có XSD cho XML của API công khai~~ — **đã khắc phục** | TT 39/2017 (XML Schema bắt buộc) | `GET /api/v1/public/schema.xsd`, tệp `api/public-api.xsd` (mục 2) | — |
| 7 | ~~FHIR tối giản, chưa có CapabilityStatement~~ — **đã khắc phục**; còn chưa gắn profile VN Core IG | TT 13/2025/TT-BYT | Máy chủ FHIR R4 chỉ đọc có `CapabilityStatement`, `NutritionOrder`, `Observation`, `OperationOutcome`, đạt HL7 Validator (mục 3). Gắn profile khi VN Core IG có profile dinh dưỡng | Theo yêu cầu |
| 8 | Thẩm định và phê duyệt cấp độ 2 | NĐ 331/2026 Đ18, Đ21 | Viện lập hồ sơ theo `11_HoSo_DeXuat_CapDo2.md`; đơn vị chuyên trách an ninh mạng của Viện thẩm định, Viện phê duyệt | Viện |
| 9 | Kiểm thử độc lập | HSMT (phối hợp đơn vị kiểm thử phần mềm, kiểm thử ATTT chuyên ngành) | Nhà thầu đã tự kiểm thử (`04`, ZAP, k6). Kiểm thử độc lập do đơn vị Viện chỉ định thực hiện; nhà thầu phối hợp và khắc phục | Viện + nhà thầu |

## 7. Nguồn đối chiếu

- Thông tư 39/2017/TT-BTTTT, Phụ lục Danh mục tiêu chuẩn kỹ thuật về ứng dụng CNTT trong cơ quan nhà nước.
- Thông tư 13/2025/TT-BYT ngày 06/06/2025 hướng dẫn triển khai hồ sơ bệnh án điện tử; HL7 Việt Nam — VN Core FHIR IG (`fhir.hl7.org.vn`).
- TCVN 14423:2026 *An ninh mạng – Hệ thống thông tin – Yêu cầu cơ bản*, xuất bản lần 2 (§4 Yêu cầu cơ bản đối với hệ thống thông tin cấp độ 2, Phụ lục A).
- Luật An ninh mạng 2025 (hiệu lực 01/07/2026); Nghị định 331/2026/NĐ-CP ngày 19/08/2026.
- Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15 (hiệu lực 01/01/2026); Nghị định 356/2025/NĐ-CP.
