using FluentValidation;
using Usps.V3.Models.Prices;

namespace Usps.V3.Validation;

public sealed class DomesticPriceRequestValidator : AbstractValidator<DomesticPriceRequest>
{
    public DomesticPriceRequestValidator()
    {
        RuleFor(x => x.OriginZip).NotEmpty();
        RuleFor(x => x.DestinationZip).NotEmpty();
        RuleFor(x => x.WeightOz).GreaterThan(0);
    }
}

public sealed class InternationalPriceRequestValidator : AbstractValidator<InternationalPriceRequest>
{
    public InternationalPriceRequestValidator()
    {
        RuleFor(x => x.DestinationCountryCode).NotEmpty().Length(2, 3);
        RuleFor(x => x.WeightOz).GreaterThan(0);
    }
}

