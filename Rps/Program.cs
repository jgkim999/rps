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

// 환경별 설정 파일 추가
var environment = builder.Environment.EnvironmentName;
var environmentFromEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
if (string.IsNullOrWhiteSpace(environmentFromEnv) == false)
    environment = environmentFromEnv;

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(); // 환경 변수가 JSON 설정을 오버라이드

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

    var options = new ConfigurationOptions
    {
        EndPoints = { redisConfig.FusionCacheRedisCache },
        ChannelPrefix = environment
    };

    ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(options);
    builder.Services.AddSingleton(new RedisManager(redis, environment));
    Log.Information("Redis Connection:{IsConnected}", redis.IsConnected); // Should be true
    #endregion

    #region FusionCache

    builder.Services.AddFusionCache()
        .WithSerializer(
            new FusionCacheNewtonsoftJsonSerializer()
        )
        .WithDistributedCache(
            new RedisCache(new RedisCacheOptions { Configuration = redisConfig.FusionCacheRedisCache, InstanceName = environment })
        )
        .WithBackplane(
            new RedisBackplane(new RedisBackplaneOptions { Configuration = redisConfig.FusionCacheBackplane })
        )
        .WithDefaultEntryOptions(new FusionCacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(1),
            JitterMaxDuration = TimeSpan.FromSeconds(10)
        });
    builder.Services.ConfigureAll<FusionCacheOptions>(opts => opts.CacheKeyPrefix = $"{environment}:{opts.CacheName}:");

    #endregion

    // Add services to the container.
    builder.Services.AddRazorPages();

    #region SignalR

    builder.Services.AddSignalR()
        .AddStackExchangeRedis(redisConfig.SignalRBackplane, options =>
        {
            options.Configuration.ConnectRetry = 5;
            options.Configuration.ChannelPrefix = $"{environment}.";
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

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseCors("AllowAll"); // Apply CORS policy

    //app.UseHttpsRedirection();

    app.UseRouting();

    app.UseAuthorization();

    app.MapStaticAssets();

    app.MapRazorPages()
        .WithStaticAssets();

    app.MapHub<ChatHub>("/chathub");

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