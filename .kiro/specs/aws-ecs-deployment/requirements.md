# Requirements Document

## Introduction

이 문서는 Rps 프로젝트를 AWS에 배포하기 위한 Terraform 인프라 구성 요구사항을 정의합니다. ECR을 통한 Docker 이미지 관리, ECS를 통한 컨테이너 배포, Redis 클러스터, RabbitMQ 인스턴스, 그리고 Route53과 SSL 인증서를 통한 도메인 설정을 포함합니다.

## Glossary

- **Terraform**: Infrastructure as Code (IaC) 도구로 AWS 리소스를 코드로 관리
- **ECR (Elastic Container Registry)**: AWS의 Docker 컨테이너 이미지 저장소
- **ECS (Elastic Container Service)**: AWS의 컨테이너 오케스트레이션 서비스
- **Fargate**: 서버리스 컨테이너 실행 환경
- **ElastiCache**: AWS의 관리형 Redis 서비스
- **Amazon MQ**: AWS의 관리형 RabbitMQ 서비스
- **Route53**: AWS의 DNS 웹 서비스
- **ACM (AWS Certificate Manager)**: SSL/TLS 인증서 관리 서비스
- **VPC (Virtual Private Cloud)**: AWS의 격리된 가상 네트워크
- **ALB (Application Load Balancer)**: 애플리케이션 레벨 로드 밸런서
- **Security Group**: AWS의 가상 방화벽

## Requirements

### Requirement 1

**User Story:** 개발자로서, Docker 이미지를 안전하게 저장하고 관리할 수 있는 ECR 저장소가 필요합니다.

#### Acceptance Criteria

1. THE Terraform Configuration SHALL create an ECR repository for the Rps application
2. THE ECR Repository SHALL enable image scanning on push for security vulnerabilities
3. THE ECR Repository SHALL retain the last 10 images and remove older images automatically
4. THE Terraform Configuration SHALL output the ECR repository URL for CI/CD integration

### Requirement 2

**User Story:** 운영자로서, ECS를 사용하여 컨테이너화된 애플리케이션을 배포하고 실행할 수 있어야 합니다.

#### Acceptance Criteria

1. THE Terraform Configuration SHALL create an ECS cluster using Fargate launch type
2. THE ECS Task Definition SHALL reference the ECR repository image
3. THE ECS Task Definition SHALL configure environment variables for Redis and RabbitMQ connection strings
4. THE ECS Service SHALL maintain at least 2 running tasks for high availability
5. THE ECS Service SHALL integrate with Application Load Balancer for traffic distribution

### Requirement 3

**User Story:** 개발자로서, 애플리케이션이 Redis 클러스터에 연결하여 캐싱과 SignalR 백플레인을 사용할 수 있어야 합니다.

#### Acceptance Criteria

1. THE Terraform Configuration SHALL create an ElastiCache Redis cluster with the smallest available instance type
2. THE Redis Cluster SHALL be deployed in cluster mode with at least 1 shard
3. THE Redis Cluster SHALL be accessible only from the ECS tasks through security group rules
4. THE Terraform Configuration SHALL output the Redis cluster endpoint for application configuration

### Requirement 4

**User Story:** 개발자로서, RabbitMQ 메시지 브로커를 사용하여 비동기 메시징을 처리할 수 있어야 합니다.

#### Acceptance Criteria

1. THE Terraform Configuration SHALL create an Amazon MQ RabbitMQ broker with the smallest available instance type
2. THE RabbitMQ Broker SHALL be deployed in single-instance mode for cost optimization
3. THE RabbitMQ Broker SHALL be accessible only from the ECS tasks through security group rules
4. THE Terraform Configuration SHALL output the RabbitMQ broker endpoint and credentials

### Requirement 5

**User Story:** 사용자로서, 도메인 이름을 통해 HTTPS로 안전하게 애플리케이션에 접속할 수 있어야 합니다.

#### Acceptance Criteria

1. THE Terraform Configuration SHALL create or reference a Route53 hosted zone for the domain
2. THE Terraform Configuration SHALL request an ACM SSL certificate for the domain
3. THE Application Load Balancer SHALL terminate SSL connections using the ACM certificate
4. THE Route53 Configuration SHALL create an A record pointing to the Application Load Balancer
5. THE Application Load Balancer SHALL redirect HTTP traffic to HTTPS

### Requirement 6

**User Story:** 운영자로서, 안전한 네트워크 구성으로 리소스 간 통신을 제어할 수 있어야 합니다.

#### Acceptance Criteria

1. THE Terraform Configuration SHALL create a VPC with public and private subnets across multiple availability zones
2. THE Security Groups SHALL allow inbound HTTPS traffic from the internet to the Application Load Balancer
3. THE Security Groups SHALL allow traffic from ECS tasks to Redis and RabbitMQ only
4. THE Security Groups SHALL allow traffic from the Application Load Balancer to ECS tasks on port 5184
5. THE Private Subnets SHALL use NAT Gateway for outbound internet access

### Requirement 7

**User Story:** 개발자로서, Terraform 코드를 모듈화하여 재사용 가능하고 유지보수하기 쉬운 구조로 관리하고 싶습니다.

#### Acceptance Criteria

1. THE Terraform Configuration SHALL be organized in the iac directory with separate files for each resource type
2. THE Terraform Configuration SHALL use variables for configurable parameters like domain name and environment
3. THE Terraform Configuration SHALL output important values like endpoints and URLs
4. THE Terraform Configuration SHALL include a Dockerfile for building the Rps application image
5. THE Terraform Configuration SHALL include documentation for deployment procedures
