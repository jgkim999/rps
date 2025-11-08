# DuckDNS 설정 가이드 (rps100.duckdns.org)

이 가이드는 DuckDNS 도메인 `rps100.duckdns.org`를 AWS ECS 인프라와 연결하는 방법을 설명합니다.

## 현재 설정

- **도메인**: rps100.duckdns.org
- **토큰**: 283c1e08-9570-412b-b9a3-3ac6681eab64
- **검증 방법**: EMAIL (DuckDNS는 TXT 레코드 미지원)

## 배포 절차

### 1단계: terraform.tfvars 파일 생성

```bash
cd iac
cp terraform.tfvars.example terraform.tfvars
```

`terraform.tfvars` 파일 내용 확인 (이미 올바르게 설정됨):
```hcl
domain_name = "rps100.duckdns.org"
```

### 2단계: Terraform 초기화 및 배포

```bash
# Terraform 초기화
terraform init

# 배포 계획 확인
terraform plan

# 인프라 배포
terraform apply
```

배포는 약 15-20분 소요됩니다.

### 3단계: ACM 인증서 EMAIL 검증

**문제**: DuckDNS는 TXT 레코드를 지원하지 않아 DNS 검증이 불가능합니다.

**해결 방법 2가지**:

#### 방법 A: AWS Console에서 EMAIL 검증으로 변경 (권장)

1. AWS Console → Certificate Manager 접속
2. 생성된 인증서 확인 (PENDING_VALIDATION 상태)
3. 인증서 삭제
4. "Request certificate" 클릭
5. **Email validation** 선택
6. 도메인 입력: `rps100.duckdns.org`
7. 검증 이메일 수신 대기 (admin@rps100.duckdns.org 등)
8. 이메일의 검증 링크 클릭

**참고**: DuckDNS 도메인은 이메일을 받을 수 없으므로 이 방법도 작동하지 않습니다.

#### 방법 B: HTTP만 사용 (개발 환경 권장) ⭐

HTTPS 없이 HTTP만 사용하여 빠르게 시작:

1. `alb.tf` 수정:
```hcl
# HTTPS 리스너 주석 처리
# resource "aws_lb_listener" "https" {
#   ...
# }

# HTTP 리스너 수정 (redirect 제거)
resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.main.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.ecs.arn
  }
}
```

2. 다시 배포:
```bash
terraform apply
```

3. HTTP로 접속:
```bash
http://rps100.duckdns.org
```

### 4단계: DuckDNS A 레코드 업데이트

ALB의 IP 주소를 DuckDNS에 설정합니다.

#### 자동 업데이트 (권장)

```bash
cd iac
./update-duckdns.sh
```

이 스크립트는:
- ALB DNS 이름 가져오기
- ALB IP 주소 확인
- DuckDNS API로 자동 업데이트
- DNS 전파 확인

#### 수동 업데이트

```bash
# 1. ALB DNS 이름 확인
cd iac
terraform output alb_dns_name

# 2. ALB IP 주소 확인
nslookup <alb-dns-name>
# 또는
dig +short <alb-dns-name>

# 3. DuckDNS 업데이트 (첫 번째 IP 사용)
curl "https://www.duckdns.org/update?domains=rps100&token=283c1e08-9570-412b-b9a3-3ac6681eab64&ip=<ALB_IP>"

# 응답이 "OK"면 성공
```

#### DuckDNS 웹사이트에서 수동 업데이트

1. https://www.duckdns.org 로그인
2. `rps100` 도메인 찾기
3. IP 주소 입력란에 ALB IP 입력
4. "update ip" 클릭

### 5단계: 접속 테스트

```bash
# DNS 확인
dig rps100.duckdns.org

# HTTP 접속 테스트
curl -I http://rps100.duckdns.org

# 브라우저에서 접속
open http://rps100.duckdns.org
```

## 중요 참고사항

### ALB IP 주소 변경

ALB의 IP 주소는 AWS에서 자동으로 관리되며 변경될 수 있습니다.

**해결 방법**:

1. **정기적으로 업데이트 스크립트 실행**:
```bash
# cron job 설정 (매 시간마다 실행)
crontab -e

# 다음 줄 추가
0 * * * * cd /path/to/iac && ./update-duckdns.sh >> /tmp/duckdns-update.log 2>&1
```

2. **Lambda 함수로 자동화** (선택사항):
   - CloudWatch Events로 주기적 실행
   - ALB IP 변경 감지 시 DuckDNS 업데이트

