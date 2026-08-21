# Database Relationships - CubeNexus

## Tổng quan

Database gồm **22 bảng** chia thành 7 nhóm chức năng:

```
┌─────────────────────────────────────────────────────────────────┐
│                        MASTER DATA                              │
│   users ─── puzzle_types ─── elo_config                         │
└──────┬──────────────┬───────────────────────────────────────────┘
       │              │
       ▼              ▼
┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌──────────────┐
│   OFFLINE   │ │   ONLINE    │ │    VIDEO    │ │   PRACTICE   │
│  TOURNAMENT │ │    ARENA    │ │ CHALLENGES  │ │              │
└─────────────┘ └─────────────┘ └─────────────┘ └──────────────┘
       │              │
       ▼              ▼
┌─────────────────────────────────────────────────────────────────┐
│              NOTIFICATIONS  &  REFRESH TOKENS                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 1. MASTER DATA & IDENTITY

### users
Bảng trung tâm của toàn bộ hệ thống. Mọi bảng khác đều liên kết về đây.

| Quan hệ | Bảng liên kết | Mô tả |
|---------|---------------|--------|
| 1 → N | offline_tournaments | User tạo giải (created_by) |
| 1 → N | offline_tournament_managers | User quản lý giải |
| 1 → N | offline_registrations | User đăng ký tham gia giải |
| 1 → N | offline_results | User làm judge (judged_by) |
| 1 → N | offline_disputes | User báo cáo/giải quyết khiếu nại |
| 1 → N | online_profiles | User có profile Elo theo từng puzzle |
| 1 → N | online_matchmaking_queue | User xếp hàng chờ match |
| 1 → N | online_matches | User tham gia trận (player1/player2/winner) |
| 1 → N | online_fraud_reports | User báo cáo/bị báo cáo gian lận |
| 1 → N | video_challenges | User tạo thử thách (created_by) |
| 1 → N | video_challenge_submissions | User nộp bài giải |
| 1 → N | practice_solves | User luyện tập |
| 1 → N | notifications | User nhận thông báo |
| 1 → N | refresh_tokens | User có nhiều refresh token |
| 1 → N | elo_config | User (admin) cập nhật config |

### puzzle_types
Loại puzzle (3x3, 2x2, 4x4, Megaminx...). Được dùng xuyên suốt cả offline, online, video challenges, practice.

| Quan hệ | Bảng liên kết | Mô tả |
|---------|---------------|--------|
| 1 → N | offline_events | Event thuộc loại puzzle nào |
| 1 → N | offline_medley_puzzles | Puzzle trong event medley |
| 1 → N | offline_scrambles | Scramble cho loại puzzle nào |
| 1 → N | online_profiles | Profile Elo theo puzzle type |
| 1 → N | online_matchmaking_queue | Queue theo puzzle type |
| 1 → N | online_matches | Match cho puzzle type nào |
| 1 → N | video_challenges | Thử thách cho puzzle type nào |
| 1 → N | practice_solves | Luyện tập puzzle type nào |

### elo_config
Cấu hình hệ thống Elo (singleton, chỉ 1 record active). Không có FK đến bảng khác ngoài `updated_by → users`.

---

## 2. OFFLINE TOURNAMENT

Luồng dữ liệu:

```
offline_tournaments
    │
    ├── offline_tournament_managers (ai quản lý giải)
    ├── offline_registrations (ai đăng ký)
    │
    └── offline_events (nội dung thi: 3x3 Ao5, 2x2 Bo3...)
            │
            ├── offline_medley_puzzles (nếu format = medley)
            │
            └── offline_groups (chia nhóm theo vòng)
                    │
                    ├── offline_group_competitors (thí sinh trong nhóm)
                    │       │
                    │       └── offline_results (kết quả từng lượt)
                    │               │
                    │               ├── offline_medley_result_details
                    │               └── offline_disputes (khiếu nại)
                    │
                    └── offline_scramble_sets
                            │
                            └── offline_scrambles (từng scramble)
```

### Chi tiết quan hệ:

| Bảng cha | Bảng con | Quan hệ | FK |
|----------|----------|---------|-----|
| offline_tournaments | offline_tournament_managers | 1 → N | tournament_id |
| offline_tournaments | offline_events | 1 → N | tournament_id |
| offline_tournaments | offline_registrations | 1 → N | tournament_id |
| offline_events | offline_medley_puzzles | 1 → N | event_id |
| offline_events | offline_groups | 1 → N | event_id |
| offline_groups | offline_group_competitors | 1 → N | group_id |
| offline_groups | offline_scramble_sets | 1 → N | group_id |
| offline_registrations | offline_group_competitors | 1 → N | registration_id |
| offline_scramble_sets | offline_scrambles | 1 → N | scramble_set_id |
| offline_group_competitors | offline_results | 1 → N | group_competitor_id |
| offline_scrambles | offline_results | 1 → N | scramble_id |
| offline_results | offline_medley_result_details | 1 → N | result_id |
| offline_results | offline_disputes | 1 → N | result_id |
| offline_medley_puzzles | offline_medley_result_details | 1 → N | medley_puzzle_id |

### Giải thích luồng:
1. **Admin/Organizer** tạo `offline_tournaments`
2. Gán **managers** vào giải
3. Tạo **events** (nội dung thi) cho giải, mỗi event gắn với 1 `puzzle_type`
4. **Players** đăng ký qua `offline_registrations`, nhận QR code
5. Chia **groups** theo vòng (round 1, round 2...)
6. Gán thí sinh vào nhóm qua `offline_group_competitors`
7. Tạo **scramble_sets** → **scrambles** cho mỗi nhóm
8. Judge ghi **results** cho từng lượt giải
9. Nếu có khiếu nại → tạo **disputes**

---

## 3. ONLINE ARENA

Luồng dữ liệu:

```
users
  │
  └── online_profiles (1 profile / puzzle_type)
          │
          ├── online_matchmaking_queue (xếp hàng chờ)
          │
          └── online_elo_history (lịch sử Elo)
                  │
                  └── online_matches (trận đấu 1v1)
                          │
                          └── online_fraud_reports (báo cáo gian lận)
