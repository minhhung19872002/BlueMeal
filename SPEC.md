# PROMPT CHO CLAUDE CODE — XÂY DỰNG PHẦN MỀM BỮA ĂN CA THÔNG MINH (BlueMeal)

> Đặt file này ở thư mục gốc repo với tên `SPEC.md`, rồi bảo Claude Code: **"Đọc SPEC.md và thực hiện lần lượt từng Phase, xong phase nào chạy test và báo cáo phase đó trước khi sang phase tiếp theo."**

---

## 0. VAI TRÒ VÀ CÁCH LÀM VIỆC

Bạn là kỹ sư phần mềm full-stack senior (.NET + React + PostgreSQL + Docker). Bạn sẽ xây dựng **từ đầu** một phần mềm web hoàn chỉnh, chạy production được, để bàn giao cho **Viện Dinh dưỡng (Bộ Y tế)** theo gói thầu *"Xây dựng phần mềm bữa ăn ca thông minh"* (IB2600574672).

Nguyên tắc bắt buộc:

1. Làm **theo đúng thứ tự Phase** ở mục 14. Hết mỗi Phase: build pass, test pass, `docker compose up` chạy được, cập nhật `docs/CHANGELOG.md`, commit git với message rõ ràng.
2. **Không bỏ sót chức năng** nào trong mục 5–9. Nếu một yêu cầu mơ hồ, chọn phương án hợp lý nhất, ghi lại giả định vào `docs/ASSUMPTIONS.md` và tiếp tục, không dừng lại hỏi.
3. Môi trường dev là **Windows (CMD/PowerShell) + Docker Desktop**. Mọi script phải có cả bản `.ps1` và `.sh`. Thêm `.gitattributes` ép `*.sh text eol=lf`.
4. Chỉ dùng thư viện **mã nguồn mở có giấy phép cho phép dùng thương mại** (MIT/Apache/BSD/PostgreSQL License). Ghi toàn bộ dependency và license vào `docs/THIRD_PARTY_LICENSES.md`, vì hồ sơ thầu yêu cầu mọi công cụ phải có bản quyền.
5. Toàn bộ mã nguồn, CSDL và thiết kế giao diện thuộc sở hữu của chủ đầu tư sau bàn giao. Không nhúng license key, telemetry hay dịch vụ SaaS bên ngoài.
6. Mã (tên biến, bảng, API) viết bằng **tiếng Anh**. Giao diện, tài liệu và thông báo lỗi hiển thị **song ngữ Việt/Anh** (mặc định tiếng Việt).

---

## 1. TỔNG QUAN SẢN PHẨM

**Tên sản phẩm:** BlueMeal — Phần mềm quản lý bữa ăn ca thông minh, dinh dưỡng hợp lý cho người lao động.

**Mục tiêu nghiệp vụ:** giúp bếp ăn tập thể hoặc đơn vị cung cấp suất ăn ca:

- quản lý danh mục thực phẩm, nguyên liệu, món ăn và công thức;
- lập thực đơn (tự động hoặc thủ công) cân đối dinh dưỡng theo từng đối tượng lao động, rồi trình duyệt;
- tính thành phần dinh dưỡng, cảnh báo khi vượt hoặc thiếu chỉ tiêu, tính chi phí suất ăn;
- quản lý kho (nhập, xuất, kiểm kê) và an toàn thực phẩm (kiểm thực 3 bước, lưu mẫu);
- xuất báo cáo Excel;
- đồng bộ dữ liệu dinh dưỡng với cổng dữ liệu của Viện Dinh dưỡng qua API;
- đáp ứng **ATTT cấp độ 2** ở tầng phần mềm.

**Yêu cầu phi chức năng cứng:**

| Mã | Yêu cầu |
|---|---|
| NFR-01 | Chạy trên nền web, cài on-premise tại phòng máy chủ của chủ đầu tư (Docker trên Linux) |
| NFR-02 | Không giới hạn số tài khoản; chịu **≥ 1.000 người dùng đồng thời**, p95 API đọc < 1 giây, p95 API ghi < 2 giây |
| NFR-03 | Song ngữ Việt/Anh, chuyển ngôn ngữ tức thì, lưu lựa chọn theo người dùng |
| NFR-04 | Trình duyệt: Chrome, Cốc Cốc, Edge, Safari. Thiết bị: Windows, Linux, iOS, Android (responsive, dùng tốt trên điện thoại cho bếp trưởng và thủ kho) |
| NFR-05 | ATTT cấp độ 2 (mục 10). Tuân thủ quy định bảo vệ dữ liệu cá nhân của Việt Nam |
| NFR-06 | Chuẩn trao đổi dữ liệu JSON (REST), hỗ trợ xuất/nhập XML. Tham chiếu TT 39/2017/TT-BTTTT và TT 13/2025/TT-BYT |
| NFR-07 | Sao lưu tự động định kỳ, sao lưu thủ công bất thường, khôi phục được |
| NFR-08 | Có màn hình "Phiên bản hệ thống" hiển thị version, ngày build, changelog |

---

## 2. STACK CÔNG NGHỆ (CỐ ĐỊNH)

**Backend**
- .NET 8 (LTS), ASP.NET Core Web API, C# 12
- EF Core 8 + Npgsql; migrations bằng code-first
- ASP.NET Core Identity (bảng user và mật khẩu) + JWT access token (15 phút) + refresh token xoay vòng (lưu DB)
- FluentValidation, Mapster (hoặc mapping thủ công), Serilog (Console + File JSON + bảng AuditLog)
- Quartz.NET 3.x (Apache-2.0) + AdoJobStore PostgreSQL (clustered) cho job nền: đồng bộ API, sao lưu, cảnh báo hết hạn, tổng hợp báo cáo *(thay Hangfire do giấy phép LGPL — xem `docs/ASSUMPTIONS.md` A-34)*
- ClosedXML để xuất/nhập Excel
- Otp.NET + QRCoder cho MFA TOTP
- StackExchange.Redis cho cache, rate limit phân tán và danh sách token bị thu hồi
- Swashbuckle (OpenAPI/Swagger, chỉ bật ở môi trường dev/staging hoặc khi admin cho phép)
- Built-in Rate Limiter, Response Compression, Health Checks (`/health/live`, `/health/ready`)

**Frontend**
- React 18 + TypeScript + Vite
- Ant Design 5 (có sẵn locale vi_VN và en_US) + @ant-design/icons
- React Router 6, TanStack Query, Zustand (state nhẹ), react-hook-form + zod
- react-i18next (namespace theo module), dayjs (locale vi)
- Recharts cho biểu đồ dinh dưỡng và chi phí
- Axios với interceptor tự refresh token

**Dữ liệu và hạ tầng**
- PostgreSQL 16 (bật extension `pgcrypto`, `unaccent`, `pg_trgm` để tìm kiếm tiếng Việt không dấu)
- Redis 7.2 (BSD-3-Clause; không dùng ≥ 7.4 vì đổi giấy phép — A-35)
- Nginx làm reverse proxy + TLS + phục vụ static frontend
- WAF: OWASP ModSecurity CRS (image `owasp/modsecurity-crs:nginx`) đứng trước API
- Docker + Docker Compose; tách các file `docker-compose.yml`, `docker-compose.dev.yml`, `docker-compose.prod.yml`

**Kiểm thử**
- xUnit + FluentAssertions + Testcontainers (PostgreSQL thật) cho backend
- Vitest + React Testing Library cho frontend
- Playwright cho E2E
- k6 cho kiểm thử tải 1.000 VU
- OWASP ZAP baseline scan chạy bằng docker

---

## 3. CẤU TRÚC REPO

