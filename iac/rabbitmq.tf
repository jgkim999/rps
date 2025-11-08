# Amazon MQ RabbitMQ Broker

# Generate random password for RabbitMQ if not provided
# Exclude characters that are not allowed by Amazon MQ: [ : =
resource "random_password" "rabbitmq" {
  length           = 32
  special          = true
  override_special = "!#$%&*()-_+<>?"
}

# Store RabbitMQ credentials in Secrets Manager
resource "aws_secretsmanager_secret" "rabbitmq_credentials" {
  name        = "${var.project_name}-${var.environment}-rabbitmq-credentials"
  description = "RabbitMQ broker credentials"

  tags = {
    Name        = "${var.project_name}-${var.environment}-rabbitmq-credentials"
    Environment = var.environment
    Project     = var.project_name
  }
}

resource "aws_secretsmanager_secret_version" "rabbitmq_credentials" {
  secret_id = aws_secretsmanager_secret.rabbitmq_credentials.id
  secret_string = jsonencode({
    username = var.rabbitmq_username
    password = var.rabbitmq_password != "" ? var.rabbitmq_password : random_password.rabbitmq.result
    host     = split("//", aws_mq_broker.rabbitmq.instances[0].endpoints[0])[1]
  })
}

# Amazon MQ RabbitMQ Broker
resource "aws_mq_broker" "rabbitmq" {
  broker_name        = "${var.project_name}-${var.environment}-rabbitmq"
  engine_type        = "RabbitMQ"
  engine_version     = var.rabbitmq_engine_version
  host_instance_type = var.rabbitmq_instance_type
  deployment_mode    = var.rabbitmq_deployment_mode

  # Use the first private subnet for single-instance deployment
  subnet_ids          = [aws_subnet.private[0].id]
  security_groups     = [aws_security_group.rabbitmq.id]
  publicly_accessible = var.rabbitmq_publicly_accessible

  # Admin user configuration
  user {
    username = var.rabbitmq_username
    password = var.rabbitmq_password != "" ? var.rabbitmq_password : random_password.rabbitmq.result
  }

  # Guest user configuration
  user {
    username = var.rabbitmq_guest_username
    password = var.rabbitmq_guest_password
  }

  # Enable automatic minor version upgrades
  auto_minor_version_upgrade = var.rabbitmq_auto_minor_version_upgrade

  # Maintenance window (Sunday 03:00-04:00 UTC)
  maintenance_window_start_time {
    day_of_week = var.rabbitmq_maintenance_day_of_week
    time_of_day = var.rabbitmq_maintenance_time_of_day
    time_zone   = var.rabbitmq_maintenance_time_zone
  }

  # CloudWatch logs configuration
  logs {
    general = var.rabbitmq_logs_general
  }

  tags = {
    Name        = "${var.project_name}-${var.environment}-rabbitmq"
    Environment = var.environment
    Project     = var.project_name
  }
}
