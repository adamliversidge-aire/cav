data "aws_caller_identity" "current" {}

variable "function_name" {
  description = "Name of the CAV Account Usage Lambda function"
  type        = string
  default     = "cav-account-usage"
}

variable "sns_topic_arn" {
  description = "ARN of the SNS topic the function publishes remediation notifications to"
  type        = string
  default     = "YOUR_SNS_TOPIC_ARN"
}

variable "schedule_expression" {
  description = "EventBridge schedule expression that triggers the function"
  type        = string
  default     = "rate(1 day)"
}

variable "log_retention_days" {
  description = "CloudWatch Logs retention for the function's log group"
  type        = number
  default     = 30
}

locals {
  lambda_package_path = "${path.module}/../../publish/function.zip"
}

resource "aws_iam_role" "lambda_execution" {
  name = "${var.function_name}-lambda-execution"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect    = "Allow"
        Action    = "sts:AssumeRole"
        Principal = { Service = "lambda.amazonaws.com" }
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
        Sid    = "CredentialReport"
        Effect = "Allow"
        Action = [
          "iam:ListUsers",
          "iam:GenerateCredentialReport",
          "iam:GetCredentialReport"
        ]
        Resource = "*"
      },
      {
        Sid    = "RemediateUsers"
        Effect = "Allow"
        Action = [
          "iam:PutUserPolicy",
          "iam:ListAccessKeys",
          "iam:UpdateAccessKey"
        ]
        Resource = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:user/*"
      },
      {
        Sid      = "PublishNotifications"
        Effect   = "Allow"
        Action   = "sns:Publish"
        Resource = var.sns_topic_arn
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
  function_name = var.function_name
  role          = aws_iam_role.lambda_execution.arn
  handler       = "CAV.AccountUsage::CAV.AccountUsage.Function::FunctionHandler"
  runtime       = "dotnet10"
  architectures = ["x86_64"]
  memory_size   = 512
  timeout       = 30

  filename         = local.lambda_package_path
  source_code_hash = filebase64sha256(local.lambda_package_path)

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
