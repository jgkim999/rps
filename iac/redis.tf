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
  family = var.redis_parameter_family

  parameter {
    name  = "maxmemory-policy"
    value = var.redis_maxmemory_policy
  }

  tags = {
    Name        = "${var.project_name}-valkey-params"
    Environment = var.environment
  }
}

# ElastiCache Valkey Replication Group (Cluster Mode Enabled)
resource "aws_elasticache_replication_group" "redis" {
  replication_group_id = "${var.project_name}-valkey"
  description          = "Valkey cluster for ${var.project_name} application"

  engine               = "valkey"
  engine_version       = var.redis_engine_version
  node_type            = var.redis_node_type
  port                 = var.redis_port
  parameter_group_name = aws_elasticache_parameter_group.redis.name

  # Cluster mode configuration (샤딩 활성화)
  cluster_mode {
    num_node_groups         = var.redis_num_node_groups
    replicas_per_node_group = var.redis_replicas_per_node_group
  }

  automatic_failover_enabled = var.redis_automatic_failover_enabled
  multi_az_enabled           = var.redis_multi_az_enabled

  # Network configuration
  subnet_group_name  = aws_elasticache_subnet_group.redis.name
  security_group_ids = [aws_security_group.redis.id]

  # Maintenance and backup
  maintenance_window       = var.redis_maintenance_window
  snapshot_window          = var.redis_snapshot_window
  snapshot_retention_limit = var.redis_snapshot_retention_limit

  # Encryption (disabled for development)
  at_rest_encryption_enabled = var.redis_at_rest_encryption_enabled
  transit_encryption_enabled = var.redis_transit_encryption_enabled

  # Auto minor version upgrade
  auto_minor_version_upgrade = var.redis_auto_minor_version_upgrade

  tags = {
    Name        = "${var.project_name}-valkey"
    Environment = var.environment
  }
}
