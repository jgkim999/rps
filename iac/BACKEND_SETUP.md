# Terraform Backend 설정 가이드

이 가이드는 Terraform State를 S3와 DynamoDB를 사용하여 원격으로 관리하는 방법을 설명합니다.

## 왜 원격 Backend가 필요한가?

### 문제점 (로컬 State 사용 시)
- 팀원들이 각자 다른 state 파일을 가지고 있어 충돌 발생
- 동시에 `terraform apply` 실행 시 리소스 중복 생성 또는 삭제
- State 파일 분실 시 인프라 관리 불가능

### 해결책 (원격 Backend 사용 시)
- ✅ S3에 중앙 집중식 state 저장
- ✅ DynamoDB로 state 잠금 (동시 실행 방지)
- ✅ State 파일 버전 관리 (롤백 가능)
- ✅ State 파일 암호화

## 설정 단계

### 1단계: Backend 리소스 생성

먼저 S3 버킷과 DynamoDB 테이블을 생성합니다.

```bash
cd iac

# backend.tf를 임시로 이름 변경 (아직 사용 안 함)
mv backend.tf backend.tf.disabled

# Backend 리소스 생성
terraform init
terraform plan
terraform apply
```

출력된 `backend_config` 값을 확인하고 복사합니다:

```bash
terraform output backend_config
```

### 2단계: Backend 설정 활성화

`backend.tf` 파일을 수정합니다:

```bash
# backend.tf 활성화
mv backend.tf.disabled backend.tf
```

`backend.tf` 파일을 열고 1단계에서 생성된 실제 값으로 수정:

```hcl
terraform {
  backend "s3" {
    bucket         = "rps-terraform-state-prod"  # 실제 생성된 버킷 이름
    key            = "prod/terraform.tfstate"
    region         = "ap-northeast-2"
    encrypt        = true
    dynamodb_table = "rps-terraform-lock-prod"   # 실제 생성된 테이블 이름
  }
}
```

### 3단계: State 마이그레이션

로컬 state를 원격 backend로 마이그레이션합니다:

```bash
# Backend 재초기화
terraform init -migrate-state

# 질문에 'yes' 입력
# "Do you want to copy existing state to the new backend?" -> yes
```

### 4단계: 확인

```bash
# 로컬 state 파일 확인 (없어야 정상)
ls terraform.tfstate*

# S3에 state 파일이 업로드되었는지 확인
aws s3 ls s3://rps-terraform-state-prod/prod/

# DynamoDB 테이블 확인
aws dynamodb describe-table \
  --table-name rps-terraform-lock-prod \
  --region ap-northeast-2
```

### 5단계: backend-setup.tf 제거 (선택사항)

Backend 리소스가 생성되었으므로 `backend-setup.tf`는 더 이상 필요하지 않습니다:

```bash
# 백업 후 제거
mv backend-setup.tf backend-setup.tf.backup
```

또는 주석 처리하여 보관할 수도 있습니다.

## 팀원 설정

다른 팀원이 프로젝트를 클론한 후:

```bash
cd iac

# Backend가 이미 설정되어 있으므로 바로 초기화
terraform init

# 원격 state를 자동으로 다운로드
terraform plan
```

## 동시 실행 방지

DynamoDB 잠금 덕분에 동시에 `terraform apply`를 실행해도 안전합니다:

```bash
# 팀원 A가 실행 중
terraform apply

# 팀원 B가 동시에 실행 시도
terraform apply
# Error: Error acquiring the state lock
# 팀원 A의 작업이 끝날 때까지 대기
```

## State 잠금 해제 (긴급 상황)

작업 중 프로세스가 비정상 종료되어 잠금이 남아있는 경우:

```bash
# 잠금 ID 확인 (에러 메시지에 표시됨)
terraform force-unlock <LOCK_ID>

# 예시
terraform force-unlock a1b2c3d4-5678-90ab-cdef-1234567890ab
```

**주의**: 다른 사람이 작업 중이 아닌지 반드시 확인 후 실행하세요!

## State 버전 관리

S3 버전 관리가 활성화되어 있어 이전 state로 롤백 가능합니다:

