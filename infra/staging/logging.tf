resource "aws_cloudwatch_log_group" "wallet" {
  name              = "/novapay/staging/wallet"
  retention_in_days = 30
  # Assessment retention policy, not a claim of regulatory retention compliance.
}

output "application_log_group" {
  value = aws_cloudwatch_log_group.wallet.name
}
