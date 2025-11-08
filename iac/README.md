# Rps AWS ECS Deployment Infrastructure

이 디렉토리는 Rps 애플리케이션을 AWS ECS Fargate에 배포하기 위한 Terraform 인프라 코드를 포함합니다.

## 목차

- [아키텍처 개요](#아키텍처-개요)
- [사전 요구사항](#사전-요구사항)
- [인프라 구성 요소](#인프라-구성-요소)
- [Terraform Backend 설정](#terraform-backend-설정)
- [시작하기](#시작하기)
- [배포 절차](#배포-절차)
- [변수 설정 가이드](#변수-설정-가이드)
- [애플리케이션 배포](#애플리케이션-배포)
- [모니터링 및 로그](#모니터링-및-로그)
- [트러블슈팅](#트러블슈팅)
- [비용 최적화](#비용-최적화)
- [정리](#정리)

## 아키텍처 개요

```
Internet
    ↓
Route53 (DNS)
    ↓
Application Load Balancer (HTTPS/SSL)
    ↓
ECS Fargate Tasks (Rps Web Application) × 2
    ↓
├── ElastiCache Valkey Cluster (캐싱 및 SignalR 백플레인)
│   └── Cluster Mode: 2 샤드 × 2 노드 (primary + replica)
└── Amazon MQ RabbitMQ (메시지 브로커)
    ↑
ECS Fargate Tasks (Game Server - Message Consumer) × 1
```

### 주요 구성 요소

- **VPC**: 10.0.0.0/16 CIDR 블록, 2개 AZ에 걸친 public/private 서브넷
- **ECR**: Docker 이미지 저장소 (rps-app, rps-game-server)
- **ECS Fargate**: 서버리스 컨테이너 실행 환경
  - **Web Application**: ALB를 통한 HTTP/HTTPS 트래픽 처리
  - **Game Server**: RabbitMQ 메시지 소비 및 게임 로직 처리
- **Application Load Balancer**: HTTPS 트래픽 분산 (Web Application용)
- **ElastiCache Valkey**: Cluster Mode 활성화, 샤딩 기반 자동 확장 지원
- **Amazon MQ RabbitMQ**: 메시지 브로커 (단일 인스턴스, 개발 환경용)
- **Route53 + ACM**: DNS 및 SSL 인증서 관리

## 사전 요구사항

### 필수 도구

1. **Terraform** (>= 1.0)
   ```bash
   # macOS
   brew install terraform
   
   # 버전 확인
   terraform version
   ```

2. **AWS CLI** (>= 2.0)
   ```bash
   # macOS
   brew install awscli
   
   # 버전 확인
   aws --version
   ```

3. **Docker**
   ```bash
   # macOS
   brew install --cask docker
   
   # 버전 확인
   docker --version
   ```

### AWS 계정 설정

1. **AWS 자격증명 구성**
   ```bash
   aws configure
   ```
   
   입력 정보:
   - AWS Access Key ID
   - AWS Secret Access Key
   - Default region: `ap-northeast-2`
   - Default output format: `json`

2. **도메인 준비 (DuckDNS 무료 도메인 사용)**
   - DuckDNS 계정 생성 (https://www.duckdns.org)
   - 무료 서브도메인 생성 (예: myrps.duckdns.org)
   - 비용: 완전 무료!

## 인프라 구성 요소

### Terraform 파일 구조

```
iac/
├── main.tf                 # Provider 및 메인 구성
├── backend.tf              # Terraform Backend 설정 (S3 + DynamoDB)
├── backend-setup.tf        # Backend 리소스 생성 (최초 1회)
├── variables.tf            # 입력 변수 정의
├── outputs.tf              # 출력 값 정의
├── vpc.tf                  # VPC, Subnets, IGW, NAT Gateway
├── security-groups.tf      # 모든 Security Groups
├── ecr.tf                  # ECR Repository (Web App + Game Server)
├── ecs.tf                  # ECS Cluster, Task Definition, Service (Web App + Game Server)
├── alb.tf                  # Application Load Balancer
├── redis.tf                # ElastiCache Valkey (Cluster Mode, 암호화 비활성화)
├── rabbitmq.tf             # Amazon MQ RabbitMQ
├── route53.tf              # Route53 및 ACM Certificate
├── iam.tf                  # IAM Roles 및 Policies
├── Dockerfile              # Rps 애플리케이션 Docker 이미지
├── terraform.tfvars        # 변수 값 (gitignore, 직접 생성 필요)
├── .terraform-version      # Terraform 버전 고정
├── README.md               # 이 문서
└── BACKEND_SETUP.md        # Backend 설정 상세 가이드
```

## Terraform Backend 설정

### 팀 협업을 위한 원격 State 관리

여러 사람이 동일한 인프라를 관리하려면 Terraform State를 원격으로 저장해야 합니다.

**장점**:
- ✅ 팀원 간 일관된 인프라 상태 공유
- ✅ 동시 실행 방지 (State Locking)
- ✅ State 파일 버전 관리 및 암호화
- ✅ State 파일 분실 방지

**비용**: ~$0.02/월 (거의 무료)

### 빠른 설정

```bash
cd iac

# 1. Backend 리소스 생성 (S3 + DynamoDB)
terraform init
terraform apply

# 2. 출력된 backend_config 확인
terraform output backend_config

# 3. backend.tf 파일 수정 (실제 생성된 값으로)
# 4. State 마이그레이션
terraform init -migrate-state
```

**상세 가이드**: [BACKEND_SETUP.md](./BACKEND_SETUP.md) 참고

### 로컬 State 사용 (개인 개발)

팀 협업이 필요 없다면 backend 설정을 건너뛰고 로컬 state를 사용할 수 있습니다:

```bash
# backend.tf 비활성화
mv backend.tf backend.tf.disabled

# backend-setup.tf도 비활성화
mv backend-setup.tf backend-setup.tf.disabled
```

## 시작하기

### 0. DuckDNS 도메인 생성

먼저 DuckDNS에서 무료 도메인을 생성합니다:

1. **DuckDNS 가입**
   - https://www.duckdns.org 접속
   - GitHub, Google 등으로 로그인

2. **서브도메인 생성**
   - 원하는 이름 입력 (예: `myrps`)
   - 생성되는 도메인: `myrps.duckdns.org`
   - Token 복사 (나중에 사용)

3. **임시 IP 설정**
   - 일단 아무 IP나 입력 (예: 1.1.1.1)
   - 나중에 ALB 주소로 업데이트

### 1. 변수 파일 생성

`terraform.tfvars` 파일을 생성하고 필요한 변수를 설정합니다:

```bash
cd iac
```

`terraform.tfvars` 파일 생성 및 편집:

```hcl
# 필수 변수 - DuckDNS 도메인 사용
domain_name = "myrps.duckdns.org"  # 위에서 생성한 도메인

# 선택적 변수 (기본값 사용 가능)
aws_region         = "ap-northeast-2"
project_name       = "rps"
environment        = "prod"
ecs_desired_count  = 2
```

### 2. Terraform 초기화

```bash
terraform init
```

이 명령은:
- 필요한 provider 플러그인 다운로드
- Backend 초기화 (설정된 경우)
- 모듈 다운로드 (사용하는 경우)

### 3. 인프라 계획 확인

```bash
terraform plan
```

생성될 리소스를 검토합니다. 약 40-50개의 리소스가 생성됩니다.

### 4. 인프라 배포

```bash
terraform apply
```

`yes`를 입력하여 배포를 확인합니다. 배포는 약 15-20분 소요됩니다.

## 배포 절차

### 단계별 배포 가이드

#### 1단계: 인프라 배포

```bash
cd iac

# 초기화
terraform init

# 계획 확인
terraform plan -out=tfplan

# 배포 실행
terraform apply tfplan
```

#### 2단계: 출력 값 확인

배포 완료 후 중요한 출력 값을 확인합니다:

```bash
# ECR Repository URL
terraform output ecr_repository_url

# ALB DNS Name
terraform output alb_dns_name

# Application URL
terraform output application_url

# Redis Endpoint
terraform output redis_endpoint

# RabbitMQ Endpoint
terraform output rabbitmq_endpoint
```

#### 3단계: DuckDNS 설정 및 ACM 인증서 검증

**중요**: DuckDNS는 TXT 레코드를 지원하지 않으므로 ACM 인증서 검증에 특별한 방법이 필요합니다.

##### 방법 1: Email 검증 (권장)

ACM 콘솔에서 이메일 검증으로 변경:

1. AWS Console → Certificate Manager 접속
2. 생성된 인증서 선택
3. "Request certificate" 다시 시작하되 **Email validation** 선택
4. 도메인 관리자 이메일로 검증 링크 수신
5. 링크 클릭하여 검증 완료

Terraform 코드 수정 필요:
```hcl
# route53.tf에서 validation_method 변경
resource "aws_acm_certificate" "main" {
  domain_name       = var.domain_name
  validation_method = "EMAIL"  # DNS에서 EMAIL로 변경
  # ...
}
```

##### 방법 2: HTTP 검증 (Let's Encrypt 스타일)

Certbot을 사용하여 Let's Encrypt 인증서 발급 후 ACM에 업로드:

```bash
# Certbot 설치 (macOS)
brew install certbot

# 인증서 발급 (수동 모드)
sudo certbot certonly --manual --preferred-challenges http -d myrps.duckdns.org

# 발급된 인증서를 ACM에 업로드
aws acm import-certificate \
  --certificate fileb:///etc/letsencrypt/live/myrps.duckdns.org/cert.pem \
  --private-key fileb:///etc/letsencrypt/live/myrps.duckdns.org/privkey.pem \
  --certificate-chain fileb:///etc/letsencrypt/live/myrps.duckdns.org/chain.pem \
  --region ap-northeast-2
```

##### 방법 3: DuckDNS A 레코드 업데이트

ACM 검증이 완료되면 DuckDNS A 레코드를 ALB로 업데이트:

```bash
# ALB DNS 이름 가져오기
ALB_DNS=$(cd iac && terraform output -raw alb_dns_name)
echo "ALB DNS: $ALB_DNS"

# ALB의 IP 주소 확인
nslookup $ALB_DNS

# DuckDNS 업데이트 (브라우저 또는 API)
# 브라우저: https://www.duckdns.org/update?domains=myrps&token=YOUR_TOKEN&ip=ALB_IP
# 또는 DuckDNS 웹사이트에서 수동 업데이트
```

**참고**: ALB는 IP가 동적으로 변경될 수 있으므로 CNAME이 이상적이지만, DuckDNS는 CNAME을 지원하지 않습니다. 대안으로 CloudFlare를 사용할 수 있습니다.

##### 방법 4: CloudFlare 사용 (가장 권장)

DuckDNS 대신 CloudFlare의 무료 DNS를 사용하면 더 쉽습니다:

1. CloudFlare 가입 (무료)
2. 무료 도메인 또는 기존 도메인 추가
3. CloudFlare DNS에서 CNAME 레코드 생성
4. CloudFlare SSL/TLS 설정 (Full 모드)
5. ACM 인증서 자동 검증

#### 3단계 대안: 간단한 HTTP 전용 배포

HTTPS 설정이 복잡하다면 개발 단계에서는 HTTP만 사용:

```bash
# alb.tf에서 HTTPS 리스너 제거하고 HTTP만 사용
# 자세한 내용은 트러블슈팅 섹션 참조
```

## 변수 설정 가이드

### 필수 변수

| 변수 | 설명 | 예시 |
|------|------|------|
| `domain_name` | 애플리케이션 도메인 | `rps.example.com` |

### 주요 선택적 변수

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `aws_region` | `ap-northeast-2` | AWS 리전 |
| `project_name` | `rps` | 프로젝트 이름 |
| `environment` | `prod` | 환경 (dev, staging, prod) |
| `ecs_desired_count` | `2` | Web App ECS 태스크 수 |
| `ecs_task_cpu` | `512` | Web App ECS 태스크 CPU (0.5 vCPU) |
| `ecs_task_memory` | `1024` | Web App ECS 태스크 메모리 (1 GB) |
| `game_server_desired_count` | `1` | Game Server ECS 태스크 수 |
| `game_server_task_cpu` | `256` | Game Server ECS 태스크 CPU (0.25 vCPU) |
| `game_server_task_memory` | `512` | Game Server ECS 태스크 메모리 (512 MB) |
| `redis_node_type` | `cache.t4g.micro` | Valkey 인스턴스 타입 |
| `redis_num_node_groups` | `2` | Valkey 샤드(노드 그룹) 개수 |
| `redis_replicas_per_node_group` | `1` | 각 샤드당 replica 개수 |
| `rabbitmq_instance_type` | `mq.t3.micro` | RabbitMQ 인스턴스 타입 |

### 네트워크 변수

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `vpc_cidr` | `10.0.0.0/16` | VPC CIDR 블록 |
| `availability_zones` | `["ap-northeast-2a", "ap-northeast-2c"]` | 가용 영역 |
| `public_subnet_cidrs` | `["10.0.1.0/24", "10.0.2.0/24"]` | Public 서브넷 CIDR |
| `private_subnet_cidrs` | `["10.0.11.0/24", "10.0.12.0/24"]` | Private 서브넷 CIDR |

### 보안 변수

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `rabbitmq_username` | `admin` | RabbitMQ 관리자 사용자명 |
| `rabbitmq_password` | (자동 생성) | RabbitMQ 비밀번호 |

**참고**: Valkey는 개발 환경을 위해 암호화 및 인증이 비활성화되어 있습니다. 프로덕션 환경에서는 TLS와 Auth Token을 활성화하는 것을 권장합니다.

## 애플리케이션 배포

### Docker 이미지 빌드 및 ECR 푸시

#### 1단계: ECR 로그인

```bash
# ECR Repository URL 가져오기
ECR_URL=$(cd iac && terraform output -raw ecr_repository_url)
AWS_REGION="ap-northeast-2"

# ECR 로그인
aws ecr get-login-password --region $AWS_REGION | \
  docker login --username AWS --password-stdin $ECR_URL
```

#### 2단계: Docker 이미지 빌드

프로젝트 루트 디렉토리에서:

```bash
# 이미지 빌드
docker build -t rps-app:latest -f iac/Dockerfile .

# 빌드 확인
docker images | grep rps-app
```

#### 3단계: 이미지 태그 및 푸시

```bash
# 이미지 태그
docker tag rps-app:latest $ECR_URL:latest
docker tag rps-app:latest $ECR_URL:$(date +%Y%m%d-%H%M%S)

# ECR에 푸시
docker push $ECR_URL:latest
docker push $ECR_URL:$(date +%Y%m%d-%H%M%S)
```

#### 4단계: ECS 서비스 업데이트

```bash
# ECS 서비스 강제 재배포 (새 이미지 사용)
aws ecs update-service \
  --cluster rps-prod-cluster \
  --service rps-prod-service \
  --force-new-deployment \
  --region ap-northeast-2

# 배포 상태 확인
aws ecs describe-services \
  --cluster rps-prod-cluster \
  --services rps-prod-service \
  --region ap-northeast-2 \
  --query 'services[0].deployments'
```

### 배포 스크립트 (선택사항)

편의를 위해 배포 스크립트를 생성할 수 있습니다:

```bash
#!/bin/bash
# deploy.sh

set -e

echo "🚀 Starting deployment..."

# Get ECR URL
cd iac
ECR_URL=$(terraform output -raw ecr_repository_url)
cd ..

# Build image
echo "📦 Building Docker image..."
docker build -t rps-app:latest -f iac/Dockerfile .

# Login to ECR
echo "🔐 Logging in to ECR..."
aws ecr get-login-password --region ap-northeast-2 | \
  docker login --username AWS --password-stdin $ECR_URL

# Tag and push
echo "⬆️  Pushing image to ECR..."
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
docker tag rps-app:latest $ECR_URL:latest
docker tag rps-app:latest $ECR_URL:$TIMESTAMP
docker push $ECR_URL:latest
docker push $ECR_URL:$TIMESTAMP

# Update ECS service
echo "🔄 Updating ECS service..."
aws ecs update-service \
  --cluster rps-prod-cluster \
  --service rps-prod-service \
  --force-new-deployment \
  --region ap-northeast-2

echo "✅ Deployment initiated! Check ECS console for status."
```

실행:
```bash
chmod +x deploy.sh
./deploy.sh
```

## 모니터링 및 로그

### CloudWatch Logs

ECS 태스크 로그 확인:

```bash
# 로그 그룹 확인
aws logs describe-log-groups \
  --log-group-name-prefix /ecs/rps \
  --region ap-northeast-2

# 최근 로그 스트림 확인
aws logs describe-log-streams \
  --log-group-name /ecs/rps-prod \
  --order-by LastEventTime \
  --descending \
  --max-items 5 \
  --region ap-northeast-2

# 로그 확인 (tail)
aws logs tail /ecs/rps-prod --follow --region ap-northeast-2
```

### ECS 서비스 상태

```bash
# 서비스 상태 확인
aws ecs describe-services \
  --cluster rps-prod-cluster \
  --services rps-prod-service \
  --region ap-northeast-2

# 실행 중인 태스크 확인
aws ecs list-tasks \
  --cluster rps-prod-cluster \
  --service-name rps-prod-service \
  --region ap-northeast-2
```

### ALB 헬스 체크

```bash
# Target Group 상태 확인
TG_ARN=$(aws elbv2 describe-target-groups \
  --names rps-prod-tg \
  --region ap-northeast-2 \
  --query 'TargetGroups[0].TargetGroupArn' \
  --output text)

aws elbv2 describe-target-health \
  --target-group-arn $TG_ARN \
  --region ap-northeast-2
```

## 트러블슈팅

### 일반적인 문제 및 해결 방법

#### 1. Terraform 초기화 실패

**증상**: `terraform init` 실패

**해결 방법**:
```bash
# Provider 캐시 삭제
rm -rf .terraform .terraform.lock.hcl

# 다시 초기화
terraform init
```

#### 2. ACM 인증서 검증 대기

**증상**: ACM 인증서가 `PENDING_VALIDATION` 상태

**해결 방법**:
- Route53 DNS 레코드가 자동으로 생성되었는지 확인
- DNS 전파 대기 (최대 30분)
- 도메인 네임서버가 Route53으로 설정되었는지 확인

```bash
# 인증서 상태 확인
aws acm describe-certificate \
  --certificate-arn $(cd iac && terraform output -raw acm_certificate_arn) \
  --region ap-northeast-2 \
  --query 'Certificate.Status'

# DNS 레코드 확인
dig $(cd iac && terraform output -raw domain_name)
```

#### 3. ECS 태스크가 시작되지 않음

**증상**: ECS 태스크가 계속 실패하거나 시작되지 않음

**해결 방법**:

1. **CloudWatch Logs 확인**:
```bash
aws logs tail /ecs/rps-prod --follow --region ap-northeast-2
```

2. **태스크 실패 이유 확인**:
```bash
aws ecs describe-tasks \
  --cluster rps-prod-cluster \
  --tasks $(aws ecs list-tasks \
    --cluster rps-prod-cluster \
    --service-name rps-prod-service \
    --region ap-northeast-2 \
    --query 'taskArns[0]' \
    --output text) \
  --region ap-northeast-2 \
  --query 'tasks[0].stopCode'
```

3. **일반적인 원인**:
   - ECR 이미지가 없음 → 이미지 빌드 및 푸시
   - 환경 변수 오류 → Task Definition 확인
   - IAM 권한 부족 → IAM Role 확인
   - 메모리/CPU 부족 → Task Definition 리소스 증가

#### 4. ALB 헬스 체크 실패

**증상**: Target Group에서 타겟이 unhealthy 상태

**해결 방법**:

1. **헬스 체크 경로 확인**:
```bash
# 애플리케이션이 / 경로에 응답하는지 확인
# ECS 태스크 내부에서 테스트
curl http://localhost:5184/
```

2. **Security Group 확인**:
   - ALB → ECS 포트 5184 허용 확인
   - ECS → Redis/RabbitMQ 연결 확인

3. **헬스 체크 설정 조정**:
   - `health_check_interval` 증가
   - `healthy_threshold` 감소
   - `health_check_timeout` 증가

#### 5. Redis 연결 실패

**증상**: 애플리케이션이 Redis에 연결할 수 없음

**해결 방법**:

1. **Redis 엔드포인트 확인**:
```bash
cd iac
terraform output redis_endpoint
```

2. **Security Group 확인**:
```bash
# ECS Security Group에서 Redis로 6379 포트 허용 확인
aws ec2 describe-security-groups \
  --filters "Name=tag:Name,Values=rps-prod-redis-sg" \
  --region ap-northeast-2
```

3. **네트워크 연결 테스트**:
   - ECS 태스크와 Redis가 같은 VPC의 private subnet에 있는지 확인
   - NAT Gateway가 정상 작동하는지 확인

#### 6. RabbitMQ 연결 실패

**증상**: 애플리케이션이 RabbitMQ에 연결할 수 없음

**해결 방법**:

1. **RabbitMQ 엔드포인트 및 자격증명 확인**:
```bash
cd iac
terraform output rabbitmq_endpoint

# Secrets Manager에서 자격증명 확인
aws secretsmanager get-secret-value \
  --secret-id rps-prod-rabbitmq-credentials \
  --region ap-northeast-2 \
  --query 'SecretString' \
  --output text
```

2. **Security Group 확인**:
   - ECS → RabbitMQ 포트 5671 (AMQPS) 허용 확인

#### 7. 도메인 접속 불가

**증상**: 도메인으로 접속 시 연결 실패

**해결 방법**:

1. **DNS 레코드 확인**:
```bash
# A 레코드 확인
dig $(cd iac && terraform output -raw domain_name)

# ALB DNS와 비교
cd iac && terraform output alb_dns_name
```

2. **네임서버 확인**:
```bash
# 도메인 네임서버 확인
dig NS $(cd iac && terraform output -raw domain_name)

# Route53 네임서버와 비교
cd iac && terraform output route53_name_servers
```

3. **도메인 등록기관에서 네임서버 업데이트**

#### 8. Terraform State Lock 오류

**증상**: `Error acquiring the state lock`

**해결 방법**:
```bash
# 강제로 lock 해제 (주의: 다른 작업이 진행 중이 아닌지 확인)
terraform force-unlock <LOCK_ID>
```

### 디버깅 명령어 모음

```bash
# ECS 태스크 로그 실시간 확인
aws logs tail /ecs/rps-prod --follow --region ap-northeast-2

# ECS 서비스 이벤트 확인
aws ecs describe-services \
  --cluster rps-prod-cluster \
  --services rps-prod-service \
  --region ap-northeast-2 \
  --query 'services[0].events[:10]'

# ALB Target Health 확인
aws elbv2 describe-target-health \
  --target-group-arn $(aws elbv2 describe-target-groups \
    --names rps-prod-tg \
    --region ap-northeast-2 \
    --query 'TargetGroups[0].TargetGroupArn' \
    --output text) \
  --region ap-northeast-2

# Security Group 규칙 확인
aws ec2 describe-security-groups \
  --filters "Name=tag:Project,Values=rps" \
  --region ap-northeast-2 \
  --query 'SecurityGroups[*].[GroupName,GroupId]'

# VPC 리소스 확인
aws ec2 describe-vpcs \
  --filters "Name=tag:Project,Values=rps" \
  --region ap-northeast-2
```

## 비용 최적화

### 예상 월간 비용 (ap-northeast-2 기준)

| 서비스 | 리소스 | 예상 비용 |
|--------|--------|-----------|
| ECS Fargate (Web App) | 2 tasks (0.5 vCPU, 1GB) | ~$30 |
| ECS Fargate (Game Server) | 1 task (0.25 vCPU, 512MB) | ~$8 |
| ALB | 1 ALB | ~$20 |
| NAT Gateway | 1 NAT Gateway | ~$35 |
| ElastiCache Valkey | cache.t4g.micro × 4 (2샤드 × 2노드) | ~$50 |
| Amazon MQ RabbitMQ | mq.t3.micro (단일 인스턴스) | ~$18 |
| ECR | 20 images (~10GB) | ~$1 |
| Route53 | 1 hosted zone | ~$0.50 |
| **총계** | | **~$162/월** |

### 비용 절감 팁

1. **개발 환경에서는 리소스 축소**:
   ```hcl
   # terraform.tfvars (dev)
   ecs_desired_count = 1
   game_server_desired_count = 1
   redis_num_node_groups = 1
   redis_replicas_per_node_group = 0  # replica 없이 primary만
   ```

2. **사용하지 않을 때 인프라 중지**:
   ```bash
   # ECS 서비스 스케일 다운
   aws ecs update-service \
     --cluster rps-prod-cluster \
     --service rps-prod-service \
     --desired-count 0 \
     --region ap-northeast-2
   
   # Game Server도 스케일 다운
   aws ecs update-service \
     --cluster rps-prod-cluster \
     --service rps-prod-game-server \
     --desired-count 0 \
     --region ap-northeast-2
   ```

3. **Fargate Spot 사용 고려** (프로덕션 제외):
   - 최대 70% 비용 절감
   - 중단 가능성 있음

4. **Reserved Capacity 구매** (장기 운영 시):
   - ElastiCache 및 RDS Reserved Instances
   - 1년 약정 시 최대 40% 절감

## 정리

### 인프라 삭제

**주의**: 이 작업은 모든 리소스를 영구적으로 삭제합니다.

```bash
cd iac

# 삭제 계획 확인
terraform plan -destroy

# 인프라 삭제
terraform destroy
```

삭제 전 확인사항:
- ECR 이미지 백업 필요 여부
- RDS 스냅샷 생성 (사용하는 경우)
- CloudWatch Logs 백업 필요 여부

### 부분 삭제

특정 리소스만 삭제:

```bash
# ECS 서비스만 삭제
terraform destroy -target=aws_ecs_service.rps

# Redis만 삭제
terraform destroy -target=aws_elasticache_replication_group.redis
```

## DuckDNS 대안: CloudFlare (더 쉬운 방법)

DuckDNS의 제약사항(TXT 레코드 미지원, CNAME 미지원) 때문에 **CloudFlare 사용을 강력히 권장**합니다.

### CloudFlare 설정 (무료)

1. **CloudFlare 가입**
   - https://www.cloudflare.com 접속
   - 무료 플랜 선택

2. **도메인 추가**
   - 기존 도메인이 있다면 추가
   - 없다면 Freenom에서 무료 도메인 (.tk, .ml 등) 획득 후 추가

3. **DNS 레코드 설정**
   ```
   Type: CNAME
   Name: @ (또는 원하는 서브도메인)
   Target: <ALB DNS 이름>
   Proxy status: Proxied (주황색 구름)
   ```

4. **SSL/TLS 설정**
   - SSL/TLS → Overview → Full (strict) 선택
   - Edge Certificates → Always Use HTTPS 활성화

5. **Terraform 변수 업데이트**
   ```hcl
   domain_name = "yourdomain.tk"  # 또는 CloudFlare 도메인
   ```

### CloudFlare 장점

- ✅ CNAME 지원 (ALB DNS 직접 연결)
- ✅ TXT 레코드 지원 (ACM 자동 검증)
- ✅ 무료 SSL/TLS (CloudFlare Origin Certificate)
- ✅ CDN 및 DDoS 보호 포함
- ✅ 자동 HTTPS 리다이렉트
- ✅ DNS 관리 UI가 훨씬 편리

### Freenom + CloudFlare 조합 (완전 무료)

1. **Freenom에서 무료 도메인 획득**
   - https://www.freenom.com
   - .tk, .ml, .ga, .cf, .gq 도메인 무료
   - 예: myrps.tk

2. **CloudFlare에 도메인 추가**
   - CloudFlare 네임서버로 변경
   - DNS 레코드 설정

3. **Terraform 배포**
   - ACM 인증서 자동 검증
   - 모든 것이 자동으로 작동

이 방법이 DuckDNS보다 훨씬 간단하고 안정적입니다!

## 추가 리소스

### AWS 문서

- [ECS Fargate](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/AWS_Fargate.html)
- [ElastiCache Redis](https://docs.aws.amazon.com/AmazonElastiCache/latest/red-ug/)
- [Amazon MQ RabbitMQ](https://docs.aws.amazon.com/amazon-mq/latest/developer-guide/rabbitmq-broker.html)
- [Application Load Balancer](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/)

### Terraform 문서

- [AWS Provider](https://registry.terraform.io/providers/hashicorp/aws/latest/docs)
- [Terraform Best Practices](https://www.terraform-best-practices.com/)

## 지원

문제가 발생하거나 질문이 있는 경우:

1. 이 README의 트러블슈팅 섹션 확인
2. CloudWatch Logs 확인
3. AWS Support 또는 개발팀에 문의

## 서비스별 상세 설명

### Web Application (rps-app)

- **역할**: HTTP/HTTPS 요청 처리, SignalR 실시간 통신
- **배포 방식**: ALB를 통한 로드 밸런싱
- **확장**: Horizontal Scaling (desired_count 조정)
- **접속**: 외부 인터넷에서 도메인을 통해 접근 가능

### Game Server (rps-game-server)

- **역할**: RabbitMQ 메시지 소비, 게임 로직 처리
- **배포 방식**: 백그라운드 워커 (ALB 없음)
- **확장**: Horizontal Scaling (game_server_desired_count 조정)
- **접속**: 외부 접근 불가, VPC 내부에서만 Redis/RabbitMQ 접근

### ElastiCache Valkey (Cluster Mode)

- **구성**: 2개 샤드 × 2개 노드 (primary + replica) = 총 4개 노드
- **자동 확장**: 샤드 개수 증가로 수평 확장 가능 (수동)
- **Failover**: 자동 장애 조치 활성화
- **Multi-AZ**: 고가용성 보장
- **접속**: Private subnet, ECS 태스크에서만 접근 가능
- **엔드포인트**: Configuration Endpoint (Cluster Mode용)

**확장 방법**:
```hcl
# terraform.tfvars
redis_num_node_groups = 3  # 샤드 2개 → 3개로 증가
```

### Amazon MQ RabbitMQ

- **구성**: 단일 인스턴스 (개발 환경용)
- **사용자**: admin (자동 생성 비밀번호), guest/guest
- **Failover**: 없음 (프로덕션에서는 CLUSTER_MULTI_AZ 권장)
- **접속**: Private subnet, ECS 태스크에서만 접근 가능
- **포트**: 5671 (AMQPS), 443 (Management Console)

**프로덕션 전환 시**:
```hcl
# rabbitmq.tf
deployment_mode    = "CLUSTER_MULTI_AZ"
host_instance_type = "mq.m5.large"  # 최소 요구사항
subnet_ids         = [aws_subnet.private[0].id, aws_subnet.private[1].id]
```

### 보안 구성

**네트워크 격리**:
- Web Application: Public subnet의 ALB → Private subnet의 ECS
- Game Server: Private subnet에서만 실행
- Valkey/RabbitMQ: Private subnet, Security Group으로 ECS만 접근 허용

**데이터 암호화**:
- Valkey: **개발 환경용으로 암호화 비활성화** (TLS/Auth Token 없음)
  - 프로덕션 환경에서는 `at_rest_encryption_enabled = true`, `transit_encryption_enabled = true` 권장
- RabbitMQ: AMQPS (TLS) 사용
- ECR: 이미지 암호화 (AES256)

**자격증명 관리**:
- RabbitMQ 비밀번호: AWS Secrets Manager에 저장
- Valkey: 인증 비활성화 (개발 환경용)

**Terraform State 관리**:
- S3: State 파일 암호화 및 버전 관리
- DynamoDB: State Locking으로 동시 실행 방지
- 팀 협업 시 일관된 인프라 상태 유지

## 주요 변경 사항 (2025-11-09)

### 1. Terraform Backend 설정 추가
- **S3 + DynamoDB 원격 State 관리** 구성 추가
- 팀 협업을 위한 State Locking 지원
- State 파일 버전 관리 및 암호화
- 상세 가이드: [BACKEND_SETUP.md](./BACKEND_SETUP.md)

### 2. Redis/Valkey 보안 설정 변경
- **개발 환경 최적화**: TLS 및 저장 시 암호화 비활성화
- Auth Token 제거 (인증 불필요)
- 프로덕션 환경에서는 암호화 재활성화 권장

### 3. Game Server 추가
- RabbitMQ 메시지 소비 전용 서비스
- 별도 ECR 리포지토리 및 ECS 서비스
- Redis/RabbitMQ 접근 가능한 백그라운드 워커

### 4. Redis Cluster Mode 활성화
- 샤딩 기반 수평 확장 지원
- 2개 샤드 × 2개 노드 (primary + replica)
- 변수로 샤드/replica 개수 조정 가능

### 5. RabbitMQ 사용자 추가
- guest/guest 사용자 추가 (개발 환경용)
- 단일 인스턴스 구성 (비용 절감)

---

**마지막 업데이트**: 2025-11-09
