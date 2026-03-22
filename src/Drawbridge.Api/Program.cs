using System.Security.Claims;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.S3;
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
builder.Services.AddSingleton<IAmazonS3>(_ =>
    new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region)));
builder.Services.AddSingleton<JobService>();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton<ApsTokenService>();
builder.Services.AddSingleton<AnnotationService>();
builder.Services.AddSingleton<MarkupService>();

builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ── Jobs ──────────────────────────────────────────────────────────────────────

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

// ── Products ──────────────────────────────────────────────────────────────────

app.MapGet("/products", async (ProductService products) =>
    Results.Ok(await products.ListProductsAsync()))
    .RequireAuthorization();

app.MapGet("/products/{partNumber}", async (string partNumber, ProductService products) =>
{
    var product = await products.GetProductWithVersionsAsync(partNumber);
    return product is null ? Results.NotFound() : Results.Ok(product);
}).RequireAuthorization();

// ── Users ─────────────────────────────────────────────────────────────────────

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

// ── APS viewer token ──────────────────────────────────────────────────────────

app.MapGet("/viewer-token", async (ApsTokenService aps) =>
{
    var (accessToken, expiresIn) = await aps.GetViewerTokenAsync();
    return Results.Ok(new { accessToken, expiresIn });
}).RequireAuthorization();

// ── Annotations ───────────────────────────────────────────────────────────────

app.MapGet("/products/{partNumber}/{version}/annotations",
    async (string partNumber, int version, AnnotationService annotations) =>
        Results.Ok(await annotations.ListAsync(partNumber, version)))
    .RequireAuthorization();

app.MapPost("/products/{partNumber}/{version}/annotations",
    async (string partNumber, int version,
           CreateAnnotationBody body, AnnotationService annotations, HttpContext ctx) =>
    {
        if (string.IsNullOrWhiteSpace(body.Text) && string.IsNullOrWhiteSpace(body.MarkupSvg))
            return Results.BadRequest("text or markupSvg is required");
        var email        = ctx.User.FindFirstValue("email") ?? "unknown";
        var mentions     = body.Mentions ?? [];
        var annotationId = Guid.NewGuid().ToString();
        var result = await annotations.CreateAsync(
            partNumber, version,
            body.ComponentIds,
            body.WorldPosition?.X, body.WorldPosition?.Y, body.WorldPosition?.Z,
            body.Text ?? string.Empty, email, body.ViewerState, body.ParentId, body.MarkupSvg,
            mentions, annotationId);
        return Results.Ok(result);
    }).RequireAuthorization();

app.MapPost("/products/{partNumber}/{version}/annotations/{annotationId}/resolve",
    async (string partNumber, int version,
           string annotationId, AnnotationService annotations) =>
    {
        try
        {
            return Results.Ok(await annotations.ToggleResolvedAsync(annotationId));
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }).RequireAuthorization();

app.MapDelete("/products/{partNumber}/{version}/annotations/{annotationId}",
    async (string partNumber, int version,
           string annotationId, AnnotationService annotations, HttpContext ctx) =>
    {
        try
        {
            await annotations.DeleteAsync(annotationId, ctx.User.FindFirstValue("email") ?? "unknown");
            return Results.NoContent();
        }
        catch (KeyNotFoundException)        { return Results.NotFound(); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
    }).RequireAuthorization();

// ── Markups ───────────────────────────────────────────────────────────────────

app.MapGet("/products/{partNumber}/{version}/markups",
    async (string partNumber, int version, MarkupService markups) =>
        Results.Ok(await markups.ListAsync(partNumber, version)))
    .RequireAuthorization();

app.MapPost("/products/{partNumber}/{version}/markups",
    async (string partNumber, int version,
           CreateMarkupBody body, MarkupService markups, HttpContext ctx) =>
    {
        if (string.IsNullOrWhiteSpace(body.PreviewDataUrl))
            return Results.BadRequest("previewDataUrl is required");
        var email = ctx.User.FindFirstValue("email") ?? "unknown";
        var result = await markups.CreateAsync(
            partNumber, version,
            body.PreviewDataUrl, body.MarkupSvg, body.ViewerState ?? string.Empty,
            body.CanvasWidth, body.CanvasHeight, email, body.Title);
        return Results.Ok(result);
    }).RequireAuthorization();

app.MapDelete("/products/{partNumber}/{version}/markups/{markupId}",
    async (string partNumber, int version, string markupId, MarkupService markups, HttpContext ctx) =>
    {
        try
        {
            await markups.DeleteAsync(markupId, partNumber, version,
                ctx.User.FindFirstValue("email") ?? "unknown");
            return Results.NoContent();
        }
        catch (KeyNotFoundException)        { return Results.NotFound(); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
    }).RequireAuthorization();

app.MapPut("/products/{partNumber}/{version}/markups/order",
    async (string partNumber, int version,
           ReorderMarkupsBody body, MarkupService markups) =>
    {
        if (body.Order is null)
            return Results.BadRequest("order is required");
        await markups.ReorderAsync(partNumber, version, body.Order);
        return Results.NoContent();
    }).RequireAuthorization();

app.Run();

// ── Request/response records ──────────────────────────────────────────────────

record OrgUserRecord(string Name, string Email);
record WorldPosition(double X, double Y, double Z);
record CreateAnnotationBody(
    string[]?      ComponentIds,
    WorldPosition? WorldPosition,
    string?        Text,
    string?        ViewerState,
    string?        ParentId,
    string?        MarkupSvg,
    string[]?      Mentions);
record CreateMarkupBody(
    string  PreviewDataUrl,
    string  MarkupSvg,
    string? ViewerState,
    int     CanvasWidth,
    int     CanvasHeight,
    string? Title);
record ReorderMarkupsBody(string[]? Order);
