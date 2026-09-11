using CAV.BillingView.Extensions;
using Xunit;

namespace CAV.BillingView.Tests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("SERVICE-PROD", "SERVICE")]
    [InlineData("SERVICE-DEV", "SERVICE")]
    [InlineData("MULTI-WORD-SERVICE-PROD", "MULTI")]
    public void GetServiceFromAccountName_ExtractsLeadingSegment(string accountName, string expected)
    {
        var result = accountName.GetServiceFromAccountName();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetServiceFromAccountName_NoSeparator_ReturnsWholeName()
    {
        var result = "SERVICEPROD".GetServiceFromAccountName();

        Assert.Equal("SERVICEPROD", result);
    }
}
