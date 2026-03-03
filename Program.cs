using U9SyncService;
using Serilog;
using U9SyncService.Db;

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        "logs\\u9sync-.log",
        rollingInterval: RollingInterval.Day)
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
