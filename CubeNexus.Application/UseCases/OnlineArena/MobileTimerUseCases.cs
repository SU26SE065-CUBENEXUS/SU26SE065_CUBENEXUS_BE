using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class ConnectMobileTimerUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IMobileTimerSessionRepository _sessionRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public ConnectMobileTimerUseCase(
        IOnlineMatchRepository matchRepo,
        IMobileTimerSessionRepository sessionRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _sessionRepo = sessionRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<MobileTimerConnectResponseDto> ExecuteAsync(Guid userId, string qrSessionCode, string? deviceInfo)
    {
        if (string.IsNullOrWhiteSpace(qrSessionCode))
            throw new ArgumentException("qrSessionCode is required.");

        var qrParts = qrSessionCode.Split(':');
        var actualQrCode = qrParts[0].Trim();
        var targetRole = qrParts.Length > 1 ? qrParts[1].Trim() : null;

        var match = await _matchRepo.GetByQrSessionCodeAsync(actualQrCode);
        if (match == null)
            throw new KeyNotFoundException("Invalid QR code - no match found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");

        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");

        if (targetRole != null)
        {
            if (targetRole == "P1" && match.Player1Id != userId)
                throw new UnauthorizedAccessException("This QR code belongs to the other player. Please scan your own QR code.");
            if (targetRole == "P2" && match.Player2Id != userId)
                throw new UnauthorizedAccessException("This QR code belongs to the other player. Please scan your own QR code.");
        }

        var latestActiveMatch = await _matchRepo.GetLatestActiveMatchAsync(userId, match.PuzzleTypeId);
        if (latestActiveMatch != null && latestActiveMatch.Id != match.Id && latestActiveMatch.CreatedAt > match.CreatedAt)
        {
            throw new ConflictException("A newer active match exists. This pairing code has expired.");
        }

        var session = await _sessionRepo.GetSessionAsync(match.Id, userId);
        if (session == null)
        {
            session = new MobileTimerSession
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                UserId = userId,
                QrSessionCode = actualQrCode,
                DeviceInfo = deviceInfo,
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };
            await _sessionRepo.AddAsync(session);
        }
        else
        {
            session.QrSessionCode = actualQrCode;
            session.DeviceInfo = deviceInfo;
            session.ConnectedAt = DateTime.UtcNow;
            session.IsActive = true;
            _sessionRepo.Update(session);
        }

        if (match.Player1Id == userId)
            match.Player1TimerReady = true;
        else
            match.Player2TimerReady = true;

        // Event-driven auto-ready: evaluate checklist after timer connected
        await MarkCameraReadyUseCase.AutoReadyIfChecklistPassedAsync(
            match, userId, _matchRepo, _notifier, _uow);

        var response = new MobileTimerConnectResponseDto
        {
            Message = "Mobile timer connected.",
            MatchId = match.Id,
            SessionId = session.Id,
            StatusCode = match.StatusCode,
            PlayerId = userId,
            Player1TimerReady = match.Player1TimerReady,
            Player2TimerReady = match.Player2TimerReady,
            DeviceInfo = deviceInfo
        };

        await _notifier.NotifyTimerConnectedAsync(match.Id, response);
        return response;
    }
}

public class DisconnectMobileTimerUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IMobileTimerSessionRepository _sessionRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public DisconnectMobileTimerUseCase(
        IOnlineMatchRepository matchRepo,
        IMobileTimerSessionRepository sessionRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _sessionRepo = sessionRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<MobileTimerDisconnectResponseDto> ExecuteAsync(Guid userId, Guid matchId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");

        var session = await _sessionRepo.GetSessionAsync(matchId, userId);
        if (session == null)
            throw new KeyNotFoundException("Mobile timer session not found.");

        session.IsActive = false;
        _sessionRepo.Update(session);

        if (match.StatusCode != OnlineMatchStatus.ONGOING.ToString())
        {
            if (match.Player1Id == userId)
                match.Player1TimerReady = false;
            else
                match.Player2TimerReady = false;

            if (match.StatusCode == OnlineMatchStatus.READY.ToString())
                match.StatusCode = OnlineMatchStatus.CREATED.ToString();

            _matchRepo.Update(match);
        }

        await _uow.SaveChangesAsync();

        var response = new MobileTimerDisconnectResponseDto
        {
            Message = "Mobile timer disconnected.",
            MatchId = match.Id,
            PlayerId = userId,
            IsActive = false,
            MatchStatus = match.StatusCode
        };

        await _notifier.NotifyTimerDisconnectedAsync(match.Id, response);
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, "Timer disconnected."));
        return response;
    }
}