```
bluemeal/
├─ SPEC.md
├─ README.md                      # hướng dẫn chạy nhanh (Windows + Linux)
├─ .gitattributes / .editorconfig / .gitignore
├─ backend/
│  ├─ BlueMeal.sln
│  ├─ src/
│  │  ├─ BlueMeal.Domain/         # Entity, enum, value object, domain rule (không phụ thuộc gì)
│  │  ├─ BlueMeal.Application/    # Use case, DTO, validator, interface, NutritionEngine, MenuGenerator, CostEngine
│  │  ├─ BlueMeal.Infrastructure/ # EF Core, repository, Redis, Quartz.NET, Excel, encryption, VDD API client
│  │  └─ BlueMeal.Api/            # Controller, middleware, auth, DI, Program.cs
│  └─ tests/
│     ├─ BlueMeal.UnitTests/
│     └─ BlueMeal.IntegrationTests/
├─ frontend/
│  ├─ src/
│  │  ├─ app/ (router, layout, providers)
│  │  ├─ features/<module>/ (pages, components, api, hooks, schemas)
│  │  ├─ shared/ (components, utils, permission guard)
│  │  └─ locales/vi/*.json, locales/en/*.json
│  └─ e2e/ (Playwright)
├─ deploy/
│  ├─ nginx/ (nginx.conf, ssl/, modsecurity overrides)
│  ├─ postgres/ (init.sql, postgresql.conf tuning)
│  ├─ backup/ (backup.sh, restore.sh, Dockerfile)
│  ├─ docker-compose.yml / .dev.yml / .prod.yml
│  └─ .env.example
├─ tools/
│  ├─ mock-vdd-api/              # mock cổng dữ liệu Viện Dinh dưỡng (ASP.NET minimal API)
│  ├─ k6/ (load-test.js)
│  ├─ zap/ (zap-baseline.ps1/.sh)
│  └─ import-templates/ (file Excel mẫu nhập liệu)
├─ scripts/ (dev-up.ps1/.sh, migrate.ps1/.sh, seed.ps1/.sh, test-all.ps1/.sh)
└─ docs/ (xem mục 13)
```

---

## 4. VAI TRÒ NGƯỜI DÙNG (seed sẵn, admin sửa được)

| Mã | Vai trò | Quyền chính |
|---|---|---|
| SYS_ADMIN | Quản trị hệ thống | Toàn quyền quản trị: tài khoản, phân quyền, cấu hình, sao lưu, nhật ký, tích hợp |
| MANAGER | Lãnh đạo / Quản lý | Xem toàn bộ dashboard và báo cáo, duyệt thực đơn cấp cuối (nếu cấu hình 2 cấp) |
| MENU_APPROVER | Người duyệt thực đơn | Duyệt hoặc từ chối thực đơn, xem dinh dưỡng và chi phí |
| NUTRITIONIST | Cán bộ dinh dưỡng | Quản lý thực phẩm, khuyến nghị, món ăn, công thức; lập thực đơn |
| HEAD_CHEF | Bếp trưởng | Lập thực đơn, quản lý công thức, kiểm thực 3 bước, lưu mẫu, đề xuất xuất kho |
| STOREKEEPER | Thủ kho | Nhập, xuất, kiểm kê kho; kiểm tra chất lượng đầu vào |
| ACCOUNTANT | Kế toán | Giá nguyên liệu, chi phí suất ăn, báo cáo nhập-xuất-tồn, báo cáo chi phí |
| CUSTOMER_REP | Đại diện đơn vị đặt suất ăn (tùy chọn) | Xem thực đơn đã công bố của đơn vị mình, đặt và điều chỉnh số suất |

Phân quyền **theo chức năng** (permission code dạng `module.action`, ví dụ `menu.view`, `menu.create`, `menu.approve`, `stock.receipt.create`, `report.export`, `system.backup`). Có thêm **phạm vi dữ liệu** theo kho và theo khách hàng: thủ kho chỉ thấy kho được gán; CUSTOMER_REP chỉ thấy khách hàng của mình.

---

## 5. MÔ HÌNH DỮ LIỆU (PostgreSQL, snake_case, schema `app`)

Mọi bảng nghiệp vụ có các cột chung: `id uuid PK`, `created_at`, `created_by`, `updated_at`, `updated_by`, `is_deleted` (soft delete), `xmin` (dùng làm concurrency token). Các danh mục có thêm `code` (unique), `name_vi`, `name_en`, `is_active`, `sort_order`.

### 5.1 Quản trị hệ thống
- `organization` — thông tin đơn vị sử dụng phần mềm: tên, mã số thuế, địa chỉ, logo, điện thoại, email, người đại diện. Chỉ có 1 bản ghi.
- `department` — cơ cấu tổ chức dạng cây (`parent_id`).
- `country`, `province`, `ward` — danh mục hành chính theo mô hình 2 cấp (tỉnh/thành phố → xã/phường) sau sắp xếp năm 2025; `ward.province_id`. Seed đầy đủ 34 tỉnh/thành. Danh sách xã/phường nhập bằng Excel.
- `unit_of_measure` — mã, tên, loại (`Mass`, `Volume`, `Count`, `Energy`), `factor_to_base` (base: g, ml, cái, kcal). Seed: g, kg, mg, µg, ml, l, cái, quả, bó, hộp, gói, kcal, kJ.
- `employee` — mã, họ tên, phòng ban, chức vụ, giới tính, ngày sinh, **điện thoại (mã hóa)**, **email (mã hóa)**, **CCCD (mã hóa, tùy chọn)**, `user_id` nullable.
- `app_user` (Identity) + `app_role` + `user_role`, `permission` (danh sách chức năng, seed từ code), `role_permission`, `user_data_scope` (gán kho/khách hàng).
- `user_session` — refresh token hash, IP, user agent, thiết bị, `created_at`, `last_seen_at`, `expires_at`, `revoked_at`, `revoked_reason`.
- `login_history` — thành công/thất bại, IP, lý do.
- `mfa_recovery_code` (hash).
- `system_version` — version, ngày phát hành, ghi chú thay đổi (seed từ CHANGELOG khi build).
- `app_setting` — key/value JSON (cấu hình ngưỡng cảnh báo, phương pháp tính giá kho, chính sách mật khẩu, lịch sao lưu…).
- `audit_log` — mục 10.3. Partition theo tháng.
- `notification` — thông báo trong ứng dụng.

### 5.2 Đối tượng, đối tác, khách hàng
- `subject_group` (danh mục đối tượng) — tên, giới tính (Nam/Nữ/Chung), tuổi từ–đến, **mức độ lao động** (`Light`, `Moderate`, `Heavy`, `VeryHeavy`), điều kiện đặc thù (nóng, độc hại…), ghi chú.
- `partner_type`, `partner` (nhà cung cấp) — tên, MST, người liên hệ, điện thoại, email, địa chỉ, tài khoản ngân hàng, nhóm hàng cung cấp, đánh giá.
- `partner_contract` — số HĐ, ngày ký, hiệu lực từ–đến, giá trị, file đính kèm, trạng thái.
- `partner_certificate` — loại giấy (GCN cơ sở đủ điều kiện ATTP, giấy khám sức khỏe, kết quả xét nghiệm…), số, cơ quan cấp, ngày cấp, **ngày hết hạn**, file.
- `customer_type`, `customer` (đơn vị đặt suất ăn: công ty A, B…) — tên, MST, địa chỉ, liên hệ, đối tượng mặc định, số lao động, đơn giá suất ăn thỏa thuận, ca làm việc.
- `customer_contact`.

