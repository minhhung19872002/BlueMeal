# QUY TRÌNH VẬN HÀNH, HỖ TRỢ VÀ BẢO HÀNH — BlueMeal v1.0

## 1. Kênh hỗ trợ

| Kênh | Thông tin (điền khi ký hợp đồng) | Thời gian |
|---|---|---|
| Đường dây nóng | ................................ | 24/7 với sự cố Mức 1; 07:30–17:30 các ngày làm việc với mức khác |
| Email hỗ trợ | ................................ | 24/7 (tiếp nhận tự động) |
| Hệ thống ticket | ................................ | 24/7 |
| Hỗ trợ tại chỗ | Theo yêu cầu | Trong thời gian xử lý của mức sự cố |

Khi báo lỗi cần cung cấp: người báo, thời điểm, màn hình/chức năng, **mã lỗi** hiển thị (dạng `00-…`, là `traceId` để tra log), mô tả bước tái hiện, ảnh chụp.

## 2. Phân loại mức độ sự cố và cam kết thời gian (SLA)

**Cam kết chung: tiếp nhận và phản hồi lỗi trong vòng 2 giờ** kể từ khi nhận được thông báo qua bất kỳ kênh nào ở mục 1.

| Mức | Định nghĩa | Ví dụ | Phản hồi | Khắc phục tạm thời | Khắc phục triệt để |
|---|---|---|---|---|---|
| **1 – Nghiêm trọng** | Hệ thống ngừng hoạt động hoặc mất/rò rỉ dữ liệu; không phục vụ được bữa ăn | Không đăng nhập được toàn hệ thống; CSDL hỏng; nghi xâm nhập | ≤ 30 phút (trong 2 giờ cam kết) | ≤ 4 giờ | ≤ 24 giờ |
| **2 – Cao** | Chức năng chính không dùng được, không có cách làm thay | Không duyệt được thực đơn; không ghi sổ kho; báo cáo chi phí sai | ≤ 2 giờ | ≤ 8 giờ làm việc | ≤ 3 ngày làm việc |
| **3 – Trung bình** | Chức năng bị lỗi nhưng có cách làm thay | Một báo cáo xuất Excel lỗi định dạng; lỗi hiển thị trên một trình duyệt | ≤ 2 giờ | ≤ 2 ngày làm việc | Bản cập nhật kế tiếp (≤ 15 ngày) |
| **4 – Thấp** | Góp ý, câu hỏi sử dụng, lỗi giao diện nhỏ | Sai chính tả; đề nghị thêm cột | ≤ 2 giờ | — | Theo kế hoạch nâng cấp |

Thời gian tính theo giờ làm việc với mức 2–4; mức 1 tính liên tục 24/7.

## 3. Quy trình xử lý sự cố

```mermaid
flowchart TD
  A[Người dùng báo lỗi] --> B[Tiếp nhận, cấp mã ticket ≤ 2 giờ]
  B --> C{Phân loại mức độ}
  C -->|Mức 1| D[Thông báo đầu mối chủ đầu tư; huy động kỹ thuật ngay]
  C -->|Mức 2-4| E[Phân công xử lý]
  D --> F[Khoanh vùng: health check, log theo traceId, nhật ký hệ thống]
  E --> F
  F --> G[Khắc phục tạm thời / hướng dẫn cách làm thay]
  G --> H[Khắc phục triệt để: sửa lỗi, kiểm thử, phát hành bản vá]
  H --> I[Xác nhận với người báo, đóng ticket]
  I --> J[Báo cáo sự cố (mức 1-2): nguyên nhân, ảnh hưởng, biện pháp phòng ngừa]
```

**Sự cố an toàn thông tin** (nghi xâm nhập, lộ dữ liệu, mã độc): xử lý như Mức 1; cô lập (thu hồi phiên, khóa tài khoản liên quan, chặn IP ở tường lửa/WAF);
lưu giữ bằng chứng (nhật ký hệ thống, log nginx/ModSecurity, log ứng dụng); đánh giá phạm vi dữ liệu cá nhân bị ảnh hưởng và thông báo theo quy định (Nghị định 13/2023/NĐ-CP);
khôi phục từ bản sao lưu sạch nếu cần; rà soát và vá lỗ hổng trước khi vận hành lại.

## 4. Vận hành thường xuyên

