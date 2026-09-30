variable "notification_email" {
  description = "Email address (e.g. a team distribution list) that receives the run summary"
  type        = string

  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.notification_email))
    error_message = "notification_email must be a valid email address."
  }
}

resource "aws_sns_topic" "notifications" {
  name              = "${var.function_name}-notifications"
  kms_master_key_id = "alias/aws/sns"
}

# AWS emails a confirmation link to this address on first apply. Nothing is delivered
# until it is clicked; until then the subscription shows as "PendingConfirmation".
resource "aws_sns_topic_subscription" "email" {
  topic_arn = aws_sns_topic.notifications.arn
  protocol  = "email"
  endpoint  = var.notification_email
}

output "sns_topic_arn" {
  value = aws_sns_topic.notifications.arn
}
