using System.Text;
using System.Threading.RateLimiting;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Infrastructure;
using CubeNexus.Infrastructure.Email;
using CubeNexus.Infrastructure.Identity;
using CubeNexus.Infrastructure.Options;
using CubeNexus.Infrastructure.Persistence;
using CubeNexus.Infrastructure.Services;
using CubeNexus.API.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add<ApiExceptionFilter>());
builder.Services.AddEndpointsApiExplorer();

// Swagger - cấu hình nhập token không cần prefix "Bearer"
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "CubeNexus API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Chỉ cần nhập token, không cần thêm 'Bearer' phía trước."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

// Email Settings
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<IEmailService, MailKitEmailService>();

// Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero // Token hết hạn chính xác, không có buffer
    };

    // SignalR truyền token qua query string "access_token" (WebSocket không gửi header)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/hubs/online-arena") || path.StartsWithSegments("/hubs/tournament")))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// Unit of Work (gom tất cả repositories, dùng chung 1 DbContext)
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
// Online Arena UseCases dùng CubeNexus.Application.Interfaces.IUnitOfWork (có transaction methods)
// Resolve cùng instance UnitOfWork đã đăng ký ở trên (cùng scope → cùng DbContext)
builder.Services.AddScoped<CubeNexus.Application.Interfaces.IUnitOfWork>(
    sp => (CubeNexus.Application.Interfaces.IUnitOfWork)sp.GetRequiredService<IUnitOfWork>());

// Services (business logic – inject IUnitOfWork, không phụ thuộc DbContext trực tiếp)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAdminTournamentService, AdminTournamentService>();
builder.Services.AddScoped<IOnlineProfileInitService, OnlineProfileInitService>();
builder.Services.AddScoped<IOnlineArenaService, OnlineArenaService>();
builder.Services.AddScoped<IPuzzleService, PuzzleService>();
builder.Services.AddScoped<IScrambleGeneratorService, ScrambleGeneratorService>();
builder.Services.AddScoped<IPracticeService, PracticeService>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<IOnlineAsyncTournamentService, OnlineAsyncTournamentService>();
builder.Services.AddScoped<ITournamentRegistrationService, TournamentRegistrationService>();
builder.Services.AddScoped<ITournamentOperationService, TournamentOperationService>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICheckInRegistrationByQrUseCase, CubeNexus.Application.UseCases.TournamentOperation.CheckInRegistrationByQrUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IStartRoundUseCase, CubeNexus.Application.UseCases.TournamentOperation.StartRoundUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ILockRoundResultsUseCase, CubeNexus.Application.UseCases.TournamentOperation.LockRoundResultsUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteRoundUseCase, CubeNexus.Application.UseCases.TournamentOperation.CompleteRoundUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteEventUseCase, CubeNexus.Application.UseCases.TournamentOperation.CompleteEventUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteTournamentUseCase, CubeNexus.Application.UseCases.TournamentOperation.CompleteTournamentUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IAdvanceRoundUseCase, CubeNexus.Application.UseCases.TournamentOperation.AdvanceRoundUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IVerifyJudgeStationByStationUseCase, CubeNexus.Application.UseCases.TournamentOperation.VerifyJudgeStationUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICorrectResultUseCase, CubeNexus.Application.UseCases.TournamentOperation.CorrectResultUseCase>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.Services.IRealtimeNotifier, CubeNexus.API.Services.RealtimeNotifier>();

