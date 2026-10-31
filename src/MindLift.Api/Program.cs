using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MindLift.Api.Auth;
using MindLift.Api.Filters;
using MindLift.Api.Hubs;
using MindLift.Api.Middleware;
using MindLift.Infrastructure;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.Infrastructure.Tenancy;
using Scalar.AspNetCore;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Cloud hosting port binding: if PORT is set (e.g. on Render), bind Kestrel to it on all interfaces.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Logs: always to the console; also to daily files (Logging:FilePath) and to a Seq server (Seq:ServerUrl) when set. Every line of a request
// carries its correlation id, company and user (see RequestLogContextMiddleware), so one customer's problem can be found among all the others.
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "MindLift")
        .WriteTo.Console();

    if (context.Configuration["Logging:FilePath"] is { Length: > 0 } logPath)
    {
        configuration.WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(), logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
    }

    if (context.Configuration["Seq:ServerUrl"] is { Length: > 0 } seqUrl)
    {
        configuration.WriteTo.Seq(seqUrl, apiKey: context.Configuration["Seq:ApiKey"]);
    }
});

// Error tracking: every unexpected error is sent to Sentry when Sentry:Dsn is set, with the request and the company it happened in.
if (builder.Configuration["Sentry:Dsn"] is { Length: > 0 } sentryDsn)
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = builder.Environment.EnvironmentName;
        options.SendDefaultPii = false;   // no personal data leaves the server
        options.TracesSampleRate = builder.Configuration.GetValue<double?>("Sentry:TracesSampleRate") ?? 0.0;
        options.SetBeforeSend((sentryEvent, hint) =>
        {
            if (sentryEvent.Request is { } req)
            {
                if (req.Url != null)
                {
                    req.Url = System.Text.RegularExpressions.Regex.Replace(req.Url, @"/manage/[^/?]+", "/manage/[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    req.Url = System.Text.RegularExpressions.Regex.Replace(req.Url, @"([?&]token=)[^&]+", "$1[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                if (req.QueryString != null)
                {
                    req.QueryString = System.Text.RegularExpressions.Regex.Replace(req.QueryString, @"([?&]token=)[^&]+", "$1[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
            return sentryEvent;
        });
    });
}

var rawConnectionString = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
var connectionString = MindLift.Infrastructure.DependencyInjection.NormalizeConnectionString(rawConnectionString);

// Override ConnectionStrings:Default with normalized Key=Value format so EF Core, Npgsql, health checks,
// and migrations all receive standard Key=Value parameters instead of raw postgresql:// URIs.
if (!string.IsNullOrEmpty(connectionString) && connectionString != rawConnectionString)
{
    builder.Configuration["ConnectionStrings:Default"] = connectionString;
}

var hangfireConnectionString = connectionString;
var jwt = builder.Configuration.GetSection("Jwt").Get<MindLift.Infrastructure.JwtOptions>() ?? new();

builder.Services.AddMindLiftInfrastructure(builder.Configuration);

// Authentication: JWT bearer. Claims keep their names (sub, tid, plat), so MapInboundClaims is off.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/hubs") && context.Request.Query["access_token"] is { Count: > 0 } token)
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirst("sub")?.Value;
                if (!Guid.TryParse(sub, out var userId))
                {
                    context.Fail("invalid_token");
                    return;
                }

                var stateCache = context.HttpContext.RequestServices.GetRequiredService<MindLift.Infrastructure.Caching.RequestStateCache>();
                var userState = await stateCache.UserAsync(userId, context.HttpContext.RequestAborted);
                if (userState is null || !userState.IsActive)
                {
                    context.Fail("user_deactivated");
                    return;
                }

                var claimsPlat = context.Principal?.FindFirst("plat")?.Value is "true";
                if (claimsPlat && !userState.IsPlatformAdmin)
                {
                    context.Fail("platform_admin_revoked");
                    return;
                }
            },
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    })
    .AddJwtBearer("Portal", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = "mindlift-portal",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal == null)
                {
                    context.Fail("Principal is missing.");
                    return;
                }

                var sub = principal.FindFirst("sub")?.Value;
                var tid = principal.FindFirst("tid")?.Value;
                if (!Guid.TryParse(sub, out var portalUserId) || !Guid.TryParse(tid, out var tenantId))
                {
                    context.Fail("Invalid subject or tenant claim.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                var user = await db.PortalUsers.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == portalUserId && u.TenantId == tenantId && !u.IsDeleted);

                if (user is null || !user.IsActive)
                {
                    context.Fail("Portal user is deactivated or deleted.");
                    return;
                }

                // Linked contact must exist and not be deleted
                var contactExists = await db.Contacts.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(c => c.Id == user.ContactId && c.TenantId == tenantId && !c.IsDeleted);
                if (!contactExists)
                {
                    context.Fail("Linked contact is deleted or inactive.");
                    return;
                }

                // Portal must be enabled for this tenant
                var portalModule = await db.ModuleSettings.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.TenantId == tenantId && m.Module == MindLift.Shared.Enums.AppModule.Portal && !m.IsDeleted)
                    .Select(m => (bool?)m.IsEnabled)
                    .FirstOrDefaultAsync();
                if (portalModule != true)
                {
                    context.Fail("Customer portal module is disabled.");
                    return;
                }

                var portalSettings = await db.PortalSettings.IgnoreQueryFilters().AsNoTracking()
                    .Where(s => s.TenantId == tenantId && !s.IsDeleted)
                    .Select(s => (bool?)s.IsEnabled)
                    .FirstOrDefaultAsync() ?? true;
                if (!portalSettings)
                {
                    context.Fail("Customer portal is disabled for this company.");
                    return;
                }

                // Check portal session version claim against the session version (bumped only on sign-out, password change or deactivation)
                var verStr = principal.FindFirst("ver")?.Value;
                if (!int.TryParse(verStr, out var tokenVer) || tokenVer != user.SessionVersion)
                {
                    context.Fail("Portal session version mismatch. Token has been revoked.");
                    return;
                }

                // Check password change / session termination version
                if (user.PasswordChangedAt.HasValue)
                {
                    var iatStr = principal.FindFirst("iat")?.Value;
                    if (long.TryParse(iatStr, out var iatSeconds))
                    {
                        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatSeconds).UtcDateTime;
                        if (issuedAt < user.PasswordChangedAt.Value.AddSeconds(-2))
                        {
                            context.Fail("Session invalidated due to password change or account update.");
                            return;
                        }
                    }
                }

                // Derive contact/account authorization identifiers from the validated server-side PortalUser
                if (principal.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    var existingCid = identity.FindFirst("cid");
                    if (existingCid != null) identity.RemoveClaim(existingCid);
                    identity.AddClaim(new System.Security.Claims.Claim("cid", user.ContactId.ToString()));

                    var existingAid = identity.FindFirst("aid");
                    if (existingAid != null) identity.RemoveClaim(existingAid);
                    if (user.AccountId.HasValue)
                    {
                        identity.AddClaim(new System.Security.Claims.Claim("aid", user.AccountId.Value.ToString()));
                    }
                }
            }
        };
    });

