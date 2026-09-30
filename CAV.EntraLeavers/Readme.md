# CAV.EntraLeavers

Scheduled Lambda that disables IAM users belonging to people who have stopped signing in to Entra ID.

## What it does

On each run (daily by default), the function:

1. Reads the Entra app registration (tenant ID, client ID, client secret) from Secrets Manager.
2. Gets a Microsoft Graph token using the OAuth2 **client-credentials** flow.
3. Queries Graph for users whose `signInActivity/lastSignInDateTime` is older than `INACTIVE_DAYS` (default 90), following `@odata.nextLink` paging.
   - Each result is re-checked against its non-interactive and last-successful sign-in times, so anyone still active through token refreshes is left alone.
4. Lists every IAM user and matches them to those Entra users, case-insensitively. A match is either:
   - the IAM **user name** equals the Entra `mail` or `userPrincipalName`, or
   - the IAM user's **`email` tag** (key set by `IAM_EMAIL_TAG_KEY`) equals it.
5. Disables each matched IAM user in a **reversible** way:
   - deletes the console login profile (password),
   - sets every Active access key to Inactive,
   - tags the user `DisabledBy=cav-entra-leavers` and `DisabledOn=yyyy-MM-dd`.
6. Publishes one summary to the SNS topic, which emails the team. The email lists each user, what was matched, their last sign-in, the actions taken, and any failures.
   - Users who were already disabled are not re-reported.
   - Nothing is sent when there is nothing to do.

Two safety features protect against accidental mass disabling:

- **`DRY_RUN`** is on by default. The function then emails a `[DRY RUN]` report of what it *would* disable and makes no IAM changes.
- **`MAX_DISABLE_PER_RUN`** (default 25) limits a live run. If more users than this match, the run disables nobody and sends an `ABORTED` alert instead.

> **Users who have never signed in to Entra are not included.** Graph's filter skips accounts with no sign-in record. This is deliberate: it avoids disabling people whose accounts were only just created.

## Project layout

```
CAV.EntraLeavers/
  Function.cs                      Handler: orchestration only
  Setup.cs                         DI registrations (Annotations [LambdaStartup])
  Configuration/LeaversOptions.cs  Environment variable parsing and validation
  Entra/EntraCredentials.cs        Secrets Manager JSON -> credentials
  Entra/EntraGraphClient.cs        Token and paged users query (plain HttpClient)
  Entra/Models/GraphUser.cs        Graph user and sign-in activity
  Matching/IamUserMatcher.cs       Email -> IAM user matching (pure)
  Iam/IamUserDisabler.cs           Remove console access, deactivate keys, tag
  Notifications/SummaryBuilder.cs  SNS subject and body (pure)
  Infrastructure/                  Terraform and CodePipeline/CodeBuild
CAV.EntraLeavers.Tests/            xUnit + NSubstitute
```

`serverless.template` is written automatically by Amazon.Lambda.Annotations on every build. Deployment uses Terraform, not this file.

## Entra setup (one-off)

1. In Entra ID, go to **App registrations** and create a new registration, e.g. `cav-entra-leavers`.
2. Under **API permissions**, add these **Microsoft Graph application permissions**, then click **Grant admin consent**:
   - `User.Read.All`
   - `AuditLog.Read.All` (needed for `signInActivity`)
3. Under **Certificates & secrets**, create a client secret and note when it expires.
4. The tenant needs Entra ID P1 or P2 for `signInActivity` to be populated.

## Configuration

