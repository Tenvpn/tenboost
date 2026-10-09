using TenBoost.Core;
using TenBoost.Service;

var layout = InstallLayout.ForCurrentProcess();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "TenBoost");
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLoggerProvider(layout.LogsDir, "service.log"));
builder.Services.AddSingleton(layout);
builder.Services.AddSingleton<NetworkProbe>();
builder.Services.AddSingleton<EngineSupervisor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EngineSupervisor>());
builder.Services.AddHostedService<ControlServer>();

builder.Build().Run();
