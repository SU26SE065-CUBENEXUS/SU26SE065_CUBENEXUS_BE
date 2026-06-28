using System;
using System.Collections.Generic;

namespace CubeNexus.Application.DTOs.Registration;

public class CheckInResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool AlreadyCheckedIn { get; set; }
    public Guid RegistrationId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TournamentName { get; set; } = string.Empty;
    public DateTime? CheckedInAt { get; set; }
    public List<string> Events { get; set; } = new();
    public List<CheckInAssignmentDto> Assignments { get; set; } = new();
}

public class CheckInAssignmentDto
{
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string GroupStatusCode { get; set; } = string.Empty;
    public int? StationNumber { get; set; }
}