```

### Chi tiết quan hệ:

| Bảng cha | Bảng con | Quan hệ | FK |
|----------|----------|---------|-----|
| users | online_profiles | 1 → N | user_id (UNIQUE với puzzle_type_id) |
| online_profiles | online_matchmaking_queue | 1 → N | online_profile_id |
| online_profiles | online_elo_history | 1 → N | online_profile_id |
| online_matches | online_elo_history | 1 → N | match_id |
| online_matches | online_fraud_reports | 1 → N | match_id |
| users | online_matches | 1 → N | player1_id, player2_id, winner_id |
| users | online_fraud_reports | 1 → N | reported_by, accused_user_id, reviewed_by |

### Giải thích luồng:
1. User tạo **online_profile** cho mỗi puzzle type muốn chơi ranked
2. Vào hàng đợi **matchmaking_queue**
3. Hệ thống ghép cặp → tạo **online_matches**
4. Sau trận → cập nhật Elo, ghi **online_elo_history**
5. Nếu nghi gian lận → tạo **online_fraud_reports**

---

## 4. VIDEO CHALLENGES (Thử thách quay video)

Bản chất: Admin ra 1 scramble, user tự giải ở nhà trong thời hạn, quay video rồi nộp.

```
video_challenges
    │
    └── video_challenge_submissions (bài nộp video của user)
```

### Chi tiết quan hệ:

| Bảng cha | Bảng con | Quan hệ | FK |
|----------|----------|---------|-----|
| video_challenges | video_challenge_submissions | 1 → N | challenge_id |
| users | video_challenges | 1 → N | created_by |
| users | video_challenge_submissions | 1 → N | user_id, reviewed_by |
| puzzle_types | video_challenges | 1 → N | puzzle_type_id |

### Giải thích luồng:
1. Admin tạo **video_challenges** với 1 scramble cố định + thời hạn nộp
2. User tự giải ở nhà, quay video, nộp **video_challenge_submissions**
3. Admin xem video, approve/reject, ghi note nếu cần

### Truy vấn phổ biến:
```sql
-- Lấy leaderboard 1 challenge (chỉ bài đã approved, sắp theo thời gian)
SELECT u.display_name, s.claimed_time_ms
FROM video_challenge_submissions s
JOIN users u ON u.id = s.user_id
WHERE s.challenge_id = ? AND s.status_code = 'approved'
ORDER BY s.claimed_time_ms ASC;

-- Lấy các challenge đang active
SELECT * FROM video_challenges
WHERE status_code = 'active' AND end_at > NOW();
```

---

## 5. PRACTICE

```
users ──→ practice_solves ←── puzzle_types
```

### Chi tiết quan hệ:

| Bảng cha | Bảng con | Quan hệ | FK |
|----------|----------|---------|-----|
| users | practice_solves | 1 → N | user_id |
| puzzle_types | practice_solves | 1 → N | puzzle_type_id |

Đơn giản nhất: mỗi lượt giải luyện tập là 1 record, gắn với user + puzzle type.

---

## 6. NOTIFICATIONS

```
users ──→ notifications
```

| Bảng cha | Bảng con | Quan hệ | FK |
|----------|----------|---------|-----|
| users | notifications | 1 → N | user_id |

Mỗi notification thuộc về 1 user. Field `payload` (JSONB) chứa data bổ sung tùy loại thông báo.

---

## 7. REFRESH TOKENS

```
users ──→ refresh_tokens
```

| Bảng cha | Bảng con | Quan hệ | FK |
|----------|----------|---------|-----|
| users | refresh_tokens | 1 → N | user_id |

Mỗi user có thể có nhiều refresh token (đăng nhập nhiều thiết bị). Token cũ bị revoke khi rotate.

---

## Tổng hợp số lượng quan hệ FK

| Nhóm | Số bảng | Số FK |
|------|---------|-------|
| Master Data | 3 | 1 |
| Offline Tournament | 12 | 16 |
| Online Arena | 5 | 11 |
| Video Challenges | 2 | 4 |
| Practice | 1 | 2 |
| Notifications | 1 | 1 |
| Refresh Tokens | 1 | 1 |
| **Tổng** | **22** | **36** |

---

## Quy ước đặt tên

| Prefix | Module | Mô tả |
|--------|--------|--------|
| `offline_` | Offline Tournament | Giải đấu trực tiếp tại địa điểm |
| `online_` | Online Arena | Đấu 1v1 ranked real-time |
| `video_challenge` | Video Challenges | Thử thách quay video giải rubik |
| `practice_` | Practice | Luyện tập cá nhân |
| _(không prefix)_ | Master Data / Shared | users, puzzle_types, elo_config, notifications, refresh_tokens |