| Tần suất | Công việc | Người thực hiện | Công cụ |
|---|---|---|---|
| Hằng ngày | Kiểm tra bản sao lưu 01:00 *Thành công*; xem cảnh báo, job lỗi (đồng bộ VDD, xuất báo cáo) | Quản trị | *Quản trị → Sao lưu*, *Tích hợp → Lịch sử* |
| Hằng ngày | Theo dõi dung lượng đĩa, CPU/RAM máy chủ | Kỹ thuật | Giám sát hạ tầng |
| Hằng tuần | Rà phiên lạ, tài khoản không dùng; kiểm tra log lỗi (mức Error) | Quản trị | *Phiên làm việc*, log JSON |
| Hằng tháng | Rà nhật ký thay đổi quyền/cấu hình/truy cập dữ liệu cá nhân; khóa sổ kho; cập nhật bản vá hệ điều hành/Docker | Quản trị, Kế toán, Kỹ thuật | *Nhật ký hệ thống*, RPT-16 |
| Hằng quý | **Diễn tập khôi phục** (`scripts/test-backup-restore.*`); quét bảo mật ZAP (`scripts/zap-baseline.*`); rà soát nhóm quyền | Kỹ thuật + Quản trị | Biên bản diễn tập |
| Hằng năm | Gia hạn chứng chỉ TLS; đổi khóa JWT/khóa mã hóa (xoay khóa); đánh giá lại hiệu năng (`scripts/load-test.*`) | Kỹ thuật | `05_HuongDan_CaiDat.md` |

**Xoay khóa mã hóa cột:** thêm khóa mới vào đầu `BLUEMEAL_ENC_KEYS`, các khóa cách nhau bằng dấu chấm phẩy (`k2:<base64>;k1:<base64>`) — đặt giá trị trong dấu nháy kép trong `deploy/.env`, khởi động lại `api`/`worker`; dữ liệu mới mã hóa bằng khóa mới, dữ liệu cũ vẫn đọc được bằng khóa cũ.
**Xoay khóa JWT:** đổi `BLUEMEAL_JWT_KEY` và khởi động lại — mọi người dùng phải đăng nhập lại.

## 5. Quy trình nâng cấp, phát hành

1. Bên cung cấp phát hành phiên bản theo SemVer kèm `CHANGELOG.md` (thay đổi, lưu ý nâng cấp) và gói ảnh Docker (`scripts/save-images.*`).
2. Thử trên môi trường kiểm thử của chủ đầu tư (bản sao dữ liệu đã ẩn danh nếu cần) → chạy kịch bản kiểm thử chính (`04_KichBan_KiemThu.md`).
3. Thống nhất thời điểm nâng cấp ngoài giờ phục vụ bữa ăn; thông báo người dùng.
4. Sao lưu bất thường → nâng cấp theo `05_HuongDan_CaiDat.md` mục 4 → kiểm tra sau nâng cấp (đăng nhập, xem thực đơn, ghi sổ kho thử, báo cáo).
5. Có sự cố: quay lại phiên bản trước và khôi phục bản sao lưu vừa tạo.

Bản vá bảo mật khẩn cấp được phát hành ngoài kế hoạch, thông báo trong 24 giờ kể từ khi phát hiện.

## 6. Bảo hành

| Hạng mục | Cam kết |
|---|---|
| Thời gian bảo hành | Theo hợp đồng (tối thiểu 12 tháng kể từ ngày nghiệm thu) |
| Phạm vi | Sửa lỗi phần mềm so với tài liệu đặc tả và hướng dẫn; hỗ trợ cài đặt lại, khôi phục dữ liệu; cập nhật bản vá bảo mật thư viện bên thứ ba; hỗ trợ sử dụng |
| Ngoài phạm vi | Lỗi do hạ tầng của chủ đầu tư (phần cứng, mạng, hệ điều hành không đáp ứng yêu cầu), thao tác sai cố ý, chỉnh sửa mã nguồn bởi bên thứ ba, yêu cầu chức năng mới (báo giá riêng) |
| Tài liệu | Cập nhật hướng dẫn sử dụng khi có thay đổi chức năng |

## 7. Đầu mối

| Vai trò | Bên chủ đầu tư | Bên cung cấp |
|---|---|---|
| Đầu mối quản lý hợp đồng | ................................ | ................................ |
| Quản trị hệ thống / kỹ thuật | ................................ | ................................ |
| Người giữ passphrase sao lưu | ................................ (khác người quản trị ứng dụng) | — |
