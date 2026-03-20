using System.Security.Claims;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using Drawbridge.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var region     = builder.Configuration["Api:AwsRegion"]         ?? "us-east-2";
var userPoolId = builder.Configuration["Api:CognitoUserPoolId"] ?? "";
var clientId   = builder.Configuration["Api:CognitoClientId"]   ?? "";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority        = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience    = clientId,
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                var email  = ctx.Principal?.FindFirstValue("email") ?? "";
                var domain = email.Contains('@') ? email.Split('@')[1] : "";
                if (!domain.Equals("thewoweffect.com", StringComparison.OrdinalIgnoreCase))
                    ctx.Fail("Access restricted to thewoweffect.com accounts");
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection("Api"));
builder.Services.AddSingleton<IAmazonSQS>(_ =>
    new AmazonSQSClient(Amazon.RegionEndpoint.GetBySystemName(region)));
builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
    new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(region)));
builder.Services.AddSingleton<JobService>();

builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/jobs", async (SubmitJobRequest req, JobService jobs, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.VaultFilePath))
        return Results.BadRequest("VaultFilePath is required");

    req.SubmittedBy ??= ctx.User.FindFirstValue("email") ?? "unknown";
    var jobId = await jobs.SubmitJobAsync(req);
    return Results.Ok(new { jobId });
});

app.MapGet("/jobs/{jobId}", async (string jobId, JobService jobs) =>
{
    var job = await jobs.GetJobAsync(jobId);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.Run();
