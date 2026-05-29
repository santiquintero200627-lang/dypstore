using DYPStore.Models;
using DYPStore.Models.ViewModels;
using DYPStore.Services; 
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace DYPStore.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(UserManager<ApplicationUser> um, SignInManager<ApplicationUser> sm, IEmailSender emailSender, IConfiguration configuration, ILogger<AccountController> logger) 
        { 
            _userManager = um; 
            _signInManager = sm; 
            _emailSender = emailSender;
            _configuration = configuration;
            _logger = logger;
        }

        // --- LOGIN ---
        [HttpGet] 
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
          if (!ModelState.IsValid)
          {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers["Accept"].ToString().Contains("application/json"))
            {
              var errors = ModelState.Where(kvp => kvp.Value?.Errors.Count > 0)
                .Select(kvp => new { Field = kvp.Key, Messages = kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray() });
              return BadRequest(new { Errors = errors });
            }

            return View(model);
          }

            try
            {
            // Check lockout status to give a clearer message if the account is blocked
            var userForLock = await _userManager.FindByEmailAsync(model.Email);
            if (userForLock != null && await _userManager.IsLockedOutAsync(userForLock))
            {
              ModelState.AddModelError("", "Cuenta bloqueada temporalmente por varios intentos fallidos. Intenta de nuevo más tarde.");
              return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    var user = await _userManager.FindByEmailAsync(model.Email);
                    if (user != null && await _userManager.IsInRoleAsync(user, "Admin"))
                        return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

                    if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                        return Redirect(model.ReturnUrl);

                    return RedirectToAction("Index", "Home");
                }

                ModelState.AddModelError("", "Correo o contraseña incorrectos.");
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers["Accept"].ToString().Contains("application/json"))
                {
                  return BadRequest(new { Errors = new[] { new { Field = "", Message = "Correo o contraseña incorrectos." } } });
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "El servicio no está disponible en este momento. Intenta de nuevo en unos segundos.");
            }

            return View(model);
        }

        // --- REGISTER ---
        [HttpGet] 
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
              if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers["Accept"].ToString().Contains("application/json"))
              {
                var errors = ModelState.Where(kvp => kvp.Value?.Errors.Count > 0)
                  .Select(kvp => new { Field = kvp.Key, Messages = kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray() });
                return BadRequest(new { Errors = errors });
              }

              return View(model);
            }

            try
            {
            // Trim and normalize inputs
            model.FullName = model.FullName?.Trim() ?? string.Empty;
            model.Email = model.Email?.Trim().ToLowerInvariant() ?? string.Empty;

            // Validate full name (letters, spaces, hyphens, accents)
            var namePattern = "^[\\p{L} .'-]{2,100}$";
            if (!Regex.IsMatch(model.FullName, namePattern))
            {
              ModelState.AddModelError(nameof(model.FullName), "Nombre inválido. Usa sólo letras, espacios y caracteres comunes.");
              return View(model);
            }

            // Password strength server-side (same rules as Identity options)
            var pwdPattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*\W).{8,}$";
            if (!Regex.IsMatch(model.Password, pwdPattern))
            {
              ModelState.AddModelError(nameof(model.Password), "La contraseña debe tener al menos 8 caracteres, incluir mayúsculas, minúsculas, números y símbolos.");
              return View(model);
            }

            // Ensure email is unique before attempting creation
            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing != null)
            {
              ModelState.AddModelError(nameof(model.Email), "Ya existe una cuenta registrada con ese correo.");
              return View(model);
            }

            var user = new ApplicationUser { UserName = model.Email, Email = model.Email, FullName = model.FullName, EmailConfirmed = true };
            var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, "User");
                    await _signInManager.SignInAsync(user, false);
                    return RedirectToAction("Index", "Home");
                }

                foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "El servicio no está disponible en este momento. Intenta de nuevo en unos segundos.");
            }

            return View(model);
        }

        // --- RECUPERAR CONTRASEÑA ---
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            try 
            {
                if (!ModelState.IsValid) return View(model);
                
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user != null)
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var callbackUrl = Url.Action("ResetPassword", "Account", new { token, email = user.Email }, protocol: Request.Scheme);
                    
                    var requestTime = System.DateTime.Now.ToString("dd 'de' MMMM 'de' yyyy 'a las' HH:mm", new System.Globalization.CultureInfo("es-CO"));
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Desconocida";
                    var userAgent = Request.Headers["User-Agent"].ToString();
                    string emailBody = $@"<!DOCTYPE html>
