using AgroShield.Application.DTOs.Subsidies;
using FluentValidation;

namespace AgroShield.Application.Validators;

public class CreateSubsidyDtoValidator : AbstractValidator<CreateSubsidyDto>
{
    public CreateSubsidyDtoValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.DeclaredArea).GreaterThan(0);
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(500);
    }
}
