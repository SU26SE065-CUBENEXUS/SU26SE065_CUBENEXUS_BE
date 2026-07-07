using CubeNexus.API.Security;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CubeNexus.API.Hubs;

[Authorize]
public class OnlineArenaHub : Hub
{
    private readonly IOnlineMatchRepository _matchRepository;
    private readonly ILogger<OnlineArenaHub> _logger;

    public OnlineArenaHub(
        IOnlineMatchRepository matchRepository,
        ILogger<OnlineArenaHub> logger)
    {
        _matchRepository = matchRepository;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            _logger.LogWarning("OnlineArenaHub connection rejected due to missing/invalid user id claim. ConnectionId: {ConnectionId}", Context.ConnectionId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetUserGroup(userId));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetCurrentUserId(out var userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetUserGroup(userId));

        if (exception != null)
            _logger.LogDebug(exception, "OnlineArenaHub disconnected with error. ConnectionId: {ConnectionId}", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinMatchRoom(Guid matchId)
    {
        var currentUserId = RequireCurrentUserId();
        var match = await RequireMatchAccessAsync(matchId, currentUserId, allowAdmin: true);

        await Groups.AddToGroupAsync(Context.ConnectionId, GetMatchGroup(matchId));

        var payload = new
        {
            matchId = match.Id,
            userId = currentUserId,
            joinedAt = DateTime.UtcNow
        };

        await Clients.Group(GetMatchGroup(matchId)).SendAsync("MatchJoined", payload);

        // Pre-populate or refresh the cache
        _matchCache[match.Id] = new CachedMatch
        {
            Id = match.Id,
            Player1Id = match.Player1Id,
            Player2Id = match.Player2Id,
            StatusCode = match.StatusCode,
            CachedAt = DateTime.UtcNow
        };
    }

    public Task LeaveMatchRoom(Guid matchId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GetMatchGroup(matchId));

    public async Task SendWebRtcOffer(Guid matchId, Guid targetUserId, string offer)
    {
        if (string.IsNullOrWhiteSpace(offer))
            throw new HubException("offer is required.");

        var currentUserId = RequireCurrentUserId();
        var match = await RequireMatchAccessCachedAsync(matchId, currentUserId, allowAdmin: false);
        ValidateOpponentTarget(match, currentUserId, targetUserId);
        EnsureNonTerminal(match.StatusCode);

        await Clients.Group(GetUserGroup(targetUserId)).SendAsync("WebRtcOfferReceived", new
        {
            matchId,
            fromUserId = currentUserId,
            targetUserId,
            offer
        });
    }

    public async Task SendWebRtcAnswer(Guid matchId, Guid targetUserId, string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            throw new HubException("answer is required.");

        var currentUserId = RequireCurrentUserId();
        var match = await RequireMatchAccessCachedAsync(matchId, currentUserId, allowAdmin: false);
        ValidateOpponentTarget(match, currentUserId, targetUserId);
        EnsureNonTerminal(match.StatusCode);

        await Clients.Group(GetUserGroup(targetUserId)).SendAsync("WebRtcAnswerReceived", new
        {
            matchId,
            fromUserId = currentUserId,
            targetUserId,
            answer
        });
    }

    public async Task SendIceCandidate(Guid matchId, Guid targetUserId, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            throw new HubException("candidate is required.");

        var currentUserId = RequireCurrentUserId();
        var match = await RequireMatchAccessCachedAsync(matchId, currentUserId, allowAdmin: false);
        ValidateOpponentTarget(match, currentUserId, targetUserId);
        EnsureNonTerminal(match.StatusCode);

        await Clients.Group(GetUserGroup(targetUserId)).SendAsync("IceCandidateReceived", new
        {
            matchId,
            fromUserId = currentUserId,
            targetUserId,
            candidate
        });
    }

    private Guid RequireCurrentUserId()
    {
        if (!TryGetCurrentUserId(out var userId))
            throw new HubException("Unauthorized: missing or invalid userId claim.");

        return userId;
    }

    private bool TryGetCurrentUserId(out Guid userId)
        => UserClaimsHelper.TryGetUserId(Context.User, out userId);

    private async Task<CubeNexus.Domain.Entities.OnlineMatch> RequireMatchAccessAsync(Guid matchId, Guid currentUserId, bool allowAdmin)
    {
        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
            throw new HubException("Match not found.");

        var isParticipant = match.Player1Id == currentUserId || match.Player2Id == currentUserId;
        var isAdmin = allowAdmin && UserClaimsHelper.IsAdminOrManager(Context.User);

        if (!isParticipant && !isAdmin)
            throw new HubException("You are not allowed to access this match room.");

        return match;
    }

    // In-memory cache to bypass DB hits during high-frequency signaling (SDP/ICE candidate) exchanges
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CachedMatch> _matchCache = new();

    private class CachedMatch
    {
        public Guid Id { get; set; }
        public Guid Player1Id { get; set; }
        public Guid Player2Id { get; set; }
        public string StatusCode { get; set; } = string.Empty;
        public DateTime CachedAt { get; set; }
    }

    private async Task<CachedMatch> RequireMatchAccessCachedAsync(Guid matchId, Guid currentUserId, bool allowAdmin)
    {
        if (!_matchCache.TryGetValue(matchId, out var cached) || (DateTime.UtcNow - cached.CachedAt).TotalSeconds > 15)
        {
            var match = await _matchRepository.GetByIdAsync(matchId);
            if (match == null)
                throw new HubException("Match not found.");

            cached = new CachedMatch
            {
                Id = match.Id,
                Player1Id = match.Player1Id,
                Player2Id = match.Player2Id,
                StatusCode = match.StatusCode,
                CachedAt = DateTime.UtcNow
            };
            _matchCache[matchId] = cached;
        }

        var isParticipant = cached.Player1Id == currentUserId || cached.Player2Id == currentUserId;
        var isAdmin = allowAdmin && UserClaimsHelper.IsAdminOrManager(Context.User);

        if (!isParticipant && !isAdmin)
            throw new HubException("You are not allowed to access this match room.");

        return cached;
    }

    private static void ValidateOpponentTarget(CachedMatch match, Guid currentUserId, Guid targetUserId)
    {
        if (targetUserId == currentUserId)
            throw new HubException("targetUserId must be the opponent.");

        var expectedOpponentId = match.Player1Id == currentUserId ? match.Player2Id : match.Player1Id;
        if (expectedOpponentId != targetUserId)
            throw new HubException("targetUserId must be the opponent in this match.");
    }

    private static void EnsureNonTerminal(string statusCode)
    {
        if (statusCode is nameof(OnlineMatchStatus.COMPLETED) or nameof(OnlineMatchStatus.DRAW) or nameof(OnlineMatchStatus.CANCELLED))
            throw new HubException($"Match is already terminal ({statusCode}).");
    }

    private static string GetUserGroup(Guid userId) => $"user:{userId}";
    private static string GetMatchGroup(Guid matchId) => $"online-match:{matchId}";
}

