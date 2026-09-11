# CAV.BillingView

An AWS Lambda function that automatically provisions a scoped AWS Billing View and shares it with a newly enrolled account when AWS Control Tower raises a successful `CreateManagedAccount` event.

## Overview

When a new account is created through AWS Control Tower, this function:

1. Receives the `CreateManagedAccount` CloudWatch Event from Control Tower
2. Validates the event state is `SUCCEEDED` and the account is a production workload (account name contains `PROD`)
3. Creates an AWS Billing View filtered to the new account's linked account ID
4. Shares the billing view with the new account via AWS RAM (Resource Access Manager)

## Architecture

- **Trigger**: CloudWatch Event — `CreateManagedAccount` from AWS Control Tower
- **Runtime**: .NET 10 (AOT compiled)
- **Handler**: `CAV.BillingView::CAV.BillingView.Function::FunctionHandler`
- **AWS Services**: Amazon Billing, AWS RAM

## Project Structure

- `Function.cs` — Lambda handler and billing view provisioning logic
- `Models/ControlTowerEvent.cs` — Strongly-typed model for the Control Tower event payload
- `aws-lambda-tools-defaults.json` — Default deployment settings for the AWS Lambda Tools CLI and Visual Studio

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Amazon.Lambda.Tools](https://github.com/aws/aws-extensions-for-dotnet-cli#aws-lambda-amazonlambdatools)
- AWS credentials with permissions to deploy Lambda functions and access Amazon Billing and AWS RAM

## Getting Started

Install the Lambda CLI tools if not already installed:
```
dotnet tool install -g Amazon.Lambda.Tools
```

If already installed, check for updates:
```
dotnet tool update -g Amazon.Lambda.Tools
```

## Running Tests

```
dotnet test
```

## Deployment

```
dotnet lambda deploy-function
```

Or from Visual Studio, right-click the project in Solution Explorer and select **Publish to AWS Lambda**.

## Required IAM Permissions

The Lambda execution role requires:
- `billing:CreateBillingView`
- `ram:CreateResourceShare`
