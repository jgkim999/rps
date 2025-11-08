# Design Document

## Overview

이 설계는 Rps ASP.NET Core 애플리케이션을 AWS ECS Fargate에 배포하기 위한 완전한 인프라를 Terraform으로 구성합니다. 애플리케이션은 Docker 컨테이너로 패키징되어 ECR에 저장되고, ECS에서 실행됩니다. Redis와 RabbitMQ는 관리형 서비스로 제공되며, Application Load Balancer와 Route53을 통해 HTTPS로 외부 접근이 가능합니다.

## Architecture

### High-Level Architecture

```
Internet
    ↓
Route53 (DNS)
    ↓
Application Load Balancer (HTTPS/SSL)
    ↓
ECS Fargate Tasks (Rps Application)
    ↓
├── ElastiCache Redis (캐싱 및 SignalR 백플레인)
└── Amazon MQ RabbitMQ (메시지 브로커)
```

### Network Architecture

- **VPC**: 10.0.0.0/16 CIDR 블록
- **Public Subnets**: 2개 AZ에 걸쳐 배포 (10.0.1.0/24, 10.0.2.0/24)
  - ALB 배치
  - NAT Gateway 배치
- **Private Subnets**: 2개 AZ에 걸쳐 배포 (10.0.11.0/24, 10.0.12.0/24)
  - ECS Tasks 배치
  - ElastiCache Redis 배치
  - Amazon MQ RabbitMQ 배치

### Security Groups

1. **ALB Security Group**
   - Inbound: 443 (HTTPS) from 0.0.0.0/0
   - Inbound: 80 (HTTP) from 0.0.0.0/0 (redirect to HTTPS)
   - Outbound: 5184 to ECS Security Group

2. **ECS Security Group**
   - Inbound: 5184 from ALB Security Group
   - Outbound: 6379 to Redis Security Group
   - Outbound: 5671 to RabbitMQ Security Group
   - Outbound: 443 to 0.0.0.0/0 (for external API calls)

3. **Redis Security Group**
   - Inbound: 6379 from ECS Security Group

4. **RabbitMQ Security Group**
   - Inbound: 5671 from ECS Security Group
   - Inbound: 443 from ECS Security Group (for management console)

## Components and Interfaces

### 1. Terraform File Structure

```
iac/
├── main.tf                 # Provider 및 메인 구성
├── variables.tf            # 입력 변수 정의
├── outputs.tf              # 출력 값 정의
├── vpc.tf                  # VPC, Subnets, IGW, NAT Gateway
├── security-groups.tf      # 모든 Security Groups
├── ecr.tf                  # ECR Repository
├── ecs.tf                  # ECS Cluster, Task Definition, Service
├── alb.tf                  # Application Load Balancer
├── redis.tf                # ElastiCache Redis
├── rabbitmq.tf             # Amazon MQ RabbitMQ
├── route53.tf              # Route53 및 ACM Certificate
├── iam.tf                  # IAM Roles 및 Policies
├── terraform.tfvars        # 변수 값 (gitignore)
└── Dockerfile              # Rps 애플리케이션 Docker 이미지
```

### 2. ECR Repository

- **Repository Name**: `rps-app`
- **Image Scanning**: 푸시 시 자동 스캔 활성화
- **Lifecycle Policy**: 최근 10개 이미지만 유지
- **Encryption**: AES256 기본 암호화

### 3. ECS Configuration

#### ECS Cluster
- **Launch Type**: Fargate
- **Container Insights**: 활성화 (모니터링)

#### Task Definition
- **CPU**: 512 (0.5 vCPU)
- **Memory**: 1024 MB (1 GB)
- **Network Mode**: awsvpc
- **Container Port**: 5184
- **Environment Variables**:
  - `ASPNETCORE_ENVIRONMENT`: Production
  - `Redis__SignalRBackplane`: ElastiCache endpoint
  - `Redis__FusionCacheRedisCache`: ElastiCache endpoint
  - `Redis__FusionCacheBackplane`: ElastiCache endpoint
  - `RabbitMQ__Host`: Amazon MQ endpoint
  - `RabbitMQ__Username`: (Secrets Manager에서 참조)
  - `RabbitMQ__Password`: (Secrets Manager에서 참조)

