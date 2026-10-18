resource "aws_db_subnet_group" "wallet" {
  name       = local.name
  subnet_ids = aws_subnet.database[*].id
}

resource "aws_db_parameter_group" "wallet" {
  name   = "${local.name}-postgres17"
  family = "postgres17"
  parameter {
    name  = "rds.force_ssl"
    value = "1"
  }
}

resource "aws_db_instance" "wallet" {
  identifier                      = local.name
  engine                          = "postgres"
  engine_version                  = "17"
  instance_class                  = "db.t4g.micro"
  allocated_storage               = 20
  storage_type                    = "gp3"
  storage_encrypted               = true
  db_name                         = "novapay"
  username                        = "novapay_admin"
  manage_master_user_password     = true
  db_subnet_group_name            = aws_db_subnet_group.wallet.name
  parameter_group_name            = aws_db_parameter_group.wallet.name
  vpc_security_group_ids          = [aws_security_group.database.id]
  publicly_accessible             = false
  multi_az                        = false
  backup_retention_period         = 7
  backup_window                   = "02:00-03:00"
  maintenance_window              = "sun:03:30-sun:04:30"
  auto_minor_version_upgrade      = true
  allow_major_version_upgrade     = false
  apply_immediately               = false
  deletion_protection             = true
  skip_final_snapshot             = false
  final_snapshot_identifier       = "${local.name}-final"
  copy_tags_to_snapshot           = true
  enabled_cloudwatch_logs_exports = ["postgresql"]
}

# Create the container for the application's secret only. A separate privileged
# bootstrap will create the read-only DB role and write its password at runtime.
# Never give the API the RDS master secret or store passwords in Terraform state.
resource "aws_secretsmanager_secret" "application_database" {
  name                    = "${local.name}/database/application"
  description             = "Read-only novapay_app database credentials; populated during bootstrap"
  recovery_window_in_days = 7
}
