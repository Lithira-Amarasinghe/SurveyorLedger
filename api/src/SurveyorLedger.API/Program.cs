using Azure.Communication.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using SurveyorLedger.API.Services;
using SurveyorLedger.API.Middleware;
using SurveyorLedger.Data;

var builder = WebApplication.CreateBuilder(args);

// String enum JSON conversion, registered once here rather than per-DTO with
// [JsonConverter] attributes - every enum in this API (DocumentCategory, DocumentVisibility,
// SubscriptionTier, etc) round-trips as its name, matching what every client (this API's own
// TypeScript interfaces) expects on both request bodies and response payloads.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Model validation failures ([Required], [RegularExpression], etc.) go through the same
// ApiResponse envelope as every other error instead of ASP.NET's default ValidationProblemDetails.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
            SurveyorLedger.API.Models.Responses.ApiResponse<object>.Fail("Validation failed", errors));
    };
});

// Per-IP throttle on the auth endpoints. This is the other half of brute-force defence
// from the per-account lockout in AuthService: lockout stops someone hammering one
// account, this stops spraying many accounts from one source and stops the OTP/
// forgot-password endpoints being used to bomb an inbox. Built-in limiter, no dependency.
const string AuthRateLimitPolicy = "auth";
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10),
                Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimiting:AuthWindowMinutes", 1)),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            SurveyorLedger.API.Models.Responses.ApiResponse<object>.Fail("Too many requests. Please wait a moment and try again."),
            cancellationToken);
    };
});

const string UiCorsPolicy = "UiCorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(UiCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, b =>
        b.MigrationsAssembly("SurveyorLedger.Data").EnableRetryOnFailure(maxRetryCount: 5)));

builder.Services.AddSingleton(sp =>
{
    var acsConnectionString = sp.GetRequiredService<IConfiguration>()["AzureCommunicationServices:ConnectionString"]!;
    return new EmailClient(acsConnectionString);
});
builder.Services.AddScoped<IEmailService, EmailService>();

// Register authentication services
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Register workspace service
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();

// Register invitation service
builder.Services.AddScoped<IInvitationService, InvitationService>();

// Register land service
builder.Services.AddScoped<ILandService, LandService>();

// Register job service
builder.Services.AddScoped<IJobService, JobService>();

// Register milestone service
builder.Services.AddScoped<IMilestoneService, MilestoneService>();

// Register file storage. Local disk for dev - see LocalFileStorageService for the
// swap-to-Azure-Blob path.
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();

// Register document service
builder.Services.AddScoped<IDocumentService, DocumentService>();

// Register document request service
builder.Services.AddScoped<IDocumentRequestService, DocumentRequestService>();

// Register billing services.
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IStaffPaymentService, StaffPaymentService>();

// Register the shared UserAccess grant/revoke service (workspace-scope and job-scope
// membership both go through this - see UserAccessGrantService for why).
builder.Services.AddScoped<IUserAccessGrantService, UserAccessGrantService>();

// Register the shared record-level access check (job/land second-step scoping) - see
// ScopedAccessService for why this replaced four copy-pasted implementations.
builder.Services.AddScoped<IScopedAccessService, ScopedAccessService>();

// Register RBAC service. Singleton: the enforcer is shared in-memory state that must
// survive across requests, not per-request scoped state.
builder.Services.AddSingleton<ICasbinService, CasbinService>();

// Configure JWT Authentication
var jwtKey = builder.Configuration["JwtSettings:Key"] ?? throw new InvalidOperationException("JwtSettings:Key not configured");
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? throw new InvalidOperationException("JwtSettings:Issuer not configured");
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? throw new InvalidOperationException("JwtSettings:Audience not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

var app = builder.Build();

// Initialize Casbin after DB migration
using (var scope = app.Services.CreateScope())
{
    // Client stopped being a workspace-level role (AddMemberRoleAndDecoupleJobRoles migration) -
    // convert any pre-existing workspace-scope Client grants to Member before Casbin loads
    // roles from the DB. Safe to run every startup: the WHERE clause matches zero rows once
    // the conversion has happened once, so this is a no-op forever after.
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.ExecuteSqlRawAsync(
        "UPDATE UserAccesses SET RoleId = '00000000-0000-0000-0000-000000000005' " +
        "WHERE ScopeType = 'Workspace' AND RoleId = '00000000-0000-0000-0000-000000000004'");

    var casbinService = scope.ServiceProvider.GetRequiredService<ICasbinService>();
    await casbinService.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(UiCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();
app.MapControllers();

app.Run();