### 5.3 Thực phẩm, nguyên liệu, món ăn, công thức
- `warehouse` (danh mục kho) — mã, tên, loại (`Dry`, `Chilled`, `Frozen`, `Other`), vị trí, thủ kho phụ trách, nhiệt độ yêu cầu.
- `food_group` (phân nhóm thực phẩm, dạng cây) — ví dụ: Ngũ cốc và sản phẩm chế biến; Khoai củ; Hạt, quả giàu đạm và béo; Rau; Quả chín; Dầu, mỡ, bơ; Thịt; Thủy sản; Trứng; Sữa; Đồ hộp; Gia vị, nước chấm; Đồ ngọt; Nước giải khát. Có cờ `is_animal_source` dùng để tính tỷ lệ đạm động vật.
- `nutrient` (danh mục chất dinh dưỡng) — mã, tên vi/en, đơn vị, nhóm (Năng lượng / Đa lượng / Khoáng chất / Vitamin / Khác), `is_core`, thứ tự hiển thị. Seed tối thiểu: `ENERGY` (kcal), `WATER`, `PROTEIN`, `LIPID`, `GLUCID` (carbohydrate), `FIBER`, `ASH`, `CA`, `P`, `FE`, `ZN`, `NA`, `K`, `MG`, `VIT_A`, `BETA_CAROTENE`, `VIT_B1`, `VIT_B2`, `VIT_PP`, `VIT_C`, `VIT_D`, `CHOLESTEROL`, `SUGAR`, `SFA`.
- `food` (bảng thành phần thực phẩm) — mã (giữ mã theo Bảng thành phần thực phẩm Việt Nam nếu có), tên vi/en, nhóm, **tỷ lệ thải bỏ % (`refuse_rate`)**, `is_animal_source` (kế thừa nhóm, cho phép ghi đè), nguồn dữ liệu (`VDD_API`, `Manual`, `Import`), `source_version`, `last_synced_at`.
- `food_nutrient` — `food_id`, `nutrient_id`, `value_per_100g_edible`.
- `food_safety_spec` (quản lý ATTP, chất lượng thực phẩm theo từng thực phẩm/nguyên liệu) — tiêu chí cảm quan (màu, mùi, trạng thái), nhiệt độ bảo quản, hạn dùng tối đa sau nhập, yêu cầu giấy tờ nguồn gốc, chất gây dị ứng.
- `ingredient` (danh mục nguyên liệu — mặt hàng mua thực tế) — mã, tên, `food_id` (ánh xạ sang bảng thành phần để tính dinh dưỡng), đơn vị mua, **quy đổi ra gram**, kho mặc định, tồn tối thiểu, hạn dùng, điều kiện bảo quản, **giá tham chiếu hiện hành**, nhà cung cấp ưu tiên, chất gây dị ứng.
- `ingredient_price_history` — giá theo thời gian, nguồn (`Receipt` / `Manual` / `Contract`).
- `dish` (danh mục món ăn/thành phẩm) — mã, tên vi/en, **loại món** (`MainProtein` — món mặn, `StirFry` — món xào, `Soup` — canh, `Vegetable` — rau, `Staple` — cơm/tinh bột, `Dessert` — tráng miệng, `Drink`, `Other`), phương pháp chế biến (luộc, hấp, xào, chiên, kho, nướng, rim…), khối lượng 1 suất (g), ảnh, tag, chất gây dị ứng, `is_active`.
- `recipe` (công thức cấu tạo món ăn) — **có phiên bản**: `dish_id`, `version`, `is_current`, số suất cơ sở (mặc định 1), hướng dẫn chế biến, thời gian chế biến, **hệ số hao hụt khi chế biến** theo phương pháp, người lập, trạng thái (Draft / Active / Archived).
- `recipe_item` — `recipe_id`, `ingredient_id`, **khối lượng sống (gross, g)**, khối lượng sạch (net) tính tự động từ tỷ lệ thải bỏ (cho phép ghi đè), ghi chú sơ chế.
- `nutrient_retention_factor` (tùy chọn) — hệ số giữ lại vitamin theo phương pháp chế biến; mặc định = 1.
- `recommendation` (danh mục khuyến nghị) — `subject_group_id`, tên, nguồn tham chiếu (văn bản, năm), **nhu cầu năng lượng/ngày (kcal)**, **tỷ lệ bữa ca so với nhu cầu ngày (%)**, cơ cấu năng lượng P:L:G (min–max %), tỷ lệ đạm động vật/tổng đạm (min–max %), ngân sách/suất (VND).
- `recommendation_nutrient` — `recommendation_id`, `nutrient_id`, `min_per_meal`, `max_per_meal`, mức cảnh báo (`Info` / `Warning` / `Critical`).
- `report_group` (danh mục nhóm báo cáo) — nhóm các báo cáo để phân quyền xem và hiển thị menu báo cáo.

> **Lưu ý seed:** tạo bộ dữ liệu **MẪU** khoảng 80 thực phẩm, 40 nguyên liệu, 30 món và 3 bộ khuyến nghị (lao động nhẹ/vừa/nặng). Gắn nhãn rõ `[DỮ LIỆU MẪU]` và giá trị đặt trong `app_setting` để có thể xóa nhanh. Số liệu chính thức sẽ đồng bộ từ API Viện Dinh dưỡng hoặc nhập qua Excel. **Không được trình bày dữ liệu mẫu như số liệu chính thức.**

### 5.4 Thực đơn
- `meal_shift` — ca ăn (Ca 1, Ca 2, Ca 3, Bữa phụ…), giờ phục vụ.
- `meal_structure_template` — cơ cấu bữa ăn, ví dụ "Bữa ca chuẩn": 1 món mặn + 1 món xào + 1 canh + 1 rau + cơm + tráng miệng.
- `meal_structure_slot` — `template_id`, loại món, số lượng, bắt buộc/tùy chọn.
- `menu_plan` (kế hoạch xây dựng thực đơn) — tên, kỳ (`Week` / `Month` / `Custom`), từ ngày–đến ngày, khách hàng (nullable), đối tượng, cơ cấu bữa, ca ăn, ngân sách/suất, số suất dự kiến/ngày, quy tắc không lặp món (N ngày), trạng thái, người lập.
- `menu` (thực đơn của 1 ngày × 1 ca × 1 đối tượng/khách hàng) — `plan_id` nullable, ngày, ca, đối tượng, khách hàng, số suất, **cách tạo** (`Auto` / `Manual` / `Copied`), **trạng thái** (mục 7.4), **snapshot kết quả tính toán** (JSON: dinh dưỡng, cơ cấu, chi phí, danh sách cảnh báo; được tính lại mỗi khi sửa), điểm đánh giá dinh dưỡng (0–100).
- `menu_item` — `menu_id`, `dish_id`, `recipe_id` (chốt phiên bản công thức tại thời điểm duyệt), hệ số khẩu phần (mặc định 1.0), slot, thứ tự.
- `menu_approval` — lịch sử: hành động (Submit / Approve / Reject / Return / Publish / Cancel), cấp duyệt, người thực hiện, thời gian, ý kiến.
- `meal_order` (đặt suất ăn) — khách hàng, ngày, ca, đối tượng, số suất đăng ký, số suất thực tế, `menu_id`, trạng thái.

### 5.5 Kho
- `stock_receipt` (phiếu nhập) + `stock_receipt_line` — số phiếu tự sinh (`PN-YYYYMMDD-####`), ngày, kho, nhà cung cấp, hợp đồng, số hóa đơn; mỗi dòng gồm nguyên liệu, số lượng, đơn vị, đơn giá, thành tiền, **số lô, NSX, HSD**, **kết quả kiểm tra chất lượng đầu vào** (Đạt / Không đạt / Đạt có điều kiện), nhiệt độ khi nhận, ghi chú, ảnh.
- `stock_issue` (phiếu xuất) + `stock_issue_line` — số phiếu, ngày, kho, lý do (`ForMenu` / `Disposal` / `Return` / `Transfer` / `Other`), `menu_id` nullable; dòng gồm nguyên liệu, lô, số lượng, đơn giá xuất (tính theo phương pháp giá kho).
- `stock_take` (kiểm kê) + `stock_take_line` — kho, ngày, thành viên tổ kiểm kê; dòng gồm tồn sổ sách, tồn thực tế, chênh lệch, nguyên nhân, xử lý. Khi duyệt kiểm kê tự sinh phiếu điều chỉnh.
- `stock_ledger` — sổ cái bất biến mọi biến động (in/out/adjust), dùng để tính tồn kho theo thời điểm.
- `stock_balance` — bảng tồn hiện thời (materialized, cập nhật trong cùng transaction).
- `purchase_request` (dự trù / đề xuất mua) — sinh từ thực đơn đã duyệt: tổng nhu cầu nguyên liệu trừ tồn khả dụng trừ tồn tối thiểu.

### 5.6 An toàn thực phẩm
- `food_inspection_step1` — kiểm tra trước khi chế biến (thực phẩm nhập vào): gắn với phiếu nhập, từng dòng nguyên liệu, tiêu chí cảm quan, giấy tờ nguồn gốc, kết quả, người kiểm tra.
- `food_inspection_step2` — kiểm tra trong quá trình chế biến: điều kiện vệ sinh, người tham gia chế biến, thời gian bắt đầu/kết thúc chế biến, cảm quan món.
- `food_inspection_step3` — kiểm tra trước khi ăn: thời gian chia món, nhiệt độ, cảm quan, dụng cụ.
- `food_sample` (lưu mẫu thức ăn) — món, thực đơn, ngày giờ lấy mẫu, khối lượng, nhiệt độ bảo quản, người lấy mẫu, **thời gian hủy mẫu dự kiến (mặc định ≥ 24 giờ, cấu hình được)**, thời gian hủy thực tế, người hủy.
- Biểu mẫu in của 3 bước và sổ lưu mẫu bám theo hướng dẫn kiểm thực 3 bước và lưu mẫu thức ăn của Bộ Y tế. Cấu trúc biểu mẫu cấu hình được vì chủ đầu tư có thể yêu cầu chỉnh.

### 5.7 Tích hợp và vận hành
- `integration_endpoint` — cấu hình kết nối (base URL, kiểu xác thực, credential mã hóa, timeout, lịch đồng bộ).
- `integration_log` — request/response tóm tắt, trạng thái, thời lượng, số bản ghi.
- `sync_conflict` — bản ghi local đã bị sửa tay nhưng nguồn API cũng thay đổi, chờ người dùng chọn giữ bên nào.
- `backup_job` — loại (Scheduled / Manual), thời gian, kích thước, checksum SHA-256, vị trí lưu, trạng thái, người yêu cầu.
- `export_job` — báo cáo nặng chạy nền, link tải có hạn.

