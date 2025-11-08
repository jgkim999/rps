# Implementation Plan

- [x] 1. Terraform 기본 구성 파일 생성
  - iac/main.tf에 provider 설정 및 backend 구성 작성
  - iac/variables.tf에 모든 입력 변수 정의
  - iac/outputs.tf에 출력 값 정의
  - iac/terraform.tfvars.example 예제 파일 생성
  - _Requirements: 7.1, 7.2, 7.3_

- [x] 2. VPC 및 네트워크 인프라 구성
  - iac/vpc.tf에 VPC, Internet Gateway, NAT Gateway 생성
  - 2개 AZ에 걸쳐 public subnets 생성 (ALB용)
  - 2개 AZ에 걸쳐 private subnets 생성 (ECS, Redis, RabbitMQ용)
  - Route tables 및 associations 구성
  - _Requirements: 6.1, 6.5_

- [x] 3. Security Groups 구성
  - iac/security-groups.tf에 ALB security group 생성 (80, 443 inbound)
  - ECS security group 생성 (5184 from ALB, outbound to Redis/RabbitMQ)
  - Redis security group 생성 (6379 from ECS)
  - RabbitMQ security group 생성 (5671, 443 from ECS)
  - _Requirements: 6.2, 6.3, 6.4_

- [x] 4. ECR Repository 생성
  - iac/ecr.tf에 ECR repository 리소스 생성
  - Image scanning on push 활성화
  - Lifecycle policy로 최근 10개 이미지만 유지 설정
  - Repository URL output 추가
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 5. IAM Roles 및 Policies 구성
  - iac/iam.tf에 ECS Task Execution Role 생성
  - ECS Task Role 생성
  - ECR, CloudWatch Logs, Secrets Manager 접근 권한 부여
  - _Requirements: 2.3_

- [x] 6. ElastiCache Redis 클러스터 생성
  - iac/redis.tf에 subnet group 생성
  - Redis replication group 생성 (cache.t4g.micro, cluster mode)
  - Parameter group 구성
  - Redis endpoint output 추가
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 7. Amazon MQ RabbitMQ 브로커 생성
  - iac/rabbitmq.tf에 RabbitMQ broker 생성 (mq.t3.micro)
  - Single-instance deployment mode 설정
  - Secrets Manager에 자격증명 저장
  - RabbitMQ endpoint output 추가
  - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 8. Application Load Balancer 구성
  - iac/alb.tf에 ALB 생성 (internet-facing, public subnets)
  - Target group 생성 (HTTP:5184, health check 설정)
  - HTTP listener 생성 (80 → 443 redirect)
  - HTTPS listener 생성 (443, ACM certificate 연결)
  - ALB DNS name output 추가
  - _Requirements: 2.5, 5.3, 5.5_

- [x] 9. Route53 및 ACM Certificate 구성
  - iac/route53.tf에 Route53 hosted zone 참조 또는 생성
  - ACM certificate 요청 (DNS validation)
  - Certificate validation record 생성
  - A record 생성 (ALB alias)
  - _Requirements: 5.1, 5.2, 5.4_

- [x] 10. ECS Cluster 및 Task Definition 생성
  - iac/ecs.tf에 ECS cluster 생성 (Fargate)
  - CloudWatch log group 생성
  - Task definition 생성 (CPU: 512, Memory: 1024)
  - Container definition에 환경변수 설정 (Redis, RabbitMQ endpoints)
  - _Requirements: 2.1, 2.2, 2.3_

- [x] 11. ECS Service 생성 및 ALB 연동
  - ECS service 생성 (desired count: 2)
  - Load balancer target group 연결
  - Network configuration 설정 (private subnets, security groups)
  - Health check grace period 설정
  - _Requirements: 2.4, 2.5_

- [x] 12. Dockerfile 작성
  - iac/Dockerfile 생성
  - .NET 9 SDK 이미지 사용하여 빌드 스테이지 구성
  - ASP.NET Core runtime 이미지로 최종 이미지 생성
  - 포트 5184 노출
  - 환경변수 설정 지원
  - _Requirements: 7.4_

- [x] 13. 배포 문서 작성
  - iac/README.md 생성
  - Terraform 초기화 및 적용 절차 문서화
  - Docker 이미지 빌드 및 ECR 푸시 절차 문서화
  - 변수 설정 가이드 작성
  - 트러블슈팅 가이드 추가
  - _Requirements: 7.5_
