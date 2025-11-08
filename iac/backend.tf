# Terraform Backend Configuration
# S3 bucket and DynamoDB table must be created before using this backend

terraform {
  backend "s3" {
    bucket         = "rps-terraform-state"    # S3 버킷 이름 (변경 필요)
    key            = "prod/terraform.tfstate" # State 파일 경로
    region         = "ap-northeast-2"
    encrypt        = true                 # State 파일 암호화
    dynamodb_table = "rps-terraform-lock" # DynamoDB 테이블 이름 (변경 필요)

    # State 파일 버전 관리 및 잠금
    # DynamoDB 테이블은 LockID (String) 속성을 Primary Key로 가져야 함
  }
}
