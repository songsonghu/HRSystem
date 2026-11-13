using FluentValidation;

namespace HRSystem.Application.DTOs.Validators;

public class UserEditDtoValidator : AbstractValidator<UserEditDto>
{
    public UserEditDtoValidator()
    {
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().WithMessage("Invalid email format.");
        RuleFor(x => x.FullName).MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Initial password is required.")
            .When(x => string.IsNullOrEmpty(x.Id));
    }
}

public class ResetPasswordDtoValidator : AbstractValidator<ResetPasswordDto>
{
    public ResetPasswordDtoValidator()
    {
        RuleFor(x => x.NewPassword).MinimumLength(8);
    }
}

public class RoleEditDtoValidator : AbstractValidator<RoleEditDto>
{
    public RoleEditDtoValidator()
    {
        RuleFor(x => x.Name).MaximumLength(256);
        RuleForEach(x => x.Permissions)
            .Must(Security.Permissions.IsDefined).WithMessage("Unknown permission '{PropertyValue}'.");
    }
}
