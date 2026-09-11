using Amazon.IdentityManagement;
using Amazon.Lambda.Annotations;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.DependencyInjection;

namespace CAV.AccountUsage;

[LambdaStartup]
internal static class Startup
{
    internal static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAmazonIdentityManagementService, AmazonIdentityManagementServiceClient>();
        services.AddSingleton<IAmazonSimpleNotificationService, AmazonSimpleNotificationServiceClient>();
    }
}
