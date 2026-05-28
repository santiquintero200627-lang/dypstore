using System.ComponentModel.DataAnnotations;
namespace DYPStore.Models.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage="El nombre es requerido")][StringLength(100)]
        [Display(Name="Nombre completo")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage="El correo es requerido")]
        [EmailAddress(ErrorMessage="Correo inválido")]
        [Display(Name="Correo electrónico")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage="La contraseña es requerida")]
        [StringLength(100, MinimumLength=8, ErrorMessage="Mínimo 8 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name="Contraseña")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage="Confirma tu contraseña")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage="Las contraseñas no coinciden")]
        [Display(Name="Confirmar contraseña")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