```bash
# S3 버전 목록 확인
aws s3api list-object-versions \
  --bucket rps-terraform-state-prod \
  --prefix prod/terraform.tfstate

# 특정 버전 다운로드
aws s3api get-object \
  --bucket rps-terraform-state-prod \
  --key prod/terraform.tfstate \
  --version-id <VERSION_ID> \
  terraform.tfstate.backup
```

## 환경별 State 분리

개발/스테이징/프로덕션 환경을 분리하려면:

```hcl
# backend.tf
terraform {
  backend "s3" {
    bucket         = "rps-terraform-state-prod"
    key            = "${var.environment}/terraform.tfstate"  # dev, staging, prod
    region         = "ap-northeast-2"
    encrypt        = true
    dynamodb_table = "rps-terraform-lock-prod"
  }
}
```

또는 Terraform Workspace 사용:

```bash
# Workspace 생성
terraform workspace new dev
terraform workspace new staging
terraform workspace new prod

# Workspace 전환
terraform workspace select dev

# 현재 workspace 확인
terraform workspace show
```

## 비용

### S3
- 저장: $0.025/GB/월
- State 파일 크기: ~1-10MB
- 예상 비용: **$0.01/월 미만**

### DynamoDB
- PAY_PER_REQUEST 모드
- 읽기/쓰기: $0.25/백만 요청
- Terraform 실행 시에만 사용
- 예상 비용: **$0.01/월 미만**

**총 예상 비용: ~$0.02/월** (거의 무료)

## 보안

### S3 버킷
- ✅ 퍼블릭 액세스 차단
- ✅ 암호화 활성화 (AES256)
- ✅ 버전 관리 활성화
- ✅ IAM 정책으로 접근 제어

### DynamoDB
- ✅ IAM 정책으로 접근 제어
- ✅ 암호화 활성화 (기본)

### IAM 정책 예시

팀원에게 필요한 최소 권한:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::rps-terraform-state-prod/*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket"
      ],
      "Resource": "arn:aws:s3:::rps-terraform-state-prod"
    },
    {
      "Effect": "Allow",
      "Action": [
        "dynamodb:GetItem",
        "dynamodb:PutItem",
        "dynamodb:DeleteItem"
      ],
      "Resource": "arn:aws:dynamodb:ap-northeast-2:*:table/rps-terraform-lock-prod"
    }
  ]
}
```

## 트러블슈팅

### 문제: Backend 초기화 실패

```bash
Error: Failed to get existing workspaces
```

**해결**:
```bash
# S3 버킷이 존재하는지 확인
aws s3 ls | grep terraform-state

# 버킷이 없다면 1단계부터 다시 시작
```

### 문제: State 잠금 타임아웃

```bash
Error: Error acquiring the state lock
```

**해결**:
1. 다른 팀원이 작업 중인지 확인
2. 아무도 작업 중이 아니면 강제 잠금 해제:
   ```bash
   terraform force-unlock <LOCK_ID>
   ```

### 문제: State 파일 충돌

```bash
Error: state snapshot was created by Terraform v1.x.x
```

**해결**:
```bash
# Terraform 버전 확인
terraform version

# 팀원들과 동일한 버전 사용
# .terraform-version 파일로 버전 고정 권장
```

## 모범 사례

1. **Backend 설정은 Git에 커밋**: `backend.tf`는 팀원들과 공유
2. **State 파일은 Git에 커밋 금지**: `.gitignore`에 추가
3. **환경별 State 분리**: dev/staging/prod 별도 관리
4. **정기적인 State 백업**: S3 버전 관리 활용
5. **최소 권한 원칙**: IAM 정책으로 접근 제어
6. **Terraform 버전 고정**: `.terraform-version` 파일 사용

## 참고 자료

- [Terraform Backend Configuration](https://www.terraform.io/language/settings/backends/s3)
- [State Locking](https://www.terraform.io/language/state/locking)
- [AWS S3 Backend](https://www.terraform.io/language/settings/backends/s3)

---

**마지막 업데이트**: 2025-11-09
