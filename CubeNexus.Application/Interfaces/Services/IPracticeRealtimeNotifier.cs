using CubeNexus.Application.DTOs.Practice;

namespace CubeNexus.Application.Interfaces.Services;

public interface IPracticeRealtimeNotifier
{
    Task NotifyPracticeAttemptUpdatedAsync(Guid userId, PracticeAttemptResponseDto payload);
    Task NotifyPracticeMobileConnectedAsync(Guid userId, Guid sessionId);
}
