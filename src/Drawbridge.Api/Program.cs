using System.Security.Claims;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using Drawbridge.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
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
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(_ =>
    new AmazonCognitoIdentityProviderClient(Amazon.RegionEndpoint.GetBySystemName(region)));
builder.Services.AddSingleton<JobService>();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton<ApsTokenService>();

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

app.MapGet("/products", async (ProductService products) =>
    Results.Ok(await products.ListProductsAsync()))
    .RequireAuthorization();

app.MapGet("/products/{partNumber}", async (string partNumber, ProductService products) =>
{
    var product = await products.GetProductWithVersionsAsync(partNumber);
    return product is null ? Results.NotFound() : Results.Ok(product);
}).RequireAuthorization();

app.MapGet("/users", async (IAmazonCognitoIdentityProvider cognito, IOptions<ApiSettings> settings) =>
{
    var poolId = settings.Value.CognitoUserPoolId;
    var users  = new List<OrgUserRecord>();
    string? token = null;
    do
    {
        var res = await cognito.ListUsersAsync(new ListUsersRequest
        {
            UserPoolId      = poolId,
            AttributesToGet = ["email", "name"],
            Limit           = 60,
            PaginationToken = token,
        });
        foreach (var u in res.Users)
        {
            var email = u.Attributes.FirstOrDefault(a => a.Name == "email")?.Value;
            var name  = u.Attributes.FirstOrDefault(a => a.Name == "name")?.Value;
            if (!string.IsNullOrEmpty(email))
                users.Add(new OrgUserRecord(name ?? email, email));
        }
        token = res.PaginationToken;
    } while (!string.IsNullOrEmpty(token));

    return Results.Ok(users.OrderBy(u => u.Name));
}).RequireAuthorization();

app.MapGet("/viewer-token", async (ApsTokenService aps) =>
{
    var (accessToken, expiresIn) = await aps.GetViewerTokenAsync();
    return Results.Ok(new { accessToken, expiresIn });
}).RequireAuthorization();

app.Run();

record OrgUserRecord(string Name, string Email);
