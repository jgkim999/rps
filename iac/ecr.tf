# ECR Repository for Rps Application
resource "aws_ecr_repository" "rps" {
  name                 = "${var.project_name}-app"
  image_tag_mutability = var.ecr_image_tag_mutability

  # Enable image scanning on push for security vulnerabilities
  image_scanning_configuration {
    scan_on_push = var.ecr_scan_on_push
  }

  # Enable encryption at rest
  encryption_configuration {
    encryption_type = var.ecr_encryption_type
  }

  tags = {
    Name        = "${var.project_name}-ecr"
    Environment = var.environment
    Project     = var.project_name
  }
}

# Lifecycle policy to retain only the last N images
resource "aws_ecr_lifecycle_policy" "rps" {
  repository = aws_ecr_repository.rps.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep last ${var.ecr_image_retention_count} images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = var.ecr_image_retention_count
        }
        action = {
          type = "expire"
        }
      }
    ]
  })
}

# ECR Repository for Game Server
resource "aws_ecr_repository" "game_server" {
  name                 = "${var.project_name}-game-server"
  image_tag_mutability = var.ecr_image_tag_mutability

  image_scanning_configuration {
    scan_on_push = var.ecr_scan_on_push
  }

  encryption_configuration {
    encryption_type = var.ecr_encryption_type
  }

  tags = {
    Name        = "${var.project_name}-game-server-ecr"
    Environment = var.environment
    Project     = var.project_name
  }
}

resource "aws_ecr_lifecycle_policy" "game_server" {
  repository = aws_ecr_repository.game_server.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep last ${var.ecr_image_retention_count} images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = var.ecr_image_retention_count
        }
        action = {
          type = "expire"
        }
      }
    ]
  })
}
