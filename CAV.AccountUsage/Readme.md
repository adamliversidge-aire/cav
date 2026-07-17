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

## CI/CD pipeline and infrastructure

Everything needed to build, test, and deploy the function lives under `Infrastructure/`:

* `pipeline.yaml` - AWS CodePipeline definition with four stages:
  1. **Source** - pulls the repo via a CodeStar GitHub connection (`master` branch).
  2. **BuildAndTest** - runs `buildspec-build-test.yml` (CodeBuild project `dotnet-lambda-build-test`).
  3. **TerraformCheck** - runs `buildspec-terraform-plan.yml` (CodeBuild project `dotnet-lambda-terraform-plan`)
     and exposes whether changes are pending via the `TerraformPlanVars.TF_CHANGES_PENDING` pipeline variable.
  4. **DeployInfra** - runs `buildspec-terraform-apply.yml` (CodeBuild project `dotnet-lambda-terraform-apply`),
     skipped unless `TF_CHANGES_PENDING` is `true`.
* `pipeline-policy.json` - IAM policy for the pipeline/CodeBuild service role: CloudWatch Logs, the
  pipeline artifact bucket, the Terraform state bucket/lock table, and Lambda + Lambda execution role
  management, all scoped to `eu-west-2`.
* `buildspec-build-test.yml` - restores, lint-checks (`dotnet format --verify-no-changes`), builds,
  unit-tests, and packages the function with `dotnet lambda package` into `publish/function.zip`.
* `buildspec-terraform-plan.yml` - before running Terraform, checks (via `aws s3api head-bucket` /
  `aws dynamodb describe-table`) whether the remote state bucket and lock table already exist and, if
  not, bootstraps them from `Infrastructure/bootstrap`. It then runs `terraform plan` in
  `Infrastructure/` and exports `TF_CHANGES_PENDING`.
* `buildspec-terraform-apply.yml` - runs `terraform apply` against the saved plan.

### Terraform layout

* `Infrastructure/main.tf` - the `aws` provider and the S3 `backend` block used for the function's
  remote state (bucket/key/region/lock table are placeholders — see below).
* `Infrastructure/lambda.tf` - the actual deployment: the Lambda execution IAM role/policy, the
  function itself (sourced from `../../publish/function.zip`, matching `aws-lambda-tools-defaults.json`),
  its CloudWatch log group, and an EventBridge schedule rule (`rate(1 day)` by default) that triggers it.
* `Infrastructure/bootstrap/main.tf` - a separate, local-state-only config that creates the Terraform
  state S3 bucket and DynamoDB lock table. It's isolated from `main.tf` deliberately, since a backend
  can't be initialized against a bucket that doesn't exist yet; `buildspec-terraform-plan.yml` applies it
  automatically the first time it detects the bucket/table are missing.

### Placeholders to fill in before deploying

`pipeline.yaml`, `pipeline-policy.json`, and the Terraform files use placeholder values that need real
AWS resource identifiers before the pipeline can run: `ACCOUNT_ID` and `CONNECTION_ID` (CodeStar
connection), `YOUR_TF_STATE_BUCKET` / `YOUR_TF_LOCK_TABLE` (Terraform backend), and `YOUR_SNS_TOPIC_ARN`
(remediation notifications).

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
