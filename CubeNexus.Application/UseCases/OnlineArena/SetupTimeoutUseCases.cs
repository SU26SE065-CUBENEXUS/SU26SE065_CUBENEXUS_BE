using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

/// <summary>
/// Xử lý timeout setup phase (setupDeadlineAt hết mà chưa BothChecklistPassed).
/// - match CANCELLED, không tính Elo
/// - player gây kẹt bị cooldown matchmaking (lưu ở profile)
/// - player không gây lỗi được queue lại ngay
/// - IDEMPOTENT: dùng SetupTimeoutPenaltyAppliedAt để tránh apply trùng
/// </summary>
public class ApplySetupTimeoutUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public ApplySetupTimeoutUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineProfileRepository profileRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _profileRepo = profileRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task ExecuteAsync(OnlineMatch match, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // IDEMPOTENCY: đã apply rồi thì bỏ qua
        if (match.SetupTimeoutPenaltyAppliedAt.HasValue)
            return;
        if (match.StatusCode == nameof(OnlineMatchStatus.CANCELLED))
            return;

        // Xác định ai chưa pass checklist
        var p1Passed = OnlineArenaFlowHelpers.IsChecklistPassed(match, true);
        var p2Passed = OnlineArenaFlowHelpers.IsChecklistPassed(match, false);

        Guid? timeoutPlayerId = null;
        if (!p1Passed && p2Passed) timeoutPlayerId = match.Player1Id;
        else if (!p2Passed && p1Passed) timeoutPlayerId = match.Player2Id;
        // Cả hai chưa pass → timeout cả 2

        // Cancel match
        match.StatusCode = nameof(OnlineMatchStatus.CANCELLED);
        match.Phase = "CANCELLED";
        match.Outcome = nameof(OnlineMatchOutcome.CANCELLED);
        match.CancelReason = "SETUP_TIMEOUT";
        match.TimeoutPlayerId = timeoutPlayerId;
        match.EloChanged = false; // Setup timeout KHÔNG tính Elo
        match.EndedAt = now;
        match.SetupTimeoutPenaltyAppliedAt = now; // idempotency stamp

        _matchRepo.Update(match);

        // Apply cooldown cho player(s) gây timeout
        if (!p1Passed)
            await ApplyCooldownAsync(match.Player1Id, match.Player1ProfileId, match.Id, ct);

        if (!p2Passed)
            await ApplyCooldownAsync(match.Player2Id, match.Player2ProfileId, match.Id, ct);

        await _uow.SaveChangesAsync(ct);

        // Notify
        var payload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Setup timeout. Match cancelled.");

        await _notifier.NotifySetupTimeoutAsync(match.Id, payload);
        await _notifier.NotifyMatchCancelledAsync(match.Id, payload);

        // Notify cooldown player(s)
        if (!p1Passed)
        {
            var p1Profile = await _profileRepo.GetByUserIdAsync(match.Player1Id);
            await _notifier.NotifyMatchmakingCooldownAppliedAsync(match.Player1Id, new
            {
                matchId = match.Id,
                reason = "SETUP_TIMEOUT",
                cooldownUntil = p1Profile?.MatchmakingCooldownUntil,
                serverNow = DateTime.UtcNow
            });
        }
        if (!p2Passed)
        {
            var p2Profile = await _profileRepo.GetByUserIdAsync(match.Player2Id);
            await _notifier.NotifyMatchmakingCooldownAppliedAsync(match.Player2Id, new
            {
                matchId = match.Id,
                reason = "SETUP_TIMEOUT",
                cooldownUntil = p2Profile?.MatchmakingCooldownUntil,
                serverNow = DateTime.UtcNow
            });
        }
    }

    private async Task ApplyCooldownAsync(Guid userId, Guid profileId, Guid matchId, CancellationToken ct)
    {
        var profile = await _profileRepo.GetByUserIdAsync(userId);
        if (profile == null) return;

        var now = DateTime.UtcNow;

        profile.SetupTimeoutCount++;
        profile.LastSetupTimeoutAt = now;
        if (!profile.SetupTimeoutWindowStartedAt.HasValue)
            profile.SetupTimeoutWindowStartedAt = now;

        var cooldownDuration = OnlineArenaFlowHelpers.GetCooldownDuration(profile.SetupTimeoutCount);
        profile.MatchmakingCooldownUntil = now.Add(cooldownDuration);

        _profileRepo.Update(profile);
    }
}

