# CAV Account Usage Security Automation Lambda Function

An AWS Lambda function (.NET 10 / C#) that audits IAM account usage and automatically
remediates stale credentials. It is intended to be invoked on a schedule via an Amazon
EventBridge event.

## What the function does

When invoked, `Function.FunctionHandler` performs the following steps:

1. Lists all IAM users in the account (`ListUsersAsync`).
2. Requests a fresh IAM credential report (`GenerateCredentialReportAsync`) and polls until
   it is ready, regenerating it if AWS reports it as expired.
3. Parses the returned credential report CSV into `UserReportLineItem` records using CsvHelper.
4. Disables **inactive console users** — IAM users whose console password is enabled but who
   have not signed in within the last 90 days (including users who have never signed in). Each
   matching user has a deny-all inline policy (`DisableUserPolicy`) attached.
5. Deactivates **stale access keys** — active access keys belonging to IAM users that have not
   been used within the last 90 days (including keys that have never been used) are set to
   `Inactive`.
6. Publishes Amazon SNS notifications summarising the users that were disabled and the access
   keys that were deactivated.

The 90-day threshold is currently hard-coded in `Function.ProcessReportAsync`.

## Project layout

* `Function.cs` - Lambda entry point; orchestrates report generation, parsing, and the
  disable/deactivate remediation actions, plus SNS notifications.
* `Filtering/CredentialReportFilters.cs` - pure, unit-tested filtering logic
  (`GetInactiveConsoleUsers`, `GetUsersWithStaleAccessKeys`, and the `TryParseAwsDate` helper
  that handles AWS sentinel values such as `N/A`, `no_information`, and `not_supported`).
* `Models/UserReportLineItem.cs` - strongly typed representation of a row in the IAM
  credential report CSV.
* `Models/EventBridgeEvent.cs` - envelope model for the EventBridge event that triggers the
  function.
* `aws-lambda-tools-defaults.json` - default argument settings for Visual Studio and the AWS
  command line deployment tools.

The function is deployed with the settings in `aws-lambda-tools-defaults.json`: region
`eu-west-2`, runtime `dotnet10`, `x86_64` architecture, 512 MB memory, a 30 second timeout,
and the handler `CAV.AccountUsage::CAV.AccountUsage.Function::FunctionHandler`.

## Required AWS permissions

The function's execution role needs IAM permissions to read users and the credential report
and to perform remediation: `iam:ListUsers`, `iam:GenerateCredentialReport`,
`iam:GetCredentialReport`, `iam:PutUserPolicy`, `iam:ListAccessKeys`, `iam:UpdateAccessKey`,
and `sns:Publish` for the target topic.

## Tests

Unit tests for the filtering logic live in the `CAV.AccountUsage.Tests` project (xUnit) in
`CredentialReportFiltersTests.cs`.

## Here are some steps to follow from Visual Studio:

To deploy your function to AWS Lambda, right click the project in Solution Explorer and select *Publish to AWS Lambda*.

To view your deployed function open its Function View window by double-clicking the function name shown beneath the AWS Lambda node in the AWS Explorer tree.

To perform testing against your deployed function use the Test Invoke tab in the opened Function View window.

To configure event sources for your deployed function, for example to have your function invoked when an object is created in an Amazon S3 bucket, use the Event Sources tab in the opened Function View window.

To update the runtime configuration of your deployed function use the Configuration tab in the opened Function View window.

To view execution logs of invocations of your function use the Logs tab in the opened Function View window.

## Here are some steps to follow to get started from the command line:

Once you have edited your template and code you can deploy your application using the [Amazon.Lambda.Tools Global Tool](https://github.com/aws/aws-extensions-for-dotnet-cli#aws-lambda-amazonlambdatools) from the command line.

Install Amazon.Lambda.Tools Global Tools if not already installed.
```
    dotnet tool install -g Amazon.Lambda.Tools
```

If already installed check if new version is available.
```
    dotnet tool update -g Amazon.Lambda.Tools
```

Execute unit tests
```
    cd "CAV.AccountUsage.Tests"
    dotnet test
```

Deploy function to AWS Lambda
```
    cd "CAV.AccountUsage"
    dotnet lambda deploy-function
```
