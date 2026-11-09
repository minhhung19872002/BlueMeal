# Lighthouse — BlueMeal

Đo ngày 2026-11-09 trên http://localhost:8080 (preset desktop, Chromium headless; stack Docker dev qua nginx + WAF). Ngưỡng SPEC §11: ≥ 85.

| Trang | Đường dẫn | Performance | Accessibility | Best practices |
|---|---|---|---|---|
| login | `/login` | 98 | 96 | 100 |
| dashboard | `/` | 91 | 97 | 93 |
| menus | `/menus` | 88 | 90 | 93 |
| menu-published | `/menus/published` | 87 | 93 | 100 |
| recipes | `/recipes` | 89 | 93 | 93 |
| foods | `/catalog/foods` | 85 | 93 | 93 |
| stock-balance | `/stock/balance` | 90 | 93 | 93 |
| reports | `/reports` | 92 | 97 | 100 |

Tất cả trang đạt ngưỡng ≥ 85.

Báo cáo chi tiết từng trang: các file `*.html` trong thư mục này.
