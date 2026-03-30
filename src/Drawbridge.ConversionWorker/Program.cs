using Drawbridge.ConversionWorker;
using Drawbridge.ConversionWorker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Worker"));
builder.Services.AddSingleton<S3Service>();
builder.Services.AddSingleton<DynamoService>();
builder.Services.AddSingleton<ApsService>();
builder.Services.AddHostedService<ConversionWorkerService>();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Drawbridge Conversion Worker";
});

var host = builder.Build();
host.Run();
