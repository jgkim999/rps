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
  engine_version     = "3.13"
  host_instance_type = var.rabbitmq_instance_type
  deployment_mode    = "SINGLE_INSTANCE"

  # Use the first private subnet for single-instance deployment
  subnet_ids          = [aws_subnet.private[0].id]
  security_groups     = [aws_security_group.rabbitmq.id]
  publicly_accessible = false

  # Admin user configuration
  user {
    username = var.rabbitmq_username
    password = var.rabbitmq_password != "" ? var.rabbitmq_password : random_password.rabbitmq.result
  }

  # Enable automatic minor version upgrades
  auto_minor_version_upgrade = true

  # Maintenance window (Sunday 03:00-04:00 UTC)
  maintenance_window_start_time {
    day_of_week = "SUNDAY"
    time_of_day = "03:00"
    time_zone   = "UTC"
  }

  # CloudWatch logs configuration
  logs {
    general = true
  }

  tags = {
    Name        = "${var.project_name}-${var.environment}-rabbitmq"
    Environment = var.environment
    Project     = var.project_name
  }
}
