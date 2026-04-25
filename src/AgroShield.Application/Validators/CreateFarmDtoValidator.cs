using AgroShield.Application.DTOs.Farms;
using FluentValidation;

namespace AgroShield.Application.Validators;

public class CreateFarmDtoValidator : AbstractValidator<CreateFarmDto>
{
    public CreateFarmDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AreaHectares).GreaterThan(0);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
    }
}
