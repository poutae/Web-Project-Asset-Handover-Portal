using FluentValidation;

namespace Portal.Api.Auth;

public record SignupRequest(string Email, string Password, string DisplayName, string OrganizationName);

public record LoginRequest(string Email, string Password);

public record MembershipDto(Guid OrganizationId, string OrganizationName, string Role);

public record CurrentUserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<MembershipDto> Memberships);

public class SignupRequestValidator : AbstractValidator<SignupRequest>
{
    public SignupRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
