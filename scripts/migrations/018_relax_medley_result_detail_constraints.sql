-- Migration 018: Relax medley_result_details constraints
-- Medley Relay chỉ theo dõi TỔNG THỜI GIAN duy nhất cho toàn bộ lượt thi,
-- không theo dõi thời gian riêng từng khối Rubik trong chuỗi.
-- Vì vậy raw_time_ms và final_time_ms trên từng dòng detail có thể NULL.

-- Xóa constraint cũ yêu cầu raw_time_ms và final_time_ms NOT NULL khi is_dnf=false
ALTER TABLE medley_result_details
    DROP CONSTRAINT IF EXISTS ck_medley_result_details_dnf_consistency;

-- Giữ lại constraint kiểm tra sort_order > 0 và giá trị > 0 nếu có (không thay đổi)
-- ck_medley_result_details_values: (raw_time_ms IS NULL OR raw_time_ms > 0) AND (final_time_ms IS NULL OR final_time_ms > 0)
-- constraint này vẫn hợp lệ và đủ
