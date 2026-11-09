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
| 2026-11-09 | 1 000 | 5 phút, ramp 4 phút | 18,2 s | 22 % | 30 | **Không đạt**: PostgreSQL báo `53300 too many clients`. 2 API và worker dùng pool mặc định 100 kết nối mỗi tiến trình, vượt `max_connections=100`. Đã sửa (mục 3.6). |
| 2026-11-09 | 1 000 | 5 phút, ramp 4 phút | 5,0 s | **0 %** | 188 | **Không đạt p95**: bão hòa CPU. |
| 2026-11-09 | 400 | 2 phút, ramp 1 phút | **516 ms** | **0 %** | 119 | **Đạt** |
| 2026-11-09 | 1 000 | **10 phút, ramp 5 phút** | 4,2 s | **0 %** (185 413 request, checks 100 %) | 189 | **Không đạt p95**: máy phát triển bão hòa CPU (100 %), đồng thời có 8–11 tiến trình build (rustc/cl) của dự án khác chạy song song. |
| 2026-11-09 | **1 000** | **10 phút, ramp 5 phút** | **628 ms** | **0 %** (317 423 request, checks 100 %) | **324** | **ĐẠT** (k6 thoát mã 0). Có đủ các sửa ở mục 3. Máy đỡ tải hơn (các build khác đã dừng). Từng endpoint p95: dashboard 668 ms, thực đơn 583–589 ms, món ăn 90–740 ms, tìm thực phẩm 608 ms, xuất Excel 776 ms, đăng nhập 1 054 ms. Báo cáo `k6-load-test.html`, `k6-summary.json`. |

**Kết luận**: đạt ngưỡng SPEC §11 ở **1 000 người dùng đồng thời giữ 10 phút**: p95 đọc 628 ms < 1 s, lỗi 0 % < 1 %, checks 100 % > 99 %. Đo trên máy phát triển 16 luồng chạy chung k6, 2 API, PostgreSQL, WAF. Các lần không đạt trước đó là do lỗi pool kết nối, luồng Redis quay vòng chờ, và máy bị dự án khác chiếm CPU. Nên đo lại một lần trên hạ tầng thật khi nghiệm thu.

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

6. Pool kết nối: `DB_POOL_SIZE` mặc định đổi từ 100 xuống 40 mỗi tiến trình. Overlay perf đặt `max_connections=200`, bản production là 300. Chuỗi kết nối bật `No Reset On Close` và auto-prepare (`Max Auto Prepare=50`): `pg_stat_statements` cho thấy thời gian thực thi truy vấn chỉ chiếm phần nhỏ CPU của PostgreSQL, còn lại là chi phí giao thức (`DISCARD ALL` sau mỗi lần trả kết nối, parse/plan lặp lại).
7. StackExchange.Redis dùng `SocketManager.ThreadPool`. `dotnet-trace` cho thấy các luồng scheduler riêng của thư viện quay vòng chờ, chiếm khoảng 30 % thời gian luồng của API khi có tải.

Khi chạy `scripts/load-test.*`, k6 sinh `k6-load-test.html` (web dashboard) và `k6-summary.json` vào thư mục này. Không lưu kèm file của lần chạy không hợp lệ.
