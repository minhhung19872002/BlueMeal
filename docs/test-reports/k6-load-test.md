# Kiểm thử tải (k6) — BlueMeal

Kịch bản: `tools/k6/load-test.js` (A-45). Mỗi người dùng ảo (VU) đăng nhập một lần, sau đó lặp lại các bước: dashboard + thông báo → thực đơn tuần → món ăn → tìm thực phẩm. Cứ khoảng 5 vòng có 1 lần xuất Excel RPT-10. Giữa các bước nghỉ 2–5 giây. Stack chạy theo overlay `docker-compose.perf.yml`: 2 API phía sau nginx + ModSecurity, PostgreSQL, Redis. Chạy bằng lệnh `scripts/load-test.(sh|ps1) [VUS] [HOLD]`.

Ngưỡng theo SPEC §11: p95 của các request đọc < 1 000 ms, tỷ lệ lỗi < 1 %, checks > 99 %.

## 1. Kết quả các lần chạy (máy phát triển: 16 luồng CPU, 32 GB RAM, Docker Desktop/WSL2, chạy chung với các stack khác)

| Ngày | VU | Thời gian giữ | p95 đọc | Lỗi | req/s | Kết luận |
|---|---|---|---|---|---|---|
| 2026-11-09 | 50 | 1 phút | < 200 ms | 0 % | — | Đạt |
| 2026-11-09 | 300 | 3 phút | 158 ms | 0 % | 92 | Đạt |
| 2026-11-09 | 700 | 3 phút | 19,4 s | 37,9 % | 66 | **Không đạt**: bão đăng nhập (thử lại sau 5 s cố định) chiếm hết CPU |
| 2026-11-09 | 1 000 | 3 phút | 21,2 s | 62 % | 68 | **Không đạt**: cùng nguyên nhân |
| 2026-11-09 | 1 000 | 10 phút, ramp 5 phút | — | 100 % (đăng nhập) | — | **Không hợp lệ**: 50 tài khoản `loadtest*` còn từ lần đo trước giữ mật khẩu cũ. Đăng nhập sai từ cùng IP của k6 làm bật CAPTCHA cho cả IP, nên mọi lượt đăng nhập sau đều bị từ chối. Lần chạy còn bị dừng ở phút 11,5 vì máy thiếu RAM. Đã sửa seeder và thêm ngưỡng tự dừng sớm. |

**Chưa có kết quả hợp lệ cho mức 1 000 VU sau khi tối ưu.** Cần chạy lại khi máy đủ tài nguyên. Nên tắt các stack Docker khác, hoặc chạy k6 từ một máy riêng.

## 2. Đo chi phí từng loại request (`tools/k6/cost-probe.js`, `tools/k6/one-endpoint.js`; thời gian CPU lấy từ cgroup `cpu.stat`)

| Request | CPU API / request | CPU PostgreSQL | CPU nginx + WAF |
|---|---|---|---|
| Đăng nhập (chạy tuần tự) | ≈ 225 ms | nhỏ | nhỏ |
| Đăng nhập (10 lượt/s đồng thời) | 0,55 – 0,65 s | nhỏ | nhỏ |
| Dashboard | ≈ 30 ms | ≈ 11 ms | ≈ 7 ms |
| Thông báo, danh sách món ăn, tìm thực phẩm, thực đơn | 12 – 17 ms | 2 – 3 ms | 5 – 7 ms |
| Xuất Excel RPT-10 (ước từ docker stats) | ≈ 130 ms | ≈ 20 ms | nhỏ |

- Riêng bước băm mật khẩu PBKDF2-HMAC-SHA512 210 000 vòng (mức OWASP khuyến nghị) mất **220 ms CPU mỗi lần** khi đo trong container .NET 8 trên Linux. Toàn bộ máy thử đạt tối đa khoảng **24 lượt đăng nhập/giây**. Đây là chi phí bảo mật có chủ đích nên **giữ nguyên**.
- Ước tính cho 1 000 người dùng đồng thời: đăng nhập dàn đều trong 5 phút ≈ 3,3 lượt/s (≈ 1–1,5 lõi); phần đọc ≈ 300 req/s × ≈ 22 ms CPU (API + PostgreSQL + WAF) ≈ 6,6 lõi → tổng ≈ 8 lõi chỉ riêng ứng dụng. Với 1 000 người dùng đồng thời nên dự phòng **12–16 vCPU** (ghi trong `05_HuongDan_CaiDat.md`).

## 3. Thay đổi sau khi phân tích

1. Giới hạn đọc theo người dùng trước đây dùng claim `sub`, mà claim này bị trình xử lý JWT đổi tên, nên mọi người dùng chung một hạn mức. Đã sửa: dùng `RateLimitKeys.ForRequest` theo `NameIdentifier`, có unit test.
2. Thêm `PasswordHashGate`: mỗi API chỉ cho chạy đồng thời tối đa ¼ số lõi CPU lượt băm mật khẩu (cấu hình qua `Auth:MaxConcurrentPasswordHashes`). Khi đăng nhập dồn dập đầu ca, các lượt vượt ngưỡng phải xếp hàng, nhờ đó người đã đăng nhập không bị chậm.
3. Kịch bản k6: người dùng đăng nhập thất bại sẽ chờ lâu dần trước khi thử lại (lũy thừa, tối đa 60 s) thay vì cố định 5 s. Đo từng endpoint bằng sub-metric. Tự dừng khi quá 50 % lượt đăng nhập lỗi.
4. Seeder `--seed-loadtest`: đặt lại mật khẩu cho tài khoản còn sót từ lần trước, và xóa phiên của tài khoản thử khi gỡ.
5. Thêm biến `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0` và `DOTNET_GCDynamicAdaptationMode=1` vào overlay perf để giảm CPU lúc rảnh khi 2 bản API chạy chung một máy. Đo nhanh chưa thấy khác biệt rõ rệt.

Khi chạy `scripts/load-test.*`, k6 sinh `k6-load-test.html` (web dashboard) và `k6-summary.json` vào thư mục này. Không lưu kèm file của lần chạy không hợp lệ.