// Authorization: permission policies (perm:{module}:{action}) and the platform-admin policy.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdmin", policy => policy.RequireAuthenticatedUser().RequireClaim("plat", "true"));
    options.AddPolicy("PortalUser", policy => policy.AddAuthenticationSchemes("Portal").RequireAuthenticatedUser().RequireClaim("kind", "portal"));
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ModuleHandler>();

// FluentValidation: register every AbstractValidator<T> in the API assembly.
foreach (var type in typeof(Program).Assembly.GetTypes().Where(t =>
             !t.IsAbstract && t.BaseType is { IsGenericType: true } baseType
             && baseType.GetGenericTypeDefinition() == typeof(FluentValidation.AbstractValidator<>)))
{
    var argument = type.BaseType!.GetGenericArguments()[0];
    builder.Services.AddScoped(typeof(IValidator<>).MakeGenericType(argument), type);
}

builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options => MindLift.Api.Hosting.ForwardedHeadersSetup.Configure(options, builder.Configuration));
builder.Services.AddControllers(options => options.Filters.Add<ValidationActionFilter>());

// Rate limits per client address. "public" covers sign-in, sign-up, magic links, public booking and website forms; "webhook" covers provider callbacks.
// Both are configurable, and the limits are per minute.
var publicPermits = builder.Configuration.GetValue<int?>("RateLimiting:PublicPermitsPerMinute") ?? 120;
var webhookPermits = builder.Configuration.GetValue<int?>("RateLimiting:WebhookPermitsPerMinute") ?? 1200;
var uploadPermits = builder.Configuration.GetValue<int?>("RateLimiting:UploadPermitsPerMinute") ?? 30;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = publicPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("webhook", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = webhookPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("uploads", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.User?.FindFirst("tenant_id")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = uploadPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
// Live notifications. With more than one server, Redis carries each message to the server the person is connected to.
var signalR = builder.Services.AddSignalR();
if (builder.Configuration["Redis:ConnectionString"] is { Length: > 0 } redisConnection)
{
    signalR.AddStackExchangeRedis(redisConnection, options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("mindlift"));
}
builder.Services.AddSingleton<MindLift.Application.Abstractions.INotificationPusher, MindLift.Api.Hubs.SignalRNotificationPusher>();

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "database")
    .AddCheck<MindLift.Infrastructure.Cpq.OcrHealthCheck>("ocr");

// Hangfire: one storage object per application, handed to the job client and the worker explicitly.
// Hangfire's global storage is shared by every application in the process, so several hosts (the tests run many) would send jobs to one another's database.
var hangfireStorage = new PostgreSqlStorage(hangfireConnectionString, new PostgreSqlStorageOptions
{
    QueuePollInterval = TimeSpan.FromSeconds(1),
    TransactionSynchronisationTimeout = TimeSpan.FromSeconds(5),
    InvisibilityTimeout = TimeSpan.FromMinutes(5),
    UseSlidingInvisibilityTimeout = true,
    PrepareSchemaIfNecessary = true,
    EnableTransactionScopeEnlistment = true
});
GlobalConfiguration.Configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseStorage(hangfireStorage);
builder.Services.AddHangfire(config => config.UseStorage(hangfireStorage));
builder.Services.AddSingleton<JobStorage>(hangfireStorage);
builder.Services.AddSingleton<IBackgroundJobClient>(_ => new BackgroundJobClient(hangfireStorage));
// Background work runs in this process unless Hangfire:ServerEnabled is false. Hangfire:Queues picks which work this process takes, so heavy work
// (ocr, reports, imports) can run in a separate worker container while the web server keeps only the light queues (see docker-compose.prod.yml).
var hangfireQueues = (builder.Configuration["Hangfire:Queues"] ?? "default,messaging,reports,imports,ocr")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (builder.Configuration.GetValue<bool?>("Hangfire:ServerEnabled") ?? true)
builder.Services.AddSingleton<IHostedService>(sp => new MindLift.Api.Hosting.HangfireServerHost(hangfireStorage, new BackgroundJobServerOptions
{
    Queues = hangfireQueues,

    // Hangfire's own default (5 per CPU, up to 20) stays for real runs. Tests lower it so many hosts do not exhaust the database.
    WorkerCount = builder.Configuration.GetValue<int?>("Hangfire:WorkerCount") ?? Math.Min(Environment.ProcessorCount * 5, 20),
    Activator = sp.GetRequiredService<JobActivator>()
}));
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy =>
        policy.SetIsOriginAllowed(origin => new Uri(origin).Host is "localhost" or "127.0.0.1")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());

    if (allowedOrigins.Length > 0)
    {
        options.AddPolicy("prod", policy =>
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials());
    }
});

