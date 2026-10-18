data "aws_ssm_parameter" "amazon_linux" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

resource "aws_iam_role" "instance" {
  name = "${local.name}-instance"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "instance" {
  name = "runtime"
  role = aws_iam_role.instance.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "InstanceManagement"
        Effect   = "Allow"
        Action   = ["ssm:UpdateInstanceInformation", "ssmmessages:CreateControlChannel", "ssmmessages:CreateDataChannel", "ssmmessages:OpenControlChannel", "ssmmessages:OpenDataChannel"]
        Resource = "*" # SSM agent channel APIs do not support resource-level permissions.
      },
      {
        Sid      = "RegistryAuthentication"
        Effect   = "Allow"
        Action   = ["ecr:GetAuthorizationToken"]
        Resource = "*" # ECR token acquisition requires a wildcard resource.
      },
      {
        Sid      = "PullWalletImages"
        Effect   = "Allow"
        Action   = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"]
        Resource = aws_ecr_repository.wallet.arn
      },
      {
        Sid    = "WriteApplicationLogs"
        Effect = "Allow"
        Action = ["logs:CreateLogStream", "logs:PutLogEvents"]
        # Each deployment has unique streams; access stays inside this one group.
        Resource = "${aws_cloudwatch_log_group.wallet.arn}:log-stream:*"
      },
      {
        Sid      = "ReadApplicationDatabaseSecret"
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = aws_secretsmanager_secret.application_database.arn
      }
    ]
  })
}

resource "aws_iam_instance_profile" "application" {
  name = local.name
  role = aws_iam_role.instance.name
}

resource "aws_instance" "application" {
  ami                         = nonsensitive(data.aws_ssm_parameter.amazon_linux.value)
  instance_type               = "t3.micro"
  subnet_id                   = aws_subnet.application.id
  associate_public_ip_address = true
  vpc_security_group_ids      = [aws_security_group.application.id]
  iam_instance_profile        = aws_iam_instance_profile.application.name
  user_data                   = file("${path.module}/host-init.sh")
  user_data_replace_on_change = true
  monitoring                  = false
  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
    instance_metadata_tags      = "disabled"
  }
  root_block_device {
    volume_type           = "gp3"
    volume_size           = 12
    encrypted             = true
    delete_on_termination = true
  }
  credit_specification {
    cpu_credits = "standard"
  }
  tags       = { Name = local.name }
  depends_on = [aws_route_table_association.application, aws_iam_role_policy.instance, aws_vpc_security_group_egress_rule.https]
}
