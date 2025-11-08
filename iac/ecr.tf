# ECR Repository for Rps Application
resource "aws_ecr_repository" "rps" {
  name                 = "${var.project_name}-app"
  image_tag_mutability = "MUTABLE"

  # Enable image scanning on push for security vulnerabilities
  image_scanning_configuration {
    scan_on_push = true
  }

  # Enable encryption at rest
  encryption_configuration {
    encryption_type = "AES256"
  }

  tags = {
    Name        = "${var.project_name}-ecr"
    Environment = var.environment
    Project     = var.project_name
  }
}

# Lifecycle policy to retain only the last 10 images
resource "aws_ecr_lifecycle_policy" "rps" {
  repository = aws_ecr_repository.rps.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep last 10 images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = 10
        }
        action = {
          type = "expire"
        }
      }
    ]
  })
}
