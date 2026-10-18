terraform {
  required_version = ">= 1.12, < 2.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

provider "aws" {
  region              = "eu-west-1"
  allowed_account_ids = [var.aws_account_id]
  default_tags {
    tags = {
      Project     = "novapay"
      Environment = "staging"
      ManagedBy   = "Terraform"
    }
  }
}

variable "aws_account_id" {
  description = "Explicit target account guard. Authenticate with AWS_PROFILE=novapay."
  type        = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.aws_account_id))
    error_message = "Supply the twelve-digit AWS account ID."
  }
}

data "aws_availability_zones" "available" {
  state = "available"
  filter {
    name   = "zone-type"
    values = ["availability-zone"]
  }
}

locals {
  name = "novapay-staging"
}