var app = builder.Build();

// Command line: dotnet run --project src/MindLift.Api -- seed-platform-admin --email ... --password ...
if (args.Length > 0 && args[0] == "seed-platform-admin")
{
    string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

    using var setupScope = app.Services.CreateScope();
    var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await setupDb.Database.MigrateAsync();
    await PlanCatalog.EnsureAsync(setupDb);

    var seeder = setupScope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>();
    var message = await seeder.SeedAsync(
        Arg("--email") ?? throw new ArgumentException("--email is required."),
        Arg("--password") ?? throw new ArgumentException("--password is required."),
        Arg("--first") ?? "Platform",
        Arg("--last") ?? "Admin");
    Console.WriteLine(message);
    return;
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Several servers may start together. A database-wide lock lets one of them migrate while the others wait, instead of all of them at once.
    await MindLift.Infrastructure.Persistence.MigrationLock.RunAsync(db, async () =>
    {
        await db.Database.MigrateAsync();
        await PlanCatalog.EnsureAsync(db);
    });

    // Webhook secrets saved before they were encrypted are converted once.
    await MindLift.Infrastructure.Communication.WebhookSecrets.SealAllAsync(db, scope.ServiceProvider.GetRequiredService<MindLift.Infrastructure.Communication.ISecretProtector>());
}

