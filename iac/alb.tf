# Application Load Balancer
resource "aws_lb" "main" {
  name               = "${var.project_name}-${var.environment}-alb"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb.id]
  subnets            = aws_subnet.public[*].id

  enable_deletion_protection = false
  # Disable HTTP/2 for SignalR WebSocket compatibility
  # WebSocket only works with HTTP/1.1
  enable_http2 = false

  # Increase idle timeout for WebSocket connections (SignalR/Blazor)
  idle_timeout = 300 # 5 minutes (default is 60 seconds)

  tags = {
    Name        = "${var.project_name}-${var.environment}-alb"
    Environment = var.environment
    Project     = var.project_name
  }
}

# Target Group for ECS Service
resource "aws_lb_target_group" "ecs" {
  name        = "${var.project_name}-${var.environment}-tg"
  port        = 5184
  protocol    = "HTTP"
  vpc_id      = aws_vpc.main.id
  target_type = "ip"

  health_check {
    enabled             = true
    healthy_threshold   = 2
    unhealthy_threshold = 3
    timeout             = 5
    interval            = 30
    path                = "/health"
    protocol            = "HTTP"
    matcher             = "200"
  }

  deregistration_delay = 30

  # Enable sticky sessions for SignalR WebSocket connections
  stickiness {
    enabled         = true
    type            = "lb_cookie"
    cookie_duration = 86400 # 24 hours
  }

  tags = {
    Name        = "${var.project_name}-${var.environment}-tg"
    Environment = var.environment
    Project     = var.project_name
  }
}

# HTTP Listener - Direct forward (DuckDNS doesn't support HTTPS easily)
resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.main.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.ecs.arn
  }
}

# HTTPS Listener - Commented out for DuckDNS
# DuckDNS doesn't support TXT records needed for ACM DNS validation
# To enable HTTPS:
# 1. Use CloudFlare + Freenom instead (recommended)
# 2. Or manually import Let's Encrypt certificate to ACM
# 3. Or use email validation (requires email access)
#
# resource "aws_lb_listener" "https" {
#   load_balancer_arn = aws_lb.main.arn
#   port              = 443
#   protocol          = "HTTPS"
#   ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
#   certificate_arn   = aws_acm_certificate.main.arn
#
#   default_action {
#     type             = "forward"
#     target_group_arn = aws_lb_target_group.ecs.arn
#   }
# }
