using Amazon.Billing;
using Amazon.Lambda.Annotations;
using Amazon.Organizations;
using Amazon.RAM;
using Microsoft.Extensions.DependencyInjection;

namespace CAV.BillingView;

[LambdaStartup]
internal static class Startup
{
    internal static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAmazonBilling, AmazonBillingClient>();
        services.AddSingleton<IAmazonRAM, AmazonRAMClient>();
        services.AddSingleton<IAmazonOrganizations, AmazonOrganizationsClient>();
    }
}