---

## 6. ĐỘNG CƠ TÍNH TOÁN (Application layer, viết thuần C#, unit test ≥ 90% coverage)

### 6.1 NutritionEngine
Với mỗi `recipe_item`:
```
net_g        = override_net_g ?? gross_g × (1 − refuse_rate/100)
nutrient_val = net_g / 100 × food_nutrient.value_per_100g_edible × retention_factor(nutrient, cooking_method)
```
- Món = tổng các nguyên liệu, nhân hệ số khẩu phần trên `menu_item`.
- Thực đơn = tổng các món.
- Từ đó tính thêm:
  - **Cơ cấu năng lượng**: `%P = protein×4/E`, `%L = lipid×9/E`, `%G = glucid×4/E`, với E là tổng kcal tính từ P, L, G. Đồng thời hiển thị kcal theo bảng thành phần.
  - **Tỷ lệ đạm động vật/tổng đạm** (dựa trên `is_animal_source`).
  - **% đáp ứng khuyến nghị**: giá trị thực tế chia mục tiêu bữa (mục tiêu bữa = nhu cầu ngày × tỷ lệ bữa ca, hoặc lấy `min/max_per_meal` nếu đã khai báo).
- Mọi phép tính dùng `decimal`, làm tròn chỉ ở bước hiển thị (năng lượng 0 chữ số thập phân, chất đa lượng 1, vi chất 2).
- Nguyên liệu chưa ánh xạ `food_id` thì vẫn tính chi phí, nhưng phát cảnh báo "thiếu dữ liệu dinh dưỡng".

### 6.2 WarningEngine (cảnh báo chỉ tiêu)
Rule engine cấu hình được trong `app_setting` + `recommendation_nutrient`. Các rule tối thiểu:

| Mã | Điều kiện | Mức |
|---|---|---|
| W-ENERGY-LOW / HIGH | Năng lượng < 90% hoặc > 110% mục tiêu bữa (ngưỡng cấu hình) | Warning; Critical nếu < 80% hoặc > 120% |
| W-PLG-RANGE | %P, %L, %G nằm ngoài khoảng khuyến nghị | Warning |
| W-ANIMAL-PROTEIN | Tỷ lệ đạm động vật ngoài khoảng | Warning |
| W-NUTRIENT-MIN / MAX | Bất kỳ chất có `min/max_per_meal` bị vi phạm (ví dụ Na quá cao, chất xơ thấp) | Theo cấu hình |
| W-COST-OVER | Chi phí/suất > ngân sách | Warning; Critical nếu vượt > 10% |
| W-REPEAT-DISH | Món lặp lại trong N ngày gần nhất cùng đối tượng | Info |
| W-STRUCTURE | Thiếu slot bắt buộc trong cơ cấu bữa | Critical |
| W-MISSING-DATA | Nguyên liệu chưa có dữ liệu dinh dưỡng hoặc chưa có giá | Warning |
| W-ALLERGEN | Món chứa chất gây dị ứng đã khai báo cho khách hàng/đối tượng | Warning |
| W-STOCK-SHORT | Nhu cầu nguyên liệu cho thực đơn > tồn khả dụng + hàng đang đặt | Info |
| W-CERT-EXPIRY | Giấy chứng nhận nhà cung cấp hết hạn trong ≤ 30 ngày / đã hết hạn | Warning / Critical (job hằng ngày) |
| W-STOCK-EXPIRY | Lô hàng sắp hết hạn trong ≤ 3 ngày / đã hết hạn | Warning / Critical (job hằng ngày) |
| W-STOCK-MIN | Tồn < tồn tối thiểu | Info |
| W-SAMPLE-DISPOSE | Mẫu lưu đã quá thời hạn mà chưa ghi hủy | Info |

**Thực đơn có cảnh báo Critical không được gửi duyệt**, trừ khi người lập ghi lý do và người có quyền `menu.override_critical` chấp nhận.

### 6.3 CostEngine (tính chi phí suất ăn)
```
ingredient_cost = gross_g / gram_per_purchase_unit × unit_price
dish_cost       = Σ ingredient_cost × serving_factor
menu_food_cost  = Σ dish_cost
menu_cost/suất  = menu_food_cost + overhead
```
- `overhead` gồm chất đốt, nhân công, khấu hao, vật tư tiêu hao. Mỗi khoản cấu hình theo **% giá thực phẩm** hoặc **số tiền cố định/suất**.
- Nguồn đơn giá chọn trong cấu hình:
  - (a) giá tham chiếu hiện hành;
  - (b) giá nhập gần nhất;
  - (c) giá bình quân gia quyền trong kho (**mặc định**);
  - (d) giá theo hợp đồng nhà cung cấp.
- Hiển thị: tổng chi phí, chi phí/suất, chi phí theo món, theo nhóm thực phẩm, chênh lệch so với ngân sách và đơn giá thỏa thuận với khách hàng (lãi/lỗ dự kiến).
- So sánh **chi phí kế hoạch** (theo thực đơn) với **chi phí thực tế** (theo phiếu xuất gắn thực đơn).

### 6.4 MenuGenerator (tạo thực đơn tự động)
**Đầu vào:** đối tượng/khuyến nghị, khoảng ngày, ca, cơ cấu bữa, ngân sách/suất, số ngày không lặp món, danh sách món bắt buộc/loại trừ, chất gây dị ứng cần tránh, ưu tiên dùng nguyên liệu tồn kho sắp hết hạn (tùy chọn), tỷ lệ món mới.

**Thuật toán** (có thể kiểm thử và tái lập bằng `seed`):
1. Lọc ứng viên cho từng slot: món active, đúng loại món, có công thức hiện hành, không chứa chất gây dị ứng bị cấm, không thiếu dữ liệu dinh dưỡng.
2. Với mỗi ngày, tạo K = 200 tổ hợp ngẫu nhiên có trọng số. Trọng số ưu tiên món lâu chưa dùng, món dùng nguyên liệu tồn kho, và món đa dạng phương pháp chế biến (không quá 1 món chiên/bữa).
3. Hàm mục tiêu (càng nhỏ càng tốt):
   ```
   score = w1·|E−E*|/E* + w2·Σ dev(%P,%L,%G) + w3·dev(animal_protein)
         + w4·Σ vi_phạm_vi_chất + w5·max(0, cost−budget)/budget
         + w6·lặp_món + w7·trùng_phương_pháp_chế_biến
   ```
   Các trọng số w cấu hình được, có giá trị mặc định hợp lý.
4. Lấy top 10 tổ hợp, chạy **local search** (hoán đổi 1 món/slot, tối đa 500 vòng) để giảm score.
5. Chọn tổ hợp tốt nhất. Cho phép người dùng **tạo lại** hoặc **xem 3 phương án thay thế**.
6. Sinh tuần tự theo ngày để đảm bảo ràng buộc không lặp món qua nhiều ngày.
7. Đầu ra: thực đơn ở trạng thái Draft, có snapshot dinh dưỡng, chi phí, cảnh báo và giải thích ngắn lý do chọn (song ngữ).

**Hiệu năng:** sinh thực đơn 1 tháng (≈ 26 ngày × 1 ca) dưới 5 giây với 500 món. Chạy dạng job nếu > 3 ca.

### 6.5 InventoryEngine
- Ghi sổ `stock_ledger` trong cùng transaction với phiếu; **không cho xuất âm kho** (cấu hình được).
- Phương pháp tính giá xuất: **bình quân gia quyền di động (mặc định)** hoặc FIFO theo lô.
- Gợi ý lô xuất theo **FEFO** (hết hạn trước xuất trước).
- Từ thực đơn đã duyệt: sinh **phiếu xuất dự kiến** theo định lượng × số suất, và sinh **đề xuất mua hàng**.
- Phiếu đã duyệt thì khóa sửa; muốn điều chỉnh phải lập phiếu điều chỉnh. Có khóa sổ theo kỳ (tháng).

---

## 7. CHỨC NĂNG CHI TIẾT THEO MODULE (backend API + màn hình frontend)

Mọi màn hình danh mục có chung bộ chức năng: danh sách phân trang server-side, tìm kiếm không dấu, lọc, sắp xếp, thêm/sửa/xóa mềm, khóa/mở, xem lịch sử thay đổi, **nhập Excel theo mẫu** (validate từng dòng, báo lỗi theo dòng, chế độ xem trước rồi mới ghi), **xuất Excel**.

