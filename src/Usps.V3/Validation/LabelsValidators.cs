using FluentValidation;
using Usps.V3.Models.Labels;

namespace Usps.V3.Validation;

public sealed class DomesticLabelRequestValidator : AbstractValidator<DomesticLabelRequest>
{
    public DomesticLabelRequestValidator()
    {
        RuleFor(x => x.Service).NotEmpty();
        RuleFor(x => x.WeightOz).GreaterThan(0);
        RuleFor(x => x.ShipFrom).SetValidator(new AddressValidator());
        RuleFor(x => x.ShipTo).SetValidator(new AddressValidator());
    }
}

public sealed class AddressValidator : AbstractValidator<Address>
{
    public AddressValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.AddressLine1).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.State).NotEmpty();
        RuleFor(x => x.ZipCode).NotEmpty();
    }
}

