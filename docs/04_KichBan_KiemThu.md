# KỊCH BẢN KIỂM THỬ — BlueMeal v1.0

| Mục | Nội dung |
|---|---|
| Phạm vi | Chức năng (SPEC §7), hiệu năng (§11), bảo mật (§10), tương thích trình duyệt/thiết bị |
| Môi trường | Docker Compose dev (`scripts/dev-up.*`) với dữ liệu mẫu; integration test dùng PostgreSQL 16 thật (Testcontainers) |
| Công cụ | xUnit + FluentAssertions (unit, integration), Vitest (frontend), Playwright (E2E, chụp ảnh `docs/images/`), k6 (tải), OWASP ZAP (bảo mật) |
| Chạy toàn bộ | `scripts/test-all.(sh|ps1)` — build, lint, i18n check, unit, integration, quét thư viện lỗ hổng; E2E: `cd frontend && npx playwright test` |
| Báo cáo | `docs/test-reports/` (E2E HTML + JUnit, k6, ZAP, diễn tập khôi phục) |

Cột **Tự động** chỉ tên test tự động tương ứng (U = unit, I = integration, E = E2E Playwright); "TC" = kiểm thử thủ công theo bước.

## 1. Test case chức năng

### 1.1 Xác thực và quản trị (UC-101…UC-116)

| ID | UC | Bước | Dữ liệu | Kết quả mong đợi | Tự động |
|---|---|---|---|---|---|
| TC-AUTH-01 | 111 | Đăng nhập sai mật khẩu với user có thật và user không tồn tại | `admin` / sai; `khongco` | Cùng một thông báo trung tính | I `AuthTests.Unknown_and_wrong_password_return_the_same_neutral_message` |
| TC-AUTH-02 | 111 | Sai mật khẩu 3 lần, rồi 5 lần | user thường | Lần 4 yêu cầu CAPTCHA; sau 5 lần khóa 15 phút | I `AuthTests.Account_is_locked_after_5_failures_and_captcha_required_after_3` |
| TC-AUTH-03 | 111,112 | Admin đăng nhập lần đầu | mật khẩu seed | Phiên hạn chế: bắt buộc đổi mật khẩu và bật MFA | I `AuthTests.Admin_first_login_is_restricted…`; E `p1-admin.spec.ts` |
| TC-AUTH-04 | 111 | Quên mật khẩu → mở link email (Mailpit) → đặt mật khẩu mới → dùng lại link | email đúng / sai | Thông báo trung tính; link chỉ dùng 1 lần, hết hạn 15 phút | I `AuthTests.Forgot_password_is_neutral_and_reset_token_is_single_use` |
| TC-AUTH-05 | 112 | Đổi mật khẩu trùng mật khẩu cũ / không đủ độ mạnh | — | Bị từ chối theo chính sách | I `AuthTests.Password_policy_and_history_are_enforced` |
| TC-AUTH-06 | 111 | Đăng nhập với mật khẩu đã quá 90 ngày | sửa `password_changed_at` | Phiên hạn chế, chỉ đổi mật khẩu | I `SessionAndPiiTests.Expired_password_gives_a_restricted_session` |
| TC-AUTH-07 | 111 | Dùng lại cùng mã TOTP cho lần đăng nhập thứ hai | — | Lần 2 bị từ chối | I `SessionAndPiiTests.A_totp_code_cannot_be_replayed_at_login` |
| TC-AUTH-08 | 111 | Đăng nhập bằng mã khôi phục / OTP email | mã khôi phục | Đăng nhập được; mã khôi phục dùng một lần | TC |
| TC-AUTH-09 | 111,113 | Tài khoản thường (không thuộc vai trò bắt buộc MFA) đăng nhập với chính sách mặc định | user thường, chưa bật MFA | Phiên hạn chế: phải bật MFA mới dùng được hệ thống | I `AuthTests.Mfa_is_mandatory_for_every_account_by_default` |
| TC-SES-01 | 107 | Refresh token xoay vòng; dùng lại token cũ | — | Token cũ dùng lại → thu hồi cả phiên | I `AuthTests.Refresh_token_rotates_and_reuse_revokes_the_session` |
| TC-SES-02 | 107 | Gọi refresh không có header CSRF | — | 403 | I `AuthTests.Refresh_requires_csrf_header` |
| TC-SES-03 | 107 | Admin đăng xuất cưỡng bức phiên của user | — | Request tiếp theo của user trả 401 | I `AuthTests.Logout_and_admin_force_logout_revoke_sessions` |
| TC-SES-04 | 107 | Đăng nhập lần thứ 4 khi giới hạn 3 phiên | — | Phiên cũ nhất bị thu hồi | I `SessionAndPiiTests.Concurrent_session_limit_revokes_the_oldest_session` |
| TC-SES-05 | 107 | Không thao tác 30 phút | — | Phiên hết hạn (401) | I `SessionAndPiiTests.Idle_session_expires_after_the_configured_timeout` |
| TC-ADM-01 | 105,109 | Mỗi vai trò mặc định gọi toàn bộ endpoint | 8 vai trò | Chỉ endpoint được cấp trả khác 403 | I `PermissionMatrixTests.Role_can_only_reach_endpoints_it_is_granted` |
| TC-ADM-02 | 106 | Tạo user, khóa, reset mật khẩu; xóa quản trị cuối cùng | — | Khóa thu hồi phiên; không xóa được quản trị cuối | I `CatalogTests.Users_can_be_created_locked_and_reset_by_admin`, `Last_system_admin_cannot_be_removed` |
| TC-ADM-03 | 104 | Xem nhân viên khi không có `pii.view`; tìm theo SĐT | — | SĐT/email/CCCD bị che; tìm chính xác vẫn ra | I `CatalogTests.Employee_pii_is_encrypted_at_rest_masked_without_pii_view_and_searchable_by_hash` |
| TC-ADM-04 | 104 | Xuất dữ liệu cá nhân, ẩn danh hóa nhân viên | — | File JSON có dữ liệu; sau ẩn danh không còn tên/SĐT; nhật ký `PiiAccess` | I `SessionAndPiiTests.Personal_data_export_and_anonymization_are_audited` |
| TC-ADM-05 | 115 | Sửa bản ghi → xem nhật ký; thử UPDATE/DELETE bảng `audit_log` bằng role ứng dụng | — | Có JSON diff, trường nhạy cảm bị che; UPDATE/DELETE bị PostgreSQL từ chối | I `AuditLogTests.*` |
| TC-ADM-06 | 113 | Đổi cấu hình với giá trị ngoài khoảng | idle = 1 phút | 400; giá trị hợp lệ thì lưu + audit `ConfigChange` | TC |
| TC-ADM-07 | 114 | Sao lưu bất thường → tải về: sai mật khẩu, thiếu MFA, đúng | — | 403, 403, tải được; token tải dùng 1 lần | I `SecurityP8Tests.Manual_backup_and_download_with_reauthentication`; E `p8-security.spec.ts` |
| TC-ADM-08 | 116 | Nạp / xóa dữ liệu mẫu | — | Dữ liệu gắn nhãn "Dữ liệu mẫu"; xóa không ảnh hưởng dữ liệu nhập tay | I `CatalogTests.Sample_data_is_seeded_and_clearly_labelled` |

