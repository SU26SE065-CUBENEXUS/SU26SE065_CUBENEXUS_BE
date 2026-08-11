namespace CubeNexus.Application.DTOs.Tournament;

public class TournamentJudgeDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TournamentId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string RoleCode { get; set; } = "STATION_JUDGE";
    public int? AssignedStationNumber { get; set; }
    public DateTime AssignedAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Mật khẩu thật dạng plain-text (chỉ trả về khi mới tạo hoặc vừa reset để Manager sao chép bàn giao).
    /// </summary>
    public string? RawPassword { get; set; }
}

public class ToggleJudgeStatusDto
{
    public bool IsActive { get; set; }
}

public class CreateTournamentJudgeDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string RoleCode { get; set; } = "STATION_JUDGE";
    public int? AssignedStationNumber { get; set; }
}

public class BatchCreateTournamentJudgeDto
{
    public int CheckInCount { get; set; } = 1;
    public int StationCount { get; set; } = 5;
    public int JudgesPerStation { get; set; } = 2;
    public int Count { get; set; } = 0; // Legacy fallback if positive
    public string NamePrefix { get; set; } = "Trọng tài";
    public List<string>? CustomNames { get; set; }
}

public class UpdateTournamentJudgeDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string? RoleCode { get; set; }
    public int? AssignedStationNumber { get; set; }
}

public class ResetJudgePasswordDto
{
    public string? NewPassword { get; set; }
}

public class ShuffleTournamentJudgesDto
{
    public int CheckInCount { get; set; } = 1;
    public int StationCount { get; set; } = 5;
    public int JudgesPerStation { get; set; } = 2;
}

