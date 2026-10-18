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
  allowed_account_ids = ["008971632699"]
  default_tags {
    tags = { Project = "novapay", Purpose = "terraform-state", ManagedBy = "Terraform" }
  }
}

resource "aws_s3_bucket" "state" {
  bucket        = "novapay-tfstate-008971632699-eu-west-1"
  force_destroy = false
  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_policy" "state" {
  bucket = aws_s3_bucket.state.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "DenyInsecureTransport"
      Effect    = "Deny"
      Principal = "*" # Deny applies to every caller; this grants no access.
      Action    = "s3:*"
      Resource  = [aws_s3_bucket.state.arn, "${aws_s3_bucket.state.arn}/*"]
      Condition = { Bool = { "aws:SecureTransport" = "false" } }
    }]
  })
}

# Attach only to a future Terraform operator role, never the application or
# application-deployment role. Bootstrap state remains outside this policy.
resource "aws_iam_policy" "staging_state" {
  name        = "novapay-staging-terraform-state"
  description = "Access only to the staging Terraform state and its S3 lock"
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect    = "Allow"
        Action    = ["s3:ListBucket"]
        Resource  = aws_s3_bucket.state.arn
        Condition = { StringEquals = { "s3:prefix" = ["staging/terraform.tfstate", "staging/terraform.tfstate.tflock"] } }
      },
      {
        Effect   = "Allow"
        Action   = ["s3:GetObject", "s3:PutObject"]
        Resource = "${aws_s3_bucket.state.arn}/staging/terraform.tfstate"
      },
      {
        Effect   = "Allow"
        Action   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
        Resource = "${aws_s3_bucket.state.arn}/staging/terraform.tfstate.tflock"
      }
    ]
  })
}

output "state_bucket" {
  value = aws_s3_bucket.state.id
}