### 1.2 Danh mục (UC-CAT, UC-201…UC-309)

| ID | UC | Bước | Kết quả mong đợi | Tự động |
|---|---|---|---|---|
| TC-CAT-01 | CAT | Thêm, sửa, tìm không dấu, xóa mềm, xem lịch sử | Đúng AC-1…AC-4 | I `CatalogTests.Crud_search_without_accents_soft_delete_and_history`; E `p2-catalogs.spec.ts` |
| TC-CAT-02 | CAT | `pageSize=1000` | Bị giới hạn 200 | I `CatalogTests.Paging_is_server_side_and_capped_at_200` |
| TC-CAT-03 | CAT | Tải mẫu → nhập có dòng lỗi → xem trước → ghi → xuất | Lỗi theo dòng; chỉ ghi dòng hợp lệ | I `CatalogTests.Excel_template_import_preview_commit_and_export_round_trip` |
| TC-CAT-04 | CAT | Nhập file không phải Excel | Bị từ chối | I `CatalogTests.Import_rejects_non_excel_content` |
| TC-CAT-05 | 303 | Nhập 500 thực phẩm từ Excel | < 30 giây | I `CatalogP2Tests.Import_500_foods_from_excel_under_30_seconds` |
| TC-CAT-06 | 303 | Sửa lưới dinh dưỡng; sửa thực phẩm nguồn VDD khi chưa mở khóa | Lưu nhanh; nguồn VDD bị khóa (423) | I `CatalogP2Tests.Food_nutrient_grid_inline_edit_and_api_lock` |
| TC-CAT-07 | 109 | Thủ kho chỉ được 1 kho, truy cập kho khác theo id | Danh sách 1 kho; id khác 404 | I `CatalogP2Tests.Warehouse_scope_hides_other_warehouses_and_blocks_idor` |
| TC-CAT-08 | 203 | Đại diện khách hàng xem khách hàng | Chỉ thấy đơn vị mình | I `CatalogP2Tests.Customer_rep_only_sees_own_customer` |
| TC-CAT-09 | 202 | Hợp đồng, giấy chứng nhận (upload PDF), đánh giá đối tác | Lưu đủ; file sai loại bị từ chối | I `CatalogP2Tests.Partner_contracts_certificates_and_ratings`; `SessionAndPiiTests.Upload_rejects_content_that_does_not_match_the_extension` |
| TC-CAT-10 | 305 | Đổi giá tham chiếu nguyên liệu | Ghi lịch sử giá | I `CatalogP2Tests.Ingredient_reference_price_change_is_recorded_in_history` |
| TC-REC-01 | 307 | Soạn công thức, nhập khối lượng → xem dinh dưỡng/chi phí realtime; quy đổi 350 suất | Số liệu khớp tính tay | U `NutritionEngineTests`, `CostAndWarningEngineTests`; I `RecipeTests.Realtime_compute_matches_hand_calculation`, `RecipeTests.Price_mode_contract_uses_active_contract_price`; E `p3-recipes.spec.ts` |
| TC-REC-02 | 307 | Tạo phiên bản mới, so sánh, kích hoạt; sửa bản đang dùng | Kích hoạt lưu trữ bản cũ; bản đang dùng không sửa được | I `RecipeTests.Versioning_activate_archives_previous_and_compare` |
| TC-REC-03 | 306 | Xem nhanh dinh dưỡng món | Hiện năng lượng, P:L:G, chi phí 1 suất | E `p3-recipes.spec.ts` |
| TC-REC-04 | 307 | Nhập công thức từ Excel: món mới, món đã có công thức, món có dòng lỗi, mã món không tồn tại | 6 dòng / 4 món | Xem trước 3 dòng hợp lệ; `skipErrors=false` không ghi gì; nhập: 1 công thức mới đang dùng, 1 phiên bản nháp, món lỗi không có công thức; token dùng 1 lần | I `RecipeTests.Recipes_import_from_excel_whole_dish_or_nothing` |

