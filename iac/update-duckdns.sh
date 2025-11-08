#!/bin/bash
# DuckDNS 자동 업데이트 스크립트
# ALB의 IP 주소를 DuckDNS에 자동으로 업데이트합니다

set -e

# 설정
DUCKDNS_DOMAIN="rps100"
DUCKDNS_TOKEN="283c1e08-9570-412b-b9a3-3ac6681eab64"
AWS_REGION="ap-northeast-2"

echo "🔍 ALB DNS 이름 가져오는 중..."
cd "$(dirname "$0")"
ALB_DNS=$(terraform output -raw alb_dns_name 2>/dev/null)

if [ -z "$ALB_DNS" ]; then
  echo "❌ 오류: ALB DNS 이름을 가져올 수 없습니다."
  echo "   terraform apply를 먼저 실행하세요."
  exit 1
fi

echo "✅ ALB DNS: $ALB_DNS"

echo ""
echo "🔍 ALB IP 주소 확인 중..."
ALB_IPS=$(dig +short "$ALB_DNS" | grep -E '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')

if [ -z "$ALB_IPS" ]; then
  echo "❌ 오류: ALB IP 주소를 확인할 수 없습니다."
  exit 1
fi

# 첫 번째 IP 사용
ALB_IP=$(echo "$ALB_IPS" | head -n 1)
echo "✅ ALB IP: $ALB_IP"
echo ""
echo "📋 모든 ALB IP 주소:"
echo "$ALB_IPS" | while read ip; do echo "   - $ip"; done

echo ""
echo "🔄 DuckDNS 업데이트 중..."
RESPONSE=$(curl -s "https://www.duckdns.org/update?domains=$DUCKDNS_DOMAIN&token=$DUCKDNS_TOKEN&ip=$ALB_IP")

if [ "$RESPONSE" = "OK" ]; then
  echo "✅ DuckDNS 업데이트 성공!"
  echo ""
  echo "📝 업데이트 정보:"
  echo "   도메인: $DUCKDNS_DOMAIN.duckdns.org"
  echo "   IP: $ALB_IP"
  echo ""
  echo "🌐 접속 테스트:"
  echo "   http://$DUCKDNS_DOMAIN.duckdns.org"
  echo "   https://$DUCKDNS_DOMAIN.duckdns.org (ACM 인증서 검증 후)"
  echo ""
  echo "⚠️  참고: ALB IP는 변경될 수 있습니다."
  echo "   정기적으로 이 스크립트를 실행하거나 cron job으로 설정하세요."
else
  echo "❌ DuckDNS 업데이트 실패: $RESPONSE"
  exit 1
fi

echo ""
echo "🔍 DNS 전파 확인 중..."
sleep 2
CURRENT_IP=$(dig +short "$DUCKDNS_DOMAIN.duckdns.org" | head -n 1)
if [ "$CURRENT_IP" = "$ALB_IP" ]; then
  echo "✅ DNS 전파 완료! ($CURRENT_IP)"
else
  echo "⏳ DNS 전파 대기 중... (현재: $CURRENT_IP, 예상: $ALB_IP)"
  echo "   최대 5분 정도 소요될 수 있습니다."
fi