#### ECS Service
- **Desired Count**: 2 (고가용성)
- **Deployment Configuration**: Rolling update
- **Health Check Grace Period**: 60초
- **Load Balancer Integration**: ALB Target Group

### 4. ElastiCache Redis

- **Engine**: Redis 7.x
- **Node Type**: cache.t4g.micro (가장 작은 인스턴스)
- **Cluster Mode**: Enabled (1 shard, 1 replica)
- **Automatic Failover**: Enabled
- **Subnet Group**: Private subnets
- **Parameter Group**: 기본 설정

### 5. Amazon MQ RabbitMQ

- **Engine**: RabbitMQ 3.11.x
- **Instance Type**: mq.t3.micro (가장 작은 인스턴스)
- **Deployment Mode**: Single-instance
- **Storage Type**: EBS
- **Subnet**: Private subnet (single AZ)
- **Public Access**: Disabled
- **Auto Minor Version Upgrade**: Enabled

### 6. Application Load Balancer

- **Scheme**: Internet-facing
- **Subnets**: Public subnets (multi-AZ)
- **Listeners**:
  - Port 80 (HTTP): Redirect to 443
  - Port 443 (HTTPS): Forward to ECS Target Group
- **Target Group**:
  - Protocol: HTTP
  - Port: 5184
  - Health Check Path: /
  - Health Check Interval: 30초
  - Healthy Threshold: 2
  - Unhealthy Threshold: 3

### 7. Route53 and SSL

- **Hosted Zone**: 기존 도메인 또는 새로 생성
- **ACM Certificate**: 
  - Domain validation
  - DNS validation method
  - Automatic renewal
- **DNS Record**: A record (Alias to ALB)

### 8. IAM Roles

#### ECS Task Execution Role
- **Purpose**: ECR 이미지 pull, CloudWatch Logs 작성
- **Policies**:
  - AmazonECSTaskExecutionRolePolicy
  - SecretsManager 읽기 권한 (RabbitMQ 자격증명)

#### ECS Task Role
- **Purpose**: 애플리케이션이 AWS 서비스에 접근
- **Policies**:
  - CloudWatch Logs 작성
  - Secrets Manager 읽기 (필요시)

## Data Models

### Terraform Variables

```hcl
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

variable "vpc_cidr" {
  description = "VPC CIDR block"
  type        = string
  default     = "10.0.0.0/16"
}

variable "ecs_task_cpu" {
  description = "ECS task CPU units"
  type        = number
  default     = 512
}

variable "ecs_task_memory" {
  description = "ECS task memory in MB"
  type        = number
  default     = 1024
}

variable "ecs_desired_count" {
  description = "Desired number of ECS tasks"
  type        = number
  default     = 2
}
```

### Terraform Outputs

```hcl
output "ecr_repository_url" {
  description = "ECR repository URL"
  value       = aws_ecr_repository.rps.repository_url
}

output "alb_dns_name" {
  description = "ALB DNS name"
  value       = aws_lb.main.dns_name
}

output "application_url" {
  description = "Application URL"
  value       = "https://${var.domain_name}"
}

output "redis_endpoint" {
  description = "Redis cluster endpoint"
  value       = aws_elasticache_replication_group.redis.configuration_endpoint_address
}

output "rabbitmq_endpoint" {
  description = "RabbitMQ broker endpoint"
  value       = aws_mq_broker.rabbitmq.instances[0].endpoints[0]
}
```

## Error Handling

### Terraform State Management

- **Backend**: S3 bucket with DynamoDB for state locking
- **State File**: Encrypted at rest
- **Versioning**: Enabled for rollback capability

### Resource Dependencies

- Terraform `depends_on` 사용하여 리소스 생성 순서 보장
- VPC → Subnets → Security Groups → Services 순서