### HTTPS 사용하려면?

DuckDNS의 제약사항 때문에 HTTPS 설정이 복잡합니다.

**권장 대안**:

#### 옵션 1: CloudFlare 사용 (가장 권장) ⭐

1. CloudFlare 무료 계정 생성
2. Freenom에서 무료 도메인 획득 (.tk, .ml 등)
3. CloudFlare DNS 설정
4. CNAME 레코드로 ALB 연결
5. CloudFlare SSL/TLS 활성화

자세한 내용은 `DUCKDNS_SETUP.md` 참조

#### 옵션 2: Let's Encrypt + Certbot

```bash
# Certbot 설치
brew install certbot

# 인증서 발급 (DNS 챌린지)
sudo certbot certonly --manual \
  --preferred-challenges dns \
  -d rps100.duckdns.org

# DuckDNS에 TXT 레코드 추가 필요 (불가능!)
```

**문제**: DuckDNS는 TXT 레코드를 지원하지 않아 이 방법도 불가능합니다.

#### 옵션 3: HTTP 챌린지 사용

```bash
# HTTP 챌린지로 인증서 발급
sudo certbot certonly --manual \
  --preferred-challenges http \
  -d rps100.duckdns.org

# 챌린지 파일을 웹서버에 배치
# 임시 EC2 인스턴스 또는 S3 + CloudFront 필요
```

발급 후 ACM에 업로드:
```bash
aws acm import-certificate \
  --certificate fileb:///etc/letsencrypt/live/rps100.duckdns.org/cert.pem \
  --private-key fileb:///etc/letsencrypt/live/rps100.duckdns.org/privkey.pem \
  --certificate-chain fileb:///etc/letsencrypt/live/rps100.duckdns.org/chain.pem \
  --region ap-northeast-2
```

## 트러블슈팅

### DuckDNS 업데이트 실패

**증상**: `curl` 명령이 "KO" 반환

**원인**:
- 잘못된 토큰
- 잘못된 도메인 이름
- 네트워크 문제

**해결**:
```bash
# 토큰 확인
echo "283c1e08-9570-412b-b9a3-3ac6681eab64"

# 도메인 확인
echo "rps100"

# DuckDNS 웹사이트에서 수동 업데이트 시도
```

### DNS 전파 안 됨

**증상**: `dig rps100.duckdns.org`가 이전 IP 반환

**해결**:
```bash
# DNS 캐시 플러시 (macOS)
sudo dscacheutil -flushcache
sudo killall -HUP mDNSResponder

# 다시 확인
dig rps100.duckdns.org
```

### ALB 접속 안 됨

**증상**: HTTP 접속 시 타임아웃

**원인**:
- Security Group 설정 오류
- ECS 태스크 실행 안 됨
- Target Group unhealthy

**해결**:
```bash
# ECS 서비스 상태 확인
aws ecs describe-services \
  --cluster rps-prod-cluster \
  --services rps-prod-service \
  --region ap-northeast-2

# Target Group 상태 확인
aws elbv2 describe-target-health \
  --target-group-arn $(aws elbv2 describe-target-groups \
    --names rps-prod-tg \
    --region ap-northeast-2 \
    --query 'TargetGroups[0].TargetGroupArn' \
    --output text) \
  --region ap-northeast-2
```

## 비용

DuckDNS 사용 시 비용:
- DuckDNS: **무료**
- AWS 인프라: 약 $129/월 (README.md 참조)

## 다음 단계

1. ✅ DuckDNS 도메인 등록 완료
2. ✅ Terraform 변수 설정
3. ⏳ Terraform 배포
4. ⏳ DuckDNS A 레코드 업데이트
5. ⏳ 애플리케이션 접속 테스트
6. ⏳ Docker 이미지 빌드 및 배포

다음 명령으로 시작:
```bash
cd iac
terraform init
terraform apply
./update-duckdns.sh
```

## 요약

**현재 설정으로 가능한 것**:
- ✅ HTTP 접속 (http://rps100.duckdns.org)
- ✅ 무료 도메인 사용
- ✅ 자동 DuckDNS 업데이트

**제약사항**:
- ❌ HTTPS 설정 복잡 (DuckDNS TXT 레코드 미지원)
- ⚠️ ALB IP 변경 시 수동 업데이트 필요 (또는 cron job)

**프로덕션 환경 권장**:
- CloudFlare + Freenom 조합 사용
- 완전한 HTTPS 지원
- CNAME 레코드로 ALB 직접 연결
