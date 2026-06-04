using CubeNexus.Application.DTOs.Practice;

namespace CubeNexus.Application.Interfaces.Services;

public interface IPracticeService
{
    /// <summary>Competitor: bắt đầu session tập luyện mới</summary>
    Task<PracticeSessionResponseDto> StartSessionAsync(Guid userId, StartPracticeSessionDto dto);

    /// <summary>Competitor: nộp 1 lần giải, nhận về kết quả + rolling Ao5</summary>
    Task<PracticeSolveResponseDto> SubmitSolveAsync(Guid userId, SubmitSolveDto dto);

    /// <summary>Competitor: kết thúc session, lấy thống kê tổng</summary>
    Task<PracticeSessionSummaryDto> EndSessionAsync(Guid userId, Guid sessionId);

    /// <summary>Competitor: xem lịch sử session</summary>
    Task<IReadOnlyList<PracticeSessionResponseDto>> GetMySessionsAsync(Guid userId, Guid? puzzleTypeId = null, int page = 1, int pageSize = 20);

    /// <summary>Competitor: xem chi tiết 1 session (tất cả lần giải)</summary>
    Task<PracticeSessionSummaryDto> GetSessionDetailAsync(Guid userId, Guid sessionId);
}
