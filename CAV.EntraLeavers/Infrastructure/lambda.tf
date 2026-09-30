data "aws_caller_identity" "current" {}

variable "function_name" {
  description = "Name of the Lambda function"
  type        = string
  default     = "cav-entra-leavers"
}

variable "schedule_expression" {
  description = "EventBridge schedule expression for how often the function runs"
  type        = string
  default     = "rate(1 day)"
}

variable "log_retention_days" {
  description = "Number of days to retain CloudWatch logs"
  type        = number
  default     = 30
}

variable "inactive_days" {
  description = "Disable IAM users whose Entra account has not signed in for this many days"
  type        = number
  default     = 90

  validation {
    condition     = var.inactive_days >= 1 && floor(var.inactive_days) == var.inactive_days
    error_message = "inactive_days must be a positive whole number."
  }
}

variable "dry_run" {
  description = "When true, the function only reports which users it would disable. Set to false once the report looks right."
  type        = bool
  default     = true
}

variable "max_disable_per_run" {
  description = "Safety limit: a live run disables nobody (and alerts) if more users than this match"
  type        = number
  default     = 25
}

variable "iam_email_tag_key" {
  description = "IAM user tag holding the user's email, used when the IAM user name is not the email"
  type        = string
  default     = "email"
}

locals {
  lambda_package_path = "${path.module}/../../publish/function.zip"
}

# The secret value is set out-of-band so the client secret never lands in Terraform state:
#   aws secretsmanager put-secret-value --secret-id cav-entra-leavers/entra-app \
#     --secret-string '{"tenantId":"...","clientId":"...","clientSecret":"..."}'
resource "aws_secretsmanager_secret" "entra" {
  name        = "${var.function_name}/entra-app"
  description = "Entra app registration (tenantId, clientId, clientSecret) used by ${var.function_name}"
}

resource "aws_iam_role" "lambda_execution" {
  name = "${var.function_name}-lambda-execution"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = "sts:AssumeRole"
        Principal = {
          Service = "lambda.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_role_policy" "lambda_execution" {
  name = "${var.function_name}-execution"
  role = aws_iam_role.lambda_execution.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "ListUsers"
        Effect   = "Allow"
        Action   = "iam:ListUsers"
        Resource = "*"
      },
      {
        Sid    = "DisableUsers"
        Effect = "Allow"
        Action = [
          "iam:ListUserTags",
          "iam:GetLoginProfile",
          "iam:DeleteLoginProfile",
          "iam:ListAccessKeys",
          "iam:UpdateAccessKey",
          "iam:TagUser"
        ]
        Resource = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:user/*"
      },
      {
        Sid      = "ReadEntraSecret"
        Effect   = "Allow"
        Action   = "secretsmanager:GetSecretValue"
        Resource = aws_secretsmanager_secret.entra.arn
      },
      {
        Sid      = "PublishNotifications"
        Effect   = "Allow"
        Action   = "sns:Publish"
        Resource = aws_sns_topic.notifications.arn
      },
      {
        Sid    = "Logging"
        Effect = "Allow"
        Action = [
          "logs:CreateLogGroup",
          "logs:CreateLogStream",
          "logs:PutLogEvents"
        ]
        Resource = "${aws_cloudwatch_log_group.lambda.arn}:*"
      }
    ]
  })
}

resource "aws_cloudwatch_log_group" "lambda" {
  name              = "/aws/lambda/${var.function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "this" {
  function_name    = var.function_name
  role             = aws_iam_role.lambda_execution.arn
  handler          = "CAV.EntraLeavers::CAV.EntraLeavers.Function_FunctionHandler_Generated::FunctionHandler"
  runtime          = "dotnet10"
  architectures    = ["x86_64"]
  memory_size      = 512
  timeout          = 120
  filename         = local.lambda_package_path
  source_code_hash = filebase64sha256(local.lambda_package_path)

  environment {
    variables = {
      INACTIVE_DAYS       = tostring(var.inactive_days)
      DRY_RUN             = tostring(var.dry_run)
      MAX_DISABLE_PER_RUN = tostring(var.max_disable_per_run)
      SNS_TOPIC_ARN       = aws_sns_topic.notifications.arn
      ENTRA_SECRET_ARN    = aws_secretsmanager_secret.entra.arn
      IAM_EMAIL_TAG_KEY   = var.iam_email_tag_key
    }
  }

  depends_on = [aws_cloudwatch_log_group.lambda]
}

resource "aws_cloudwatch_event_rule" "schedule" {
  name                = "${var.function_name}-schedule"
  schedule_expression = var.schedule_expression
}

resource "aws_cloudwatch_event_target" "lambda" {
  rule = aws_cloudwatch_event_rule.schedule.name
  arn  = aws_lambda_function.this.arn
}

resource "aws_lambda_permission" "allow_eventbridge" {
  statement_id  = "AllowEventBridgeInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.this.function_name
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.schedule.arn
}

output "lambda_function_arn" {
  value = aws_lambda_function.this.arn
}

output "lambda_function_name" {
  value = aws_lambda_function.this.function_name
}

output "entra_secret_arn" {
  value = aws_secretsmanager_secret.entra.arn
}
