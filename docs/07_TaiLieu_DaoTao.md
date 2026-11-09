# TÀI LIỆU ĐÀO TẠO — BlueMeal v1.0

## 1. Kế hoạch đào tạo

| Khóa | Đối tượng | Thời lượng | Hình thức | Tài liệu |
|---|---|---|---|---|
| K0 – Tổng quan | Tất cả người dùng | 1 giờ | Trình chiếu + demo | `06_HDSD_00_ChungChoMoiNguoi.md` |
| K1 – Quản trị hệ thống | SYS_ADMIN, cán bộ CNTT | 4 giờ | Thực hành trên máy | `06_HDSD_QuanTri.md`, `05_HuongDan_CaiDat.md`, `03_ThietKe_ATTT_CapDo2.md` |
| K2 – Dinh dưỡng và thực đơn | NUTRITIONIST, HEAD_CHEF | 6 giờ (2 buổi) | Thực hành | `06_HDSD_DinhDuong.md`, `06_HDSD_BepTruong.md` |
| K3 – Phê duyệt và điều hành | MENU_APPROVER, MANAGER | 2 giờ | Thực hành | `06_HDSD_NguoiDuyet.md` |
| K4 – Kho và an toàn thực phẩm | STOREKEEPER, HEAD_CHEF | 4 giờ | Thực hành (có điện thoại) | `06_HDSD_ThuKho.md`, `06_HDSD_BepTruong.md` |
| K5 – Chi phí và báo cáo | ACCOUNTANT, MANAGER | 3 giờ | Thực hành | `06_HDSD_KeToan.md` |
| K6 – Đơn vị đặt suất | CUSTOMER_REP | 1 giờ | Trực tuyến | `06_HDSD_KhachHang.md` |

**Chuẩn bị:** môi trường đào tạo cài bằng Docker với `SEED_SAMPLE_DATA=true` (dữ liệu "[DỮ LIỆU MẪU]": 81 thực phẩm, 42 nguyên liệu, 30 món có công thức, 3 khuyến nghị, kho, khách hàng mẫu);
mỗi học viên một tài khoản theo vai trò; máy chiếu; điện thoại có ứng dụng Authenticator; 1 file Excel mẫu nhập liệu (`tools/import-templates/`).
Sau đào tạo, quản trị **xóa dữ liệu mẫu** trước khi vận hành thật.

## 2. Giáo án theo khóa

### K0 – Tổng quan (1 giờ)

| Thời gian | Nội dung | Hoạt động |
|---|---|---|
| 15' | Mục tiêu hệ thống: bữa ăn ca cân đối dinh dưỡng, kiểm soát chi phí, an toàn thực phẩm | Trình bày |
| 20' | Đăng nhập, MFA, đổi mật khẩu, giao diện, đổi ngôn ngữ, chế độ tối, thông báo | Mỗi học viên tự đăng nhập, bật MFA |
| 15' | Thao tác danh mục: tìm không dấu, lọc, nhập/xuất Excel, lịch sử | Demo + làm theo |
| 10' | Bảo mật: không chia sẻ tài khoản, khóa màn hình, báo cáo sự cố | Hỏi đáp |

### K1 – Quản trị (4 giờ)

1. Thông tin đơn vị, cơ cấu tổ chức, nhân viên (nhập Excel), tài khoản, nhóm quyền, phạm vi dữ liệu.
2. Chính sách mật khẩu/phiên/MFA, SMTP (gửi thử), cấu hình nghiệp vụ.
3. Phiên làm việc, đăng xuất cưỡng bức, nhật ký hệ thống, xuất dữ liệu cá nhân/ẩn danh hóa.
4. Sao lưu bất thường, tải bản sao lưu (xác thực lại), **diễn tập khôi phục** với cán bộ kỹ thuật.
5. Tích hợp Viện Dinh dưỡng: kết nối, đồng bộ, xử lý xung đột; tạo client API cho hệ thống khác.

### K2 – Dinh dưỡng và thực đơn (2 buổi × 3 giờ)

Buổi 1: thực phẩm (lưới dinh dưỡng, đồng bộ), nguyên liệu, đối tượng, khuyến nghị, cơ cấu bữa, món ăn, công thức (realtime, phiên bản, so sánh, quy đổi số suất).
Buổi 2: kế hoạch tuần, tạo tự động (tham số, phương án thay thế, seed), tạo thủ công (panel realtime, cảnh báo, đổi món), gửi duyệt, phân tích dinh dưỡng, cảnh báo.

### K3 – Phê duyệt (2 giờ)

Đọc snapshot dinh dưỡng – chi phí – cảnh báo; duyệt/trả lại/từ chối có ý kiến; cảnh báo nghiêm trọng; duyệt hàng loạt; công bố; thu hồi; dashboard và báo cáo điều hành.

### K4 – Kho và ATTP (4 giờ)

Phiếu nhập có kiểm tra chất lượng (mã vạch), phiếu xuất theo thực đơn (FEFO), chuyển kho, kiểm kê trên điện thoại, tồn kho/thẻ kho/lô, đề xuất mua, kiểm thực 3 bước, lưu mẫu và hủy mẫu, in biểu mẫu.

