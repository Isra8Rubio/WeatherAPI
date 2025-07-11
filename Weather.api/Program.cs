using Core.DTO;
using Core.Entities;
using Core.Validators;
using FluentValidation;
using Infraestructura.Data;
using Infraestructura.Repositories;
using Infraestructura.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NLog;
using NLog.Web;
using RestSharp;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Limpiamos los providers por defecto y usamos NLog y httpcontext para capturar datoa de usuario
builder.Logging.ClearProviders();
builder.Host.UseNLog();
builder.Services.AddHttpContextAccessor();

// AppConfiguration bind
var appConfig = new Infraestructura.Configuration.AppConfiguration(builder.Configuration);
appConfig.Load();
builder.Services.AddSingleton(appConfig);

// Data & Domain Services
builder.Services.AddDbContext<ApplicationDbContext>(opts =>
    opts.UseSqlServer(
        config.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly("Infraestructura")
    )
);

builder.Services.AddScoped<WeatherCompleteRepository>();
builder.Services.AddScoped<WeatherCompleteService>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<UserService>();


// External Client
builder.Services.AddSingleton(_ =>
{
    var options = new RestClientOptions("https://www.el-tiempo.net/api/json/v2/")
    {
    };
    return new RestClient(options);
});
builder.Services.AddTransient<WeatherClient>();
builder.Services.AddHostedService<WeatherUpdateHostedService>();


// Identity & Data Protection
builder.Services.AddDataProtection();
builder.Services
    .AddIdentity<Usuario, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();


// JWT Configuration usando AppConfiguration
var jwtCfg = appConfig.Jwt;
var keyBytes = Encoding.UTF8.GetBytes(jwtCfg.Key!);

// Limpia el mapeo automático de claims
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(opts =>
    {
        // 1) Parámetros básicos de validación
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtCfg.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtCfg.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // 2) Evento para comprobar SecurityStamp
        opts.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var userMgr = ctx.HttpContext.RequestServices
                                      .GetRequiredService<UserManager<Usuario>>();
                var userId = ctx.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
                var stampToken = ctx.Principal?.FindFirstValue("securityStamp");

                var user = await userMgr.FindByIdAsync(userId!);
                if (user == null || user.SecurityStamp != stampToken)
                {
                    ctx.Fail("Token inválido: SecurityStamp no coincide.");
                }
            }
        };
    });


builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("isAdmin", policy =>
        policy.RequireClaim("isAdmin", "true"));

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});


// Controllers & Swagger
builder.Services.AddControllers();

builder.Services.AddSingleton<IValidator<CredentialsUserDTO>, CredentialsUserDTOValidator>();
builder.Services.AddSingleton<IValidator<CreateWeatherCompleteDTO>, CreateWeatherCompleteDTOValidator>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Weather API",
        Version = "v1",
        Description = "API The time"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Introduce:{token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});


var app = builder.Build();

try
{
    // Crear carpeta de logs si no existe
    Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "logs"));

    app.UseRouting();

    // Middlewares
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Weather API 1");
        options.RoutePrefix = string.Empty;
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.Run();
}
catch (Exception ex)
{
    // En caso de fallo en el arranque
    var logger = LogManager.GetCurrentClassLogger();
    logger.Error(ex, "Se produjo un error al arrancar la aplicación");
    throw;
}
finally
{
    // Fuerza a NLog a cerrar y vaciar todos los targets
    LogManager.Shutdown();
}


