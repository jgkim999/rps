using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Rps;
using Rps.Configs;
using Rps.Hubs;

using Serilog;

using StackExchange.Redis;

using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.NewtonsoftJson;

var builder = WebApplication.CreateBuilder(args);

// 모든 네트워크 인터페이스에서 접속 허용
builder.WebHost.UseUrls("http://0.0.0.0:5184");

// 환경변수 세팅
var environment = builder.Environment.EnvironmentName;
var environmentFromEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
if (string.IsNullOrWhiteSpace(environmentFromEnv) == false)
    environment = environmentFromEnv;

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(); // ȯ�� ������ JSON ������ �������̵�

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    #region SeriLog

    builder.Host.UseSerilog();

    builder.Services.AddSerilog((services, lc) =>
    {
        lc.ReadFrom.Configuration(builder.Configuration);
        lc.ReadFrom.Services(services);
    });
    
    #endregion

    Log.Information("Starting application");

    var redisConfig = builder.Configuration.GetSection("Redis").Get<RedisConfig>();
    if (redisConfig is null)
        throw new NullReferenceException();
    builder.Services.Configure<RedisConfig>(builder.Configuration.GetSection("Redis"));

    #region Redis

    ConfigurationOptions redisOptions = new()
    {
        EndPoints = { redisConfig.FusionCacheRedisCache },
        ChannelPrefix = environment,
        AbortOnConnectFail = false
    };

    if (builder.Environment.IsProduction())
    {
        redisOptions.Ssl = true;
        redisOptions.Password = redisConfig.AuthToken;
    }

    ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);
    builder.Services.AddSingleton(new RedisManager(redis, environment));
    Log.Information("Redis Connection:{IsConnected}", redis.IsConnected); // Should be true

    // Configure Data Protection to use Redis for key storage (multi-instance support)
    builder.Services.AddDataProtection()
        .PersistKeysToStackExchangeRedis(redis, $"{environment}:DataProtection-Keys")
        .SetApplicationName("Rps");

    #endregion

    #region FusionCache

    var redisCacheOptions = new ConfigurationOptions
    {
        EndPoints = { redisConfig.FusionCacheRedisCache },
        AbortOnConnectFail = false
    };
    
    if (builder.Environment.IsProduction())
    {
        redisCacheOptions.Ssl = true;
        redisCacheOptions.Password = redisConfig.AuthToken;
    }

    var redisBackplaneOptions = new ConfigurationOptions
    {
        EndPoints = { redisConfig.FusionCacheBackplane },
        AbortOnConnectFail = false
    };

    if (builder.Environment.IsProduction())
    {
        redisBackplaneOptions.Ssl = true;
        redisBackplaneOptions.Password = redisConfig.AuthToken;
    }

    builder.Services.AddFusionCache()
        .WithSerializer(
            new FusionCacheNewtonsoftJsonSerializer()
        )
        .WithDistributedCache(
            new RedisCache(new RedisCacheOptions { ConfigurationOptions = redisCacheOptions, InstanceName = environment })
        )
        .WithBackplane(
            new RedisBackplane(new RedisBackplaneOptions { ConfigurationOptions = redisBackplaneOptions })
        )
        .WithDefaultEntryOptions(new FusionCacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            JitterMaxDuration = TimeSpan.FromSeconds(10)
        });
    builder.Services.ConfigureAll<FusionCacheOptions>(opts => opts.CacheKeyPrefix = $"{environment}:{opts.CacheName}:");

    #endregion

    // Add services to the container.
    builder.Services.AddRazorPages();
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // Register UserService
    builder.Services.AddScoped<Rps.Services.IUserService, Rps.Services.UserService>();

    // Add Health Checks
    builder.Services.AddHealthChecks();

    // Configure Blazor Server Circuit options for better error handling
    builder.Services.AddServerSideBlazor()
        .AddCircuitOptions(options =>
        {
            options.DetailedErrors = true; //builder.Environment.IsDevelopment();
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
            options.DisconnectedCircuitMaxRetained = 100;
            options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
        });

    #region SignalR

    // 프록시 헤더 설정을 서비스에 추가
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = 
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    });

    builder.Services.AddSignalR(options =>
        {
            // Keep-alive settings for WebSocket connections through ALB
            options.KeepAliveInterval = TimeSpan.FromSeconds(15); // Send ping every 15 seconds
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // Client timeout after 60 seconds
            options.HandshakeTimeout = TimeSpan.FromSeconds(30); // Handshake timeout
        })
        .AddStackExchangeRedis(redisConfig.SignalRBackplane, options =>
        {
            options.Configuration.ConnectRetry = 5;
            options.Configuration.ChannelPrefix = $"{environment}.";
            if (builder.Environment.IsProduction())
            {
                options.Configuration.Ssl = true;
                if (!string.IsNullOrEmpty(redisConfig.AuthToken))
                {
                    options.Configuration.Password = redisConfig.AuthToken;
                }
            }
        });

    #endregion

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll",
            policy => policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());
    });

    var app = builder.Build();

    app.UseForwardedHeaders();

    // Configure the HTTP request pipeline.
    //if (!app.Environment.IsDevelopment())
    //{
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
                var exception = exceptionHandlerPathFeature?.Error;
                
                if (exception != null)
                {
                    Log.Error(exception, "Unhandled exception occurred: {Message}", exception.Message);
                }
                
                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("<html><body><h1>An error occurred</h1></body></html>");
            });
        });
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    //}
    //else
    //{
    //    app.UseDeveloperExceptionPage();
    //}

    app.UseCors("AllowAll"); // Apply CORS policy

    //app.UseHttpsRedirection();

    // WebSocket 지원 활성화 (Blazor Server/SignalR에 필수)
    app.UseWebSockets(new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(120)
    });

    // 정적 파일 서빙 (wwwroot 폴더)
    app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthorization();

    app.UseAntiforgery();

    app.MapStaticAssets();

    app.MapRazorPages()
        .WithStaticAssets();

    app.MapRazorComponents<Rps.Components.App>()
        .AddInteractiveServerRenderMode();

    app.MapHub<GameHub>("/gamehub");

    // Map Health Check endpoint
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
