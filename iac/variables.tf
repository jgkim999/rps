# region

variable "aws_region" {
  description = "AWS region"
  type        = string
  default     = "ap-northeast-2"
}

variable "project_name" {
  description = "Project name"
  type        = string
  default     = "rps"
}

variable "environment" {
  description = "Environment (dev, staging, prod)"
  type        = string
  default     = "prod"
}

variable "domain_name" {
  description = "Domain name for the application"
  type        = string
}

# vpc

variable "vpc_cidr" {
  description = "VPC CIDR block"
  type        = string
  default     = "10.0.0.0/16"
}

variable "availability_zones" {
  description = "List of availability zones"
  type        = list(string)
  default     = ["ap-northeast-2a", "ap-northeast-2c"]
}

variable "public_subnet_cidrs" {
  description = "CIDR blocks for public subnets"
  type        = list(string)
  default     = ["10.0.1.0/24", "10.0.2.0/24"]
}

variable "private_subnet_cidrs" {
  description = "CIDR blocks for private subnets"
  type        = list(string)
  default     = ["10.0.11.0/24", "10.0.12.0/24"]
}

variable "health_check_path" {
  description = "Health check path for ALB target group"
  type        = string
  default     = "/"
}

variable "health_check_interval" {
  description = "Health check interval in seconds"
  type        = number
  default     = 30
}

variable "health_check_timeout" {
  description = "Health check timeout in seconds"
  type        = number
  default     = 5
}

variable "healthy_threshold" {
  description = "Number of consecutive health checks successes required"
  type        = number
  default     = 2
}

variable "unhealthy_threshold" {
  description = "Number of consecutive health check failures required"
  type        = number
  default     = 3
}

variable "deregistration_delay" {
  description = "Time to wait before deregistering a target"
  type        = number
  default     = 30
}

# Redis/Valkey Configuration

variable "redis_node_type" {
  description = "ElastiCache Valkey node type"
  type        = string
  default     = "cache.t4g.micro"
}

variable "redis_num_node_groups" {
  description = "Number of Valkey node groups (shards) in cluster mode"
  type        = number
  default     = 2
}

variable "redis_replicas_per_node_group" {
  description = "Number of replica nodes per shard in cluster mode"
  type        = number
  default     = 0
}

variable "redis_engine_version" {
  description = "Valkey engine version"
  type        = string
  default     = "8.2"
}

variable "redis_port" {
  description = "Valkey port"
  type        = number
  default     = 6379
}

variable "redis_parameter_family" {
  description = "Valkey parameter group family"
  type        = string
  default     = "valkey8"
}

variable "redis_maxmemory_policy" {
  description = "Valkey maxmemory eviction policy"
  type        = string
  default     = "allkeys-lru"
}

variable "redis_maintenance_window" {
  description = "Valkey maintenance window"
  type        = string
  default     = "sun:05:00-sun:06:00"
}

variable "redis_snapshot_window" {
  description = "Valkey snapshot window"
  type        = string
  default     = "03:00-04:00"
}

variable "redis_snapshot_retention_limit" {
  description = "Number of days to retain Valkey snapshots"
  type        = number
  default     = 1
}

variable "redis_automatic_failover_enabled" {
  description = "Enable automatic failover for Valkey"
  type        = bool
  default     = true
}

variable "redis_multi_az_enabled" {
  description = "Enable Multi-AZ for Valkey"
  type        = bool
  default     = true
}

variable "redis_at_rest_encryption_enabled" {
  description = "Enable at-rest encryption for Valkey"
  type        = bool
  default     = false
}

variable "redis_transit_encryption_enabled" {
  description = "Enable transit encryption (TLS) for Valkey"
  type        = bool
  default     = false
}

variable "redis_auto_minor_version_upgrade" {
  description = "Enable automatic minor version upgrades for Valkey"
  type        = bool
  default     = false
}

# RabbitMQ Configuration

variable "rabbitmq_instance_type" {
  description = "Amazon MQ RabbitMQ instance type"
  type        = string
  default     = "mq.t3.micro"
}

variable "rabbitmq_username" {
  description = "RabbitMQ admin username"
  type        = string
  default     = "admin"
  sensitive   = true
}

variable "rabbitmq_password" {
  description = "RabbitMQ admin password (leave empty to auto-generate)"
  type        = string
  default     = ""
  sensitive   = true
}

variable "rabbitmq_engine_version" {
  description = "RabbitMQ engine version"
  type        = string
  default     = "3.13"
}

