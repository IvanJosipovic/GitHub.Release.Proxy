using FluentValidation;

namespace Github.Release.Proxy;

public class SettingsValidator : AbstractValidator<Settings>
{
    public SettingsValidator()
    {
        RuleFor(x => x.Organization).NotEmpty();
        RuleFor(x => x.Project).NotEmpty();
    }
}
