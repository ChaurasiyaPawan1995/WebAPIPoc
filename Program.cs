using CrudJwtSerilogApi.Configuration;
using CrudJwtSerilogApi.Repositories;
using CrudJwtSerilogApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// --------------------
// Serilog
// --------------------
//Log.Logger = new LoggerConfiguration()
//    .ReadFrom.Configuration(builder.Configuration)
//    .Enrich.FromLogContext()
//    .WriteTo.Console()
//    .WriteTo.File(
//        "logs/api-.log",
//        rollingInterval: RollingInterval.Day)
//    .CreateLogger();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.EventCollector(
        "https://inputs.prd-p-3umg3.splunkcloud.com/8088/services/collector",
        "9c0bcfe5-476b-438a-8809-acdfb2b53a4c",
        index:"dotnetwebapi",
        source: "dotnetwebapi"
        )
    .CreateLogger();

builder.Host.UseSerilog();

// --------------------
// Services
// --------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Name = "Authorization",
        Description = "Enter your JWT bearer token."
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});

// --------------------
// JWT
// --------------------
var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });

builder.Services.AddAuthorization();

// --------------------
// SOLID DI
// --------------------
builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ITokenService, TokenService>();

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        // Ignore Swagger requests
        if (httpContext.Request.Path.StartsWithSegments("/swagger"))
        {
            return LogEventLevel.Verbose;
        }

        // Log errors
        if (ex != null || httpContext.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        // Log client errors
        if (httpContext.Response.StatusCode >= 400)
        {
            return LogEventLevel.Warning;
        }

        // Log normal API requests
        return LogEventLevel.Information;
    };
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
