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
        private const string PasswordPattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9\s<>&""'/])[^<>&""'/]{8,}$";

        private const string EmailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

        public RegisterViewModelValidator(UserManager<ApplicationUser> userManager)
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("El nombre es requerido.")
                .Matches(NamePattern).WithMessage("Nombre inválido. Usa sólo letras, espacios y caracteres comunes.")
                .MaximumLength(100);

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("El correo es requerido")
                .Matches(EmailPattern).WithMessage("Correo electrónico inválido. Asegúrate de incluir el dominio (ej. @gmail.com)");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("La contraseña es requerida")
                .Matches(PasswordPattern).WithMessage("La contraseña debe tener al menos 8 caracteres, mayúsculas, minúsculas, números y símbolos permitidos (No uses < > & \" ' /).");

            RuleFor(x => x.ConfirmPassword)
                .Equal(x => x.Password).WithMessage("Las contraseñas no coinciden");
        }
    }
}
