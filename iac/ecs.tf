# ECS Cluster
resource "aws_ecs_cluster" "main" {
  name = "${var.project_name}-${var.environment}-cluster"

  setting {
    name  = "containerInsights"
    value = var.enable_container_insights ? "enabled" : "disabled"
  }

  tags = {
    Name        = "${var.project_name}-${var.environment}-cluster"
    Environment = var.environment
    Project     = var.project_name
  }
}

# CloudWatch Log Group for ECS Tasks
resource "aws_cloudwatch_log_group" "ecs" {
  name              = "/ecs/${var.project_name}/${var.environment}"
  retention_in_days = var.cloudwatch_log_retention_days

  tags = {
    Name        = "${var.project_name}-${var.environment}-ecs-logs"
    Environment = var.environment
    Project     = var.project_name
  }
}

# ECS Task Definition
resource "aws_ecs_task_definition" "app" {
  family                   = "${var.project_name}-${var.environment}-app"
  network_mode             = var.ecs_network_mode
  requires_compatibilities = var.ecs_requires_compatibilities
  cpu                      = var.ecs_task_cpu
  memory                   = var.ecs_task_memory
  execution_role_arn       = aws_iam_role.ecs_task_execution_role.arn
  task_role_arn            = aws_iam_role.ecs_task_role.arn

  container_definitions = jsonencode([
    {
      name      = "${var.project_name}-app"
      image     = "${aws_ecr_repository.rps.repository_url}:latest"
      essential = true

      portMappings = [
        {
          containerPort = var.container_port
          protocol      = "tcp"
        }
      ]

      environment = [
        {
          name  = "ASPNETCORE_ENVIRONMENT"
          value = var.aspnetcore_environment
        },
        {
          name  = "ASPNETCORE_URLS"
          value = "http://+:${var.container_port}"
        },
        {
          name  = "Redis__SignalRBackplane"
          value = "${aws_elasticache_replication_group.redis.primary_endpoint_address}:${var.redis_port}"
        },
        {
          name  = "Redis__FusionCacheRedisCache"
          value = "${aws_elasticache_replication_group.redis.primary_endpoint_address}:${var.redis_port}"
        },
        {
          name  = "Redis__FusionCacheBackplane"
          value = "${aws_elasticache_replication_group.redis.primary_endpoint_address}:${var.redis_port}"
        }
      ]

      secrets = [
        {
          name      = "RabbitMQ__Host"
          valueFrom = "${aws_secretsmanager_secret.rabbitmq_credentials.arn}:host::"
        },
        {
          name      = "RabbitMQ__Username"
          valueFrom = "${aws_secretsmanager_secret.rabbitmq_credentials.arn}:username::"
        },
        {
          name      = "RabbitMQ__Password"
          valueFrom = "${aws_secretsmanager_secret.rabbitmq_credentials.arn}:password::"
        }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.ecs.name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "ecs"
        }
      }

      healthCheck = {
        command     = ["CMD-SHELL", "curl -f http://localhost:${var.container_port}/health || exit 1"]
        interval    = var.ecs_health_check_interval
        timeout     = var.ecs_health_check_timeout
        retries     = var.ecs_health_check_retries
        startPeriod = var.ecs_health_check_start_period
      }
    }
  ])

  tags = {
    Name        = "${var.project_name}-${var.environment}-task-definition"
    Environment = var.environment
    Project     = var.project_name
  }
}

# ECS Service
resource "aws_ecs_service" "app" {
  name            = "${var.project_name}-${var.environment}-service"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.app.arn
  desired_count   = var.ecs_desired_count
  launch_type     = var.ecs_launch_type

  network_configuration {
    subnets          = aws_subnet.private[*].id
    security_groups  = [aws_security_group.ecs.id]
    assign_public_ip = var.ecs_assign_public_ip
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.ecs.arn
    container_name   = "${var.project_name}-app"
    container_port   = var.container_port
  }

  health_check_grace_period_seconds = var.ecs_health_check_grace_period

  deployment_maximum_percent         = var.ecs_deployment_maximum_percent
  deployment_minimum_healthy_percent = var.ecs_deployment_minimum_healthy_percent

  depends_on = [
    aws_lb_listener.http,
    aws_iam_role_policy_attachment.ecs_task_execution_role_policy
  ]

  tags = {
    Name        = "${var.project_name}-${var.environment}-service"
    Environment = var.environment
    Project     = var.project_name
  }
}

