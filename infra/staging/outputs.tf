output "ecr_repository_url" {
  value = aws_ecr_repository.wallet.repository_url
}

output "instance_id" {
  value = aws_instance.application.id
}

output "database_endpoint" {
  value = aws_db_instance.wallet.address
}

output "application_database_secret_arn" {
  value = aws_secretsmanager_secret.application_database.arn
}

output "database_admin_secret_arn" {
  description = "Privileged bootstrap identity only; runtime instance has no access."
  value       = aws_db_instance.wallet.master_user_secret[0].secret_arn
}
