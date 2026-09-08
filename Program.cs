using DYPStore.Data;
using DYPStore.Models;
using DYPStore.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;

AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", true);
ServicePointManager.DnsRefreshTimeout = 0;

var builder = WebApplication.CreateBuilder(args);

// 1. Base de Datos (Failover seguro con DatabaseSteward)
builder.Services.AddSingleton<DatabaseSteward>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => {
    try
    {
        var steward = sp.GetRequiredService<DatabaseSteward>();
        options.UseNpgsql(steward.GetConnectionString());
    }
    catch
    {
        // Fallback directo con SSL flexible para IIS
        string fallbackConn = "Host=pg-cc4b12f-dypstore2026-77e3.a.aivencloud.com;Port=28541;Database=defaultdb;Username=avnadmin;Password=AVNS_fCtSlob8Z5sI0el0S6t;SSL Mode=Require;Trust Server Certificate=true;";
        options.UseNpgsql(fallbackConn);
    }
});

builder.Services.AddDbContext<DataProtectionKeyContext>((sp, options) => {
    try
    {
        var steward = sp.GetRequiredService<DatabaseSteward>();
        options.UseNpgsql(steward.GetConnectionString());
    }
    catch
    {
        string fallbackConn = "Host=pg-cc4b12f-dypstore2026-77e3.a.aivencloud.com;Port=28541;Database=defaultdb;Username=avnadmin;Password=AVNS_fCtSlob8Z5sI0el0S6t;SSL Mode=Require;Trust Server Certificate=true;";
        options.UseNpgsql(fallbackConn);
    }
});

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<DataProtectionKeyContext>()
    .SetApplicationName("DYPStore");

// 2. Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// 3. Servicios
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<ChatbotService>();

builder.Services.AddControllersWithViews();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<DYPStore.Validators.RegisterViewModelValidator>();

builder.Services.AddSession();

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

app.Use(async (context, next) =>
{
    try
    {
        var steward = context.RequestServices.GetRequiredService<DatabaseSteward>();
        context.Items["IsSecondaryDb"] = steward.IsUsingSecondaryDb;
    }
    catch
    {
        context.Items["IsSecondaryDb"] = false;
    }
    await next();
});

// Inicialización de DB en segundo plano para EVITAR que tumben IIS si la red falla en el startup
_ = Task.Run(async () =>
{
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
        Console.WriteLine($"[WARNING] No se pudo inicializar la base de datos en segundo plano: {ex.Message}");
    }
});

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseRateLimiter();
app.UseSession();
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