### 7.1 Quản lý doanh nghiệp và tài khoản (quản lý hệ thống)
1. **Thông tin đơn vị** — sửa thông tin, logo (logo hiển thị trên header và báo cáo).
2. **Cơ cấu tổ chức** — cây phòng ban, kéo-thả để sắp xếp.
3. **Danh mục chung** — quốc gia, tỉnh/thành, xã/phường, đơn vị đo lường (có công cụ quy đổi).
4. **Danh mục nhân viên** — CRUD; tạo tài khoản đăng nhập từ nhân viên.
5. **Danh mục phân quyền nhân viên / Nhóm quyền** — tạo nhóm quyền (Kế toán, Thủ kho, Bếp trưởng, Người duyệt thực đơn…), ma trận tích chọn quyền theo module × hành động (Xem / Thêm / Sửa / Xóa / Duyệt / Xuất / Nhập).
6. **Tài khoản người dùng** — thêm, sửa, khóa/mở, reset mật khẩu, bắt buộc đổi mật khẩu lần đầu, gán nhóm quyền, gán phạm vi dữ liệu, bật/bắt buộc MFA, xem phiên đăng nhập của user.
7. **Phiên làm việc** — danh sách ai đang đăng nhập, IP, thiết bị/trình duyệt, thời gian đăng nhập và hoạt động gần nhất. Admin **đăng xuất cưỡng bức** từng phiên hoặc toàn bộ phiên của một user.
8. **Danh sách chức năng** — liệt kê toàn bộ permission (sinh từ code, chỉ đọc), mô tả song ngữ.
9. **Phân quyền hệ thống** — quy định ai được xem, ai được sửa từng mục dữ liệu (permission + phạm vi dữ liệu).
10. **Phiên bản hệ thống** — version hiện tại, build time, commit hash, changelog.
11. **Đăng nhập / đăng xuất / quên mật khẩu** — quên mật khẩu tự phục vụ qua email (link token 1 lần, hết hạn 15 phút). Nếu chưa cấu hình SMTP thì cho admin reset. Chống dò tài khoản: luôn trả thông báo trung tính.
12. **Hồ sơ cá nhân** — đổi mật khẩu, bật MFA (quét QR), mã khôi phục, ngôn ngữ, xem lịch sử đăng nhập của mình.
13. **Cấu hình hệ thống** — chính sách mật khẩu, ngưỡng cảnh báo, phương pháp giá kho, chi phí chung, SMTP, lịch sao lưu, thời gian lưu mẫu, trọng số MenuGenerator.

### 7.2 Danh mục đối tượng, đối tác, khách hàng
- **Đối tượng** (subject group) — CRUD; gắn với khuyến nghị.
- **Loại đối tác / Đối tác** — CRUD; tab Hợp đồng, tab Giấy chứng nhận ATTP (upload file PDF/ảnh), tab Lịch sử nhập hàng, tab Đánh giá.
- **Loại khách hàng / Khách hàng** — CRUD; tab Liên hệ, tab Đơn giá suất ăn, tab Đặt suất ăn, tab Thực đơn đã công bố.

### 7.3 Món ăn và công thức
1. **Danh mục kho**
2. **Phân nhóm thực phẩm** (cây)
3. **Danh mục thực phẩm** — danh sách thực phẩm với tỷ lệ thải bỏ và **bảng giá trị dinh dưỡng/100g** dạng lưới chỉnh sửa nhanh. Có nút "Đồng bộ từ Viện Dinh dưỡng". Hiển thị nguồn và phiên bản dữ liệu; dữ liệu từ API mặc định khóa sửa, mở khóa cần quyền.
4. **Quản lý ATTP, chất lượng thực phẩm** — tiêu chuẩn cảm quan, điều kiện bảo quản, giấy tờ yêu cầu theo thực phẩm/nguyên liệu; màn hình kiểm thực 3 bước và sổ lưu mẫu (mục 5.6); dashboard cảnh báo hết hạn.
5. **Danh mục nguyên liệu** — ánh xạ thực phẩm (combobox tìm không dấu), quy đổi đơn vị, giá tham chiếu, lịch sử giá, tồn hiện tại.
6. **Danh mục món ăn/thành phẩm** — CRUD, ảnh, loại món, phương pháp chế biến; xem nhanh dinh dưỡng và chi phí 1 suất.
7. **Công thức cấu tạo món ăn** — trình soạn công thức: thêm nguyên liệu, nhập khối lượng sống, tự tính khối lượng sạch, **tính dinh dưỡng và chi phí realtime** khi gõ, biểu đồ cơ cấu P:L:G. Quản lý phiên bản (tạo bản mới, so sánh 2 phiên bản, kích hoạt). Nhân bản công thức. Quy đổi theo số suất (ví dụ xem định lượng cho 350 suất).
8. **Danh mục khuyến nghị** — CRUD; bảng mục tiêu theo chất dinh dưỡng; gắn đối tượng.
9. **Danh mục nhóm báo cáo** — CRUD; gán báo cáo vào nhóm; phân quyền xem theo nhóm.

### 7.4 Lập thực đơn và quản lý thực đơn
1. **Kế hoạch xây dựng thực đơn** — tạo kế hoạch tuần/tháng; màn hình **lịch** (tuần/tháng) hiển thị từng ô ngày × ca với trạng thái màu; kéo-thả món giữa các ngày; nhân bản tuần; áp dụng cho nhiều khách hàng.
2. **Danh sách thực đơn** — xem lại toàn bộ thực đơn đã lập, lọc theo ngày, ca, đối tượng, khách hàng, trạng thái, người lập; xem chi tiết; sao chép thành thực đơn mới; in.
3. **Tạo thực đơn tự động** — form tham số (mục 6.4), chạy, xem kết quả và phương án thay thế; mỗi món có nút "đổi món" gợi ý top 5 món thay thế cùng slot làm điểm tốt hơn; lưu thành Draft.
4. **Tạo thực đơn thủ công** — chọn món theo slot (tìm kiếm, lọc theo loại món/nhóm), chỉnh hệ số khẩu phần; panel bên phải hiển thị **realtime**: năng lượng, P/L/G, % đáp ứng, chi phí/suất, danh sách cảnh báo màu.
5. **Phê duyệt thực đơn** — workflow:
   ```
   Draft → Submitted → (Approved | Rejected | Returned) → Published → (Cancelled)
   ```
   - Hỗ trợ cấu hình **1 cấp hoặc 2 cấp duyệt**. Duyệt hàng loạt theo tuần.
   - Người duyệt xem đầy đủ snapshot dinh dưỡng, chi phí và cảnh báo trước khi quyết định; bắt buộc nhập ý kiến khi từ chối hoặc trả lại.
   - Chỉ thực đơn **Approved/Published** mới được dùng để nấu chính thức, sinh phiếu xuất và đề xuất mua.
   - Thực đơn đã duyệt bị khóa; muốn sửa phải "Thu hồi" (yêu cầu quyền + lý do), và hệ thống ghi audit.
   - Thông báo trong app (và email nếu có SMTP) cho người duyệt khi có thực đơn chờ duyệt, và cho người lập khi có kết quả.
6. **Công bố thực đơn** — trang thực đơn tuần cho khách hàng/người lao động xem (responsive, có thể in A4 hoặc xuất PDF từ trình duyệt), hiển thị món và năng lượng, ẩn chi phí.
7. **Đặt suất ăn** — khách hàng hoặc admin nhập số suất theo ngày/ca; tổng hợp số suất để tính định lượng.

### 7.5 Tính và phân tích thành phần dinh dưỡng
- Màn hình phân tích cho **1 món / 1 thực đơn / 1 khoảng ngày**:
  - bảng đầy đủ các chất dinh dưỡng;
  - % đáp ứng khuyến nghị (thanh tiến độ màu);
  - biểu đồ tròn P:L:G;
  - biểu đồ cột vi chất;
  - biểu đồ xu hướng năng lượng theo ngày;
  - trung bình tuần/tháng.
- So sánh 2 thực đơn hoặc 2 đối tượng.
- Xuất Excel bảng phân tích.

### 7.6 Cảnh báo chỉ tiêu
- Hiển thị inline trong màn hình thực đơn và công thức.
- Trung tâm cảnh báo (`/alerts`): lọc theo mức/loại/trạng thái, đánh dấu đã xử lý kèm ghi chú.
- Chuông thông báo trên header.
- Màn hình cấu hình ngưỡng (admin/nutritionist).

