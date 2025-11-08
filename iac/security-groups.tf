# Security Groups for Rps Application

# ALB Security Group
resource "aws_security_group" "alb" {
  name        = "${var.project_name}-${var.environment}-alb-sg"
  description = "Security group for Application Load Balancer"
  vpc_id      = aws_vpc.main.id

  tags = {
    Name        = "${var.project_name}-${var.environment}-alb-sg"
    Environment = var.environment
    Project     = var.project_name
  }
}

# ECS Security Group
resource "aws_security_group" "ecs" {
  name        = "${var.project_name}-${var.environment}-ecs-sg"
  description = "Security group for ECS tasks"
  vpc_id      = aws_vpc.main.id

  tags = {
    Name        = "${var.project_name}-${var.environment}-ecs-sg"
    Environment = var.environment
    Project     = var.project_name
  }
}

# Redis Security Group
resource "aws_security_group" "redis" {
  name        = "${var.project_name}-${var.environment}-redis-sg"
  description = "Security group for ElastiCache Redis"
  vpc_id      = aws_vpc.main.id

  tags = {
    Name        = "${var.project_name}-${var.environment}-redis-sg"
    Environment = var.environment
    Project     = var.project_name
  }
}

# RabbitMQ Security Group
resource "aws_security_group" "rabbitmq" {
  name        = "${var.project_name}-${var.environment}-rabbitmq-sg"
  description = "Security group for Amazon MQ RabbitMQ"
  vpc_id      = aws_vpc.main.id

  tags = {
    Name        = "${var.project_name}-${var.environment}-rabbitmq-sg"
    Environment = var.environment
    Project     = var.project_name
  }
}

# Security Group Rules

# ALB Rules
resource "aws_security_group_rule" "alb_http_ingress" {
  type              = "ingress"
  description       = "HTTP from internet"
  from_port         = 80
  to_port           = 80
  protocol          = "tcp"
  cidr_blocks       = ["0.0.0.0/0"]
  security_group_id = aws_security_group.alb.id
}

resource "aws_security_group_rule" "alb_https_ingress" {
  type              = "ingress"
  description       = "HTTPS from internet"
  from_port         = 443
  to_port           = 443
  protocol          = "tcp"
  cidr_blocks       = ["0.0.0.0/0"]
  security_group_id = aws_security_group.alb.id
}

resource "aws_security_group_rule" "alb_to_ecs_egress" {
  type                     = "egress"
  description              = "To ECS tasks"
  from_port                = 5184
  to_port                  = 5184
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.ecs.id
  security_group_id        = aws_security_group.alb.id
}

# ECS Rules
resource "aws_security_group_rule" "ecs_from_alb_ingress" {
  type                     = "ingress"
  description              = "From ALB"
  from_port                = 5184
  to_port                  = 5184
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.alb.id
  security_group_id        = aws_security_group.ecs.id
}

resource "aws_security_group_rule" "ecs_to_redis_egress" {
  type                     = "egress"
  description              = "To Redis"
  from_port                = 6379
  to_port                  = 6379
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.redis.id
  security_group_id        = aws_security_group.ecs.id
}

resource "aws_security_group_rule" "ecs_to_rabbitmq_amqps_egress" {
  type                     = "egress"
  description              = "To RabbitMQ AMQPS"
  from_port                = 5671
  to_port                  = 5671
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.rabbitmq.id
  security_group_id        = aws_security_group.ecs.id
}

resource "aws_security_group_rule" "ecs_to_rabbitmq_mgmt_egress" {
  type                     = "egress"
  description              = "To RabbitMQ Management"
  from_port                = 443
  to_port                  = 443
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.rabbitmq.id
  security_group_id        = aws_security_group.ecs.id
}

resource "aws_security_group_rule" "ecs_to_internet_egress" {
  type              = "egress"
  description       = "To internet"
  from_port         = 443
  to_port           = 443
  protocol          = "tcp"
  cidr_blocks       = ["0.0.0.0/0"]
  security_group_id = aws_security_group.ecs.id
}

# Redis Rules
resource "aws_security_group_rule" "redis_from_ecs_ingress" {
  type                     = "ingress"
  description              = "From ECS tasks"
  from_port                = 6379
  to_port                  = 6379
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.ecs.id
  security_group_id        = aws_security_group.redis.id
}

# RabbitMQ Rules
resource "aws_security_group_rule" "rabbitmq_amqps_from_ecs_ingress" {
  type                     = "ingress"
  description              = "AMQPS from ECS tasks"
  from_port                = 5671
  to_port                  = 5671
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.ecs.id
  security_group_id        = aws_security_group.rabbitmq.id
}

resource "aws_security_group_rule" "rabbitmq_mgmt_from_ecs_ingress" {
  type                     = "ingress"
  description              = "Management Console from ECS tasks"
  from_port                = 443
  to_port                  = 443
  protocol                 = "tcp"
  source_security_group_id = aws_security_group.ecs.id
  security_group_id        = aws_security_group.rabbitmq.id
}
