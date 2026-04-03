using U9SyncService;
using Serilog;
using U9SyncService.Db;

// 使用 AppContext.BaseDirectory 程序执行目录
var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "u9sync-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()  // 设置最小日志级别
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
    .CreateLogger();


var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("AppOptions"));
builder.Services.Configure<Databases>(builder.Configuration.GetSection("Databases"));

//  清理默认日志（包含 EventLog）
//builder.Logging.ClearProviders();

builder.Logging.AddSerilog();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "U9SyncService";
});



builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddSingleton(typeof(IRepository<>),typeof(Repository<>));
builder.Services.AddSingleton<ICRMSyncService,CRMSyncService>();

builder.Services.AddHostedService<Worker>();


var host = builder.Build();
host.Run();