### Deployment Failures

- ECS 서비스는 health check 실패 시 자동으로 이전 버전으로 롤백
- ALB health check를 통해 unhealthy 타겟 자동 제거
- CloudWatch Alarms로 서비스 상태 모니터링

### Cost Optimization

- Fargate Spot 사용 고려 (프로덕션 환경에서는 On-Demand 권장)
- ElastiCache와 Amazon MQ는 가장 작은 인스턴스 타입 사용
- NAT Gateway는 단일 AZ에 배치하여 비용 절감 (고가용성 필요시 multi-AZ)

## Testing Strategy

### Infrastructure Testing

1. **Terraform Validation**
   - `terraform fmt` - 코드 포맷 검증
   - `terraform validate` - 구문 검증
   - `terraform plan` - 변경사항 미리보기

2. **Security Testing**
   - Security Group 규칙 검증
   - IAM 권한 최소 권한 원칙 확인
   - Secrets Manager 사용 확인

3. **Connectivity Testing**
   - ECS 태스크에서 Redis 연결 테스트
   - ECS 태스크에서 RabbitMQ 연결 테스트
   - ALB health check 통과 확인

### Application Testing

1. **Container Build**
   - Dockerfile 빌드 성공 확인
   - 로컬에서 컨테이너 실행 테스트

2. **Integration Testing**
   - 배포 후 애플리케이션 접근 확인
   - SignalR 연결 테스트
   - Redis 캐싱 동작 확인

3. **Load Testing**
   - ALB를 통한 트래픽 분산 확인
   - Auto-scaling 동작 확인 (필요시)

## Deployment Procedures

### Initial Setup

1. AWS CLI 및 Terraform 설치
2. AWS 자격증명 구성
3. `terraform.tfvars` 파일 생성 및 변수 설정
4. S3 backend 구성 (선택사항)

### Infrastructure Deployment

```bash
cd iac
terraform init
terraform plan
terraform apply
```

### Application Deployment

```bash
# Docker 이미지 빌드
docker build -t rps-app:latest -f iac/Dockerfile .

# ECR 로그인
aws ecr get-login-password --region ap-northeast-2 | \
  docker login --username AWS --password-stdin <ecr-url>

# 이미지 태그 및 푸시
docker tag rps-app:latest <ecr-url>/rps-app:latest
docker push <ecr-url>/rps-app:latest

# ECS 서비스 업데이트 (자동으로 새 이미지 배포)
aws ecs update-service --cluster rps-cluster \
  --service rps-service --force-new-deployment
```

### DNS Configuration

1. Route53 Hosted Zone에서 NS 레코드 확인
2. 도메인 등록기관에서 네임서버 업데이트
3. ACM 인증서 DNS 검증 완료 대기 (자동)

## Design Decisions and Rationales

### 1. Fargate vs EC2 Launch Type

**Decision**: Fargate 사용

**Rationale**: 
- 서버 관리 불필요
- 자동 스케일링 간편
- 사용한 만큼만 비용 지불
- 소규모 애플리케이션에 적합

### 2. ElastiCache Cluster Mode

**Decision**: Cluster mode enabled with 1 shard

**Rationale**:
- 향후 확장성 확보
- Automatic failover 지원
- 최소 비용으로 시작

### 3. Single-Instance RabbitMQ

**Decision**: Single-instance deployment

**Rationale**:
- 비용 최적화
- 현재 요구사항에 충분
- 필요시 cluster mode로 업그레이드 가능

### 4. Multi-AZ ECS Deployment

**Decision**: 2개 AZ에 ECS 태스크 배포

**Rationale**:
- 고가용성 확보
- AZ 장애 시에도 서비스 지속
- ALB가 자동으로 트래픽 분산

### 5. Private Subnet for Services

**Decision**: ECS, Redis, RabbitMQ를 private subnet에 배치

**Rationale**:
- 보안 강화
- 인터넷 직접 노출 방지
- NAT Gateway를 통한 아웃바운드 트래픽만 허용
