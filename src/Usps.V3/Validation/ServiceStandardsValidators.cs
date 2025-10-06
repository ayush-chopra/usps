using FluentValidation;
using Usps.V3.Models.ServiceStandards;

namespace Usps.V3.Validation;

public sealed class ServiceStandardsLookupRequestValidator : AbstractValidator<ServiceStandardsLookupRequest>
{
    public ServiceStandardsLookupRequestValidator()
    {
        RuleFor(x => x.OriginZip).NotEmpty();
        RuleFor(x => x.DestinationZip).NotEmpty();
        RuleFor(x => x.Service).NotEmpty();
    }
}