variable "rabbitmq_deployment_mode" {
  description = "RabbitMQ deployment mode (SINGLE_INSTANCE or CLUSTER_MULTI_AZ)"
  type        = string
  default     = "SINGLE_INSTANCE"

  validation {
    condition     = contains(["SINGLE_INSTANCE", "CLUSTER_MULTI_AZ"], var.rabbitmq_deployment_mode)
    error_message = "Deployment mode must be SINGLE_INSTANCE or CLUSTER_MULTI_AZ."
  }
}

variable "rabbitmq_publicly_accessible" {
  description = "Make RabbitMQ broker publicly accessible"
  type        = bool
  default     = false
}

variable "rabbitmq_auto_minor_version_upgrade" {
  description = "Enable automatic minor version upgrades for RabbitMQ"
  type        = bool
  default     = false
}

variable "rabbitmq_maintenance_day_of_week" {
  description = "Day of week for RabbitMQ maintenance"
  type        = string
  default     = "SUNDAY"
}

variable "rabbitmq_maintenance_time_of_day" {
  description = "Time of day for RabbitMQ maintenance (HH:MM format)"
  type        = string
  default     = "03:00"
}

variable "rabbitmq_maintenance_time_zone" {
  description = "Time zone for RabbitMQ maintenance"
  type        = string
  default     = "UTC"
}

variable "rabbitmq_logs_general" {
  description = "Enable general logs for RabbitMQ"
  type        = bool
  default     = true
}

variable "rabbitmq_guest_username" {
  description = "RabbitMQ guest username"
  type        = string
  default     = "guest"
}

variable "rabbitmq_guest_password" {
  description = "RabbitMQ guest password"
  type        = string
  default     = "guest"
  sensitive   = true
}

# CloudWatch Logs Configuration

variable "cloudwatch_log_retention_days" {
  description = "CloudWatch log retention in days"
  type        = number
  default     = 7
}

# ECS Configuration

variable "ecs_task_cpu" {
  description = "ECS task CPU units"
  type        = number
  default     = 256
}

variable "ecs_task_memory" {
  description = "ECS task memory in MB"
  type        = number
  default     = 512
}

variable "ecs_desired_count" {
  description = "Desired number of ECS tasks"
  type        = number
  default     = 2
}

variable "container_port" {
  description = "Container port for the application"
  type        = number
  default     = 5184
}

variable "game_server_task_cpu" {
  description = "Game Server ECS task CPU units"
  type        = number
  default     = 256
}

variable "game_server_task_memory" {
  description = "Game Server ECS task memory in MB"
  type        = number
  default     = 512
}

variable "game_server_desired_count" {
  description = "Desired number of Game Server ECS tasks"
  type        = number
  default     = 2
}

variable "ecs_network_mode" {
  description = "ECS task network mode"
  type        = string
  default     = "awsvpc"
}

variable "ecs_requires_compatibilities" {
  description = "ECS task compatibility"
  type        = list(string)
  default     = ["FARGATE"]
}

variable "ecs_launch_type" {
  description = "ECS service launch type"
  type        = string
  default     = "FARGATE"
}

variable "ecs_assign_public_ip" {
  description = "Assign public IP to ECS tasks"
  type        = bool
  default     = false
}

variable "ecs_deployment_maximum_percent" {
  description = "Maximum percentage of tasks during deployment"
  type        = number
  default     = 200
}

variable "ecs_deployment_minimum_healthy_percent" {
  description = "Minimum healthy percentage of tasks during deployment"
  type        = number
  default     = 100
}

variable "ecs_health_check_grace_period" {
  description = "Health check grace period in seconds"
  type        = number
  default     = 60
}

variable "ecs_health_check_interval" {
  description = "ECS container health check interval in seconds"
  type        = number
  default     = 30
}

variable "ecs_health_check_timeout" {
  description = "ECS container health check timeout in seconds"
  type        = number
  default     = 5
}

variable "ecs_health_check_retries" {
  description = "ECS container health check retries"
  type        = number
  default     = 3
}

variable "ecs_health_check_start_period" {
  description = "ECS container health check start period in seconds"
  type        = number
  default     = 60
}

variable "aspnetcore_environment" {
  description = "ASP.NET Core environment"
  type        = string
  default     = "Production"
}

variable "enable_container_insights" {
  description = "Enable CloudWatch Container Insights for ECS"
  type        = bool
  default     = true
}

# ECR Configuration

variable "ecr_image_tag_mutability" {
  description = "Image tag mutability setting for ECR"
  type        = string
  default     = "MUTABLE"
}

variable "ecr_image_retention_count" {
  description = "Number of images to retain in ECR"
  type        = number
  default     = 10
}

variable "ecr_scan_on_push" {
  description = "Enable image scanning on push for ECR"
  type        = bool
  default     = true
}

variable "ecr_encryption_type" {
  description = "ECR encryption type"
  type        = string
  default     = "AES256"
}
