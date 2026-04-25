using AgroShield.Application.DTOs.Chat;
using FluentValidation;

namespace AgroShield.Application.Validators;

public class ChatRequestDtoValidator : AbstractValidator<ChatRequestDto>
{
    public ChatRequestDtoValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
    }
}
