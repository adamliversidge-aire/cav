using CAV.EntraLeavers.Configuration;
using CAV.EntraLeavers.Entra;
using Xunit;

namespace CAV.EntraLeavers.Tests;

public class LeaversOptionsTests
{
    private static Func<string, string?> Env(Dictionary<string, string?> values) =>
        name => values.GetValueOrDefault(name);

    private static Dictionary<string, string?> RequiredOnly() => new()
    {
        ["SNS_TOPIC_ARN"] = "arn:aws:sns:eu-west-2:123456789012:team",
        ["ENTRA_SECRET_ARN"] = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:entra"
    };

    [Fact]
    public void OptionalValuesMissing_UsesDefaults()
    {
        var options = LeaversOptions.FromEnvironment(Env(RequiredOnly()));

        Assert.Equal(90, options.InactiveDays);
        Assert.True(options.DryRun);
        Assert.Equal(25, options.MaxDisablePerRun);
        Assert.Equal("email", options.IamEmailTagKey);
    }

    [Fact]
    public void AllValuesSet_AreRead()
    {
        var env = RequiredOnly();
        env["INACTIVE_DAYS"] = "30";
        env["DRY_RUN"] = "False";
        env["MAX_DISABLE_PER_RUN"] = "5";
        env["IAM_EMAIL_TAG_KEY"] = "Owner";

        var options = LeaversOptions.FromEnvironment(Env(env));

        Assert.Equal(30, options.InactiveDays);
        Assert.False(options.DryRun);
        Assert.Equal(5, options.MaxDisablePerRun);
        Assert.Equal("Owner", options.IamEmailTagKey);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("no")]
    [InlineData("0")]
    [InlineData("flase")]
    public void DryRun_AnythingButFalse_StaysOn(string value)
    {
        var env = RequiredOnly();
        env["DRY_RUN"] = value;

        Assert.True(LeaversOptions.FromEnvironment(Env(env)).DryRun);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("ninety")]
    public void InactiveDays_Invalid_Throws(string value)
    {
        var env = RequiredOnly();
        env["INACTIVE_DAYS"] = value;

        Assert.Throws<InvalidOperationException>(() => LeaversOptions.FromEnvironment(Env(env)));
    }

    [Theory]
    [InlineData("SNS_TOPIC_ARN")]
    [InlineData("ENTRA_SECRET_ARN")]
    public void RequiredValueMissing_Throws(string name)
    {
        var env = RequiredOnly();
        env.Remove(name);

        var ex = Assert.Throws<InvalidOperationException>(() => LeaversOptions.FromEnvironment(Env(env)));
        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void EntraCredentials_ValidJson_IsParsed()
    {
        var credentials = EntraCredentials.FromJson("""{"tenantId":"t","clientId":"c","clientSecret":"s"}""");

        Assert.Equal(new EntraCredentials("t", "c", "s"), credentials);
        Assert.DoesNotContain("s }", credentials.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"tenantId":"t","clientId":"c"}""")]
    public void EntraCredentials_Incomplete_Throws(string json)
    {
        Assert.Throws<InvalidOperationException>(() => EntraCredentials.FromJson(json));
    }
}