### K5 – Chi phí và báo cáo (3 giờ)

Giá nguyên liệu và lịch sử giá, hợp đồng, phương pháp giá, chi phí chung; bảng tính chi phí; 16 báo cáo, lọc, lưu bộ lọc, xuất Excel, xuất nền; khóa sổ.

### K6 – Đơn vị đặt suất (1 giờ)

Xem/in thực đơn công bố, đặt và điều chỉnh số suất, tổng hợp.

## 3. Bài tập thực hành

| Mã | Khóa | Đề bài | Kết quả cần đạt |
|---|---|---|---|
| BT-01 | K0 | Đăng nhập lần đầu, đổi mật khẩu, bật MFA, đổi giao diện tiếng Anh rồi trở lại tiếng Việt | Đăng nhập lại bằng mã MFA thành công |
| BT-02 | K1 | Tạo nhóm quyền "Tổ trưởng bếp" chỉ xem thực đơn và kiểm thực; tạo tài khoản và gán thủ kho cho 1 kho | Tài khoản thủ kho chỉ thấy 1 kho |
| BT-03 | K1 | Sao lưu bất thường; tải về với xác thực lại; xem nhật ký hành động `Backup` | Có bản sao lưu *Thành công* và dòng nhật ký |
| BT-04 | K2 | Tạo món "Đậu phụ sốt cà chua", công thức cho 10 suất; xem định lượng cho 350 suất | Năng lượng, P:L:G, chi phí hiển thị; bảng định lượng 350 suất |
| BT-05 | K2 | Tạo tự động thực đơn Ca 1 cho tuần sau, đối tượng "Nam lao động vừa", ngân sách 35.000 ₫; chọn phương án 2 cho thứ Tư; lưu nháp | 6 thực đơn nháp (bỏ Chủ nhật) |
| BT-06 | K2 | Sửa thực đơn thứ Hai: đổi món canh theo gợi ý, gửi duyệt | Trạng thái *Chờ duyệt* |
| BT-07 | K3 | Trả lại 1 thực đơn với ý kiến; duyệt hàng loạt các thực đơn còn lại; công bố | Thực đơn hiện ở trang công bố |
| BT-08 | K4 | Nhập 25 kg gạo (lô L01, HSD), 1 dòng thịt Không đạt → xử lý; xuất theo thực đơn đã duyệt; kiểm kê và duyệt | Tồn khớp số đếm; phiếu điều chỉnh được tạo |
| BT-09 | K4 | Ghi kiểm thực bước 1–3 cho bữa trưa; lấy mẫu 3 món; in sổ | Biểu mẫu in ra đủ 3 bước |
| BT-10 | K5 | Xem bảng chi phí tháng theo khách hàng; xuất RPT-02 và RPT-11; lưu bộ lọc "Tháng này" | 2 file Excel có tiêu đề đơn vị, kỳ, người lập |
| BT-11 | K6 | Đặt 120 suất Ca 1 ngày mai, điều chỉnh thành 135 | Trạng thái *Đã điều chỉnh* |

## 4. Đánh giá sau đào tạo

Mỗi học viên hoàn thành các bài tập của khóa mình; giảng viên đánh dấu Đạt/Chưa đạt. Học viên Chưa đạt được hướng dẫn lại trong buổi hỗ trợ.

## 5. Mẫu biên bản xác nhận tham gia đào tạo

---

**CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM**
**Độc lập – Tự do – Hạnh phúc**

**BIÊN BẢN XÁC NHẬN THAM GIA ĐÀO TẠO**
**Phần mềm quản lý bữa ăn ca thông minh BlueMeal**

Hôm nay, ngày ..... tháng ..... năm ........, tại ........................................................................

Chúng tôi gồm:

**Bên A (Chủ đầu tư):** ........................................................................ Đại diện: ................................ Chức vụ: ........................

**Bên B (Đơn vị cung cấp):** ........................................................................ Đại diện: ................................ Chức vụ: ........................

Cùng xác nhận đã tổ chức khóa đào tạo:

| Khóa | Thời gian | Giảng viên |
|---|---|---|
| ........................................ | từ ...h... đến ...h... | ........................ |

Danh sách học viên tham gia:

| STT | Họ và tên | Đơn vị / Bộ phận | Vai trò sử dụng | Kết quả bài tập (Đạt / Chưa đạt) | Ký tên |
|---|---|---|---|---|---|
| 1 | | | | | |
| 2 | | | | | |
| 3 | | | | | |
| 4 | | | | | |
| 5 | | | | | |

Nội dung đã đào tạo: theo giáo án khóa ........ tại tài liệu `07_TaiLieu_DaoTao.md`. Tài liệu đã bàn giao: hướng dẫn sử dụng theo vai trò (bản điện tử).

Ý kiến của học viên / Bên A: ......................................................................................................................................................

Biên bản lập thành 02 bản, mỗi bên giữ 01 bản có giá trị như nhau.

| ĐẠI DIỆN BÊN A | ĐẠI DIỆN BÊN B |
|---|---|
| (Ký, ghi rõ họ tên) | (Ký, ghi rõ họ tên) |
