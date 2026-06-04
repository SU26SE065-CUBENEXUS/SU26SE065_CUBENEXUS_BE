# 📘 CubeNexus API – Hướng dẫn tích hợp cho Frontend

> **Version:** 1.0.0 | **Cập nhật lần cuối:** 2026-06-04  
> **Base URL (local):** `https://localhost:7xxx` hoặc `http://localhost:5xxx`  
> **Swagger UI:** `{Base URL}/swagger`

---

## 📑 Mục lục

1. [Tổng quan kiến trúc & luồng nghiệp vụ](#1-tổng-quan-kiến-trúc--luồng-nghiệp-vụ)
2. [Xác thực (Authentication)](#2-xác-thực-authentication)
3. [Puzzle Types – Danh sách loại Rubik](#3-puzzle-types--danh-sách-loại-rubik)
4. [Luồng 1 – Practice (Tập luyện)](#4-luồng-1--practice-tập-luyện)
5. [Luồng 2 – Elo Seeding (Khởi tạo hồ sơ thi đấu)](#5-luồng-2--elo-seeding-khởi-tạo-hồ-sơ-thi-đấu)
6. [Luồng 3 – Online Arena (Thi đấu PVP)](#6-luồng-3--online-arena-thi-đấu-pvp)
7. [Bảng tổng hợp tất cả API](#7-bảng-tổng-hợp-tất-cả-api)
8. [Xử lý lỗi chung](#8-xử-lý-lỗi-chung)
9. [Hướng dẫn tích hợp token cho mọi request](#9-hướng-dẫn-tích-hợp-token-cho-mọi-request)
10. [Sơ đồ luồng toàn bộ](#10-sơ-đồ-luồng-toàn-bộ)

---

## 1. Tổng quan kiến trúc & luồng nghiệp vụ

### 🗺️ Sơ đồ tổng thể (3 giai đoạn chính)

```
┌─────────────────────────────────────────────────────────────────────┐
│  GIAI ĐOẠN 1: PRACTICE                                              │
│  Người chơi tập luyện, ghi nhận kết quả giải Rubik.                │
│  Cần ≥ 5 lần giải hợp lệ để tiến sang Giai đoạn 2.                │
└─────────────────────────┬───────────────────────────────────────────┘
                          │ Đủ ≥5 solves → tính Ao5
                          ▼
┌─────────────────────────────────────────────────────────────────────┐
│  GIAI ĐOẠN 2: ELO SEEDING (Khởi tạo hồ sơ)                        │
│  Tính Ao5 từ practice → chuyển đổi thành Elo seeding               │
│  → Khởi tạo Online Profile → Elo bị ẨN                             │
│  Phải thắng/thua 5 trận PVP để Elo được CÔNG KHAI                  │
└─────────────────────────┬───────────────────────────────────────────┘
                          │ Hoàn thành 5 trận placement
                          ▼
┌─────────────────────────────────────────────────────────────────────┐
│  GIAI ĐOẠN 3: STANDARD ARENA (Thi đấu thường xuyên)               │
│  Elo công khai trên bảng xếp hạng                                   │
│  K-factor giảm xuống (ổn định hơn)                                  │
│  Có thể thi đấu PVP bình thường                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 🔑 Quy tắc về xác thực (Auth)

| Nhóm endpoint                  | Yêu cầu                    |
|-------------------------------|----------------------------|
| `POST /api/auth/register`     | ❌ Không cần token          |
| `POST /api/auth/login`        | ❌ Không cần token          |
| `GET /api/puzzles`            | ❌ Không cần token (public) |
| `GET /api/arena/leaderboard`  | ❌ Không cần token (public) |
| Tất cả `/api/practice/*`      | ✅ Token với role `COMPETITOR` |
| Tất cả `/api/elo-seeding/*`   | ✅ Token với role `COMPETITOR` |
| Tất cả `/api/arena/*` (còn lại) | ✅ Token với role `COMPETITOR` |
| Admin endpoints               | ✅ Token với role `ADMIN`   |

---

## 2. Xác thực (Authentication)

### 2.1 Đăng ký tài khoản

**`POST /api/auth/register`**

> Không cần token. Tất cả user đăng ký mới đều có role `COMPETITOR`.

**Request Body:**
```json
{
  "email": "player@example.com",
  "password": "MatKhau@123",
  "displayName": "Khoa Rubik",
  "avatarUrl": "https://example.com/avatar.png"  // optional
}
```

| Field         | Kiểu   | Bắt buộc | Mô tả                              |
|--------------|--------|----------|------------------------------------|
| `email`      | string | ✅        | Email duy nhất trong hệ thống      |
| `password`   | string | ✅        | Mật khẩu (BE tự hash)              |
| `displayName`| string | ✅        | Tên hiển thị                       |
| `avatarUrl`  | string | ❌        | URL ảnh đại diện                   |

**Response `200 OK`:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "player@example.com",
  "displayName": "Khoa Rubik"
}
```

**Lỗi có thể gặp:**
- `400 Bad Request`: `{ "message": "Email đã được sử dụng." }`

---

### 2.2 Đăng nhập

**`POST /api/auth/login`**

> Không cần token.

**Request Body:**
```json
{
  "email": "player@example.com",
  "password": "MatKhau@123"
}
```

**Response `200 OK`:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "base64encodedRefreshToken...",
  "accessTokenExpiresAt": "2026-06-04T11:00:00Z",
  "displayName": "Khoa Rubik",
  "email": "player@example.com"
}
```

| Field                  | Mô tả                                              |
|-----------------------|----------------------------------------------------|
| `accessToken`         | JWT token dùng để gọi các API có xác thực          |
| `refreshToken`        | Token dài hạn để lấy accessToken mới khi hết hạn   |
| `accessTokenExpiresAt`| Thời điểm accessToken hết hạn (UTC)                |

> ⚠️ **Lưu ý quan trọng:** Frontend cần lưu cả `accessToken` và `refreshToken` vào `localStorage` hoặc `sessionStorage`.

**Lỗi có thể gặp:**
- `401 Unauthorized`: `{ "message": "Email hoặc mật khẩu không đúng." }`
- `401 Unauthorized`: `{ "message": "Tài khoản đã bị vô hiệu hóa." }`
- `401 Unauthorized`: `{ "message": "Tài khoản đã bị cấm." }`

---

### 2.3 Làm mới Access Token (Refresh)

**`POST /api/auth/refresh-token`**

> Không cần Authorization header. Gọi khi accessToken hết hạn.

**Request Body:**
```json
{
  "refreshToken": "base64encodedRefreshToken..."
}
```

**Response `200 OK`:** _(Cùng format với Login response)_
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "newBase64encodedRefreshToken...",
  "accessTokenExpiresAt": "2026-06-04T12:00:00Z",
  "displayName": "Khoa Rubik",
  "email": "player@example.com"
}
```

> ⚠️ **Lưu ý:** Mỗi lần refresh sẽ **vô hiệu hóa token cũ** và cấp token mới. Frontend phải **cập nhật cả 2** vào storage.

**Lỗi có thể gặp:**
- `401 Unauthorized`: `{ "message": "Refresh token không hợp lệ." }`
- `401 Unauthorized`: `{ "message": "Refresh token đã hết hạn hoặc bị thu hồi." }`

---

## 3. Puzzle Types – Danh sách loại Rubik

> **Không cần token.** Gọi ngay khi app khởi động để lấy danh sách loại Rubik.  
> `puzzleTypeId` (UUID) lấy từ đây sẽ được dùng xuyên suốt toàn bộ các API phía dưới.

### 3.1 Lấy danh sách tất cả loại Rubik

**`GET /api/puzzles`**

**Response `200 OK`:**
```json
[
  {
    "id": "aaaaaaaa-0000-0000-0000-000000000001",
    "name": "Rubik 3x3",
    "code": "333",
    "scrambleLength": 20,
    "isActive": true,
    "createdAt": "2026-01-01T00:00:00Z"
  },
  {
    "id": "bbbbbbbb-0000-0000-0000-000000000002",
    "name": "Rubik 2x2",
    "code": "222",
    "scrambleLength": 10,
    "isActive": true,
    "createdAt": "2026-01-01T00:00:00Z"
  }
]
```

> 💡 **Gợi ý:** Chỉ hiển thị những loại có `isActive: true` cho người dùng thông thường.

### 3.2 Lấy chi tiết 1 loại Rubik

**`GET /api/puzzles/{id}`**

**Response `200 OK`:** _(Cùng cấu trúc với 1 phần tử trong mảng trên)_

---

## 4. Luồng 1 – Practice (Tập luyện)

> **Yêu cầu:** Token `COMPETITOR` trong header Authorization.  
> **Mục đích:** Người chơi tập luyện, ghi nhận kết quả. Đây cũng là bước BẮT BUỘC để có đủ dữ liệu cho Elo Seeding.

### 🔄 Luồng Practice hoàn chỉnh

```
1. [POST /api/practice/sessions]  → Tạo session mới, nhận sessionId
        ↓
2. [POST /api/practice/solves]    → Ghi từng lần giải (lặp lại nhiều lần)
        ↓                           Mỗi lần trả về Ao5 rolling
3. [POST /api/practice/sessions/{sessionId}/end]  → Kết thúc session, nhận tổng kết
```

---

### 4.1 Bắt đầu session tập luyện

**`POST /api/practice/sessions`**

**Headers:**
```
Authorization: Bearer {accessToken}
Content-Type: application/json
```

**Request Body:**
```json
{
  "puzzleTypeId": "aaaaaaaa-0000-0000-0000-000000000001"
}
```

**Response `201 Created`:**
```json
{
  "id": "sess-uuid-1234",
  "userId": "user-uuid-5678",
  "puzzleTypeId": "aaaaaaaa-0000-0000-0000-000000000001",
  "puzzleTypeName": "Rubik 3x3",
  "puzzleTypeCode": "333",
  "startedAt": "2026-06-04T10:30:00Z",
  "endedAt": null,
  "totalSolves": 0
}
```

> 📌 Lưu lại `id` (sessionId) để dùng cho bước 4.2 và 4.3.

---

### 4.2 Ghi nhận 1 lần giải

**`POST /api/practice/solves`**

**Headers:**
```
Authorization: Bearer {accessToken}
Content-Type: application/json
```

**Request Body:**
```json
{
  "sessionId": "sess-uuid-1234",
  "scrambleSequence": "R U R' U' R U2 R' F R U R' U' F'",
  "timeMs": 23450,
  "penalty": "OK"
}
```

| Field              | Kiểu   | Bắt buộc | Giá trị chấp nhận                           |
|-------------------|--------|----------|---------------------------------------------|
| `sessionId`       | UUID   | ✅        | ID session đang tập                         |
| `scrambleSequence`| string | ✅        | Chuỗi tráo (FE tự generate hoặc lấy từ app)|
| `timeMs`          | int    | ✅        | Thời gian giải tính bằng milliseconds       |
| `penalty`         | string | ❌        | `"OK"`, `"PLUS_2"`, `"DNF"` (không phân biệt hoa/thường) |

**Response `200 OK`:**
```json
{
  "id": "solve-uuid-9999",
  "sessionId": "sess-uuid-1234",
  "scrambleSequence": "R U R' U' R U2 R' F R U R' U' F'",
  "timeMs": 23450,
  "penaltyCode": "OK",
  "displayTimeMs": 23450,
  "solvedAt": "2026-06-04T10:31:05Z",
  "currentAo5Ms": null
}
```

| Field           | Mô tả                                                          |
|----------------|----------------------------------------------------------------|
| `timeMs`       | Thời gian gốc (ms), chưa cộng penalty                         |
| `penaltyCode`  | Penalty đã normalize: `OK`, `PLUS_2`, `DNF`                  |
| `displayTimeMs`| **Thời gian để hiển thị:** đã cộng 2000ms nếu PLUS_2, `-1` nếu DNF |
| `currentAo5Ms` | Ao5 rolling sau lần giải này. `null` nếu chưa đủ 5 lần giải  |

> 💡 **Ao5 là gì?** Average of 5 theo chuẩn WCA: lấy 5 lần giải gần nhất, bỏ lần nhanh nhất và chậm nhất, lấy trung bình 3 lần còn lại. `currentAo5Ms` sẽ xuất hiện từ lần giải thứ 5 trở đi.

**Quy tắc penalty:**
- `"OK"`: không phạt → `displayTimeMs = timeMs`
- `"PLUS_2"`: cộng 2 giây → `displayTimeMs = timeMs + 2000`
- `"DNF"`: loại → `displayTimeMs = -1` (hiển thị "DNF" trên UI)

---

### 4.3 Kết thúc session

**`POST /api/practice/sessions/{sessionId}/end`**

**Headers:**
```
Authorization: Bearer {accessToken}
```

**Response `200 OK`:**
```json
{
  "sessionId": "sess-uuid-1234",
  "puzzleTypeCode": "333",
  "startedAt": "2026-06-04T10:30:00Z",
  "endedAt": "2026-06-04T10:55:00Z",
  "totalSolves": 12,
  "dnfCount": 1,
  "meanMs": 24500,
  "bestMs": 19800,
  "bestAo5Ms": 22100,
  "solves": [
    {
      "id": "solve-uuid-0001",
      "sessionId": "sess-uuid-1234",
      "scrambleSequence": "...",
      "timeMs": 23450,
      "penaltyCode": "OK",
      "displayTimeMs": 23450,
      "solvedAt": "2026-06-04T10:31:05Z",
      "currentAo5Ms": null
    }
    // ... các solve khác
  ]
}
```

| Field        | Mô tả                                                  |
|-------------|--------------------------------------------------------|
| `meanMs`    | Thời gian trung bình (ms), **không tính DNF**          |
| `bestMs`    | Thời gian tốt nhất (ms), **không tính DNF**            |
| `bestAo5Ms` | Ao5 tốt nhất trong toàn bộ session                    |
| `dnfCount`  | Số lần DNF                                             |

---

### 4.4 Xem lịch sử session

**`GET /api/practice/sessions?puzzleTypeId={id}&page=1&pageSize=20`**

| Query param    | Kiểu | Bắt buộc | Mô tả                               |
|---------------|------|----------|-------------------------------------|
| `puzzleTypeId`| UUID | ❌        | Lọc theo loại Rubik                 |
| `page`        | int  | ❌        | Trang hiện tại (mặc định: 1)        |
| `pageSize`    | int  | ❌        | Số session mỗi trang (mặc định: 20) |

**Response `200 OK`:** _(Danh sách `PracticeSessionResponseDto`)_

---

### 4.5 Xem chi tiết 1 session

**`GET /api/practice/sessions/{sessionId}`**

**Response `200 OK`:** _(Cùng cấu trúc với kết quả `end session`)_

---

## 5. Luồng 2 – Elo Seeding (Khởi tạo hồ sơ thi đấu)

> **Yêu cầu:** Token `COMPETITOR`.  
> **Điều kiện tiên quyết:** Đã có ≥ N lần giải Practice hợp lệ (N lấy từ `requiredSolves` trong practice-status).

### 🔄 Luồng Seeding hoàn chỉnh

```
1. [GET /api/elo-seeding/practice-status]   → Kiểm tra đủ điều kiện chưa
        ↓ (IsEligibleForSeeding = true)
2. [POST /api/elo-seeding/calculate-ao5]    → Tính Ao5 seeding
        ↓ (có snapshot)
3. [POST /api/elo-seeding/initialize-profile] → Khởi tạo Online Profile
        ↓ (201 Created)
   → Có thể vào PVP, Elo đang ẨN (PLACEMENT phase)
```

---

### 5.1 Kiểm tra trạng thái seeding

**`GET /api/elo-seeding/practice-status?puzzleTypeId={id}`**

> Gọi API này trước để biết người chơi đang ở bước nào và cần làm gì tiếp theo.

**Response `200 OK`:**
```json
{
  "solvesCount": 7,
  "requiredSolves": 5,
  "isEligibleForSeeding": true,
  "canInitializeProfile": true,
  "latestAo5Ms": 25300,
  "latestAo5Display": "25.30s",
  "expectedSeedElo": 1200,
  "hasOnlineProfile": false,
  "canJoinPvp": false,
  "placementMatchesDone": 0,
  "placementMatchCount": 5,
  "isPlacementComplete": false,
  "currentStage": "PRACTICE",
  "nextStepHint": "Bạn đã đủ điều kiện! Hãy gọi /calculate-ao5 để tính điểm seeding."
}
```

**Ý nghĩa `currentStage`:**

| Giá trị      | Ý nghĩa                                                      | Hành động FE                   |
|-------------|--------------------------------------------------------------|-------------------------------|
| `"PRACTICE"`  | Chưa đủ solves hoặc chưa seeding                           | Ẩn nút PVP, cho tập luyện     |
| `"PLACEMENT"` | Đã seeding, đang trong 5 trận đầu, Elo ẩn                  | Hiện nút PVP (nhưng Elo ẩn)   |
| `"STANDARD"`  | Đã hoàn thành 5 trận, Elo công khai                        | Hiện đầy đủ thông tin Elo     |

**Logic hiển thị UI dựa vào response:**

```javascript
if (!status.isEligibleForSeeding) {
  // Hiện: "Bạn cần X lần giải nữa" 
  // progressBar: solvesCount / requiredSolves
} else if (status.canInitializeProfile) {
  // Hiện nút "Khởi tạo hồ sơ thi đấu"
  // Hiện Ao5 dự kiến: latestAo5Display
  // Hiện Elo dự kiến: expectedSeedElo
} else if (status.currentStage === "PLACEMENT") {
  // Hiện: "Trận placement: X/5"
  // Nút PVP active
} else if (status.currentStage === "STANDARD") {
  // Hiện Elo đầy đủ
  // Nút PVP active
}
```

---

### 5.2 Tính Ao5 seeding

**`POST /api/elo-seeding/calculate-ao5?puzzleTypeId={id}`**

**Headers:**
```
Authorization: Bearer {accessToken}
```

**Response `200 OK`:**
```json
{
  "id": "snapshot-uuid-xxxx",
  "ao5TimeMs": 25300,
  "ao5Display": "25.30s",
  "assignedElo": 1200,
  "calculatedAt": "2026-06-04T11:00:00Z"
}
```

> Gọi API này **một lần duy nhất** sau khi đủ điều kiện. Bước tiếp theo là gọi `initialize-profile`.

**Lỗi có thể gặp:**
- `400 Bad Request`:
  ```json
  {
    "message": "Chưa đủ số lượt giải Practice để tính Ao5. Hãy kiểm tra /practice-status để biết số lượt cần thiết."
  }
  ```

---

### 5.3 Khởi tạo Online Profile

**`POST /api/elo-seeding/initialize-profile?puzzleTypeId={id}`**

**Headers:**
```
Authorization: Bearer {accessToken}
```

**Response `201 Created`:**
```json
{
  "id": "profile-uuid-yyyy",
  "userId": "user-uuid-5678",
  "puzzleTypeId": "aaaaaaaa-0000-0000-0000-000000000001",
  "seedElo": 1200,
  "seedSourceCode": "PRACTICE",
  "practiceAo5Ms": 25300,
  "ao5Display": "25.30s",
  "kFactorCurrent": 100,
  "placementMatchesRemaining": 5,
  "currentStage": "PLACEMENT",
  "message": "🎉 Online Profile đã được khởi tạo! Elo seeding: 1200 (từ Ao5 Practice). Elo của bạn đang ẩn – hãy hoàn thành 5 trận PVP để Elo được công khai trên bảng xếp hạng.",
  "nextStep": "Vào hàng đợi matchmaking tại /api/arena/queue để tìm đối thủ."
}
```

> 🎉 Sau bước này, người chơi đã có Online Profile và có thể tham gia PVP.

**Lỗi có thể gặp:**
- `400 Bad Request` (chưa có snapshot):
  ```json
  {
    "message": "Chưa có Practice Ao5 Snapshot. Vui lòng tính Ao5 trước.",
    "nextStep": "Gọi POST /api/elo-seeding/calculate-ao5 trước để tính Ao5 seeding."
  }
  ```
- `400 Bad Request` (chưa đủ solves):
  ```json
  {
    "message": "Chưa đủ số lượt giải Practice...",
    "nextStep": "Gọi POST /api/practice/solves để thêm lượt giải tập luyện."
  }
  ```

---

## 6. Luồng 3 – Online Arena (Thi đấu PVP)

> **Yêu cầu:** Token `COMPETITOR`.  
> **Điều kiện:** Đã có Online Profile (hoàn thành bước 5.3).

### 🔄 Luồng Arena hoàn chỉnh

```
1. [GET /api/arena/eligibility]              → Kiểm tra tư cách vào PVP
        ↓ (canJoinPvp = true)
2. Vào matchmaking queue (WS/SignalR – TBD)  → Tìm đối thủ
        ↓ (match tìm được)
3. [GET /api/arena/profile]                  → Xem hồ sơ của mình
4. [GET /api/arena/profile/{userId}]         → Xem hồ sơ đối thủ
        ↓ (trận kết thúc)
5. [POST /api/arena/match/{matchId}/result]  → Ghi kết quả + cập nhật Elo
        ↓
6. [GET /api/arena/leaderboard]              → Xem bảng xếp hạng
```

---

### 6.1 Kiểm tra tư cách tham gia PVP

**`GET /api/arena/eligibility?puzzleTypeId={id}`**

> **⭐ API quan trọng nhất cho FE.** Gọi API này để quyết định hiển thị UI nào cho người chơi.

**Response `200 OK`:**
```json
{
  "userId": "user-uuid-5678",
  "puzzleTypeId": "aaaaaaaa-0000-0000-0000-000000000001",
  "canJoinPvp": true,
  "blockReason": null,
  "hasOnlineProfile": true,
  "isPlacementComplete": false,
  "placementMatchesDone": 2,
  "placementMatchCount": 5,
  "hiddenElo": 1200,
  "publicElo": null,
  "currentStage": "PLACEMENT",
  "nextStepHint": "Bạn đang trong Placement Phase. Hoàn thành 3 trận nữa để Elo được công khai."
}
```

**Ý nghĩa các field:**

| Field                   | Mô tả                                                      |
|------------------------|------------------------------------------------------------|
| `canJoinPvp`           | `true` = được vào hàng đợi PVP                             |
| `blockReason`          | Lý do bị chặn (null nếu được phép)                         |
| `hasOnlineProfile`     | Đã khởi tạo Online Profile chưa                            |
| `isPlacementComplete`  | Đã hoàn thành 5 trận placement chưa                        |
| `hiddenElo`            | Elo hiện tại khi đang PLACEMENT (chỉ hiện cho chính người đó) |
| `publicElo`            | Elo công khai sau khi hoàn thành placement                  |
| `currentStage`         | `"NO_PROFILE"`, `"PLACEMENT"`, hoặc `"STANDARD"`           |

**Ý nghĩa `currentStage` trong eligibility:**

| Stage          | Giải thích                    | canJoinPvp | Hiển thị Elo       |
|---------------|-------------------------------|------------|-------------------|
| `NO_PROFILE`  | Chưa seeding xong             | `false`    | Không có          |
| `PLACEMENT`   | Đang 5 trận đầu               | `true`     | Elo ẩn (hiddenElo)|
| `STANDARD`    | Đã placed, thi đấu thường     | `true`     | Elo công khai     |

**Logic điều hướng UI:**
```javascript
const eligibility = await getEligibility(puzzleTypeId);

if (eligibility.currentStage === "NO_PROFILE") {
  // Chuyển sang màn hình Practice/Seeding
  navigate("/practice");
} else if (eligibility.currentStage === "PLACEMENT") {
  // Hiện banner: "Placement X/5 - Elo đang ẩn"
  showPlacementBanner(eligibility.placementMatchesDone, eligibility.placementMatchCount);
  enablePvpButton(); // Vẫn cho vào PVP
} else { // STANDARD
  // Hiện Elo đầy đủ
  showElo(eligibility.publicElo);
  enablePvpButton();
}
```

---

### 6.2 Lấy hồ sơ Online Arena của bản thân

**`GET /api/arena/profile?puzzleTypeId={id}`**

**Response `200 OK`:**
```json
{
  "userId": "user-uuid-5678",
  "puzzleTypeId": "aaaaaaaa-0000-0000-0000-000000000001",
  "puzzleTypeName": "Rubik 3x3",
  "eloVisible": null,
  "peakElo": null,
  "seedElo": 1200,
  "seedSourceCode": "PRACTICE",
  "practiceAo5Ms": 25300,
  "placementMatchesDone": 2,
  "placementMatchCount": 5,
  "isPlacementComplete": false,
  "placementCompletedAt": null,
  "totalWins": 1,
  "totalLosses": 1,
  "totalDraws": 0,
  "winRate": 0.5,
  "createdAt": "2026-06-04T11:00:00Z"
}
```

> **`eloVisible`**: `null` khi đang PLACEMENT phase. Sau khi hoàn thành, sẽ có giá trị số thực.  
> **`winRate`**: Tỷ lệ thắng từ 0.0 đến 1.0 (nhân 100 để hiện %).

**Response `404 Not Found`:**
```json
{
  "message": "Chưa có Online Profile. Hãy gọi /api/elo-seeding/initialize-profile để khởi tạo.",
  "nextStep": "Hoàn thành ≥5 lượt giải Practice → calculate-ao5 → initialize-profile"
}
```

---

### 6.3 Lấy hồ sơ của người chơi khác

**`GET /api/arena/profile/{userId}?puzzleTypeId={id}`**

> Endpoint public, không cần token. Dùng để hiển thị profile của đối thủ.

**Response:** Cùng cấu trúc với 6.2.

---

### 6.4 Ghi nhận kết quả trận đấu

**`POST /api/arena/match/{matchId}/result?winnerId={userId}`**

> **Lưu ý:** `matchId` là UUID của trận đấu (tạo bởi matchmaking system).  
> `winnerId` là UUID người thắng. **Bỏ trống query param nếu hòa.**

**Ví dụ người thắng:**
```
POST /api/arena/match/match-uuid-xxxx/result?winnerId=user-uuid-5678
```

**Ví dụ hòa:**
```
POST /api/arena/match/match-uuid-xxxx/result
```

**Response `200 OK`:**
```json
{
  "matchId": "match-uuid-xxxx",
  "player1": {
    "userId": "user-uuid-5678",
    "displayName": "Khoa Rubik",
    "eloBefore": 1200,
    "eloAfter": 1250,
    "delta": 50,
    "actualScore": 1.0,
    "expectedScore": 0.5,
    "kFactorUsed": 100,
    "placementMatchesDone": 3,
    "isPlacementComplete": false
  },
  "player2": {
    "userId": "user-uuid-9999",
    "displayName": "Nam Speed",
    "eloBefore": 1200,
    "eloAfter": 1150,
    "delta": -50,
    "actualScore": 0.0,
    "expectedScore": 0.5,
    "kFactorUsed": 100,
    "placementMatchesDone": 2,
    "isPlacementComplete": false
  },
  "isPlacementMatch": true,
  "player1PlacementCompleted": false,
  "player2PlacementCompleted": false
}
```

**Giải thích công thức Elo:**
- `R' = R + K × (S - E)`
  - `R` = Elo hiện tại
  - `K` = K-factor (100 trong placement, ~20-30 sau placement)
  - `S` = Kết quả thực tế (1.0 = thắng, 0.0 = thua, 0.5 = hòa)
  - `E` = Kết quả kỳ vọng (tính theo xác suất thắng dựa trên Elo chênh lệch)

**Hiển thị sau trận:**
```javascript
const result = await recordMatchResult(matchId, winnerId);

if (result.player1PlacementCompleted || result.player2PlacementCompleted) {
  // 🎉 Hiện thông báo "Placement hoàn thành! Elo của bạn đã công khai!"
}
// Hiện bảng kết quả với delta Elo
showMatchResult({
  myEloChange: result.player1.delta, // +50 hoặc -50
  myNewElo: result.player1.eloAfter
});
```

---

### 6.5 Bảng xếp hạng

**`GET /api/arena/leaderboard?puzzleTypeId={id}&page=1&pageSize=50`**

> **Public endpoint – không cần token.** Chỉ hiển thị players đã hoàn thành Placement Phase.

| Query param    | Mô tả                                 | Mặc định |
|---------------|---------------------------------------|---------|
| `puzzleTypeId`| ID loại Rubik (bắt buộc)              | -       |
| `page`        | Trang hiện tại                        | 1       |
| `pageSize`    | Số dòng/trang (tối đa 100)            | 50      |

**Response `200 OK`:**
```json
{
  "entries": [
    {
      "rank": 1,
      "userId": "user-uuid-aaaa",
      "displayName": "Champion Player",
      "avatarUrl": "https://example.com/avatar1.png",
      "elo": 1850,
      "peakElo": 1900,
      "totalWins": 120,
      "totalLosses": 30,
      "totalDraws": 5,
      "winRate": 0.78,
      "placementCompletedAt": "2026-05-01T09:00:00Z"
    }
    // ...
  ],
  "totalCount": 250,
  "page": 1,
  "pageSize": 50,
  "totalPages": 5
}
```

**Phân trang:**
```javascript
// Tải trang tiếp theo
const nextPage = await getLeaderboard(puzzleTypeId, currentPage + 1, 50);
```

---

## 7. Bảng tổng hợp tất cả API

### 🔐 Auth

| Method | Endpoint                  | Auth | Mô tả                        |
|--------|--------------------------|------|------------------------------|
| POST   | `/api/auth/register`      | ❌    | Đăng ký tài khoản mới        |
| POST   | `/api/auth/login`         | ❌    | Đăng nhập, nhận JWT token    |
| POST   | `/api/auth/refresh-token` | ❌    | Làm mới access token         |

### 🧩 Puzzle Types

| Method | Endpoint              | Auth | Mô tả                          |
|--------|-----------------------|------|-------------------------------|
| GET    | `/api/puzzles`        | ❌    | Danh sách loại Rubik          |
| GET    | `/api/puzzles/{id}`   | ❌    | Chi tiết 1 loại Rubik         |
| POST   | `/api/puzzles`        | ADMIN| Tạo loại Rubik mới            |
| PUT    | `/api/puzzles/{id}`   | ADMIN| Cập nhật loại Rubik           |
| PATCH  | `/api/puzzles/{id}/deactivate` | ADMIN | Vô hiệu hóa            |
| PATCH  | `/api/puzzles/{id}/activate`   | ADMIN | Kích hoạt lại           |

### 🏃 Practice (Tập luyện)

| Method | Endpoint                                    | Auth        | Mô tả                         |
|--------|---------------------------------------------|-------------|-------------------------------|
| POST   | `/api/practice/sessions`                    | COMPETITOR  | Bắt đầu session tập luyện     |
| POST   | `/api/practice/solves`                      | COMPETITOR  | Ghi nhận 1 lần giải           |
| POST   | `/api/practice/sessions/{sessionId}/end`    | COMPETITOR  | Kết thúc session, xem tổng kết|
| GET    | `/api/practice/sessions`                    | COMPETITOR  | Lịch sử sessions (có phân trang)|
| GET    | `/api/practice/sessions/{sessionId}`        | COMPETITOR  | Chi tiết 1 session            |

### 🌱 Elo Seeding

| Method | Endpoint                                 | Auth        | Mô tả                            |
|--------|------------------------------------------|-------------|----------------------------------|
| GET    | `/api/elo-seeding/practice-status`       | COMPETITOR  | Kiểm tra trạng thái seeding      |
| POST   | `/api/elo-seeding/calculate-ao5`         | COMPETITOR  | Tính Ao5 seeding từ practice      |
| POST   | `/api/elo-seeding/initialize-profile`    | COMPETITOR  | Khởi tạo Online Profile          |

### ⚔️ Online Arena

| Method | Endpoint                                    | Auth        | Mô tả                              |
|--------|---------------------------------------------|-------------|-------------------------------------|
| GET    | `/api/arena/eligibility`                    | COMPETITOR  | Kiểm tra tư cách PVP (quan trọng!) |
| GET    | `/api/arena/profile`                        | COMPETITOR  | Hồ sơ Arena của bản thân           |
| GET    | `/api/arena/profile/{userId}`               | Tùy chọn   | Hồ sơ Arena của người khác         |
| GET    | `/api/arena/leaderboard`                    | ❌           | Bảng xếp hạng (public)             |
| POST   | `/api/arena/match/{matchId}/result`         | COMPETITOR  | Ghi kết quả trận + cập nhật Elo    |

---

## 8. Xử lý lỗi chung

### HTTP Status Codes

| Status | Ý nghĩa                        | Xử lý FE                              |
|--------|-------------------------------|---------------------------------------|
| 200    | Thành công                    | Hiển thị dữ liệu                      |
| 201    | Tạo mới thành công            | Thông báo thành công                  |
| 400    | Dữ liệu không hợp lệ         | Hiển thị `message` từ response body   |
| 401    | Chưa xác thực / Token hết hạn | Gọi refresh-token hoặc redirect login |
| 403    | Không có quyền                | Thông báo "Không có quyền truy cập"   |
| 404    | Không tìm thấy                | Hiển thị thông báo 404                |
| 409    | Conflict (trùng dữ liệu)      | Hiển thị `message` từ response body   |
| 500    | Lỗi server                    | Thông báo lỗi chung, thử lại sau      |

### Format lỗi chuẩn

```json
{
  "message": "Mô tả lỗi cụ thể bằng tiếng Việt",
  "nextStep": "Hướng dẫn người dùng làm gì tiếp theo" // Chỉ có trong một số endpoint
}
```

### Xử lý 401 tự động (Token Refresh)

```javascript
// Interceptor axios hoặc fetch wrapper
async function apiRequest(url, options) {
  let response = await fetch(url, {
    ...options,
    headers: {
      'Authorization': `Bearer ${getAccessToken()}`,
      'Content-Type': 'application/json',
      ...options.headers
    }
  });

  if (response.status === 401) {
    // Thử refresh token
    const refreshed = await refreshAccessToken();
    if (refreshed) {
      // Thử lại request với token mới
      response = await fetch(url, {
        ...options,
        headers: {
          'Authorization': `Bearer ${getAccessToken()}`,
          ...options.headers
        }
      });
    } else {
      // Redirect đến trang login
      window.location.href = '/login';
    }
  }

  return response;
}

async function refreshAccessToken() {
  const refreshToken = localStorage.getItem('refreshToken');
  if (!refreshToken) return false;

  const res = await fetch('/api/auth/refresh-token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken })
  });

  if (res.ok) {
    const data = await res.json();
    localStorage.setItem('accessToken', data.accessToken);
    localStorage.setItem('refreshToken', data.refreshToken);
    return true;
  }
  return false;
}
```

---

## 9. Hướng dẫn tích hợp token cho mọi request

### Cách gửi token

Tất cả các API yêu cầu xác thực đều cần header sau:

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

> ⚠️ **Quan trọng:** Phải có chữ `Bearer ` (có khoảng trắng) trước token.

### Ví dụ với fetch API

```javascript
const response = await fetch('https://localhost:7xxx/api/practice/sessions', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${localStorage.getItem('accessToken')}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({ puzzleTypeId: selectedPuzzleTypeId })
});
```

### Ví dụ với Axios

```javascript
import axios from 'axios';

const api = axios.create({
  baseURL: 'https://localhost:7xxx',
});

// Interceptor tự động gắn token
api.interceptors.request.use(config => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});
```

### Lưu trữ token sau khi đăng nhập

```javascript
async function login(email, password) {
  const res = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password })
  });

  if (res.ok) {
    const data = await res.json();
    localStorage.setItem('accessToken', data.accessToken);
    localStorage.setItem('refreshToken', data.refreshToken);
    localStorage.setItem('displayName', data.displayName);
    localStorage.setItem('email', data.email);
    localStorage.setItem('tokenExpiry', data.accessTokenExpiresAt);
    return data;
  }
  throw new Error('Login failed');
}
```

---

## 10. Sơ đồ luồng toàn bộ

### 🗺️ Luồng từ đầu đến cuối (Happy Path)

```
[Người dùng mới]
      │
      ▼
POST /api/auth/register          → Tạo tài khoản
      │
      ▼
POST /api/auth/login             → Lấy accessToken + refreshToken
      │
      ▼
GET  /api/puzzles                → Chọn loại Rubik (ví dụ: 333)
      │
      ▼
POST /api/practice/sessions      → Bắt đầu session tập
      │
      ▼
POST /api/practice/solves        → Giải lần 1 (lặp lại ≥5 lần)
POST /api/practice/solves        → Giải lần 2
...
POST /api/practice/solves        → Giải lần N (N ≥ 5)
      │
      ▼
POST /api/practice/sessions/{id}/end  → Kết thúc session
      │
      ▼
GET  /api/elo-seeding/practice-status → Kiểm tra đủ điều kiện
      │ (isEligibleForSeeding = true)
      ▼
POST /api/elo-seeding/calculate-ao5   → Tính Ao5 → Elo seeding
      │
      ▼
POST /api/elo-seeding/initialize-profile → Khởi tạo hồ sơ
      │ (201 Created, stage = PLACEMENT)
      ▼
GET  /api/arena/eligibility      → Kiểm tra canJoinPvp = true
      │
      ▼
[Matchmaking - WS/SignalR TBD]   → Tìm đối thủ
      │
      ▼
GET  /api/arena/profile          → Xem hồ sơ mình
GET  /api/arena/profile/{opponentId} → Xem hồ sơ đối thủ
      │
      ▼
[Thi đấu thực tế]                → Giải rubik theo thời gian thực
      │
      ▼
POST /api/arena/match/{matchId}/result → Ghi kết quả + cập nhật Elo
      │ (Lặp lại 5 lần cho Placement Phase)
      ▼
[isPlacementComplete = true]     → Elo công khai!
      │
      ▼
GET  /api/arena/leaderboard      → Xem bảng xếp hạng
```

---

## 📌 Lưu ý quan trọng cho Frontend Team

1. **`puzzleTypeId` cần lấy trước** từ `GET /api/puzzles` và lưu lại, vì nó được dùng trong hầu hết mọi API.

2. **Luôn gọi `GET /api/arena/eligibility` trước** khi hiển thị màn hình Arena để quyết định UI phù hợp.

3. **Token có thời hạn** – Implement interceptor để tự động refresh khi nhận 401.

4. **Ao5 rolling** trong Practice: `currentAo5Ms` sẽ là `null` cho đến lần giải thứ 5. Đây là Ao5 cho **5 lần giải GẦN NHẤT**, không phải 5 lần đầu tiên.

5. **Elo trong PLACEMENT phase** (`eloVisible = null`): Người chơi đang placement KHÔNG thấy Elo thật trên public profile. Nhưng `eligibility.hiddenElo` sẽ có giá trị để hiển thị riêng cho họ.

6. **`displayTimeMs = -1`** có nghĩa là DNF – FE cần check điều kiện này trước khi format số.

7. **Bảng xếp hạng chỉ hiện players đã placement** – Đây là thiết kế có chủ đích, không phải bug.

8. **Matchmaking (queue PVP)** hiện chưa có REST API – sẽ được implement qua **WebSocket/SignalR**. Tài liệu sẽ được bổ sung khi có.

---

*📧 Liên hệ BE team nếu có thắc mắc về API hoặc cần thêm thông tin.*