### 1.3 Thực đơn (UC-401…UC-407)

| ID | UC | Bước | Kết quả mong đợi | Tự động |
|---|---|---|---|---|
| TC-MENU-01 | 403 | Tạo tự động 5 ngày, xem 3 phương án/ngày, lưu nháp | Số ngày đúng (bỏ Chủ nhật), có giải thích, lưu Draft | I `MenuTests.Generate_edit_submit_approve_publish_end_to_end` |
| TC-MENU-02 | 403 | Chạy lại cùng seed | Kết quả giống hệt | U `MenuGeneratorTests.Same_seed_gives_same_menus` |
| TC-MENU-03 | 403 | Sinh 1 tháng với 500 món | < 5 giây | U `MenuGeneratorTests.Performance_one_month_with_500_dishes_under_5_seconds` |
| TC-MENU-04 | 403 | Món bắt buộc / loại trừ / dị ứng / không lặp N ngày / ≤ 1 món chiên | Ràng buộc được tôn trọng | U `MenuGeneratorTests.Excluded_and_allergen_dishes_are_never_used_and_required_dish_is_placed`, `No_dish_repeats_within_the_no_repeat_window`, `At_most_one_fried_dish_per_meal`, `Budget_is_respected_when_feasible`, `Every_day_follows_the_meal_structure` |
| TC-MENU-05 | 404 | Tạo thủ công, đổi hệ số khẩu phần; xem gợi ý đổi món | Panel realtime cập nhật; gợi ý cải thiện điểm | E `p4-menus.spec.ts`; I `MenuTests.Plan_calendar_copy_week_and_swap_suggestions` |
| TC-MENU-06 | 405 | Gửi duyệt → duyệt (1 hoặc 2 cấp) → công bố | Trạng thái và lịch sử duyệt đúng; công thức bị chốt phiên bản | I `MenuTests.Generate_edit_submit_approve_publish_end_to_end`, `MenuTests.Two_level_approval_and_recall`; E `p4-menus.spec.ts` |
| TC-MENU-07 | 405 | Từ chối/trả lại không ý kiến | Bị từ chối (422) | I `MenuTests.Critical_warning_blocks_submit_without_reason_and_reject_requires_comment` |
| TC-MENU-08 | 405 | Gửi duyệt thực đơn có cảnh báo nghiêm trọng không lý do; duyệt khi không có quyền override | Bị chặn | I `MenuTests.Critical_warning_blocks_submit_without_reason_and_reject_requires_comment`, `MenuTests.Approver_without_override_permission_cannot_approve_critical_menu` |
| TC-MENU-09 | 405 | Sửa thực đơn đã duyệt; thu hồi có lý do | Sửa bị chặn; thu hồi về Nháp, ghi audit | I `MenuTests.Two_level_approval_and_recall` |
| TC-MENU-10 | 401 | Lịch kế hoạch: kéo món giữa 2 ngày; nhân bản tuần; áp dụng cho khách hàng khác | Món chuyển, thực đơn tính lại; bản sao Draft | I `MenuTests.Plan_calendar_copy_week_and_swap_suggestions` (nhân bản tuần); kéo-thả và áp dụng nhiều khách hàng: TC |
| TC-MENU-11 | 406 | Người không có quyền chi phí xem thực đơn công bố; in A4 | Không có trường chi phí; bản in vừa trang A4 | TC (API ẩn trường `cost` khi thiếu quyền) |
| TC-MENU-12 | 407 | Đặt suất → tổng hợp → cập nhật số suất vào thực đơn | Số suất thực đơn = tổng đặt; đại diện khách hàng chỉ thao tác đơn vị mình | I `MenuTests.Meal_orders_summary_and_customer_scope`; E `p4-menus.spec.ts` |

