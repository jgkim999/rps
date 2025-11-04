using Rps.Configs;
using Rps.Hubs;

using Serilog;

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
    builder.Host.UseSerilog();

    builder.Services.AddSerilog((services, lc) =>
    {
        lc.ReadFrom.Configuration(builder.Configuration);
        lc.ReadFrom.Services(services);
    });

    Log.Information("Starting application");

    var redisConfig = builder.Configuration.GetSection("Redis").Get<RedisConfig>();
    if (redisConfig is null)
        throw new NullReferenceException();
    builder.Services.Configure<RedisConfig>(builder.Configuration.GetSection("Redis"));

    // Add services to the container.
    builder.Services.AddRazorPages();
    builder.Services.AddSignalR()
        .AddStackExchangeRedis(redisConfig.Backplane);

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();

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