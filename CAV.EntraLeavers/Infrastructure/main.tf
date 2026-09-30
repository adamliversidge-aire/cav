terraform {
  required_version = ">= 1.9.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    bucket         = "YOUR_TF_STATE_BUCKET"
    key            = "cav-entra-leavers/terraform.tfstate"
    region         = "eu-west-2"
    dynamodb_table = "YOUR_TF_LOCK_TABLE"
    encrypt        = true
  }
}

provider "aws" {
  region = var.region
}

variable "region" {
  description = "AWS region to deploy into"
  type        = string
  default     = "eu-west-2"
}