# Outputs
output "ecs_cluster_id" {
  description = "ID of the ECS cluster"
  value       = aws_ecs_cluster.main.id
}

output "ecs_cluster_name" {
  description = "Name of the ECS cluster"
  value       = aws_ecs_cluster.main.name
}

output "ecs_task_definition_arn" {
  description = "ARN of the ECS task definition"
  value       = aws_ecs_task_definition.app.arn
}

output "ecs_task_definition_family" {
  description = "Family of the ECS task definition"
  value       = aws_ecs_task_definition.app.family
}

output "ecs_log_group_name" {
  description = "Name of the CloudWatch log group for ECS"
  value       = aws_cloudwatch_log_group.ecs.name
}

output "ecs_service_name" {
  description = "Name of the ECS service"
  value       = aws_ecs_service.app.name
}

output "ecs_service_id" {
  description = "ID of the ECS service"
  value       = aws_ecs_service.app.id
}

# CloudWatch Log Group for Game Server
resource "aws_cloudwatch_log_group" "game_server" {
  name              = "/ecs/${var.project_name}/${var.environment}/game-server"
  retention_in_days = var.cloudwatch_log_retention_days

  tags = {
    Name        = "${var.project_name}-${var.environment}-game-server-logs"
    Environment = var.environment
    Project     = var.project_name
  }
}

# ECS Task Definition for Game Server
resource "aws_ecs_task_definition" "game_server" {
  family                   = "${var.project_name}-${var.environment}-game-server"
  network_mode             = var.ecs_network_mode
  requires_compatibilities = var.ecs_requires_compatibilities
  cpu                      = var.game_server_task_cpu
  memory                   = var.game_server_task_memory
  execution_role_arn       = aws_iam_role.ecs_task_execution_role.arn
  task_role_arn            = aws_iam_role.ecs_task_role.arn

  container_definitions = jsonencode([
    {
      name      = "${var.project_name}-game-server"
      image     = "${aws_ecr_repository.game_server.repository_url}:latest"
      essential = true

      environment = [
        {
          name  = "ASPNETCORE_ENVIRONMENT"
          value = var.aspnetcore_environment
        },
        {
          name  = "Redis__Host"
          value = "${aws_elasticache_replication_group.redis.configuration_endpoint_address}:${var.redis_port}"
        }
      ]

      secrets = [
        {
          name      = "RabbitMQ__Host"
          valueFrom = "${aws_secretsmanager_secret.rabbitmq_credentials.arn}:host::"
        },
        {
          name      = "RabbitMQ__Username"
          valueFrom = "${aws_secretsmanager_secret.rabbitmq_credentials.arn}:username::"
        },
        {
          name      = "RabbitMQ__Password"
          valueFrom = "${aws_secretsmanager_secret.rabbitmq_credentials.arn}:password::"
        }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.game_server.name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "game-server"
        }
      }
    }
  ])

  tags = {
    Name        = "${var.project_name}-${var.environment}-game-server-task"
    Environment = var.environment
    Project     = var.project_name
  }
}

# ECS Service for Game Server (Message Consumer)
resource "aws_ecs_service" "game_server" {
  name            = "${var.project_name}-${var.environment}-game-server"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.game_server.arn
  desired_count   = var.game_server_desired_count
  launch_type     = var.ecs_launch_type

  network_configuration {
    subnets          = aws_subnet.private[*].id
    security_groups  = [aws_security_group.ecs.id]
    assign_public_ip = var.ecs_assign_public_ip
  }

  deployment_maximum_percent         = var.ecs_deployment_maximum_percent
  deployment_minimum_healthy_percent = var.ecs_deployment_minimum_healthy_percent

  depends_on = [
    aws_iam_role_policy_attachment.ecs_task_execution_role_policy
  ]

  tags = {
    Name        = "${var.project_name}-${var.environment}-game-server"
    Environment = var.environment
    Project     = var.project_name
  }
}

# Outputs for Game Server
output "game_server_ecr_repository_url" {
  description = "URL of the Game Server ECR repository"
  value       = aws_ecr_repository.game_server.repository_url
}

output "game_server_task_definition_arn" {
  description = "ARN of the Game Server task definition"
  value       = aws_ecs_task_definition.game_server.arn
}

output "game_server_service_name" {
  description = "Name of the Game Server ECS service"
  value       = aws_ecs_service.game_server.name
}
