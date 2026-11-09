# THIẾT KẾ HỆ THỐNG — BlueMeal

*Phần mềm quản lý bữa ăn ca thông minh — Gói thầu IB2600574672 — Viện Dinh dưỡng (Bộ Y tế)*

| Thuộc tính | Giá trị |
|---|---|
| Phiên bản tài liệu | 1.0 |
| Căn cứ | `SPEC.md`, `docs/ASSUMPTIONS.md` |

## 1. Kiến trúc tổng thể

```mermaid
flowchart LR
  subgraph Client["Người dùng (Chrome / Cốc Cốc / Edge / Safari — PC, iOS, Android)"]
    B[Trình duyệt]
  end
  subgraph DMZ["Máy chủ on-premise (Docker, mạng nội bộ bluemeal_net)"]
    W["nginx + ModSecurity CRS<br/>TLS 1.2+, HSTS, static SPA"]
    A1["api #1<br/>ASP.NET Core 8"]
    A2["api #2<br/>ASP.NET Core 8"]
    WK["worker<br/>Quartz.NET scheduler"]
    PG[("PostgreSQL 16<br/>schema app, quartz")]
    RD[("Redis 7.2<br/>cache, rate-limit, token thu hồi")]
    BK["backup<br/>pg_dump + openssl"]
    FS[("volume files / exports / backups")]
  end
  VDD["Cổng dữ liệu<br/>Viện Dinh dưỡng"]
  EXT["Hệ thống khác<br/>(public API JSON/XML)"]
  B -- HTTPS --> W
  W -- "/api (least_conn)" --> A1 & A2
  A1 & A2 & WK --> PG
  A1 & A2 & WK --> RD
  A1 & A2 & WK --> FS
  WK -- "HTTPS (Polly retry, circuit breaker)" --> VDD
  EXT -- HTTPS client credentials --> W
  BK --> PG
  BK --> FS
```

### 1.1 Phân lớp backend (Clean Architecture)

| Lớp | Nội dung | Phụ thuộc |
|---|---|---|
| `BlueMeal.Domain` | Entity, enum, value object, quy tắc nghiệp vụ thuần | — |
| `BlueMeal.Application` | DTO, validator, interface (`IAppDbContext`, `INutritionDataProvider`, `ICurrentUser`…), các engine: `NutritionEngine`, `WarningEngine`, `CostEngine`, `MenuGenerator`, `InventoryEngine` | Domain |
| `BlueMeal.Infrastructure` | EF Core + Npgsql, interceptor audit/soft-delete, mã hóa cột AES-GCM, Redis, Quartz.NET job, ClosedXML, VDD API client, email SMTP, lưu file | Application |
| `BlueMeal.Api` | Controller, middleware (ProblemDetails, audit, idempotency, security headers), xác thực JWT, phân quyền, rate limit, Swagger, health check | Infrastructure |

Engine tính toán viết thuần C# với input là các record bất biến → kiểm thử đơn vị độc lập DB.

### 1.2 Frontend

React 18 + TypeScript + Vite, Ant Design 5. Tổ chức theo `features/<module>`; `shared/` chứa `CatalogPage` (màn hình danh mục generic), `PermissionGuard`, định dạng số/tiền/ngày kiểu Việt Nam. Access token chỉ trong bộ nhớ (Zustand), refresh token trong cookie HttpOnly.

## 2. Phân hệ

| Phân hệ | Chức năng chính | Mục SPEC |
|---|---|---|
| Quản trị hệ thống | Đơn vị, cơ cấu tổ chức, danh mục chung, nhân viên, nhóm quyền, tài khoản, phiên, chức năng, phiên bản, cấu hình, sao lưu, nhật ký | 7.1, 10 |
| Đối tượng – Đối tác – Khách hàng | Đối tượng lao động, nhà cung cấp (hợp đồng, giấy chứng nhận), khách hàng (liên hệ, đơn giá, đặt suất) | 7.2 |
| Thực phẩm – Món ăn | Kho, nhóm TP, chất dinh dưỡng, thực phẩm, ATTP, nguyên liệu, món, công thức, khuyến nghị, nhóm báo cáo | 7.3 |
| Thực đơn | Kế hoạch, lịch, tạo tự động/thủ công, duyệt, công bố, đặt suất | 7.4 |
| Dinh dưỡng – Cảnh báo – Chi phí | Phân tích, so sánh, trung tâm cảnh báo, bảng tính chi phí | 7.5–7.7 |
| Kho – ATTP | Nhập, xuất, kiểm kê, thẻ kho, khóa sổ, kiểm thực 3 bước, lưu mẫu | 7.8, 5.6 |
| Báo cáo | 16 báo cáo + dashboard | 7.9 |
| Tích hợp | Đồng bộ VDD, xung đột, public API, XML, FHIR tối giản | 7.10 |

## 3. Luồng dữ liệu chính

```mermaid
sequenceDiagram
  autonumber
  participant N as Cán bộ dinh dưỡng
  participant API as BlueMeal API
  participant G as MenuGenerator
  participant E as Nutrition/Cost/Warning Engine
  participant AP as Người duyệt
  participant K as Thủ kho
  N->>API: Tạo kế hoạch + chạy tạo tự động
  API->>G: tham số (đối tượng, ngân sách, cơ cấu, seed)
  G->>E: chấm điểm tổ hợp món
  E-->>G: dinh dưỡng, chi phí, cảnh báo
  G-->>API: thực đơn Draft + snapshot
  N->>API: Gửi duyệt (chặn nếu có Critical chưa được chấp nhận)
  API-->>AP: Thông báo chờ duyệt
  AP->>API: Duyệt / Từ chối / Trả lại (bắt buộc ý kiến khi từ chối)
  API-->>N: Thông báo kết quả
  N->>API: Công bố
  K->>API: Sinh phiếu xuất dự kiến (FEFO) + đề xuất mua
```