// Behind a reverse proxy this makes the client address and scheme the real ones. Off unless ForwardedHeaders:Enabled is set.
if (!MindLift.Api.Hosting.ForwardedHeadersSetup.IsEnabled(app.Configuration) && app.Environment.IsProduction())
{
    app.Logger.LogWarning("ForwardedHeaders:Enabled is false. Behind a reverse proxy every visitor shares the proxy's address, so one busy client can use up the sign-in limit for everyone. Set FORWARDED_HEADERS_ENABLED=true and the proxy address.");
}

if (MindLift.Api.Hosting.ForwardedHeadersSetup.IsEnabled(app.Configuration))
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// In non-development environments, refuse plaintext HTTP requests on authentication and portal endpoints.
app.Use(async (context, next) =>
{
    var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString();
    var isHttps = context.Request.IsHttps || string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
    var isPlaintextHttp = !isHttps;

    if (!app.Environment.IsDevelopment() && isPlaintextHttp)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
            (path.StartsWith("/api/v1/portal/", StringComparison.OrdinalIgnoreCase) && path.Contains("/auth/", StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.io/400",
                title = "HTTPS Required",
                status = 400,
                detail = "HTTPS is required for authentication endpoints."
            });
            return;
        }
    }
    await next();
});

app.UseMiddleware<MindLift.Api.Middleware.CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (path.Contains("/booking/manage", StringComparison.OrdinalIgnoreCase) || path.Contains("/manage/", StringComparison.OrdinalIgnoreCase))
        {
            return Serilog.Events.LogEventLevel.Verbose;
        }
        return ex != null || httpContext.Response.StatusCode > 499
            ? Serilog.Events.LogEventLevel.Error
            : Serilog.Events.LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagnostics, http) =>
    {
        var rawPath = http.Request.Path.Value ?? string.Empty;
        var rawQuery = http.Request.QueryString.Value ?? string.Empty;
        var safePath = System.Text.RegularExpressions.Regex.Replace(rawPath, @"/manage/[^/?]+", "/manage/[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var safeQuery = System.Text.RegularExpressions.Regex.Replace(rawQuery, @"([?&]token=)[^&]+", "$1[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        diagnostics.Set("RequestPath", $"{safePath}{safeQuery}");
        diagnostics.Set("CorrelationId", http.TraceIdentifier);
        diagnostics.Set("TenantId", http.User.FindFirst("tid")?.Value ?? "-");
        diagnostics.Set("UserId", http.User.FindFirst("sub")?.Value ?? "-");
    };
});
app.UseExceptionHandler();

// Baseline browser protections for every response. Framing is refused and MIME sniffing is off.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Off unless Hangfire:DashboardEnabled is set. When on, only requests from this machine are answered.
    if (app.Configuration.GetValue<bool>("Hangfire:DashboardEnabled"))
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new MindLift.Api.Hosting.LocalOnlyDashboardFilter()] });
    }

    // Development only. After a rebuild the browser must not reuse an old Blazor boot file, or the app shows a blank page.
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path == "/" || path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-cache, no-store";
                return Task.CompletedTask;
            });
        }

        await next();
    });
}
else if (allowedOrigins.Length > 0)
{
    app.UseCors("prod");
}

// Static files & modern .NET 10 static assets (replaces UseBlazorFrameworkFiles)
app.UseStaticFiles();