// --- Online Arena services & usecases ---
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IOnlineArenaRealtimeNotifier, CubeNexus.API.Services.OnlineArenaRealtimeNotifier>();
builder.Services.AddScoped<CubeNexus.Domain.Services.IEloCalculator, CubeNexus.Domain.Services.EloCalculator>();
builder.Services.Configure<AiRubikOptions>(builder.Configuration.GetSection(AiRubikOptions.SectionName));
builder.Services.AddOptions<R2Options>()
    .Bind(builder.Configuration.GetSection(R2Options.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.UploadUrlExpirationMinutes == 15, "R2:UploadUrlExpirationMinutes must be 15.")
    .Validate(options => options.PlaybackUrlExpirationMinutes == 60, "R2:PlaybackUrlExpirationMinutes must be 60.");
builder.Services.AddHttpClient<CubeNexus.Application.Interfaces.Services.IAiRubikClient, AiRubikClient>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.Services.IRecordingStorageService, R2RecordingStorageService>();

// Repositories (Since they are created via UnitOfWork or registered individually, let's register the specific repositories for DI if any use case injects them directly)
builder.Services.AddScoped<CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository, CubeNexus.Infrastructure.Repositories.PuzzleTypeRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.Repositories.IOnlineAsyncAttemptRepository, CubeNexus.Infrastructure.Repositories.OnlineAsyncAttemptRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository, CubeNexus.Infrastructure.Repositories.OnlineProfileRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IMatchmakingQueueRepository, CubeNexus.Infrastructure.Repositories.MatchmakingQueueRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IOnlineMatchConfirmationRepository, CubeNexus.Infrastructure.Repositories.OnlineMatchConfirmationRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IOnlineMatchRepository, CubeNexus.Infrastructure.Repositories.OnlineMatchRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IOnlineMatchAiCheckRepository, CubeNexus.Infrastructure.Repositories.OnlineMatchAiCheckRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IOnlineMatchVideoEvidenceRepository, CubeNexus.Infrastructure.Repositories.OnlineMatchVideoEvidenceRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IOnlineMatchAuditLogRepository, CubeNexus.Infrastructure.Repositories.OnlineMatchAuditLogRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IMobileTimerSessionRepository, CubeNexus.Infrastructure.Repositories.MobileTimerSessionRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IEloHistoryRepository, CubeNexus.Infrastructure.Repositories.EloHistoryRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.OnlineArena.IFraudReportRepository, CubeNexus.Infrastructure.Repositories.FraudReportRepository>();
builder.Services.AddScoped<CubeNexus.Application.Interfaces.Repositories.IEloConfigRepository, CubeNexus.Infrastructure.Repositories.EloConfigRepository>();

// UseCases
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.InitOnlineProfileUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetMyOnlineProfilesUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetOnlineLeaderboardUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetMyMatchHistoryUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.FindOnlineMatchUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.CancelMatchmakingUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetMatchmakingStatusUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ConfirmOnlineMatchUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ApplyConfirmationTimeoutUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.MarkCameraReadyUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.MarkWebRtcConnectedUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.MarkVideoRecordingStartedUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.MarkPlayerReadyUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.StartOnlineMatchUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetMatchDetailUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ReconcileOnlineMatchStatusUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.MockOnlineMatchFinishPassUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.CancelActiveMatchUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ConnectMobileTimerUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.DisconnectMobileTimerUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.RunAiRubikCheckUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ValidateScrambleCubeStateUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ValidateFinishCubeStateUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.StartOnlineMatchScannerSessionUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetOnlineMatchScannerSessionUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ObserveOnlineMatchScannerFrameUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.RetryOnlineMatchScannerFaceUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ResetOnlineMatchScannerSessionUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.CompleteOnlineMatchScannerUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.CreateMatchRecordingUploadUrlUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.CompleteMatchRecordingUploadUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.UploadDirectMatchRecordingUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetMatchRecordingPlaybackUrlUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.SubmitOnlineMatchResultUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.CompleteOnlineMatchUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.CreateFraudReportUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetPendingFraudReportsUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetFraudReportDetailUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ReviewFraudReportUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ApplySetupTimeoutUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ApplyReadyTimeoutUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.ApplySolveTimeoutUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.TransitionToSolvingUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.SubmitMobileTimerSolveTimeUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetEloConfigUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.UpdateEloConfigUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetAdminPlayerEloListUseCase>();
builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.AdjustPlayerEloUseCase>();

builder.Services.AddScoped<CubeNexus.Application.UseCases.OnlineArena.GetMatchRecoveryStateUseCase>();
builder.Services.AddSingleton<CubeNexus.Application.UseCases.OnlineArena.IMatchTransitionScheduler, CubeNexus.API.Services.MatchTransitionSchedulerImpl>();

// Background Services
builder.Services.AddHostedService<CubeNexus.API.BackgroundServices.OnlineArenaBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVite", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("AiRubikScannerTest", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "local",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

// Initialize the MatchTransitionScheduler static helper for RAM-based timers
CubeNexus.Application.UseCases.OnlineArena.MatchTransitionScheduler.Instance = 
    app.Services.GetRequiredService<CubeNexus.Application.UseCases.OnlineArena.IMatchTransitionScheduler>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.Environment.EnvironmentName = "Development"; // Just a placeholder
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowVite");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<CubeNexus.API.Hubs.TournamentHub>("/hubs/tournament");
app.MapHub<CubeNexus.API.Hubs.OnlineArenaHub>("/hubs/online-arena");

app.Run();
