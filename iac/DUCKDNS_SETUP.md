# DuckDNS 무료 도메인 설정 가이드

DuckDNS를 사용하여 완전 무료로 도메인을 설정하는 방법입니다.

## ⚠️ 중요: DuckDNS의 제약사항

DuckDNS는 다음 기능을 지원하지 않습니다:
- ❌ TXT 레코드 (ACM DNS 검증 불가)
- ❌ CNAME 레코드 (ALB 직접 연결 불가)
- ❌ A 레코드만 지원 (IP 주소만 가능)

**결론**: DuckDNS는 AWS ECS + ALB 환경에서 사용하기 어렵습니다.

## 권장 대안

### 옵션 1: CloudFlare (가장 권장) ⭐

**장점**: 완전 무료, 모든 기능 지원, 쉬운 설정

1. **Freenom에서 무료 도메인 획득**
   ```
   https://www.freenom.com
   - 무료 TLD: .tk, .ml, .ga, .cf, .gq
   - 예: myrps.tk
   ```

2. **CloudFlare 설정**
   ```
   https://www.cloudflare.com
   - 무료 플랜 선택
   - 도메인 추가
   - 네임서버 변경 (Freenom에서)
   ```

3. **DNS 레코드 추가**
   ```
   Type: CNAME
   Name: @
   Target: <ALB DNS 이름>
   Proxy: Enabled
   ```

4. **SSL/TLS 설정**
   ```
   SSL/TLS → Full (strict)
   Edge Certificates → Always Use HTTPS
   ```

5. **Terraform 배포**
   ```bash
   cd iac
   terraform apply
   # ACM 인증서 자동 검증됨!
   ```

### 옵션 2: HTTP만 사용 (개발 환경)

HTTPS 없이 HTTP만 사용하여 빠르게 테스트:

1. **route53.tf 수정**
   ```hcl
   # ACM 인증서 관련 코드 모두 주석 처리
   ```

