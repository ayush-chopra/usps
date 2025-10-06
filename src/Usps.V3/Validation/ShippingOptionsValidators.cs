using FluentValidation;
using Usps.V3.Models.ShippingOptions;

namespace Usps.V3.Validation;

public sealed class ShippingOptionsQuoteRequestValidator : AbstractValidator<ShippingOptionsQuoteRequest>
{
    public ShippingOptionsQuoteRequestValidator()
    {
        RuleFor(x => x.OriginZip).NotEmpty();
        RuleFor(x => x.DestinationZip).NotEmpty();
        RuleFor(x => x.WeightOz).GreaterThan(0);
    }
}