### 7.7 Tính chi phí suất ăn
- Chi phí realtime trong công thức và thực đơn.
- Màn hình **Bảng tính chi phí** theo ngày/tuần/tháng × khách hàng: số suất, chi phí kế hoạch, chi phí thực tế (từ phiếu xuất), doanh thu theo đơn giá thỏa thuận, chênh lệch.
- Cấu hình chi phí chung (overhead).

### 7.8 Kho
- Phiếu nhập: tạo từ đề xuất mua hoặc thủ công; kiểm tra chất lượng đầu vào (bước 1 kiểm thực) ngay trên phiếu; quét mã vạch nếu có (dùng camera trên mobile qua thư viện JS mã nguồn mở, tùy chọn); in phiếu.
- Phiếu xuất: tạo từ thực đơn đã duyệt (tự tính định lượng × số suất, gợi ý lô FEFO) hoặc thủ công; in phiếu.
- Kiểm kê: tạo phiên kiểm kê, nhập số thực tế trên mobile, duyệt, tự sinh điều chỉnh.
- Thẻ kho theo nguyên liệu; tồn kho theo thời điểm; cảnh báo hết hạn và tồn tối thiểu.
- Khóa sổ theo tháng.

### 7.9 Báo cáo và xuất Excel
Tất cả báo cáo có: bộ lọc (khoảng ngày, kho, khách hàng, đối tượng, nhóm thực phẩm), xem trên web, **xuất Excel** (ClosedXML, có tiêu đề đơn vị, tên báo cáo, kỳ báo cáo, người lập, ngày lập, chữ ký; định dạng số kiểu Việt Nam), lưu bộ lọc yêu thích. Báo cáo nặng chạy nền qua `export_job`.

| Mã | Báo cáo | Nhóm |
|---|---|---|
| RPT-01 | **Thống kê nhập/xuất thực phẩm** — tổng hợp theo nguyên liệu/nhóm/nhà cung cấp: số lượng, giá trị, thời điểm | Kho |
| RPT-02 | **Nhập – xuất – tồn** theo kỳ | Kho |
| RPT-03 | **Kiểm kê kho thực phẩm** — đối chiếu tồn thực tế với sổ sách, chênh lệch, nguyên nhân | Kho |
| RPT-04 | Thẻ kho chi tiết theo nguyên liệu | Kho |
| RPT-05 | Hàng sắp hết hạn / đã hết hạn / dưới tồn tối thiểu | Kho |
| RPT-06 | **Khuyến nghị dinh dưỡng** — mức dinh dưỡng thực tế đạt được so với khuyến nghị theo ngày/tuần/tháng × đối tượng × khách hàng, % đáp ứng, số ngày đạt/không đạt | Dinh dưỡng |
| RPT-07 | Phân tích dinh dưỡng thực đơn (chi tiết từng chất) | Dinh dưỡng |
| RPT-08 | Thống kê cảnh báo chỉ tiêu | Dinh dưỡng |
| RPT-09 | Tần suất sử dụng món ăn / nguyên liệu | Thực đơn |
| RPT-10 | Thực đơn tuần/tháng (mẫu công bố) | Thực đơn |
| RPT-11 | Chi phí suất ăn: kế hoạch và thực tế, theo khách hàng | Chi phí |
| RPT-12 | Biến động giá nguyên liệu | Chi phí |
| RPT-13 | Sổ kiểm thực 3 bước | ATTP |
| RPT-14 | Sổ lưu mẫu và hủy mẫu thức ăn | ATTP |
| RPT-15 | Giấy chứng nhận nhà cung cấp sắp hết hạn | ATTP |
| RPT-16 | Nhật ký truy cập / thao tác hệ thống (cho admin) | Hệ thống |

**Dashboard trang chủ** (theo vai trò): thực đơn chờ duyệt, cảnh báo Critical, số suất hôm nay, chi phí/suất tuần này so với ngân sách, % đáp ứng năng lượng trung bình 7 ngày, hàng sắp hết hạn, mẫu lưu cần hủy.

### 7.10 Tích hợp API dữ liệu dinh dưỡng (Viện Dinh dưỡng)
Đặc tả API thật của cổng dữ liệu hiện **chưa có**. Vì vậy:

1. Định nghĩa interface `INutritionDataProvider` trong Application:
   ```csharp
   Task<IReadOnlyList<ExtFoodGroup>>      GetFoodGroupsAsync(DateTimeOffset? modifiedSince, CancellationToken ct);
   Task<PagedResult<ExtFood>>             GetFoodsAsync(DateTimeOffset? modifiedSince, int page, int pageSize, CancellationToken ct);
   Task<IReadOnlyList<ExtNutrient>>       GetNutrientsAsync(CancellationToken ct);
   Task<IReadOnlyList<ExtRecommendation>> GetRecommendationsAsync(DateTimeOffset? modifiedSince, CancellationToken ct);
   Task<PushResult>                       PushAggregatedReportAsync(NutritionSummaryPayload payload, CancellationToken ct); // tùy chọn
   ```
2. Implementation `VddApiNutritionDataProvider` (HttpClient + Polly retry/circuit breaker, timeout cấu hình). Hỗ trợ các kiểu xác thực: API Key header, Basic, **OAuth2 client credentials**. Credential lưu mã hóa trong `integration_endpoint`.
3. **Tầng ánh xạ (mapping)** tách riêng, cấu hình bằng file JSON trong `integration_endpoint.mapping_json` (tên field nguồn → field đích, quy đổi đơn vị). Khi có đặc tả thật chỉ cần sửa mapping, không sửa code.
4. **Mock server** `tools/mock-vdd-api` trả dữ liệu mẫu đúng hợp đồng giả định, có trong docker-compose dev để demo và test.
5. **Đồng bộ:**
   - Quartz job theo lịch (mặc định 2h sáng) + nút đồng bộ thủ công.
   - Đồng bộ tăng dần theo `modifiedSince`, upsert theo mã.
   - Bản ghi đã sửa tay mà nguồn cũng đổi thì tạo `sync_conflict` để người dùng chọn.
   - Ghi `integration_log`. Màn hình lịch sử đồng bộ và màn hình xử lý xung đột.
6. **API của chính BlueMeal** cho hệ thống khác gọi vào: `/api/v1/public/...` (bảo vệ bằng client credentials + scope), trả JSON. Cung cấp thêm **XML** qua content negotiation (`Accept: application/xml`). Tài liệu OpenAPI xuất ra `docs/api/openapi.json`.
7. (Tùy chọn, làm sau cùng nếu còn thời gian) endpoint xuất dữ liệu theo HL7 FHIR R4 cho thực đơn/dinh dưỡng ở mức tối giản. Ghi rõ phạm vi trong ASSUMPTIONS.

---

## 8. QUY ƯỚC API

- REST, prefix `/api/v1`, JSON camelCase, thời gian ISO-8601 có offset (múi giờ mặc định `Asia/Ho_Chi_Minh`).
- Phân trang `?page=&pageSize=&sort=&q=` → `{ items, page, pageSize, totalItems }`; `pageSize` tối đa 200.
- Lỗi theo **RFC 7807 ProblemDetails**, có `traceId` và `errors` theo field. Thông điệp lỗi lấy theo `Accept-Language` (vi/en).
- Kiểm soát đồng thời bằng `xmin`: client gửi `version`, xung đột trả 409.
- Idempotency-Key cho các POST tạo phiếu kho.
- Mọi endpoint gắn `[HasPermission("...")]`; mặc định **deny** nếu thiếu attribute (thêm test quét toàn bộ controller để đảm bảo).
- Không trả entity trực tiếp, chỉ trả DTO.

---

## 9. FRONTEND — YÊU CẦU GIAO DIỆN

- Layout Ant Design Pro-like: sidebar menu theo quyền (ẩn mục không có quyền), header có chọn ngôn ngữ VI/EN, chuông thông báo, avatar/hồ sơ, breadcrumb.
- Màu chủ đạo xanh lá dịu (chủ đề dinh dưỡng); hỗ trợ dark mode.
- **Responsive**: các màn hình kho, kiểm thực, lưu mẫu, xem thực đơn và duyệt thực đơn phải dùng tốt trên điện thoại (≥ 360px).
- Bảng dữ liệu: cột cố định, ẩn/hiện cột, lưu cấu hình cột theo user.
- Form: validate bằng zod (thông báo song ngữ), cảnh báo khi rời trang mà chưa lưu.
- Định dạng số và tiền kiểu Việt Nam (`1.234.567 ₫`), ngày `dd/MM/yyyy`.
- Toàn bộ chuỗi giao diện nằm trong `locales/vi` và `locales/en`. Viết script CI kiểm tra **không thiếu key** giữa hai ngôn ngữ và **không có chuỗi hard-code** trong JSX (ESLint rule).
- Trang lỗi 403/404/500 thân thiện; trang bảo trì.
- Chỉ lưu access token trong bộ nhớ; refresh token dùng **cookie HttpOnly, Secure, SameSite=Strict**. Không lưu token trong localStorage.