2. **alb.tf 수정**
   ```hcl
   # HTTPS 리스너 제거
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

3. **배포 후 ALB DNS로 접속**
   ```bash
   terraform output alb_dns_name
   # http://<alb-dns-name> 으로 접속
   ```

**단점**: 
- HTTPS 없음 (보안 취약)
- SignalR WebSocket이 일부 브라우저에서 작동 안 할 수 있음

### 옵션 3: Let's Encrypt + Certbot

수동으로 인증서 발급 후 ACM에 업로드:

1. **Certbot 설치**
   ```bash
   brew install certbot
   ```

2. **DuckDNS 도메인 생성**
   ```
   https://www.duckdns.org
   - 서브도메인 생성: myrps.duckdns.org
   - Token 복사
   ```

3. **인증서 발급 (HTTP 챌린지)**
   ```bash
   sudo certbot certonly --manual \
     --preferred-challenges http \
     -d myrps.duckdns.org
   
   # 챌린지 파일을 웹서버에 배치하라는 지시가 나옴
   # 임시로 S3 + CloudFront 또는 EC2 웹서버 필요
   ```

4. **ACM에 인증서 업로드**
   ```bash
   aws acm import-certificate \
     --certificate fileb:///etc/letsencrypt/live/myrps.duckdns.org/cert.pem \
     --private-key fileb:///etc/letsencrypt/live/myrps.duckdns.org/privkey.pem \
     --certificate-chain fileb:///etc/letsencrypt/live/myrps.duckdns.org/chain.pem \
     --region ap-northeast-2
   ```

5. **Terraform에서 인증서 ARN 사용**
   ```hcl
   # alb.tf
   resource "aws_lb_listener" "https" {
     # ...
     certificate_arn = "arn:aws:acm:ap-northeast-2:xxxxx:certificate/xxxxx"
   }
   ```

6. **DuckDNS A 레코드 업데이트**
   ```bash
   # ALB IP 확인
   nslookup <alb-dns-name>
   
   # DuckDNS 업데이트
   curl "https://www.duckdns.org/update?domains=myrps&token=YOUR_TOKEN&ip=ALB_IP"
   ```

**단점**:
- 복잡한 수동 과정
- 90일마다 갱신 필요
- ALB IP 변경 시 DuckDNS 업데이트 필요

## 비교표

| 방법 | 비용 | 난이도 | HTTPS | 자동화 | 권장도 |
|------|------|--------|-------|--------|--------|
| CloudFlare + Freenom | 무료 | 쉬움 | ✅ | ✅ | ⭐⭐⭐⭐⭐ |
| HTTP만 사용 | 무료 | 매우 쉬움 | ❌ | ✅ | ⭐⭐ (개발용) |
| DuckDNS + Let's Encrypt | 무료 | 어려움 | ✅ | ❌ | ⭐⭐ |
| Route53 도메인 구매 | $13/년 | 쉬움 | ✅ | ✅ | ⭐⭐⭐⭐ |

## 최종 권장사항

### 프로덕션 환경
→ **CloudFlare + Freenom** 또는 **Route53 도메인 구매**

### 개발/테스트 환경
→ **HTTP만 사용** (가장 빠르고 간단)

### DuckDNS 사용 시
→ **Let's Encrypt + 수동 설정** (복잡하지만 가능)

## CloudFlare 설정 상세 가이드

### 1단계: Freenom 도메인 획득

1. https://www.freenom.com 접속
2. 원하는 도메인 검색 (예: myrps)
3. 사용 가능한 무료 TLD 선택 (.tk, .ml, .ga, .cf, .gq)
4. "Get it now!" 클릭
5. "Checkout" → 기간 선택 (최대 12개월 무료)
6. 이메일로 가입 및 도메인 등록 완료

### 2단계: CloudFlare 설정

1. https://www.cloudflare.com 가입
2. "Add a Site" 클릭
3. Freenom 도메인 입력 (예: myrps.tk)
4. Free 플랜 선택
5. DNS 레코드 스캔 완료 대기
6. CloudFlare 네임서버 확인 (예: `ns1.cloudflare.com`, `ns2.cloudflare.com`)

### 3단계: Freenom 네임서버 변경

1. Freenom 로그인 → Services → My Domains
2. "Manage Domain" 클릭
3. Management Tools → Nameservers
4. "Use custom nameservers" 선택
5. CloudFlare 네임서버 입력
6. "Change Nameservers" 클릭

### 4단계: CloudFlare DNS 설정

1. CloudFlare Dashboard → DNS → Records
2. 기존 레코드 삭제 (있다면)
3. 새 레코드 추가:
   ```
   Type: CNAME
   Name: @
   Target: (나중에 ALB DNS로 업데이트)
   Proxy status: Proxied (주황색)
   TTL: Auto
   ```

### 5단계: SSL/TLS 설정

1. SSL/TLS → Overview
2. Encryption mode: **Full (strict)** 선택
3. Edge Certificates 탭:
   - Always Use HTTPS: **On**
   - Automatic HTTPS Rewrites: **On**
   - Minimum TLS Version: **TLS 1.2**

### 6단계: Terraform 배포

1. `terraform.tfvars` 수정:
   ```hcl
   domain_name = "myrps.tk"  # Freenom 도메인
   ```

2. Terraform 배포:
   ```bash
   cd iac
   terraform init
   terraform apply
   ```

3. ALB DNS 확인:
   ```bash
   terraform output alb_dns_name
   ```

4. CloudFlare DNS 업데이트:
   - CNAME 레코드의 Target을 ALB DNS로 변경
   - 예: `rps-prod-alb-123456789.ap-northeast-2.elb.amazonaws.com`

### 7단계: ACM 인증서 검증

1. Terraform output에서 ACM 검증 레코드 확인:
   ```bash
   terraform output acm_validation_records
   ```

2. CloudFlare DNS에 CNAME 레코드 추가:
   ```
   Type: CNAME
   Name: <validation record name>
   Target: <validation record value>
   Proxy status: DNS only (회색)
   ```

3. 검증 완료 대기 (5-30분):
   ```bash
   aws acm describe-certificate \
     --certificate-arn $(terraform output -raw acm_certificate_arn) \
     --region ap-northeast-2 \
     --query 'Certificate.Status'
   ```

### 8단계: 접속 테스트

```bash
# HTTPS로 접속
curl -I https://myrps.tk

# 또는 브라우저에서
open https://myrps.tk
```

## 문제 해결

### ACM 인증서 검증이 안 됨

**원인**: CloudFlare Proxy가 활성화되어 있음

**해결**:
```
CloudFlare DNS → ACM 검증 레코드 → Proxy status를 "DNS only"로 변경
```

### CloudFlare SSL 오류

**원인**: SSL/TLS 모드가 잘못 설정됨

**해결**:
```
SSL/TLS → Overview → Full (strict) 선택
```

### 도메인 접속 안 됨

**원인**: DNS 전파 대기 중

**해결**:
```bash
# DNS 전파 확인
dig myrps.tk
nslookup myrps.tk

# 최대 24시간 소요 (보통 5-10분)
```

## 결론

**CloudFlare + Freenom 조합이 최선의 무료 솔루션입니다!**

- ✅ 완전 무료
- ✅ HTTPS 지원
- ✅ 자동화 가능
- ✅ 설정 간단
- ✅ CDN 및 보안 기능 포함

DuckDNS는 제약사항이 많아 AWS 환경에서 사용하기 어렵습니다.
