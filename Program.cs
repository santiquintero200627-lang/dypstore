using DYPStore.Data;
using DYPStore.Models;
using DYPStore.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Net.Sockets;
using System;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;

// Forzar IPv4 para evitar problemas con DNS que devuelve IPv6 pero la red local no lo soporta
AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", true);
ServicePointManager.DnsRefreshTimeout = 0;

var builder = WebApplication.CreateBuilder(args);

// 1. Base de Datos (Failover con DatabaseSteward)
builder.Services.AddSingleton<DatabaseSteward>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => {
    var steward = sp.GetRequiredService<DatabaseSteward>();
    options.UseNpgsql(steward.GetConnectionString());
});

builder.Services.AddDbContext<DataProtectionKeyContext>((sp, options) => {
    var steward = sp.GetRequiredService<DatabaseSteward>();
    options.UseNpgsql(steward.GetConnectionString());
});

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<DataProtectionKeyContext>()
    .SetApplicationName("DYPStore");

// 2. Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password strength: minimum recommended by OWASP
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    // Lockout settings to mitigate brute force attempts
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// 3. Servicios
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<ChatbotService>();

builder.Services.AddControllersWithViews();

// FluentValidation: enable automatic validation and register validators
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<DYPStore.Validators.RegisterViewModelValidator>();

builder.Services.AddSession();

// Rate limiting: protege /api/faceid/verify contra fuerza bruta
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("faceid", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit = 10;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Middleware para inyectar el estado de Failover para el Frontend
app.Use(async (context, next) =>
{
    var steward = context.RequestServices.GetRequiredService<DatabaseSteward>();
    context.Items["IsSecondaryDb"] = steward.IsUsingSecondaryDb;
    await next();
});

// Inicializar DB: protegido con try-catch para evitar crash en hosting
try
{
    using (var scope = app.Services.CreateScope())
    {
        await DYPStore.Data.DbInitializer.InitializeAsync(scope.ServiceProvider);
        var dataProtectionContext = scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>();
        await dataProtectionContext.Database.EnsureCreatedAsync();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[WARNING] Error en inicialización de DB: {ex.Message}");
}

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Home/Error");
    // app.UseHsts(); // Desactivado para HTTP en MonsterASP
}

// app.UseHttpsRedirection(); // Comentado para evitar ERR_CONNECTION_RESET
app.UseStaticFiles();
app.UseRouting();

app.UseRateLimiter();

app.UseSession(); // Debe ir ANTES de Authentication
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();