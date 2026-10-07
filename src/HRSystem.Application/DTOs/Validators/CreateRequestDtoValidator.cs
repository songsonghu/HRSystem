using FluentValidation;

namespace HRSystem.Application.DTOs.Validators;

/// <summary>Validation rules for creating account requests. Lengths mirror the EF configuration.</summary>
public class CreateRequestDtoValidator : AbstractValidator<CreateRequestDto>
{
    public CreateRequestDtoValidator()
    {
        RuleFor(x => x.EmployeeId)
            .GreaterThan(0).WithMessage("Employee is required.");

        RuleFor(x => x.AccountTypeIds)
            .NotEmpty().WithMessage("Select at least one account type.");

        RuleFor(x => x.Remark).MaximumLength(1000);
        RuleFor(x => x.ReplacementOf).MaximumLength(100);
    }
}
