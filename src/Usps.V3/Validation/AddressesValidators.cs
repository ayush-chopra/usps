using FluentValidation;
using Usps.V3.Models.Addresses;

namespace Usps.V3.Validation;

public sealed class StandardizeAddressRequestValidator : AbstractValidator<StandardizeAddressRequest>
{
    public StandardizeAddressRequestValidator()
    {
        RuleFor(x => x.Addresses).NotEmpty();
        RuleForEach(x => x.Addresses).SetValidator(new AddressInputValidator());
    }
}

public sealed class AddressInputValidator : AbstractValidator<AddressInput>
{
    public AddressInputValidator()
    {
        RuleFor(x => x.AddressLine1).NotEmpty();
        RuleFor(x => x)
            .Must(HaveCityStateOrZip)
            .WithMessage("Provide city/state or zipCode");
    }

    private bool HaveCityStateOrZip(AddressInput a)
    {
        var hasCityState = !string.IsNullOrWhiteSpace(a.City) && !string.IsNullOrWhiteSpace(a.State);
        var hasZip = !string.IsNullOrWhiteSpace(a.ZipCode);
        return hasCityState || hasZip;
    }
}