## 4. Mô hình dữ liệu (ERD)

Toàn bộ bảng nằm trong schema `app`, tên `snake_case`. Cột chung: `id uuid`, `created_at`, `created_by`, `updated_at`, `updated_by`, `is_deleted`, `xmin` (concurrency). Danh mục thêm `code` (unique trong các bản ghi chưa xóa), `name_vi`, `name_en`, `is_active`, `sort_order`, `search_text`.

### 4.1 Quản trị hệ thống

```mermaid
erDiagram
  organization ||--o{ department : has
  department ||--o{ department : parent
  department ||--o{ employee : has
  employee |o--o| app_user : account
  app_user ||--o{ user_role : has
  app_role ||--o{ user_role : has
  app_role ||--o{ role_permission : grants
  permission ||--o{ role_permission : in
  app_user ||--o{ user_data_scope : scoped
  app_user ||--o{ user_session : sessions
  app_user ||--o{ login_history : logs
  app_user ||--o{ mfa_recovery_code : codes
  app_user ||--o{ password_history : history
  app_user ||--o{ notification : receives
  country ||--o{ province : has
  province ||--o{ ward : has
  audit_log }o--|| app_user : by
```

### 4.2 Đối tượng – đối tác – khách hàng

```mermaid
erDiagram
  subject_group ||--o{ recommendation : targets
  partner_type ||--o{ partner : classifies
  partner ||--o{ partner_contract : signs
  partner ||--o{ partner_certificate : holds
  partner ||--o{ partner_rating : rated
  customer_type ||--o{ customer : classifies
  customer ||--o{ customer_contact : has
  customer ||--o{ customer_price : agreed
  customer }o--o| subject_group : default_subject
```

### 4.3 Thực phẩm – món ăn – công thức

```mermaid
erDiagram
  food_group ||--o{ food_group : parent
  food_group ||--o{ food : contains
  food ||--o{ food_nutrient : has
  nutrient ||--o{ food_nutrient : measured
  food ||--o| food_safety_spec : spec
  food ||--o{ ingredient : maps
  ingredient ||--o{ ingredient_price_history : prices
  warehouse ||--o{ ingredient : default_wh
  partner ||--o{ ingredient : preferred
  dish ||--o{ recipe : versions
  recipe ||--o{ recipe_item : lines
  ingredient ||--o{ recipe_item : used
  nutrient ||--o{ nutrient_retention_factor : retention
  recommendation ||--o{ recommendation_nutrient : targets
  nutrient ||--o{ recommendation_nutrient : for
  report_group ||--o{ report_definition : groups
```

### 4.4 Thực đơn

```mermaid
erDiagram
  meal_structure_template ||--o{ meal_structure_slot : slots
  menu_plan ||--o{ menu : days
  meal_shift ||--o{ menu : shift
  subject_group ||--o{ menu : subject
  customer ||--o{ menu : customer
  menu ||--o{ menu_item : items
  dish ||--o{ menu_item : dish
  recipe ||--o{ menu_item : locked_recipe
  menu ||--o{ menu_approval : history
  customer ||--o{ meal_order : orders
  menu ||--o{ meal_order : fulfils
```

### 4.5 Kho và ATTP

```mermaid
erDiagram
  warehouse ||--o{ stock_receipt : receives
  partner ||--o{ stock_receipt : supplies
  stock_receipt ||--o{ stock_receipt_line : lines
  warehouse ||--o{ stock_issue : issues
  menu ||--o{ stock_issue : for_menu
  stock_issue ||--o{ stock_issue_line : lines
  warehouse ||--o{ stock_take : counts
  stock_take ||--o{ stock_take_line : lines
  ingredient ||--o{ stock_ledger : movements
  ingredient ||--o{ stock_balance : balance
  menu ||--o{ purchase_request : demand
  purchase_request ||--o{ purchase_request_line : lines
  stock_receipt ||--o{ food_inspection_step1 : inspected
  menu ||--o{ food_inspection_step2 : cooking
  menu ||--o{ food_inspection_step3 : serving
  menu ||--o{ food_sample : samples
  stock_period_lock }o--|| warehouse : locks
```

### 4.6 Tích hợp – vận hành

```mermaid
erDiagram
  integration_endpoint ||--o{ integration_log : logs
  integration_endpoint ||--o{ sync_conflict : conflicts
  app_user ||--o{ backup_job : requests
  app_user ||--o{ export_job : requests
  api_client ||--o{ integration_log : calls
```

## 5. Thiết kế API

- Prefix `/api/v1`, JSON camelCase, ISO-8601 có offset.
- Phân trang `?page=&pageSize=&sort=&q=` → `{ items, page, pageSize, totalItems }`, `pageSize ≤ 200`.
- Lỗi RFC 7807 (`application/problem+json`) có `traceId`, `errors`; thông điệp theo `Accept-Language`.
- Concurrency: DTO có `version` (xmin), xung đột → 409.
- `Idempotency-Key` cho POST tạo phiếu kho (lưu Redis 24h).
- Mọi endpoint gắn `[HasPermission]` hoặc `[AllowAnonymous]` tường minh; fallback policy deny.
- OpenAPI xuất `docs/api/openapi.json`.

Danh sách nhóm endpoint chi tiết: xem `docs/api/openapi.json` (sinh tự động).

## 6. Thiết kế giao diện

Danh sách màn hình sẽ hoàn thiện ở Phase P10 (mục 7 của tài liệu này).

## 7. Triển khai

Xem `docs/05_HuongDan_CaiDat.md`. Môi trường: Docker Compose (`deploy/docker-compose.yml` + `.dev.yml` / `.prod.yml`).