### 1.4 Phân tích, cảnh báo, chi phí, báo cáo (UC-501…UC-702)

| ID | UC | Bước | Kết quả mong đợi | Tự động |
|---|---|---|---|---|
| TC-ANA-01 | 501 | Phân tích 1 thực đơn, khoảng ngày, 1 món | Bảng chất, % đáp ứng, xu hướng; khoảng ngày 1 thực đơn = số liệu thực đơn đó | I `ReportTests.Analysis_cost_sheet_and_dashboard`; E `p6-reports.spec.ts` |
| TC-ANA-02 | 502 | Bật So sánh, chọn 2 phạm vi | Hiển thị song song | TC |
| TC-ALR-01 | 512 | Lô hết hạn ngày mai, tồn dưới tối thiểu, mẫu quá hạn → chạy kiểm tra | 3 loại cảnh báo; chạy lại không trùng; hủy mẫu → cảnh báo đóng | I `StockTests.Food_sample_rules_and_daily_alerts`; E `p6-reports.spec.ts` |
| TC-ALR-02 | 514 | Lưu ngưỡng không hợp lệ (cảnh báo thấp > nghiêm trọng thấp) | 400; hợp lệ thì lưu | I `ReportTests.Analysis_cost_sheet_and_dashboard` |
| TC-COST-01 | 522 | Bảng chi phí cho thực đơn đã duyệt chưa xuất kho | Kế hoạch > 0, thực tế thực phẩm = 0, doanh thu theo đơn giá | I `ReportTests.Analysis_cost_sheet_and_dashboard` |
| TC-RPT-01 | 701 | Chạy và xuất Excel 16 báo cáo | Có cột; file Excel có mã báo cáo, kỳ, "Người lập" | I `ReportTests.All_sixteen_reports_run_and_export_to_excel` |
| TC-RPT-02 | 701 | RPT-02 với dữ liệu biết trước | Tồn đầu/nhập/xuất/tồn cuối khớp sổ cái | I `ReportTests.Stock_movement_report_matches_ledger` |
| TC-RPT-03 | 701 | Thủ kho 1 kho chạy RPT-02; chạy RPT-16 | Chỉ thấy kho mình; RPT-16 403 | I `ReportTests.Warehouse_scoped_user_only_sees_own_warehouse_in_reports` |
| TC-RPT-04 | 701 | Xuất nền; lưu bộ lọc yêu thích | File tải được; bộ lọc được lưu theo người dùng | I `ReportTests.Background_export_and_favorites` |
| TC-DASH-01 | 702 | Đăng nhập theo từng vai trò | Chỉ widget đúng quyền | I `ReportTests.Analysis_cost_sheet_and_dashboard`; E `p6-reports.spec.ts` |

