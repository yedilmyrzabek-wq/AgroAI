using AgroShield.Application.DTOs.Sensors;
using FluentValidation;

namespace AgroShield.Application.Validators;

public class CreateSensorReadingDtoValidator : AbstractValidator<CreateSensorReadingDto>
{
    public CreateSensorReadingDtoValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.Temp).InclusiveBetween(-60, 80);
        RuleFor(x => x.Humidity).InclusiveBetween(0, 100);
        RuleFor(x => x.WaterLevel).InclusiveBetween(0, 1024);
        RuleFor(x => x.Light).InclusiveBetween(0, 2000);
    }
}