// Direct fallback handler for un-fingerprinted blazor.webassembly.js requests
app.MapGet("/_framework/blazor.webassembly.js", (IWebHostEnvironment env) =>
{
    var webRoot = env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
    var frameworkDir = Path.Combine(webRoot, "_framework");
    if (Directory.Exists(frameworkDir))
    {
        var file = Directory.GetFiles(frameworkDir, "blazor.webassembly.*.js")
            .FirstOrDefault(f => !f.EndsWith(".gz") && !f.EndsWith(".br"));
        if (file != null)
        {
            return Results.File(file, "text/javascript");
        }
    }
    return Results.NotFound();
}).AllowAnonymous();

app.MapGet("/portal/_framework/blazor.webassembly.js", (IWebHostEnvironment env) =>
{
    var webRoot = env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
    var frameworkDir = Path.Combine(webRoot, "portal", "_framework");
    if (!Directory.Exists(frameworkDir))
    {
        frameworkDir = Path.Combine(webRoot, "_framework");
    }
    if (Directory.Exists(frameworkDir))
    {
        var file = Directory.GetFiles(frameworkDir, "blazor.webassembly.*.js")
            .FirstOrDefault(f => !f.EndsWith(".gz") && !f.EndsWith(".br"));
        if (file != null)
        {
            return Results.File(file, "text/javascript");
        }
    }
    return Results.NotFound();
}).AllowAnonymous();

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<MindLift.Api.Middleware.RequestLogContextMiddleware>();
app.UseMiddleware<MindLift.Api.Middleware.SupportSessionMiddleware>();
app.UseMiddleware<SubscriptionGuardMiddleware>();
app.UseAuthorization();
app.UseMiddleware<MindLift.Api.Middleware.MustChangePasswordMiddleware>();

app.MapStaticAssets();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHealthChecks("/health");
// For a load balancer or uptime monitor: "live" says the process answers, "ready" says it can also reach the database.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = check => check.Name == "database" });

// Unknown API and hub routes return 404 (never the SPA page).
app.MapFallback("/api/{**rest}", context => { context.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; });
app.MapFallback("/hubs/{**rest}", context => { context.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; });

// Customer portal fallback:
app.MapFallbackToFile("/portal", "portal/index.html");
app.MapFallbackToFile("/portal/{*path:nonfile}", "portal/index.html");

// SPA fallback: every other path returns index.html so the Blazor router can handle it.
app.MapFallbackToFile("index.html");

// Daily trial reminders and expiry (plan 6.9).
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Trial.TrialExpiryJob>("trial-expiry", job => job.RunAsync(CancellationToken.None), Cron.Daily(6));
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Auth.RefreshTokenCleanupJob>("refresh-token-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily(3));

// Reminders and notification housekeeping (plan 7.11).
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Notifications.ActivityReminderJob>("activity-reminders", job => job.RunAsync(CancellationToken.None), Cron.Minutely());
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Notifications.OverdueTaskJob>("overdue-tasks", job => job.RunAsync(CancellationToken.None), "*/15 * * * *");
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Notifications.DailyDigestJob>("daily-digest", job => job.RunAsync(CancellationToken.None), Cron.Hourly());
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Bookings.BookingReminderJob>("booking-reminders", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Notifications.NotificationCleanupJob>("notification-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily(3, 30));
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.CalendarSync.CalendarSyncJob>("calendar-sync", job => job.RunAsync(CancellationToken.None), "*/10 * * * *");
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Campaigns.CampaignScheduleJob>("campaign-schedule", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Billing.BillingRenewalJob>("billing-renewals", job => job.RunAsync(CancellationToken.None), Cron.Daily(5));
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Persistence.AuditRetentionJob>("audit-retention", job => job.RunAsync(CancellationToken.None), Cron.Daily(2));
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Cpq.DocumentExpiryJob>("document-expiry", job => job.RunAsync(CancellationToken.None), Cron.Daily(4));
new RecurringJobManager(hangfireStorage).AddOrUpdate<MindLift.Infrastructure.Platform.IdempotencyCleanupJob>("idempotency-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily(3, 45));

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
