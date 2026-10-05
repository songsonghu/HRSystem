using FluentValidation;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.DTOs.Validators;

/// <summary>Validation rules for editing employees.</summary>
public class EmployeeEditDtoValidator : AbstractValidator<EmployeeEditDto>
{
    public EmployeeEditDtoValidator()
    {
        RuleFor(x => x.EmployeeNo)
            .NotEmpty()
            .WithMessage("Employee number is required.")
            .Matches(@"^[A-Z0-9-]+$")
            .WithMessage("Employee number must contain only uppercase letters, numbers, or hyphens.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Full name is required.")
            .MaximumLength(100)
            .WithMessage("Full name cannot exceed 100 characters.");

        RuleFor(x => x.Email)
            .EmailAddress()
            .WithMessage("Invalid email format.")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.JoinDate)
            .LessThanOrEqualTo(DateTime.Today)
            .WithMessage("Join date cannot be in the future.");

        RuleFor(x => x.ResignDate)
            .GreaterThan(x => x.JoinDate)
            .WithMessage("Resign date must be after join date.")
            .When(x => x.ResignDate.HasValue);
    }
}