<html lang=""es"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1.0"">
<meta name=""color-scheme"" content=""dark"">
<meta name=""supported-color-schemes"" content=""dark"">
<title>Restablece tu contraseña · DYPStore</title>
<link rel=""preconnect"" href=""https://fonts.googleapis.com"">
<link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
<link href=""https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@600;700;800;900&family=Inter:wght@400;500;600;700&display=swap"" rel=""stylesheet"">
<style>
  @media only screen and (max-width:600px) {{
    .dyp-card {{ width:100% !important; border-radius:0 !important; }}
    .dyp-pad {{ padding:28px 22px !important; }}
    .dyp-h1 {{ font-size:28px !important; line-height:1.05 !important; }}
    .dyp-cta {{ display:block !important; width:100% !important; box-sizing:border-box; }}
    .dyp-stack {{ display:block !important; width:100% !important; }}
    .dyp-stack td {{ display:block !important; width:100% !important; padding:6px 0 !important; }}
  }}
</style>
</head>
<body style=""margin:0;padding:0;background:#07070a;font-family:'Inter',-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#e8e8ee;-webkit-font-smoothing:antialiased;"">
<div style=""display:none;max-height:0;overflow:hidden;opacity:0;color:#07070a;"">Restablece tu contraseña de DYPStore en un par de clics. Enlace válido por 1 hora.</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:#07070a;background-image:radial-gradient(900px 500px at 10% -10%, rgba(255,45,77,0.18), transparent 60%),radial-gradient(700px 400px at 110% 0%, rgba(78,161,255,0.10), transparent 60%);padding:40px 16px;"">
  <tr><td align=""center"">

    <table role=""presentation"" class=""dyp-card"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""max-width:600px;width:100%;background:#0d0d12;border:1px solid rgba(255,255,255,0.08);border-radius:18px;overflow:hidden;box-shadow:0 30px 70px rgba(0,0,0,0.55);"">

      <!-- HERO -->
      <tr><td style=""background:linear-gradient(135deg,#ff2d4d 0%,#ff7849 60%,#ffb547 100%);padding:36px 32px;text-align:center;position:relative;"">
        <table role=""presentation"" align=""center"" cellpadding=""0"" cellspacing=""0"" border=""0"">
          <tr>
            <td style=""background:rgba(0,0,0,0.25);width:48px;height:48px;border-radius:14px;text-align:center;vertical-align:middle;font-size:26px;line-height:48px;color:#fff;border:1px solid rgba(255,255,255,0.25);"">⚡</td>
            <td style=""padding-left:14px;font-family:'Barlow Condensed','Segoe UI',sans-serif;font-weight:900;font-size:32px;letter-spacing:.04em;color:#fff;text-transform:uppercase;line-height:1;"">DYP<span style=""color:#14141a;"">Store</span></td>
          </tr>
        </table>
        <div style=""margin-top:14px;display:inline-block;background:rgba(0,0,0,0.28);color:#fff;padding:6px 14px;border-radius:999px;font-size:11px;font-weight:700;letter-spacing:.16em;text-transform:uppercase;"">🔐 Solicitud de seguridad</div>
      </td></tr>

      <!-- BODY -->
      <tr><td class=""dyp-pad"" style=""padding:44px 40px 32px;"">
        <div style=""font-family:'Barlow Condensed','Segoe UI',sans-serif;color:#9a9aab;font-size:12px;font-weight:700;letter-spacing:.18em;text-transform:uppercase;margin-bottom:10px;"">Recupera tu acceso</div>
        <h1 class=""dyp-h1"" style=""margin:0 0 18px;font-family:'Barlow Condensed','Segoe UI',sans-serif;font-weight:900;font-size:38px;line-height:1.05;color:#ffffff;text-transform:uppercase;letter-spacing:.005em;"">
          Hola, <span style=""color:#ff2d4d;"">{user.FullName}</span>.<br/>Vuelve al ring.
        </h1>
        <p style=""margin:0 0 28px;color:#c8c8d2;font-size:16px;line-height:1.65;"">
          Recibimos una solicitud para restablecer la contraseña de tu cuenta en <strong style=""color:#fff;"">DYPStore</strong>. Toca el botón y crea una nueva en menos de 30 segundos.
        </p>

        <!-- CTA Button (bulletproof for Outlook) -->
        <table role=""presentation"" align=""center"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:8px auto 18px;"">
          <tr><td align=""center"" bgcolor=""#ff2d4d"" style=""border-radius:14px;background:linear-gradient(135deg,#ff2d4d 0%,#e6213e 100%);box-shadow:0 12px 30px rgba(255,45,77,0.35);"">
            <a class=""dyp-cta"" href=""{callbackUrl}"" target=""_blank"" style=""display:inline-block;padding:18px 42px;font-family:'Barlow Condensed','Segoe UI',sans-serif;font-weight:800;font-size:18px;color:#ffffff;text-decoration:none;text-transform:uppercase;letter-spacing:.08em;border-radius:14px;mso-padding-alt:0;"">
              ⚡ Restablecer mi contraseña →
            </a>
          </td></tr>
        </table>

        <p style=""margin:0 0 30px;text-align:center;color:#9a9aab;font-size:13px;"">
          ⏱ Este enlace expira en <strong style=""color:#ffb547;"">1 hora</strong> por tu seguridad.
        </p>

        <!-- Detalles de la solicitud -->
        <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:#16161d;border:1px solid rgba(255,255,255,0.06);border-radius:14px;margin:8px 0 26px;"">
          <tr><td style=""padding:18px 20px;"">
            <div style=""font-family:'Barlow Condensed','Segoe UI',sans-serif;color:#ff2d4d;font-size:11px;font-weight:800;letter-spacing:.18em;text-transform:uppercase;margin-bottom:10px;"">📋 Detalles de la solicitud</div>
            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
              <tr><td style=""padding:6px 0;color:#9a9aab;font-size:13px;width:110px;"">Cuenta</td><td style=""padding:6px 0;color:#fff;font-size:13px;font-weight:600;"">{user.Email}</td></tr>
              <tr><td style=""padding:6px 0;color:#9a9aab;font-size:13px;"">Fecha</td><td style=""padding:6px 0;color:#fff;font-size:13px;font-weight:600;"">{requestTime}</td></tr>
              <tr><td style=""padding:6px 0;color:#9a9aab;font-size:13px;"">IP</td><td style=""padding:6px 0;color:#fff;font-size:13px;font-weight:600;font-family:ui-monospace,Menlo,monospace;"">{ipAddress}</td></tr>
            </table>
          </td></tr>
        </table>

        <!-- Aviso de seguridad -->
        <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:rgba(255,181,71,0.08);border:1px solid rgba(255,181,71,0.25);border-radius:12px;margin:0 0 26px;"">
          <tr><td style=""padding:16px 18px;"">
            <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"">
              <tr>
                <td style=""vertical-align:top;font-size:22px;line-height:1;padding-right:12px;"">⚠️</td>
                <td>
                  <div style=""color:#ffb547;font-weight:700;font-size:14px;margin-bottom:4px;"">¿No fuiste tú?</div>
                  <div style=""color:#c8c8d2;font-size:13px;line-height:1.55;"">Ignora este mensaje. Tu contraseña actual seguirá siendo válida y nadie podrá entrar a tu cuenta.</div>
                </td>
              </tr>
            </table>
          </td></tr>
        </table>

        <!-- Tips de seguridad -->
        <div style=""font-family:'Barlow Condensed','Segoe UI',sans-serif;color:#9a9aab;font-size:12px;font-weight:700;letter-spacing:.18em;text-transform:uppercase;margin:0 0 12px;"">🛡 Tips para una contraseña fuerte</div>
        <table role=""presentation"" class=""dyp-stack"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:0 0 30px;"">
          <tr>
            <td valign=""top"" style=""width:33.33%;padding-right:6px;"">
              <div style=""background:#16161d;border:1px solid rgba(255,255,255,0.06);border-radius:12px;padding:14px;text-align:center;"">
                <div style=""font-size:22px;margin-bottom:6px;"">🔢</div>
                <div style=""color:#fff;font-size:12px;font-weight:700;line-height:1.3;"">8+ caracteres</div>
              </div>
            </td>
            <td valign=""top"" style=""width:33.33%;padding:0 3px;"">
              <div style=""background:#16161d;border:1px solid rgba(255,255,255,0.06);border-radius:12px;padding:14px;text-align:center;"">
                <div style=""font-size:22px;margin-bottom:6px;"">🔠</div>
                <div style=""color:#fff;font-size:12px;font-weight:700;line-height:1.3;"">Mayús + minús</div>
              </div>
            </td>
            <td valign=""top"" style=""width:33.33%;padding-left:6px;"">
              <div style=""background:#16161d;border:1px solid rgba(255,255,255,0.06);border-radius:12px;padding:14px;text-align:center;"">
                <div style=""font-size:22px;margin-bottom:6px;"">🎯</div>
                <div style=""color:#fff;font-size:12px;font-weight:700;line-height:1.3;"">Símbolo + número</div>
              </div>
            </td>
          </tr>
        </table>

        <!-- Enlace directo -->
        <div style=""border-top:1px solid rgba(255,255,255,0.07);padding-top:22px;"">
          <p style=""margin:0 0 8px;color:#9a9aab;font-size:12px;"">¿El botón no funciona? Copia y pega este enlace:</p>
          <div style=""background:#0a0a0e;border:1px solid rgba(255,255,255,0.08);border-radius:10px;padding:12px 14px;word-break:break-all;"">
            <a href=""{callbackUrl}"" style=""color:#ff7849;font-size:12px;text-decoration:none;font-family:ui-monospace,Menlo,monospace;"">{callbackUrl}</a>
          </div>
        </div>
      </td></tr>

      <!-- Pie con valores -->
      <tr><td style=""background:#0a0a0e;padding:24px 32px;border-top:1px solid rgba(255,255,255,0.06);"">
        <table role=""presentation"" class=""dyp-stack"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
          <tr>
            <td style=""text-align:center;padding:6px;color:#c8c8d2;font-size:11px;"">🚚 <strong style=""color:#fff;"">Envío 24-48h</strong></td>
            <td style=""text-align:center;padding:6px;color:#c8c8d2;font-size:11px;"">🔄 <strong style=""color:#fff;"">Cambios fáciles</strong></td>
            <td style=""text-align:center;padding:6px;color:#c8c8d2;font-size:11px;"">🛡 <strong style=""color:#fff;"">Garantía premium</strong></td>
          </tr>
        </table>
      </td></tr>

      <!-- Footer -->
      <tr><td style=""background:#07070a;padding:24px 32px;text-align:center;"">
        <div style=""font-family:'Barlow Condensed','Segoe UI',sans-serif;font-weight:800;font-size:18px;color:#fff;letter-spacing:.04em;text-transform:uppercase;margin-bottom:6px;"">⚡ DYPStore</div>
        <p style=""margin:0 0 10px;color:#7a7a87;font-size:11px;line-height:1.6;"">Equipamiento deportivo premium · Boxeo · Calzado · Suplementos</p>
        <p style=""margin:0;color:#5a5a67;font-size:11px;"">Este es un correo automático. Por favor no respondas. Si necesitas ayuda escribe a <a href=""mailto:soporte@dypstore.com"" style=""color:#ff7849;text-decoration:none;"">soporte@dypstore.com</a>.</p>
        <p style=""margin:14px 0 0;color:#3a3a47;font-size:10px;"">&copy; {System.DateTime.Now.Year} DYPStore. Todos los derechos reservados.</p>
      </td></tr>

    </table>

    <p style=""margin:18px auto 0;color:#3a3a47;font-size:10px;text-align:center;max-width:600px;"">Recibiste este correo porque alguien (esperamos que tú) solicitó restablecer la contraseña asociada a {user.Email}.</p>

  </td></tr>
</table>
</body>
</html>";

                    await _emailSender.SendEmailAsync(model.Email, "Recuperar Contraseña - DYPStore", emailBody);
                }
                
                return RedirectToAction("ForgotPasswordConfirmation");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error en ForgotPassword para {Email}", model.Email);
                ModelState.AddModelError("", "Ocurrió un error al procesar tu solicitud. Por favor intenta de nuevo más tarde.");
                return View(model);
            }
        }

        public IActionResult ForgotPasswordConfirmation() => View();

        [HttpGet]
        public IActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null) return RedirectToAction("Login");
            return View(new ResetPasswordViewModel { Token = token, Email = email });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null) return RedirectToAction("ResetPasswordConfirmation");
            
            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (result.Succeeded) return RedirectToAction("ResetPasswordConfirmation");
            
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View(model);
        }

        public IActionResult ResetPasswordConfirmation() => View();

        // --- LOGOUT & ACCESO DENEGADO ---
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout() 
        { 
            await _signInManager.SignOutAsync(); 
            return RedirectToAction("Index", "Home"); 
        }

        public IActionResult AccessDenied() => View();

        // --- SUPABASE SYNC ---
        [HttpPost]
        public async Task<IActionResult> SupabaseLoginSync([FromBody] SupabaseTokenModel model)
        {
            if (string.IsNullOrEmpty(model?.AccessToken)) return BadRequest("Token faltante");

            using var httpClient = new System.Net.Http.HttpClient();
            // Validar token contra los servidores de Supabase de manera segura
            try
            {
                // Decodificar el JWT de Supabase directamente (sin llamada HTTP)
                // El token tiene 3 partes separadas por "."  header.payload.signature
                var parts = model.AccessToken.Split('.');
                if (parts.Length < 2) return Unauthorized("Formato de token inválido");

                // Decodificar el payload (parte 2) en base64url
                var payload = parts[1];
                // Añadir padding si es necesario
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                payload = payload.Replace('-', '+').Replace('_', '/');

                var jsonBytes = Convert.FromBase64String(payload);
                var jsonStr = System.Text.Encoding.UTF8.GetString(jsonBytes);
                var claims = System.Text.Json.JsonDocument.Parse(jsonStr).RootElement;

                // Extraer email del JWT
                string? email = null;
                if (claims.TryGetProperty("email", out var emailProp))
                    email = emailProp.GetString();

                if (string.IsNullOrEmpty(email))
                    return Unauthorized("Email no encontrado en el token.");

                // Extraer nombre del JWT
                var name = "Usuario Google";
                if (claims.TryGetProperty("user_metadata", out var meta))
                {
                    if (meta.TryGetProperty("full_name", out var fullName))
                        name = fullName.GetString() ?? name;
                    else if (meta.TryGetProperty("name", out var nameP))
                        name = nameP.GetString() ?? name;
                }

                // Verificar o crear usuario en la base de datos local
                var user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                {
                    user = new ApplicationUser 
                    { 
                        UserName = email, 
                        Email = email, 
                        FullName = name, 
                        EmailConfirmed = true 
                    };
                    var result = await _userManager.CreateAsync(user);
                    if (result.Succeeded)
                        await _userManager.AddToRoleAsync(user, "User");
                    else
                        return BadRequest("Error interno creando el usuario local");
                }

                // Iniciar sesión en ASP.NET Core Identity (crea la cookie)
                await _signInManager.SignInAsync(user, isPersistent: true);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SupabaseLoginSync");
                return Unauthorized("Error procesando el token de Google.");
            }
        }
    }

    public class SupabaseTokenModel
    {
        public string? AccessToken { get; set; }
    }
}