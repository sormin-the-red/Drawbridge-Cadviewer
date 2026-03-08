variable "aws_region" {
  description = "AWS region to deploy into"
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Deployment environment (e.g. prod, staging)"
  type        = string
  default     = "prod"
}

variable "aps_client_id" {
  description = "Autodesk Platform Services OAuth client ID"
  type        = string
  sensitive   = true
}

variable "aps_client_secret" {
  description = "Autodesk Platform Services OAuth client secret"
  type        = string
  sensitive   = true
}