/// <summary>
/// Xử lý timeout ready phase (readyDeadlineAt hết mà chưa BothPlayerReady).
/// - match CANCELLED, không tính Elo
/// - player chưa bấm Ready bị cooldown
/// - IDEMPOTENT: check CancelReason + StatusCode
/// </summary>
public class ApplyReadyTimeoutUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public ApplyReadyTimeoutUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineProfileRepository profileRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _profileRepo = profileRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task ExecuteAsync(OnlineMatch match, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // IDEMPOTENCY
        if (match.SetupTimeoutPenaltyAppliedAt.HasValue) return;
        if (match.StatusCode == nameof(OnlineMatchStatus.CANCELLED)) return;

        // Xác định ai chưa bấm Ready
        var p1Ready = match.Player1Ready;
        var p2Ready = match.Player2Ready;

        Guid? timeoutPlayerId = null;
        if (!p1Ready && p2Ready) timeoutPlayerId = match.Player1Id;
        else if (!p2Ready && p1Ready) timeoutPlayerId = match.Player2Id;

        match.StatusCode = nameof(OnlineMatchStatus.CANCELLED);
        match.Phase = "CANCELLED";
        match.Outcome = nameof(OnlineMatchOutcome.CANCELLED);
        match.CancelReason = "READY_TIMEOUT";
        match.TimeoutPlayerId = timeoutPlayerId;
        match.EloChanged = false;
        match.EndedAt = now;
        match.SetupTimeoutPenaltyAppliedAt = now;

        _matchRepo.Update(match);

        if (!p1Ready)
            await ApplyCooldownAsync(match.Player1Id, ct);

        if (!p2Ready)
            await ApplyCooldownAsync(match.Player2Id, ct);

        await _uow.SaveChangesAsync(ct);

        var payload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Ready timeout. Match cancelled.");
        await _notifier.NotifyReadyTimeoutAsync(match.Id, payload);
        await _notifier.NotifyMatchCancelledAsync(match.Id, payload);
    }

    private async Task ApplyCooldownAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _profileRepo.GetByUserIdAsync(userId);
        if (profile == null) return;

        var now = DateTime.UtcNow;
        profile.SetupTimeoutCount++;
        profile.LastSetupTimeoutAt = now;
        if (!profile.SetupTimeoutWindowStartedAt.HasValue)
            profile.SetupTimeoutWindowStartedAt = now;

        profile.MatchmakingCooldownUntil = now.Add(
            OnlineArenaFlowHelpers.GetCooldownDuration(profile.SetupTimeoutCount));

        _profileRepo.Update(profile);
    }
}

/// <summary>
/// Xử lý timeout solve phase (solveDeadlineAt hết mà player chưa submit).
/// - Trận đã bắt đầu → có thể tính kết quả/Elo
/// - Player chưa submit bị tính DNF tự động
/// - Không apply cooldown matchmaking (đây là solve phase, không phải setup)
/// - IDEMPOTENT: chỉ apply DNF nếu ResultStatus vẫn PENDING
/// </summary>
public class ApplySolveTimeoutUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeMatchUseCase;

    public ApplySolveTimeoutUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeMatchUseCase)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
        _completeMatchUseCase = completeMatchUseCase;
    }

    public async Task ExecuteAsync(OnlineMatch match, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // IDEMPOTENCY: nếu cả hai đã submit rồi thì bỏ qua
        if (match.Player1ResultStatus != nameof(PlayerResultStatus.PENDING)
            && match.Player2ResultStatus != nameof(PlayerResultStatus.PENDING))
            return;

        bool changed = false;

        if (match.Player1ResultStatus == nameof(PlayerResultStatus.PENDING))
        {
            match.Player1ResultStatus = nameof(PlayerResultStatus.DNF);
            match.Player1IsDnf = true;
            match.Player1FinishedAt = now;
            match.Player1FinishCheckStatus = "NOT_REQUIRED";
            changed = true;
        }

        if (match.Player2ResultStatus == nameof(PlayerResultStatus.PENDING))
        {
            match.Player2ResultStatus = nameof(PlayerResultStatus.DNF);
            match.Player2IsDnf = true;
            match.Player2FinishedAt = now;
            match.Player2FinishCheckStatus = "NOT_REQUIRED";
            changed = true;
        }

        if (!changed) return;

        // The five-minute solve timeout ends the match immediately, so neither
        // player needs to complete a finish scan after this point.
        match.Player1FinishCheckStatus = "NOT_REQUIRED";
        match.Player2FinishCheckStatus = "NOT_REQUIRED";

        // A solve timeout is final: pending players are DNF and the match is
        // completed immediately. VALID vs DNF is a win; DNF vs DNF is a draw.
        // There is no PENDING_EVIDENCE phase after the five-minute deadline.
        _matchRepo.Update(match);
        await _uow.SaveChangesAsync(ct);

        await _completeMatchUseCase.ExecuteAsync(match.Id);
        var completed = await _matchRepo.GetByIdAsync(match.Id) ?? match;
        var payload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(completed, "Solve timeout. Match completed.");
        await _notifier.NotifySolveTimeoutAsync(match.Id, payload);
    }
}

/// <summary>
/// Transition từ INSPECTION → SOLVING.
/// Called by BackgroundService khi inspectionDeadlineAt hết.
/// </summary>
public class TransitionToSolvingUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public TransitionToSolvingUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task ExecuteAsync(OnlineMatch match, CancellationToken ct = default)
    {
        // IDEMPOTENCY
        if (match.Phase != "INSPECTION") return;
        if (match.SolveDeadlineAt.HasValue) return;

        var now = DateTime.UtcNow;
        match.Phase = "SOLVING";
        match.TimeLimitMs = OnlineMatch.DefaultSolveTimeLimitMs;
        match.SolveDeadlineAt = now.AddMilliseconds(OnlineMatch.DefaultSolveTimeLimitMs);

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync(ct);

        await _notifier.NotifySolveStartedAsync(match.Id,
            OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Inspection ended. Solve started."));
    }
}