| Env var | Terraform variable | Default | Purpose |
|---|---|---|---|
| `INACTIVE_DAYS` | `inactive_days` | `90` | How many days without an Entra sign-in before an IAM user is disabled |
| `DRY_RUN` | `dry_run` | `true` | Report only. Must be exactly `false` to make changes |
| `MAX_DISABLE_PER_RUN` | `max_disable_per_run` | `25` | Safety limit per live run |
| `SNS_TOPIC_ARN` | (set from the created topic) | (required) | Topic the run summary is published to |
| — | `notification_email` | (required) | Address subscribed to the topic, e.g. the team's distribution list |
| `ENTRA_SECRET_ARN` | (set from the created secret) | (required) | Secrets Manager secret holding the Entra credentials |
| `IAM_EMAIL_TAG_KEY` | `iam_email_tag_key` | `email` | IAM tag key used when the IAM user name isn't the email |

The schedule is set with `schedule_expression` (default `rate(1 day)`).

### Setting the Entra secret

Terraform creates an empty secret. Its value is set separately so the client secret never appears in Terraform state:

```bash
aws secretsmanager put-secret-value \
  --secret-id cav-entra-leavers/entra-app \
  --secret-string '{"tenantId":"<tenant-guid>","clientId":"<app-guid>","clientSecret":"<secret>"}'
```

Run the same command again whenever the client secret is rotated.

### Notification topic

`Infrastructure/sns.tf` creates:

- the `cav-entra-leavers-notifications` topic, encrypted with the AWS-managed SNS key
- an email subscription to `notification_email`

After the first apply, AWS emails a confirmation link to that address. Nothing is delivered until someone clicks it. To change the recipient, change `notification_email` and apply; Terraform replaces the subscription, and the new address has to confirm too.

## Required IAM permissions (execution role)

| Permission | Resource |
|---|---|
| `iam:ListUsers` | `*` |
| `iam:ListUserTags`, `iam:GetLoginProfile`, `iam:DeleteLoginProfile`, `iam:ListAccessKeys`, `iam:UpdateAccessKey`, `iam:TagUser` | `user/*` |
| `secretsmanager:GetSecretValue` | the Entra secret |
| `sns:Publish` | the topic |
| CloudWatch Logs | the function's log group |

## Rolling out

1. Deploy with the default `dry_run = true`, set the secret, and confirm the SNS subscription email.
2. Invoke the function manually, or wait for the schedule:
   ```bash
   aws lambda invoke --function-name cav-entra-leavers --payload '{}' --cli-binary-format raw-in-base64-out out.json
   ```
3. Check the `[DRY RUN]` email and `/aws/lambda/cav-entra-leavers` in CloudWatch Logs.
4. Once the list looks right, set `dry_run = false` and deploy.

## Re-enabling a user

Nothing is deleted except the console password:

- **Console access:** `aws iam create-login-profile --user-name <user> --password '<temp>' --password-reset-required`
- **Access keys:** `aws iam update-access-key --user-name <user> --access-key-id <id> --status Active`
- **Tags:** `aws iam untag-user --user-name <user> --tag-keys DisabledBy DisabledOn`

If the person's Entra account is still stale, the next run will disable them again. Get them to sign in to Entra first, or remove their email tag.

## Tests

```bash
dotnet test CAV.EntraLeavers.Tests
```

## Deploy

CI/CD is in `Infrastructure/` and uses the same CodePipeline/CodeBuild layout as CAV.AccountUsage:

1. Source
2. Build & test, which also packages `publish/function.zip`
3. Terraform plan
4. Terraform apply, which runs only if the plan found changes

Before the first run, fill in the placeholders:

- `ACCOUNT_ID`, `CONNECTION_ID`
- `YOUR_TF_STATE_BUCKET`, `YOUR_TF_LOCK_TABLE`
- `YOUR_NOTIFICATION_EMAIL`: set as `TF_VAR_notification_email` in `buildspec-terraform-plan.yml`. It can also be set on the CodeBuild project's environment variables instead.

To deploy manually:

```bash
dotnet lambda package --project-location CAV.EntraLeavers --configuration Release --output-package ./publish/function.zip
terraform -chdir=CAV.EntraLeavers/Infrastructure init
terraform -chdir=CAV.EntraLeavers/Infrastructure apply -var notification_email=team@example.com
```