---

## 10. AN TOÀN THÔNG TIN CẤP ĐỘ 2 (tầng phần mềm)

Thiết kế theo yêu cầu cơ bản cấp độ 2 (Nghị định 85/2016/NĐ-CP, TT 12/2022/TT-BTTTT) và quy định bảo vệ dữ liệu cá nhân hiện hành. Viết `docs/03_ThietKe_ATTT_CapDo2.md` ánh xạ **từng yêu cầu → biện pháp kỹ thuật → vị trí trong code → cách kiểm thử**.

### 10.1 Xác thực
- Mật khẩu: tối thiểu 10 ký tự, có hoa, thường, số và ký tự đặc biệt (cấu hình được); không trùng 5 mật khẩu gần nhất; hết hạn sau 90 ngày (cấu hình); hash bằng Identity PBKDF2 (≥ 100k vòng) hoặc Argon2id.
- **MFA TOTP** (Google/Microsoft Authenticator) + 10 mã khôi phục. **Bắt buộc với vai trò SYS_ADMIN, MANAGER, MENU_APPROVER, ACCOUNTANT**; các vai trò khác do admin cấu hình. Thêm tùy chọn OTP qua email làm phương án dự phòng.
- Khóa tài khoản sau 5 lần sai trong 15 phút; CAPTCHA tự host (ví dụ thử thách đơn giản phía server) sau 3 lần sai.
- Phiên: hết hạn khi không hoạt động 30 phút (cấu hình); giới hạn số phiên đồng thời/user (cấu hình); thu hồi refresh token khi đổi mật khẩu hoặc khi admin khóa tài khoản.

### 10.2 Kiểm soát truy cập
RBAC + phạm vi dữ liệu (mục 4). Kiểm tra quyền ở **server** cho mọi request. Chống IDOR: mọi truy vấn theo id đều lọc theo phạm vi dữ liệu. Test tự động cho ma trận quyền.

### 10.3 Nhật ký tập trung
- Bảng `audit_log`: thời điểm, user, IP, user agent, hành động (Login, Logout, LoginFailed, Create, Update, Delete, Approve, Reject, Export, Import, PermissionChange, ConfigChange, Backup, Restore, SyncRun…), loại đối tượng, id, **giá trị trước/sau (JSON diff, che trường nhạy cảm)**, kết quả, traceId.
- Ghi bằng EF Core SaveChanges interceptor + middleware.
- Log ứng dụng (Serilog JSON) ghi ra file xoay vòng, có thể đẩy sang syslog/SIEM qua cấu hình.
- Nhật ký **chỉ được thêm, không sửa/xóa qua ứng dụng**: user DB của app không có quyền UPDATE/DELETE trên bảng audit_log.
- Lưu tối thiểu 12 tháng (cấu hình).
- Màn hình tra cứu nhật ký cho admin, có lọc và xuất Excel.

### 10.4 Mã hóa
- **Đường truyền:** TLS 1.2+ tại Nginx, HSTS, chuyển hướng HTTP sang HTTPS; nội bộ giữa các container chạy trên mạng docker riêng.
- **Lưu trữ:**
  - Mã hóa cấp cột (AES-256-GCM qua EF ValueConverter) cho số điện thoại, email, CCCD của nhân viên/liên hệ, credential tích hợp và secret MFA. Lưu thêm cột hash (HMAC) phục vụ tìm kiếm chính xác.
  - Khóa lấy từ Docker secret/biến môi trường, hỗ trợ xoay khóa (key id trong ciphertext).
  - File backup được mã hóa (AES-256, ví dụ dùng `age` hoặc `openssl`).
- File đính kèm lưu volume riêng, tên file ngẫu nhiên, kiểm tra MIME/kích thước, chỉ phục vụ qua API có kiểm tra quyền.

### 10.5 Chống tấn công ứng dụng
- WAF ModSecurity + OWASP CRS (paranoia level 1, có file loại trừ false positive).
- Rate limit theo IP và theo user (đăng nhập, quên mật khẩu, API chung).
- Security headers: CSP chặt, X-Content-Type-Options, X-Frame-Options DENY, Referrer-Policy, Permissions-Policy.
- Chống CSRF cho cookie refresh token (SameSite + anti-forgery header).
- Chặn SQL injection (chỉ dùng EF/parameter, cấm nối chuỗi SQL) và XSS (React escape mặc định; cấm `dangerouslySetInnerHTML`).
- Validate mọi input; giới hạn kích thước request; chống mass assignment bằng DTO.
- Dependency scan (`dotnet list package --vulnerable`, `npm audit`) trong script CI.
- **OWASP ZAP baseline phải sạch lỗi mức High/Medium** trước khi bàn giao.

### 10.6 Bảo vệ dữ liệu cá nhân
- Thu thập tối thiểu.
- Che dữ liệu (masking) khi hiển thị cho người không có quyền `pii.view`.
- Nhật ký truy cập dữ liệu cá nhân.
- Chức năng xuất và xóa (ẩn danh hóa) dữ liệu cá nhân của một nhân viên theo yêu cầu.
- Không đưa dữ liệu ra hệ thống bên ngoài, trừ API tích hợp đã cấu hình.

### 10.7 Sao lưu – phục hồi
- Container `backup`: `pg_dump -Fc` hằng ngày lúc 01:00 + backup thư mục file đính kèm, mã hóa, checksum.
- Giữ 7 bản ngày, 4 bản tuần, 6 bản tháng (cấu hình). Cho phép đồng bộ ra thư mục mount/NAS ngoài.
- **Sao lưu bất thường** bằng nút trong màn hình quản trị (quyền `system.backup`), chạy qua Quartz.NET (job nền).
- Màn hình lịch sử sao lưu và tải bản sao lưu (yêu cầu nhập lại mật khẩu + MFA).
- `restore.ps1/.sh` khôi phục vào DB đích. Viết **quy trình diễn tập khôi phục** trong tài liệu, và có test tự động: backup → restore vào DB tạm → so số bản ghi.

---

## 11. HIỆU NĂNG (≥ 1.000 người dùng đồng thời)

- API stateless, chạy **2 replica** sau Nginx (`least_conn`).
- Connection pooling Npgsql (Max Pool Size cấu hình); `AsNoTracking` cho truy vấn đọc; projection thẳng ra DTO; tránh N+1.
- Index cho các cột lọc/tìm kiếm; index trigram trên tên đã bỏ dấu.
- Cache Redis cho danh mục ít đổi (thực phẩm, dinh dưỡng, món, khuyến nghị, quyền); invalidate khi sửa.
- Nén gzip/brotli; frontend code-splitting theo route; static asset cache dài hạn có hash.
- Tuning `postgresql.conf` mẫu cho máy 8 vCPU/16 GB.
- `tools/k6/load-test.js`: kịch bản đăng nhập → xem dashboard → xem thực đơn tuần → mở công thức → tìm thực phẩm → xuất báo cáo nhỏ, **ramp lên 1.000 VU giữ trong 10 phút**. Ngưỡng: p95 đọc < 1s, tỷ lệ lỗi < 1%. Xuất báo cáo HTML vào `docs/test-reports/`.

---

## 12. DOCKER VÀ TRIỂN KHAI

`docker-compose.prod.yml` gồm các service:
- `nginx` — ModSecurity CRS, TLS, phục vụ frontend build, proxy `/api` → api.
- `api` — 2 replica; healthcheck; chạy user không phải root; read-only filesystem trừ thư mục cần ghi.
- `worker` — cùng image api nhưng chạy chế độ Quartz scheduler (worker), tách khỏi API.
- `postgres:16` — volume riêng; `init.sql` tạo 2 user: `bluemeal_owner` (migration) và `bluemeal_app` (quyền tối thiểu, không UPDATE/DELETE audit_log).
- `redis:7` — có mật khẩu.
- `backup`.
- (dev) `mock-vdd-api`, `mailpit` (bắt email test), `pgadmin` (tùy chọn).

