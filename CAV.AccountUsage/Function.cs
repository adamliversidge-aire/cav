using System.Globalization;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Lambda.Core;
using Amazon.SimpleNotificationService;
using CAV.AccountUsage.Filtering;
using CAV.AccountUsage.Models;
using CsvHelper;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CAV.AccountUsage;

public class Function(IAmazonIdentityManagementService iamClient,
    IAmazonSimpleNotificationService simpleNotificationService)
{
    public async Task FunctionHandler(EventBridgeEvent<dynamic> input, ILambdaContext context)
    {
        var users = await iamClient.ListUsersAsync();
        
        await GenerateReportAsync();
        
        var report = await GetReportWhenReadyAsync();
        
        await ProcessReportAsync(report, users);
    }

    private async Task GenerateReportAsync()
    {
        var response = await iamClient.GenerateCredentialReportAsync(new GenerateCredentialReportRequest());
        Console.WriteLine($"Report generation state: {response.State}");
    }

    private async Task<IReadOnlyCollection<UserReportLineItem>> GetReportWhenReadyAsync()
    {
        while (true)
        {
            try
            {
                var response = await iamClient.GetCredentialReportAsync();
                return ParseReport(response.Content.ToArray());
            }
            catch (CredentialReportNotReadyException)
            {
                Console.WriteLine("Report not ready yet, waiting...");
                await Task.Delay(3000);
            }
            catch (CredentialReportExpiredException)
            {
                // Report expired (older than 4 hours) - regenerate
                await iamClient.GenerateCredentialReportAsync(new GenerateCredentialReportRequest());
                await Task.Delay(3000);
            }
        }
    }

    //Todo: Might have to handle csv length ? Pagination
    private static List<UserReportLineItem> ParseReport(byte[] reportBytes)
    {
        var csvContent = System.Text.Encoding.UTF8.GetString(reportBytes);
        using var reader = new StringReader(csvContent);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<UserReportLineItem>().ToList();
    }

    private async Task ProcessReportAsync(
        IReadOnlyCollection<UserReportLineItem> reportLineItems,
        ListUsersResponse users)
    {
        //todo: Get users that are about to expire, sub 7 days, need to check password / access_key_1 and access_key_2

        var iamUserNames = users.Users.Select(x => x.UserName).ToList();
        var cutOffDate = DateTime.UtcNow.AddDays(-90);
        
        //Action Disable user?
        var inactiveConsoleUsers =
            CredentialReportFilters.GetInactiveConsoleUsers(reportLineItems, iamUserNames, cutOffDate);
        
        await DisableInactiveUsers(inactiveConsoleUsers);

        //Action - removeKey?
        var inactiveAccessKeys = CredentialReportFilters.GetUsersWithStaleAccessKeys(reportLineItems, iamUserNames, cutOffDate);
        await RemoveExpiredAccessKeys(inactiveAccessKeys);
    }

    private async Task DisableInactiveUsers(IReadOnlyCollection<UserReportLineItem> inactiveUsers)
    {
        List<string> disabledUsers = [];
        foreach (var user in inactiveUsers)
        {
            await iamClient.PutUserPolicyAsync(new PutUserPolicyRequest
            {
                UserName = user.User,
                PolicyName = "DisableUserPolicy",
                PolicyDocument = """
                                 {
                                     "Version": "2012-10-17",
                                     "Statement": [
                                         {
                                             "Effect": "Deny",
                                             "Action": "*",
                                             "Resource": "*"
                                         }
                                     ]
                                 }
                                 """
            });
            disabledUsers.Add(user.User);
        }
        
        await simpleNotificationService.PublishAsync(new Amazon.SimpleNotificationService.Model.PublishRequest
        {
            TopicArn = "arn:aws:sns:us-east-1:123456789012:YourTopicName",
            Message = $"The following users have been disabled due to inactivity:\n{string.Join("\n", disabledUsers)}"
        });
        
        //TODO: Do we ant to DTQ this on failure?
    }
    
    private async Task RemoveExpiredAccessKeys(IReadOnlyCollection<UserReportLineItem> expiredAccessKeys)
    {
        List<string> usersWithKeysRemoved = [];
        foreach (var user in expiredAccessKeys)
        {
            var _user = await CheckUserKeysAsync(user);
            
            if(!string.IsNullOrWhiteSpace(_user))
            {
                usersWithKeysRemoved.Add(_user);
            }
        }
        
        if (usersWithKeysRemoved.Count > 0)
        {
            await simpleNotificationService.PublishAsync(new Amazon.SimpleNotificationService.Model.PublishRequest
            {
                TopicArn = "arn:aws:sns:us-east-1:123456789012:YourTopicName",
                Message = $"The following access keys have been disabled due to inactivity:\n{string.Join("\n", usersWithKeysRemoved)}"
            });  
        }
    }

    private async Task<string?> CheckUserKeysAsync(UserReportLineItem user)
    {
        var keys = await iamClient.ListAccessKeysAsync(new ListAccessKeysRequest
        {
            UserName = user.User
        });
            
        if (user.AccessKey1Active)
        {
            await SetKeyAsInactive(user.User!, keys.AccessKeyMetadata[0].AccessKeyId); //not null here
            
            return user.User;
        }
        
        if (!user.AccessKey2Active)
        {
            return null;
        };
            
        await SetKeyAsInactive(user.User!, keys.AccessKeyMetadata[1].AccessKeyId); //not null here
        
        return user.User;
    }
    
    private async Task SetKeyAsInactive(string user, string keyId) => 
        await iamClient.UpdateAccessKeyAsync(new UpdateAccessKeyRequest
    {
        UserName = user,
        AccessKeyId = keyId,
        Status = StatusType.Inactive
    });
}
