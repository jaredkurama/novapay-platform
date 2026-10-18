resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
}

resource "aws_iam_role" "github_deployment" {
  name                 = "${local.name}-github-deployment"
  max_session_duration = 3600
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = aws_iam_openid_connect_provider.github.arn }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          # Immutable owner/repository IDs from the failed run's CloudTrail identity.
          "token.actions.githubusercontent.com:sub" = "repo:jaredkurama@137181595/novapay-platform@1423430941:ref:refs/heads/master"
        }
      }
    }]
  })
}

resource "aws_ssm_document" "deploy" {
  name            = "${local.name}-deploy"
  document_type   = "Command"
  document_format = "JSON"
  content = jsonencode({
    schemaVersion = "2.2"
    description   = "Deploy a verified wallet digest with readiness checks and rollback"
    parameters = {
      ImageDigest = {
        type              = "String"
        allowedPattern    = "^sha256:[a-f0-9]{64}$"
        interpolationType = "ENV_VAR"
      }
      AppVersion = {
        type              = "String"
        allowedPattern    = "^[a-f0-9]{40}$"
        interpolationType = "ENV_VAR"
      }
    }
    mainSteps = [{
      action       = "aws:runShellScript"
      name         = "DeployWallet"
      precondition = { StringEquals = ["platformType", "Linux"] }
      inputs = {
        timeoutSeconds = "600"
        runCommand = [templatefile("${path.module}/deploy-staging.sh.tftpl", {
          repository = aws_ecr_repository.wallet.repository_url
          secret_arn = aws_secretsmanager_secret.application_database.arn
          db_host    = aws_db_instance.wallet.address
        })]
      }
    }]
  })
}

resource "aws_iam_role_policy" "github_deployment" {
  name = "publish-and-deploy-wallet"
  role = aws_iam_role.github_deployment.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["ecr:GetAuthorizationToken"]
        Resource = "*" # This authentication API does not support repository ARNs.
      },
      {
        Effect = "Allow"
        Action = ["ecr:BatchCheckLayerAvailability", "ecr:InitiateLayerUpload", "ecr:UploadLayerPart",
        "ecr:CompleteLayerUpload", "ecr:PutImage", "ecr:DescribeImages"]
        Resource = aws_ecr_repository.wallet.arn
      },
      {
        Effect   = "Allow"
        Action   = ["ssm:SendCommand"]
        Resource = [aws_ssm_document.deploy.arn, aws_instance.application.arn]
      },
      {
        Effect   = "Allow"
        Action   = ["ssm:GetCommandInvocation"]
        Resource = "*" # Command-result lookup does not support resource-level permissions.
      }
    ]
  })
}

output "github_deployment_role_arn" {
  value = aws_iam_role.github_deployment.arn
}

output "deployment_document_name" {
  value = aws_ssm_document.deploy.name
}
