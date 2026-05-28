using DYPStore.Data;
using DYPStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DYPStore.Controllers
{
    public class FaceIdController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _um;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly ILogger<FaceIdController> _logger;

        // Euclidean distance threshold — lower = stricter.
        // face-api.js recommendation: 0.6 lenient, 0.5 balanced, 0.42 strict
        private const double THRESHOLD = 0.42;
        // Minimum frames enrolled to accept a Face ID login
        private const int MIN_FRAMES = 3;

        public FaceIdController(ApplicationDbContext db, UserManager<ApplicationUser> um,
            SignInManager<ApplicationUser> signIn, ILogger<FaceIdController> logger)
        {
            _db = db; _um = um; _signIn = signIn; _logger = logger;
        }

        // ── GET /Account/FaceLogin ─────────────────────────────────────
        [HttpGet]
        [Route("Account/FaceLogin")]
        public IActionResult FaceLogin()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");
            return View("~/Views/Account/FaceLogin.cshtml");
        }

        // ── GET /Account/FaceEnroll (requires auth) ────────────────────
        [HttpGet]
        [Route("Account/FaceEnroll")]
        [Authorize]
        public async Task<IActionResult> FaceEnroll()
        {
            var userId = _um.GetUserId(User)!;
            var existing = await _db.FaceEnrollments.FirstOrDefaultAsync(f => f.UserId == userId);
            ViewBag.IsEnrolled = existing != null;
            ViewBag.FrameCount = existing?.FrameCount ?? 0;
            ViewBag.EnrolledAt = existing?.UpdatedAt;
            return View("~/Views/Account/FaceEnroll.cshtml");
        }

        // ── POST /api/faceid/enroll (requires auth) ────────────────────
        [HttpPost]
        [Route("api/faceid/enroll")]
        [Authorize]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Enroll([FromBody] FaceEnrollRequest req)
        {
            if (req?.Descriptor == null || req.Descriptor.Length != 128)
                return BadRequest(new { error = "Descriptor facial inválido." });

            if (req.FrameCount < MIN_FRAMES)
                return BadRequest(new { error = $"Se requieren al menos {MIN_FRAMES} fotogramas para un registro seguro." });

            var userId = _um.GetUserId(User)!;
            var ua = Request.Headers["User-Agent"].ToString();
            var descriptorJson = JsonSerializer.Serialize(req.Descriptor);

            var existing = await _db.FaceEnrollments.FirstOrDefaultAsync(f => f.UserId == userId);
            if (existing != null)
            {
                existing.DescriptorJson = descriptorJson;
                existing.FrameCount = req.FrameCount;
                existing.UserAgent = ua;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.FaceEnrollments.Add(new FaceEnrollment
                {
                    UserId = userId,
                    DescriptorJson = descriptorJson,
                    FrameCount = req.FrameCount,
                    UserAgent = ua,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Face ID enrolled for user {UserId} frames={Frames}", userId, req.FrameCount);
            return Ok(new { message = "Face ID registrado correctamente." });
        }

        // ── POST /api/faceid/verify ────────────────────────────────────
        // Public endpoint — verifies face descriptor and creates session if matched
        [HttpPost]
        [Route("api/faceid/verify")]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("faceid")] // Max 10 intentos por minuto (#8)
        public async Task<IActionResult> Verify([FromBody] FaceVerifyRequest req)
        {
            if (req?.Descriptor == null || req.Descriptor.Length != 128)
                return BadRequest(new { error = "Descriptor facial inválido." });

            if (string.IsNullOrWhiteSpace(req.Email))
                return BadRequest(new { error = "Email requerido." });

            var user = await _um.FindByEmailAsync(req.Email.Trim());
            if (user == null)
            {
                _logger.LogWarning("Face ID verify: user not found for email {Email}", req.Email);
                // Return same message as mismatch to avoid email enumeration
                return Unauthorized(new { error = "Rostro no reconocido. Verifica tu email o inicia sesión con tu contraseña." });
            }

            var enrollment = await _db.FaceEnrollments.FirstOrDefaultAsync(f => f.UserId == user.Id);
            if (enrollment == null)
                return NotFound(new { error = "Este usuario no tiene Face ID registrado." });

            // Deserialize stored descriptor
            double[]? stored;
            try { stored = JsonSerializer.Deserialize<double[]>(enrollment.DescriptorJson); }
            catch { return StatusCode(500, new { error = "Error interno al leer biometría." }); }

            if (stored == null || stored.Length != 128)
                return StatusCode(500, new { error = "Biometría almacenada inválida." });

            // Compute Euclidean distance
            double distance = 0;
            for (int i = 0; i < 128; i++)
            {
                var diff = req.Descriptor[i] - stored[i];
                distance += diff * diff;
            }
            distance = Math.Sqrt(distance);

            _logger.LogInformation("Face ID verify userId={UserId} distance={Distance:F4} threshold={Threshold}", user.Id, distance, THRESHOLD);

            if (distance > THRESHOLD)
            {
                _logger.LogWarning("Face ID mismatch userId={UserId} distance={Distance:F4}", user.Id, distance);
                return Unauthorized(new { error = "Rostro no reconocido. La similitud es insuficiente.", distance });
            }

            // Match — sign in
            await _signIn.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("Face ID login success userId={UserId} distance={Distance:F4}", user.Id, distance);

            var isAdmin = await _um.IsInRoleAsync(user, "Admin");
            var redirect = isAdmin ? "/Admin/Dashboard" : "/";
            return Ok(new { message = "Identidad verificada. Iniciando sesión...", redirect });
        }

        // ── DELETE /api/faceid/unenroll (requires auth) ────────────────
        [HttpDelete]
        [Route("api/faceid/unenroll")]
        [Authorize]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Unenroll()
        {
            var userId = _um.GetUserId(User)!;
            var existing = await _db.FaceEnrollments.FirstOrDefaultAsync(f => f.UserId == userId);
            if (existing == null) return NotFound(new { error = "No hay Face ID registrado." });

            _db.FaceEnrollments.Remove(existing);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Face ID unenrolled userId={UserId}", userId);
            return Ok(new { message = "Face ID eliminado correctamente." });
        }

        // ── GET /api/faceid/status (requires auth) ─────────────────────
        [HttpGet]
        [Route("api/faceid/status")]
        [Authorize]
        public async Task<IActionResult> Status()
        {
            var userId = _um.GetUserId(User)!;
            var e = await _db.FaceEnrollments.FirstOrDefaultAsync(f => f.UserId == userId);
            return Ok(new { isEnrolled = e != null, frameCount = e?.FrameCount, updatedAt = e?.UpdatedAt });
        }
    }

    // ── Request models ─────────────────────────────────────────────────
    public class FaceEnrollRequest
    {
        public double[] Descriptor { get; set; } = Array.Empty<double>();
        public int FrameCount { get; set; }
    }

    public class FaceVerifyRequest
    {
        public double[] Descriptor { get; set; } = Array.Empty<double>();
        public string Email { get; set; } = string.Empty;
    }
}
