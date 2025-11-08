# Backend Setup Resources
# 이 파일은 S3 버킷과 DynamoDB 테이블을 생성합니다.
# 최초 1회만 실행하고, 이후에는 backend.tf를 활성화하세요.

# S3 Bucket for Terraform State
resource "aws_s3_bucket" "terraform_state" {
  bucket = "${var.project_name}-terraform-state-${var.environment}"

  # 실수로 삭제 방지
  lifecycle {
    prevent_destroy = false # 개발 환경에서는 false, 프로덕션에서는 true로 변경
  }

  tags = {
    Name        = "${var.project_name}-terraform-state"
    Environment = var.environment
    Project     = var.project_name
    Purpose     = "Terraform State Storage"
  }
}

# S3 Bucket Versioning
resource "aws_s3_bucket_versioning" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id

  versioning_configuration {
    status = "Enabled"
  }
}

# S3 Bucket Encryption
resource "aws_s3_bucket_server_side_encryption_configuration" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# S3 Bucket Public Access Block
resource "aws_s3_bucket_public_access_block" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# DynamoDB Table for State Locking
resource "aws_dynamodb_table" "terraform_lock" {
  name         = "${var.project_name}-terraform-lock-${var.environment}"
  billing_mode = "PAY_PER_REQUEST" # 온디맨드 요금제 (비용 효율적)
  hash_key     = "LockID"

  attribute {
    name = "LockID"
    type = "S"
  }

  # 실수로 삭제 방지
  lifecycle {
    prevent_destroy = false # 개발 환경에서는 false, 프로덕션에서는 true로 변경
  }

  tags = {
    Name        = "${var.project_name}-terraform-lock"
    Environment = var.environment
    Project     = var.project_name
    Purpose     = "Terraform State Locking"
  }
}

# Outputs
output "terraform_state_bucket" {
  description = "S3 bucket name for Terraform state"
  value       = aws_s3_bucket.terraform_state.id
}

output "terraform_lock_table" {
  description = "DynamoDB table name for Terraform state locking"
  value       = aws_dynamodb_table.terraform_lock.id
}

output "backend_config" {
  description = "Backend configuration to use in backend.tf"
  value       = <<-EOT
    terraform {
      backend "s3" {
        bucket         = "${aws_s3_bucket.terraform_state.id}"
        key            = "${var.environment}/terraform.tfstate"
        region         = "${var.aws_region}"
        encrypt        = true
        dynamodb_table = "${aws_dynamodb_table.terraform_lock.id}"
      }
    }
  EOT
}