### 1.5 Kho và ATTP (UC-601…UC-608)

| ID | UC | Bước | Kết quả mong đợi | Tự động |
|---|---|---|---|---|
| TC-STK-01 | 601,604 | Nhập 10 kg × 50.000, 10 kg × 60.000; xuất 5 kg | Bình quân 55.000; xuất 275.000; tồn 15 kg / 825.000; thẻ kho đúng; tồn tại ngày đúng | U `InventoryEngineTests`; I `StockTests.Moving_average_issue_cost_balance_and_stock_card`; E `p5-stock.spec.ts` |
| TC-STK-02 | 602 | 2 lô HSD khác nhau, xuất 6 | Gợi ý FEFO; lô hết hạn sớm xuất trước | U `InventoryEngineTests`; I `StockTests.Fefo_suggestion_and_allocation_take_earliest_expiry_first` |
| TC-STK-03 | 602 | Xuất vượt tồn | 422 "Không đủ tồn kho" | I `StockTests.Negative_stock_is_rejected` |
| TC-STK-04 | 603 | Kiểm kê: thiếu 1,5 lô A, thừa 1 lô B → duyệt | Tự sinh và ghi sổ phiếu điều chỉnh; tồn = số đếm | I `StockTests.Stock_take_approval_posts_adjustments`; E `p5-stock.spec.ts` |
| TC-STK-05 | 606 | Khóa tháng 5 → ghi chứng từ tháng 5 và tháng 4 | Bị chặn; tháng 6 được; mở khóa thì ghi được | I `StockTests.Period_lock_blocks_posting_into_locked_month` |
| TC-STK-06 | 601,607 | Dòng nhập Không đạt; dòng Đạt có điều kiện | Không ghi sổ được; ghi sổ được và có biên bản bước 1 | I `StockTests.Quality_check_fail_blocks_posting_and_posting_records_step1` |
| TC-STK-07 | 601 | Gửi 2 lần cùng Idempotency-Key | Cùng một phiếu | I `StockTests.Idempotency_key_replays_the_first_response` |
| TC-STK-08 | 602,605 | Thực đơn đã duyệt → phiếu xuất dự kiến, đề xuất mua → tạo phiếu nhập | Định lượng × suất; công thức đề xuất đúng; không cho sinh phiếu xuất lần 2 | I `StockTests.Approved_menu_generates_planned_issue_and_purchase_request` |
| TC-STK-09 | 601 | Nhập mã vạch nguyên liệu trên phiếu nhập | Thêm dòng nguyên liệu tương ứng | TC |
| TC-STK-10 | 601,604 | Nhập tồn đầu kỳ từ Excel, ghi sổ ngay, ngày 30/06 | 2 dòng đúng, 2 dòng lỗi | 1 phiếu nhập đã ghi sổ 810.000; tồn 15 đơn vị; thẻ kho tháng 7 mở đầu 15; RPT-02 tồn đầu 810.000 | I `StockTests.Opening_stock_import_creates_posted_receipts_per_warehouse` |
| TC-SAF-01 | 608 | Lưu mẫu < khối lượng tối thiểu; hủy trước 24 giờ | Bị chặn | I `StockTests.Food_sample_rules_and_daily_alerts`; E `p5-stock.spec.ts` |
| TC-SAF-02 | 607 | Ghi bước 2, bước 3 với kết quả Không đạt không có biện pháp | Bị chặn; có biện pháp thì lưu; in biểu mẫu | TC + I `ReportTests.Date_filtered_safety_lists_work` |

### 1.6 Tích hợp (UC-801…UC-805)

