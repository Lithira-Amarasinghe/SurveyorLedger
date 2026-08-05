using Azure.Communication.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using SurveyorLedger.API.Services;
using SurveyorLedger.API.Middleware;
using SurveyorLedger.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, b => b.MigrationsAssembly("SurveyorLedger.Data")));

var acsConnectionString = builder.Configuration["AzureCommunicationServices:ConnectionString"]!;
builder.Services.AddSingleton(new EmailClient(acsConnectionString));
builder.Services.AddScoped<IEmailService, EmailService>();

// Register authentication services
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Register workspace service
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();

// Register RBAC service
builder.Services.AddScoped<ICasbinService, CasbinService>();

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
var casbinService = app.Services.GetRequiredService<ICasbinService>();
await casbinService.InitializeAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();
app.MapControllers();

app.Run();
