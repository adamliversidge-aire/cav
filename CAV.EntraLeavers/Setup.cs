using Amazon.IdentityManagement;
using Amazon.Lambda.Annotations;
using Amazon.SecretsManager;
using Amazon.SimpleNotificationService;
using CAV.EntraLeavers.Configuration;
using CAV.EntraLeavers.Entra;
using Microsoft.Extensions.DependencyInjection;

namespace CAV.EntraLeavers;

[LambdaStartup]
internal class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAmazonIdentityManagementService, AmazonIdentityManagementServiceClient>();
        services.AddSingleton<IAmazonSimpleNotificationService, AmazonSimpleNotificationServiceClient>();
        services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>();
        services.AddSingleton<IEntraGraphClient>(_ =>
            new EntraGraphClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));
        services.AddSingleton(_ => LeaversOptions.FromEnvironment());
    }
}