| ID | UC | Bước | Kết quả mong đợi | Tự động |
|---|---|---|---|---|
| TC-INT-01 | 801 | Kiểm tra kết nối với OAuth2, API key, Basic; API key sai | OK ×3; sai → báo HTTP 401 | I `IntegrationTests.Api_key_and_basic_auth_and_failures_are_reported` |
| TC-INT-02 | 802 | Đồng bộ đầy đủ từ mock | 17 thực phẩm mới khóa sửa; natri quy đổi g → mg; 2 khuyến nghị | I `IntegrationTests.Sync_from_mock_with_conflicts_and_resolution`; E `p7-integration.spec.ts` |
| TC-INT-03 | 802 | Đồng bộ lại khi nguồn không đổi; nguồn đổi 1 thực phẩm | 0 cập nhật; 1 cập nhật | như trên |
| TC-INT-04 | 803 | Sửa tay thực phẩm VDD rồi nguồn cũng đổi → giải quyết | 1 xung đột hiển thị local/remote; lấy nguồn → giá trị mới; giữ local → không báo lại cùng phiên bản | như trên |
| TC-INT-05 | 802 | Nguồn trả 503 hai lần | Đồng bộ vẫn thành công (retry) | như trên |
| TC-INT-06 | 805 | Token sai secret, scope không cấp; gọi JSON, XML, FHIR; tắt client | 401, 400; JSON/XML/FHIR hợp lệ; scope thiếu 403; tắt client → không lấy token | I `IntegrationTests.Public_api_with_client_credentials_json_xml_and_fhir` |
| TC-INT-07 | 805 | Sinh `docs/api/openapi.json` (chỉ ghi file khi chạy với `BLUEMEAL_EXPORT_ARTIFACTS=1`) | Có các đường dẫn `/api/v1/public/*` | I `IntegrationTests.OpenApi_document_is_exported_to_docs` |

## 2. Ma trận truy vết (tóm tắt)

| Nhóm yêu cầu (SRS) | Test case | Số test tự động liên quan |
|---|---|---|
| 4.1 Quản trị, xác thực | TC-AUTH-*, TC-SES-*, TC-ADM-* | ~30 integration + E2E P1 |
| 4.2–4.3 Danh mục, món, công thức | TC-CAT-*, TC-REC-* | Engine: 60+ unit; integration P2/P3; E2E P2/P3 |
| 4.4 Thực đơn | TC-MENU-* | MenuGenerator unit; `MenuTests`; E2E P4 |
| 4.5–4.7, 4.9 Phân tích, cảnh báo, chi phí, báo cáo | TC-ANA-*, TC-ALR-*, TC-COST-*, TC-RPT-*, TC-DASH-* | `ReportTests` (6); E2E P6 |
| 4.8 Kho, ATTP | TC-STK-*, TC-SAF-* | `InventoryEngineTests` (19); `StockTests` (10); E2E P5 |
| 4.10 Tích hợp | TC-INT-* | `NutritionMappingTests` (6); `IntegrationTests` (4); E2E P7 |
| NFR-02 Bảo mật | mục 4 | `SecurityP8Tests`, `SessionAndPiiTests`, E2E P8, ZAP |
| NFR-01 Hiệu năng | mục 3 | k6 |

Tổng số test tự động tại v1.0.0: **117 unit, 81 integration, 7 Vitest, 54 E2E Playwright (44 desktop + 10 mobile)** (xem `docs/test-reports/`).

## 3. Kế hoạch kiểm thử hiệu năng

| Hạng mục | Nội dung |
|---|---|
| Mục tiêu | ≥ 1.000 người dùng đồng thời; p95 truy vấn đọc < 1 s; tỷ lệ lỗi < 1 % (SPEC §11) |
| Kịch bản | `tools/k6/load-test.js`: mỗi người dùng ảo đăng nhập 1 lần (đăng nhập lại trước khi access token hết hạn) rồi lặp: dashboard + thông báo → thực đơn tuần (công bố + danh sách) → mở công thức → tìm thực phẩm → ~20 % số vòng xuất báo cáo Excel nhỏ; nghỉ 2–5 giây giữa các bước như người dùng thật |
| Tải | Ramp 0 → 1.000 VU trong 3 phút, giữ 10 phút, giảm 1 phút |
| Cấu hình | `deploy/docker-compose.perf.yml`: 2 replica API sau nginx `least_conn`; nới giới hạn rate theo IP (mọi VU đến từ 1 IP của container k6); giới hạn theo người dùng vẫn bật |
| Tài khoản | `--seed-loadtest=N` tạo vai trò tạm `LOAD_TEST` (chỉ đọc + xuất báo cáo) và `loadtest0001…`; xóa sau khi chạy |
| Chạy | `scripts/load-test.(sh|ps1) [VUS] [HOLD]` (smoke: `50 1m`) |
| Kết quả | `docs/test-reports/k6-load-test.html` (dashboard k6), `k6-summary.json`, mục 5 dưới đây |

