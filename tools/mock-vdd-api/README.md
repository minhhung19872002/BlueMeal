# Mock cổng dữ liệu Viện Dinh dưỡng (VDD)

Đặc tả API thật của cổng dữ liệu dinh dưỡng **chưa được công bố** (SPEC §7.10). Thư mục này chứa một máy chủ giả lập
(ASP.NET Core minimal API, không phụ thuộc gói ngoài) cài đặt **hợp đồng giả định** dưới đây, dùng để:

- demo đồng bộ trong môi trường dev (`docker compose ... -f deploy/docker-compose.dev.yml`, service `mock-vdd`, cổng `5090`);
- kiểm thử tự động: bộ integration test nạp `MockServer.cs` và chạy in-process trên `TestServer`.

Khi có đặc tả thật, **chỉ cần sửa `mapping_json`** của kết nối (màn hình *Quản trị → Tích hợp*), không sửa code (ASSUMPTIONS A-25).

## Xác thực

| Kiểu | Cách gửi | Giá trị mặc định của mock |
|---|---|---|
| API key | header `X-Api-Key` | `demo-vdd-key` |
| Basic | `Authorization: Basic base64(user:pass)` | `demo` / `demo` |
| OAuth2 client credentials | `POST /oauth/token` (form: `grant_type=client_credentials`, `client_id`, `client_secret`) → `{ access_token, token_type, expires_in }`, sau đó `Authorization: Bearer …` | `bluemeal` / `demo-vdd-secret` |

Cấu hình qua biến môi trường `Mock__ApiKey`, `Mock__ClientSecret`, `Mock__RequireAuth=false`…

## Endpoint

Tất cả trả JSON `snake_case`, danh sách bọc trong `{ "data": [...] }`. Lọc tăng dần bằng `modified_since` (ISO-8601 UTC).

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/v1/nutrients` | `nutrient_code` (tagname INFOODS: `ENERC_KCAL`, `PROCNT`, `FAT`, `CHOCDF`, `FIBTG`, `CA`, `FE`, `NA`, `VITC`, `VITA_RAE`, `THIA`…), `name_vi`, `name_en`, `unit`, `group` |
| GET | `/api/v1/food-groups?modified_since=` | `group_code`, `name_vi`, `name_en`, `parent_code`, `animal_source`, `updated_at` |
| GET | `/api/v1/foods?modified_since=&page=&page_size=` | `food_code`, `name_vi`, `name_en`, `group_code`, `refuse_pct`, `version`, `updated_at`, `composition[]` (`nutrient_code`, `value` / 100 g phần ăn được, `unit`); kèm `page`, `page_size`, `total` |
| GET | `/api/v1/recommendations?modified_since=` | `rec_code`, `name_vi`, `name_en`, `reference`, `year`, `energy_kcal_day`, `meal_share_pct`, `protein_pct`/`lipid_pct`/`glucid_pct` = `[min, max]`, `nutrients[]` (`nutrient_code`, `min`, `max` **theo ngày**) |
| POST | `/api/v1/reports/aggregated` | Nhận báo cáo tổng hợp (tùy chọn) → `{ accepted, receipt_id, message }` |
| GET | `/health` | Kiểm tra sống |

Lưu ý dữ liệu mẫu cố ý dùng **đơn vị khác** với BlueMeal (natri tính bằng `g`) để minh họa quy đổi đơn vị trong mapping,
và có một mã trùng với dữ liệu mẫu nhập tay (`M-NC01`) để minh họa **xung đột đồng bộ**.

## Endpoint quản trị (chỉ cho demo/kiểm thử)

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| POST | `/__admin/reset` | Khôi phục dữ liệu mẫu |
| POST | `/__admin/foods/{code}` | Sửa một thực phẩm phía nguồn (JSON các trường cần đổi; `composition` dạng `{ "VITC": 92.5 }`), tăng `version`, cập nhật `updated_at` |
| POST | `/__admin/fail?count=n` | `n` request tiếp theo trả `503` (kiểm tra retry / circuit breaker) |
| GET | `/__admin/stats` | Số lần gọi API, số báo cáo đã nhận |

## Ánh xạ mặc định trong BlueMeal

`MappingConfig` (backend `BlueMeal.Application.Integration`) — xem/sửa trên màn hình *Tích hợp → Kết nối → Mapping JSON*:
đường dẫn API, tên tham số truy vấn, đường dẫn mảng dữ liệu (`itemsPath`), tên trường nguồn → trường đích, bảng mã chất
(`nutrientCodes`: `PROCNT → PROTEIN`…), mã nhóm, hệ số quy đổi đơn vị (`unitFactors`: `g>mg = 1000`, `kJ>kcal = 0.239006`…)
và tiền tố mã (`codePrefix`) khi không gian mã của nguồn trùng với mã nội bộ.
