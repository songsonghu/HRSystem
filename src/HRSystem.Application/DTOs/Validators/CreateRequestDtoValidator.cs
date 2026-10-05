using FluentValidation;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.DTOs.Validators;

/// <summary>Validation rules for creating account requests.</summary>
public class CreateRequestDtoValidator : AbstractValidator<CreateRequestDto>
{
    public CreateRequestDtoValidator()
    {
        RuleFor(x => x.EmployeeId)
            .GreaterThan(0)
            .WithMessage("Employee is required.");

        RuleFor(x => x.AccountTypeIds)
            .NotNull()
            .WithMessage("Account types cannot be null.")
            .NotEmpty()
            .WithMessage("Select at least one account type.");

        RuleFor(x => x.AccountTypeIds)
            .Must(ids => ids.TrueForAll(id => id > 0))
            .When(x => x.AccountTypeIds != null && x.AccountTypeIds.Count > 0)
            .WithMessage("Invalid account type ID(s).");

        RuleFor(x => x.Remark)
            .MaximumLength(1000)
            .WithMessage("Remark cannot exceed 1000 characters.");
    }
}
