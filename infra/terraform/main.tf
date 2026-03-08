terraform {
  required_version = ">= 1.7"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Uncomment to use S3 backend for state
  # backend "s3" {
  #   bucket = "your-tf-state-bucket"
  #   key    = "drawbridge/terraform.tfstate"
  #   region = "us-east-1"
  # }
}

provider "aws" {
  region = var.aws_region
}

locals {
  prefix = "drawbridge-${var.environment}"
}

# ── S3 ────────────────────────────────────────────────────────────────────────

# resource "aws_s3_bucket" "drawings" { ... }
# resource "aws_s3_bucket" "spa" { ... }

# ── CloudFront ────────────────────────────────────────────────────────────────

# resource "aws_cloudfront_distribution" "cdn" { ... }

# ── SQS ───────────────────────────────────────────────────────────────────────

# resource "aws_sqs_queue" "jobs_dlq" { ... }
# resource "aws_sqs_queue" "jobs" { ... }

# ── DynamoDB ──────────────────────────────────────────────────────────────────

# resource "aws_dynamodb_table" "products" { ... }
# resource "aws_dynamodb_table" "versions" { ... }
# resource "aws_dynamodb_table" "jobs" { ... }
# resource "aws_dynamodb_table" "annotations" { ... }
# resource "aws_dynamodb_table" "markups" { ... }

# ── Lambda + API Gateway ───────────────────────────────────────────────────────

# resource "aws_lambda_function" "api" { ... }
# resource "aws_apigatewayv2_api" "http" { ... }

# ── IAM ───────────────────────────────────────────────────────────────────────

# resource "aws_iam_user" "conversion_worker" { ... }
# resource "aws_iam_user_policy" "conversion_worker" { ... }

# ── SSM ───────────────────────────────────────────────────────────────────────

# resource "aws_ssm_parameter" "aps_client_id" { ... }
# resource "aws_ssm_parameter" "aps_client_secret" { ... }
