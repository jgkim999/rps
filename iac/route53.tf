# ACM Certificate for DuckDNS domain - DISABLED
# DuckDNS doesn't support TXT records for DNS validation
# Email validation also doesn't work (no email service for *.duckdns.org)
# 
# For HTTPS support, consider:
# 1. Use CloudFlare + Freenom (recommended, see DUCKDNS_SETUP.md)
# 2. Manually import Let's Encrypt certificate
# 3. Use a paid domain with Route53
#
# resource "aws_acm_certificate" "main" {
#   domain_name       = var.domain_name
#   validation_method = "EMAIL"
#
#   lifecycle {
#     create_before_destroy = true
#   }
#
#   tags = {
#     Name        = "${var.project_name}-${var.environment}-cert"
#     Environment = var.environment
#     Project     = var.project_name
#   }
# }

# Note: Currently using HTTP only (port 80)
# HTTPS (port 443) is disabled until certificate is configured

# DuckDNS Update Instructions:
# After terraform apply completes, update DuckDNS with ALB IP:
# 
# 1. Get ALB DNS name:
#    terraform output alb_dns_name
#
# 2. Resolve ALB IP addresses:
#    nslookup <alb-dns-name>
#
# 3. Update DuckDNS (choose one IP from the list):
#    curl "https://www.duckdns.org/update?domains=rps100&token=283c1e08-9570-412b-b9a3-3ac6681eab64&ip=<ALB_IP>"
#
# 4. Or use the DuckDNS website to update manually
#
# Note: ALB IPs can change, so you may need to update periodically
# Consider using a Lambda function to auto-update DuckDNS
