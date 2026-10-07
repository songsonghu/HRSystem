using FluentValidation;

namespace HRSystem.Application.DTOs.Validators;

/// <summary>Validation rules for creating/editing employees. Lengths mirror the EF configuration.</summary>
public class EmployeeEditDtoValidator : AbstractValidator<EmployeeEditDto>
{
    public EmployeeEditDtoValidator()
    {
        // Required-ness of EmployeeNo/Name comes from MVC's implicit [Required] on non-nullable strings.
        RuleFor(x => x.EmployeeNo).MaximumLength(50);
        RuleFor(x => x.Name).MaximumLength(100);

        RuleFor(x => x.Email)
            .MaximumLength(200)
            .EmailAddress().WithMessage("Invalid email format.")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.Department).MaximumLength(100);
        RuleFor(x => x.Position).MaximumLength(100);

        // Join date may be in the future: HR registers new hires before their first day.
        RuleFor(x => x.ResignDate)
            .GreaterThanOrEqualTo(x => x.JoinDate)
            .WithMessage("Resign date cannot be earlier than join date.")
            .When(x => x.ResignDate.HasValue);
    }
}