Các yêu cầu khác:
- `.env.example` liệt kê đầy đủ biến; tuyệt đối không commit secret.
- Script `scripts/dev-up.ps1` chạy được ngay trên Windows: build → up → migrate → seed → in ra URL và tài khoản admin mặc định (bắt buộc đổi mật khẩu và bật MFA ở lần đăng nhập đầu).
- Migration tự chạy khi khởi động (cờ cấu hình), hoặc chạy qua script riêng.
- Có hướng dẫn cài đặt lên **Ubuntu Server 22.04/24.04** và **Windows Server có Docker** trong `docs/05_HuongDan_CaiDat.md`.
- Hỗ trợ triển khai **offline** (máy chủ không có Internet): script `save-images` và `load-images` dùng `docker save/load`.

---

## 13. TÀI LIỆU BÀN GIAO (sinh trong `docs/`, Markdown, tiếng Việt)

Đây là các sản phẩm bàn giao theo hồ sơ thầu. Viết đầy đủ, chuyên nghiệp, có thể xuất sang Word:

1. `01_SRS_DacTaYeuCau.md` — đặc tả yêu cầu nghiệp vụ: actor, use case, user story, tiêu chí chấp nhận cho từng chức năng ở mục 7.
2. `02_ThietKe_HeThong.md` — kiến trúc tổng thể (sơ đồ Mermaid), phân hệ, luồng dữ liệu, ERD (Mermaid), thiết kế API, thiết kế giao diện (danh sách màn hình), triển khai.
3. `03_ThietKe_ATTT_CapDo2.md` — như mục 10.
4. `04_KichBan_KiemThu.md` — test case chức năng (ID, bước, dữ liệu, kết quả mong đợi) phủ toàn bộ mục 7; kế hoạch kiểm thử hiệu năng và bảo mật.
5. `05_HuongDan_CaiDat.md` — cài đặt, cấu hình, nâng cấp, sao lưu/khôi phục, xử lý sự cố thường gặp.
6. `06_HDSD_<VaiTro>.md` — hướng dẫn sử dụng **theo từng vai trò** (admin, dinh dưỡng, bếp trưởng, thủ kho, kế toán, người duyệt, khách hàng), có chỗ đặt ảnh chụp màn hình (Playwright tự chụp vào `docs/images/`).
7. `07_TaiLieu_DaoTao.md` — giáo án đào tạo theo vai trò, bài tập thực hành, mẫu biên bản xác nhận tham gia đào tạo.
8. `08_QuyTrinh_VanHanh_BaoHanh.md` — kênh hỗ trợ, SLA (**tiếp nhận và phản hồi lỗi trong vòng 2 giờ**), phân loại mức độ sự cố, quy trình nâng cấp.
9. `api/openapi.json`, `CHANGELOG.md`, `ASSUMPTIONS.md`, `THIRD_PARTY_LICENSES.md`.
10. `test-reports/` — kết quả unit/integration/E2E, k6, ZAP.

---

## 14. KẾ HOẠCH THỰC HIỆN THEO PHASE

Mỗi phase phải đạt **Definition of Done**: code + migration + test + i18n đủ 2 ngôn ngữ + cập nhật tài liệu liên quan + chạy được bằng docker.

| Phase | Nội dung | Tiêu chí hoàn thành |
|---|---|---|
| **P0 – Khung dự án** | Repo, solution 4 lớp, Vite app, docker-compose dev, Serilog, ProblemDetails, health check, i18n khung, layout, CI script `test-all` | `dev-up.ps1` chạy được; trang login hiện song ngữ |
| **P1 – Xác thực và quản trị** | Mục 7.1 đầy đủ + MFA + phiên + audit log + quên mật khẩu (Mailpit) | Test ma trận quyền; test lockout; test MFA |
| **P2 – Danh mục nền** | Mục 7.2, danh mục chung, đơn vị đo, kho, nhóm thực phẩm, chất dinh dưỡng, thực phẩm, nguyên liệu; import/export Excel + file mẫu | Nhập 500 thực phẩm từ Excel < 30s; tìm không dấu hoạt động |
| **P3 – Món ăn, công thức, khuyến nghị** | Mục 7.3 + NutritionEngine + CostEngine + WarningEngine | Unit test engine với bộ số liệu chuẩn tính tay (≥ 30 case) |
| **P4 – Thực đơn** | Mục 7.4 + MenuGenerator + workflow duyệt + lịch + công bố + đặt suất | Sinh 1 tháng < 5s; E2E: tạo tự động → sửa → gửi → duyệt → công bố |
| **P5 – Kho và ATTP** | Mục 7.8 + 5.6 + InventoryEngine + đề xuất mua + job cảnh báo hết hạn | Test tồn kho, giá bình quân, FEFO, kiểm kê điều chỉnh |
| **P6 – Phân tích, chi phí, báo cáo** | Mục 7.5, 7.6, 7.7, 7.9, dashboard | Đủ 16 báo cáo xuất Excel đúng định dạng |
| **P7 – Tích hợp VDD** | Mục 7.10 + mock server + màn hình đồng bộ/xung đột + public API + XML | Đồng bộ từ mock thành công; xung đột hiển thị đúng |
| **P8 – Gia cố ATTT** | Mục 10 đầy đủ: mã hóa cột, WAF, header, rate limit, backup/restore, PII | ZAP sạch High/Medium; test backup→restore pass |
| **P9 – Hiệu năng và hoàn thiện** | Mục 11, tối ưu, k6 1.000 VU, rà soát i18n, responsive, dark mode | k6 đạt ngưỡng; Lighthouse ≥ 85 |
| **P10 – Tài liệu và đóng gói** | Mục 13 đầy đủ, ảnh chụp màn hình tự động, gói triển khai offline, bản phát hành v1.0.0 | Checklist nghiệm thu mục 15 tích đủ |

---

## 15. CHECKLIST NGHIỆM THU (đối chiếu Chương V E-HSMT)

- [ ] Khảo sát và thiết kế: SRS/BRD, thiết kế hệ thống, thiết kế ATTT cấp độ 2
- [ ] Quản lý danh mục chung (quốc gia/hành chính/đơn vị đo)
- [ ] Danh mục nhân viên; danh mục phân quyền nhân viên; danh mục đối tượng
- [ ] Loại đối tác / đối tác (tên, liên hệ, hợp đồng…)
- [ ] Loại khách hàng / khách hàng (đơn vị đặt suất ăn)
- [ ] Thông tin đơn vị, nhóm tài khoản, tài khoản người dùng (thêm/sửa/khóa), phiên làm việc, danh sách chức năng, phân quyền, phiên bản hệ thống, đăng nhập/đăng xuất/quên mật khẩu tự phục vụ
- [ ] Danh mục kho; phân nhóm thực phẩm; thực phẩm; ATTP và chất lượng thực phẩm; nguyên liệu; món ăn/thành phẩm; công thức cấu tạo món; khuyến nghị; nhóm báo cáo
- [ ] Kế hoạch thực đơn tuần/tháng; danh sách thực đơn; tạo tự động cân đối dinh dưỡng; tạo thủ công; phê duyệt trước khi áp dụng
- [ ] Tính và phân tích thành phần dinh dưỡng
- [ ] Cảnh báo chỉ tiêu
- [ ] Tính chi phí suất ăn
- [ ] Báo cáo nhập/xuất thực phẩm; kiểm kê kho; khuyến nghị dinh dưỡng; xuất Excel
- [ ] Tích hợp API với cổng dữ liệu Viện Dinh dưỡng
- [ ] ATTT: MFA, kiểm soát truy cập, nhật ký tập trung, mã hóa lưu trữ và truyền, chống tấn công ứng dụng, bảo vệ dữ liệu cá nhân, sao lưu tự động/bất thường và phục hồi
- [ ] Kiểm thử chức năng, hiệu năng, bảo mật có báo cáo
- [ ] Cài đặt trên hạ tầng của chủ đầu tư; chuyển đổi/nhập dữ liệu ban đầu (Excel import)
- [ ] Song ngữ Việt/Anh; ≥ 1.000 người dùng đồng thời; chạy trên Chrome, Cốc Cốc, Edge, Safari; Windows, Linux, iOS, Android
- [ ] Đào tạo theo vai trò, tài liệu HDSD, biên bản đào tạo
- [ ] Bàn giao toàn bộ mã nguồn, CSDL, tài liệu; danh sách license bên thứ ba

---

## 16. BẮT ĐẦU

Bắt đầu ngay với **Phase P0**. Trước khi viết code, tạo `docs/ASSUMPTIONS.md` và `docs/02_ThietKe_HeThong.md` bản nháp (kiến trúc + ERD Mermaid theo mục 5) để làm căn cứ, sau đó triển khai. Kết thúc mỗi phase, in tóm tắt: những gì đã làm, test đã chạy và kết quả, các giả định mới, việc của phase tiếp theo.
