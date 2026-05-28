using DYPStore.Models.ViewModels;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using DYPStore.Models;
using System.Text.RegularExpressions;

namespace DYPStore.Validators
{
    public class RegisterViewModelValidator : AbstractValidator<RegisterViewModel>
    {
        private const string NamePattern = "^[\\p{L} .'-]{2,100}$";
        private const string PasswordPattern = "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*\\W).{8,}$";

        public RegisterViewModelValidator(UserManager<ApplicationUser> userManager)
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("El nombre es requerido.")
                .Matches(NamePattern).WithMessage("Nombre inválido. Usa sólo letras, espacios y caracteres comunes.")
                .MaximumLength(100);

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("El correo es requerido")
                .EmailAddress().WithMessage("Correo inválido")
                .MustAsync(async (email, ct) =>
                {
                    if (string.IsNullOrWhiteSpace(email)) return false;
                    var existing = await userManager.FindByEmailAsync(email.Trim().ToLowerInvariant());
                    return existing == null;
                }).WithMessage("Ya existe una cuenta registrada con ese correo.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("La contraseña es requerida")
                .Matches(PasswordPattern).WithMessage("La contraseña debe tener al menos 8 caracteres, incluir mayúsculas, minúsculas, números y símbolos.");

            RuleFor(x => x.ConfirmPassword)
                .Equal(x => x.Password).WithMessage("Las contraseñas no coinciden");
        }
    }
}