## 4. Kế hoạch kiểm thử bảo mật

| Hạng mục | Cách làm | Tiêu chí |
|---|---|---|
| Quét động OWASP ZAP baseline | `scripts/zap-baseline.(sh|ps1)` trên stack đang chạy (qua WAF) | Không có cảnh báo High/Medium |
| WAF | Gửi payload SQLi/XSS qua nginx | 403 (E `p8-security.spec.ts`) |
| Ma trận quyền, IDOR | `PermissionMatrixTests`, `EndpointPermissionTests`, các test phạm vi dữ liệu | Không endpoint nào thiếu quyền; truy cập ngoài phạm vi 404 |
| Xác thực, phiên | `AuthTests`, `SessionAndPiiTests` | Như mục 1.1 |
| Mã hóa | `TextAndCryptoTests`; kiểm tra trực tiếp DB: cột SĐT/email/CCCD/secret là `v1:…` | Không có dữ liệu nhạy cảm dạng rõ |
| Thư viện lỗ hổng | `dotnet list package --vulnerable`, `npm audit --omit=dev` trong `test-all` | 0 lỗ hổng mức Moderate trở lên |
| Sao lưu – khôi phục | `scripts/test-backup-restore.(sh|ps1)` | Số bản ghi 12 bảng chính khớp |

## 5. Kết quả kiểm thử (v1.0.0)

| Loại | Kết quả | Báo cáo |
|---|---|---|
| Unit (backend) | 117/117 đạt; độ phủ dòng các engine tính toán 98,8 % | `dotnet test` |
| Integration (PostgreSQL thật) | 87/87 đạt (2026-11-11). Mẫu nhập `tools/import-templates/` và `docs/api/openapi.json` chỉ được sinh lại khi chạy `BLUEMEAL_EXPORT_ARTIFACTS=1 dotnet test --filter "Export"` | `dotnet test` |
| Frontend unit (Vitest) | 7/7 đạt | `npx vitest run` |
| E2E Playwright | **54/54 đạt** (44 desktop Chrome + 10 mobile Pixel 7, chạy 2026-11-09 theo 4 đợt rồi gộp báo cáo); thêm P1 + P8 (7/7) trên bản production HTTPS | `docs/test-reports/e2e/index.html`, `e2e-junit.xml` |
| ZAP baseline | 0 High, 0 Medium, 1 Low (false positive, có giải trình), còn lại Informational | `docs/test-reports/zap/zap-baseline.html` |
| Diễn tập khôi phục | PASSED — 12/12 bảng khớp số bản ghi | `docs/test-reports/restore-drill.txt` |
| Tải k6 | xem mục 5.1 | `docs/test-reports/k6-load-test.md` |
| Lighthouse | xem mục 5.2 | `docs/test-reports/lighthouse/` |

### 5.1 Kết quả tải (k6)

**Đạt**: 1 000 VU giữ 10 phút (ramp 5 phút), p95 đọc **628 ms**, lỗi **0 %**, checks 100 %, 317 423 request (324 req/s). Báo cáo `docs/test-reports/k6-load-test.html`. Chi tiết, số đo chi phí từng request và ước tính phần cứng: `docs/test-reports/k6-load-test.md`.

### 5.2 Lighthouse

Cả 8 trang đều ≥ 85 ở cả ba hạng mục Performance, Accessibility, Best practices. Thấp nhất là trang thực phẩm: Performance 85. Chi tiết trong `docs/test-reports/lighthouse/README.md`.

## 6. Tương thích trình duyệt và thiết bị

| Nền tảng | Trình duyệt | Cách kiểm |
|---|---|---|
| Windows 10/11 | Chrome, Edge, Cốc Cốc (nhân Chromium) | E2E Playwright (Chromium) + kiểm thử thủ công |
| Linux | Chrome, Firefox | kiểm thử thủ công |
| macOS / iOS | Safari | E2E Playwright WebKit: `PW_WEBKIT=1 npx playwright test --project=webkit --project=iphone`. P1 + P9 đạt 21/21 (Desktop Safari + iPhone 14, 2026-11-09) |
| Android | Chrome | E2E project `mobile` (Pixel 7) cho các màn kho/kiểm thực/lưu mẫu/thực đơn + thủ công |

Ứng dụng chỉ dùng chuẩn web hiện đại (ES2020, CSS Grid/Flex), không plugin; giao diện responsive từ 360 px.
