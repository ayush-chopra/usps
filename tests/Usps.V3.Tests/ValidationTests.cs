using System;
using FluentAssertions;
using FluentValidation.TestHelper;
using Usps.V3.Models.Addresses;
using Usps.V3.Models.Labels;
using Usps.V3.Validation;
using Xunit;

public class ValidationTests
{
    [Fact]
    public void AddressValidator_Requires_Line1_And_CityStateOrZip()
    {
        var v = new AddressInputValidator();
        var r = v.TestValidate(new AddressInput());
        r.ShouldHaveValidationErrorFor(x => x.AddressLine1);
        r.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void LabelValidator_Requires_Fields()
    {
        var v = new DomesticLabelRequestValidator();
        var r = v.TestValidate(new DomesticLabelRequest());
        r.Errors.Should().NotBeEmpty();
    }
}

