output "vpc_id" {
  description = "VPC ID"
  value       = aws_vpc.main.id
}

output "public_subnet_ids" {
  description = "Public subnet IDs"
  value       = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  description = "Private subnet IDs"
  value       = aws_subnet.private[*].id
}

output "ecr_repository_url" {
  description = "ECR repository URL"
  value       = aws_ecr_repository.rps.repository_url
}

output "ecr_repository_arn" {
  description = "ECR repository ARN"
  value       = aws_ecr_repository.rps.arn
}

# Uncomment after completing task 10 (ECS Cluster and Task Definition)
# output "ecs_cluster_id" {
#   description = "ECS cluster ID"
#   value       = aws_ecs_cluster.main.id
# }

# output "ecs_cluster_name" {
#   description = "ECS cluster name"
#   value       = aws_ecs_cluster.main.name
# }

# Uncomment after completing task 11 (ECS Service)
# output "ecs_service_name" {
#   description = "ECS service name"
#   value       = aws_ecs_service.rps.name
# }

output "alb_dns_name" {
  description = "ALB DNS name"
  value       = aws_lb.main.dns_name
}

output "alb_zone_id" {
  description = "ALB zone ID for Route53 alias"
  value       = aws_lb.main.zone_id
}

output "alb_arn" {
  description = "ALB ARN"
  value       = aws_lb.main.arn
}

output "application_url" {
  description = "Application URL (HTTP only - HTTPS disabled for DuckDNS)"
  value       = "http://${var.domain_name}"
}

output "redis_endpoint" {
  description = "Valkey primary endpoint"
  value       = aws_elasticache_replication_group.redis.primary_endpoint_address
}

output "redis_port" {
  description = "Valkey port"
  value       = aws_elasticache_replication_group.redis.port
}

output "redis_connection_string" {
  description = "Valkey connection string"
  value       = "${aws_elasticache_replication_group.redis.primary_endpoint_address}:${aws_elasticache_replication_group.redis.port}"
  sensitive   = true
}

output "rabbitmq_endpoint" {
  description = "RabbitMQ broker endpoint"
  value       = aws_mq_broker.rabbitmq.instances[0].endpoints[0]
}

output "rabbitmq_console_url" {
  description = "RabbitMQ management console URL"
  value       = aws_mq_broker.rabbitmq.instances[0].console_url
}

output "rabbitmq_arn" {
  description = "RabbitMQ broker ARN"
  value       = aws_mq_broker.rabbitmq.arn
}

output "rabbitmq_credentials_secret_arn" {
  description = "RabbitMQ credentials Secrets Manager ARN"
  value       = aws_secretsmanager_secret.rabbitmq_credentials.arn
}

# ACM Certificate outputs - Disabled for DuckDNS
# Uncomment when using CloudFlare or Route53 with proper DNS validation
# output "acm_certificate_arn" {
#   description = "ACM certificate ARN"
#   value       = aws_acm_certificate.main.arn
# }

output "duckdns_setup_instructions" {
  description = "Instructions for setting up DuckDNS"
  value = <<-EOT
    
    DuckDNS Setup Instructions (rps100.duckdns.org):
    
    1. Run the update script after terraform apply:
       cd iac && ./update-duckdns.sh
    
    2. Or manually update DuckDNS:
       - Get ALB IP: nslookup ${aws_lb.main.dns_name}
       - Update: curl "https://www.duckdns.org/update?domains=rps100&token=283c1e08-9570-412b-b9a3-3ac6681eab64&ip=<ALB_IP>"
    
    3. Access your application:
       http://rps100.duckdns.org
    
    Note: HTTPS is disabled (DuckDNS doesn't support TXT records for ACM validation)
    For HTTPS, use CloudFlare + Freenom instead (see DUCKDNS_SETUP.md)
    
    For detailed instructions, see iac/DUCKDNS_GUIDE.md
  EOT
}

# Uncomment after completing task 10 (ECS Cluster and Task Definition)
# output "cloudwatch_log_group_name" {
#   description = "CloudWatch log group name for ECS tasks"
#   value       = aws_cloudwatch_log_group.ecs.name
# }
