# ElastiCache Valkey Subnet Group
resource "aws_elasticache_subnet_group" "redis" {
  name       = "${var.project_name}-valkey-subnet-group"
  subnet_ids = aws_subnet.private[*].id

  tags = {
    Name        = "${var.project_name}-valkey-subnet-group"
    Environment = var.environment
  }
}

# ElastiCache Valkey Parameter Group
resource "aws_elasticache_parameter_group" "redis" {
  name   = "${var.project_name}-valkey-params"
  family = "valkey8"

  parameter {
    name  = "maxmemory-policy"
    value = "allkeys-lru"
  }

  tags = {
    Name        = "${var.project_name}-valkey-params"
    Environment = var.environment
  }
}

# ElastiCache Valkey Replication Group
resource "aws_elasticache_replication_group" "redis" {
  replication_group_id = "${var.project_name}-valkey"
  description          = "Valkey cluster for ${var.project_name} application"

  engine               = "valkey"
  engine_version       = "8.2"
  node_type            = "cache.t4g.micro"
  port                 = 6379
  parameter_group_name = aws_elasticache_parameter_group.redis.name

  # Cluster mode configuration
  num_cache_clusters         = 2
  automatic_failover_enabled = true
  multi_az_enabled           = true

  # Network configuration
  subnet_group_name  = aws_elasticache_subnet_group.redis.name
  security_group_ids = [aws_security_group.redis.id]

  # Maintenance and backup
  maintenance_window       = "sun:05:00-sun:06:00"
  snapshot_window          = "03:00-04:00"
  snapshot_retention_limit = 1

  # Encryption
  at_rest_encryption_enabled = true
  transit_encryption_enabled = true

  # Auth token for transit encryption (required when transit_encryption_enabled = true)
  auth_token                 = var.redis_auth_token
  auth_token_update_strategy = "ROTATE"

  # Auto minor version upgrade
  auto_minor_version_upgrade = true

  tags = {
    Name        = "${var.project_name}-valkey"
    Environment = var.environment
  }
}
