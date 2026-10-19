# AI usage and review

I used OpenAI's Codex coding assistant through a chat-based workflow to help  review this assessment. It helped draft the .NET service, Docker configuration, GitHub Actions workflow, Terraform, deployment scripts, tests, and operational documentation. It also used official documentation to investigate AWS, GitHub OIDC, Prometheus, and regulatory questions.

I directed the work in stages, asked for explanations, challenged design choices, and reviewed pipeline results. The assistant executed many implementation and verification steps.

## Example prompts for the implementation

These illustrative prompts summarize requests that could produce the resources in this project. They were written afterwards, not copied from the conversation. The implementation descriptions refer to resources we actually created.

| Illustrative prompt | Related implementation |
|---|---|
| “Help me create Terraform for a small AWS staging environment in eu-west-1. I want an EC2 instance running Docker, private RDS PostgreSQL, ECR, and Secrets Manager. Use SSM instead of SSH, keep database access restricted to the application, and give each role only the permissions it needs. Store Terraform state remotely with encryption and locking. Explain the resources and cost trade-offs.” | The staging Terraform defines the VPC, subnets, security groups, EC2, RDS, ECR, secret resource, and scoped roles. A separate bootstrap stack manages the private, versioned S3 state backend with native locking. |
| “Write a GitHub Actions pipeline for my .NET wallet API. Run formatting, tests, security analysis, dependency and secret scans, then build and scan the Docker image. Fail on critical vulnerabilities or detected secrets. Use standard Actions and visible commands. Once checks pass on master, use OIDC to push the same image to ECR and deploy it through SSM, with readiness checks and rollback.” | The workflow contains the quality/security gates, image build and runtime verification, temporary AWS authentication, ECR publishing, and SSM deployment. The deployment script uses the image digest, checks a candidate, and attempts rollback if the replacement fails. |
| “Help me add observability without exposing monitoring publicly. Send the API's JSON logs to CloudWatch with a retention period, avoid logging wallet IDs or credentials, and provide a local Prometheus setup. Add an alert for sustained wallet server errors and test that health-check traffic cannot hide those errors. Explain what is actually running and what still needs notification setup.” | Terraform and Docker configure CloudWatch application logs with 30-day retention. The optional local Compose service scrapes Prometheus metrics; native rule tests cover wallet error rates and failed scrapes. AWS Prometheus collection and notification delivery remain unimplemented. |

## Where I challenged the assistant

### Pipeline complexity

The first approach put too much scanner behavior behind custom scripts. I wanted a reviewer to understand the pipeline directly from its YAML. I challenged this, supplied an example of a visible Trivy setup/scan/report pattern, and asked whether the other tools could follow the same approach.

The assistant simplified the workflow. This was a maintainability and reviewability correction: extra abstraction was making a small assessment harder to explain.

## Errors found during implementation and verification

### GitHub OIDC trust mismatch

The assistant generated a trust policy using GitHub's older repository-name-only subject format. The first deployment pipeline failed with `Not authorized to perform sts:AssumeRoleWithWebIdentity`. I supplied the failure output and asked it to investigate.

CloudTrail showed that GitHub's actual subject included immutable owner and repository IDs. The assistant changed the exact subject match in `infra/staging/deployment.tf`, applied the correction, and I subsequently reported a passing pipeline.

The correction kept the audience and master-branch restrictions. We did not fix authentication by trusting every repository or adding administrator permissions. Commit `0da615a` records the change.

This was an AI correctness error exposed by a real pipeline run. It was **not** an overly permissive policy: the incorrect policy denied the intended caller.

### Sensitive identifiers in JSON log scopes

During earlier container verification, the assistant's automated check found that enabling JSON console scopes exposed the raw wallet path, including `demo-wallet`, through ASP.NET request scopes. Using a route template in the explicit application log message had not prevented the scope from including the raw identifier.

The assistant disabled console scopes, retained route-template/status/trace-ID fields, and checked the emitted logs again. The runtime verification script checks for wallet-identifier leakage. This was an actual insecure AI-generated application configuration caught by testing; it was discovered by the assistant's test, not by my independent manual inspection. It is not a Dockerfile or IAM defect.

### Claims about what was already working

I repeatedly asked the assistant to explain some decisons and next steps rather than treating generated files as finished infrastructure. The review now distinguishes:

- A rule that passes Prometheus tests from an alert that actually reaches an operator.
- PostgreSQL log export from a complete audit of SQL reads.
- A bootstrap tool that verifies existing credentials from a tool that rotates them.
- A private deployed API from an API available on a public IP.
- Implemented controls from PCI DSS certification or regulatory approval.

Some of these limits were identified by the assistant during review. I include them as review outcomes, not as claims that I personally discovered each one.

## Validation and limits

The work was checked through .NET tests/security analysis, pipeline scans, actual container restrictions, PostgreSQL TLS and write-denial checks, deployment/rollback tests, live SSM endpoint checks, Prometheus rule tests, and observed CloudWatch JSON events. The assistant ran many of these checks. I also reviewed GitHub results and brought failures back into the conversation.

Mocked rollback tests are not a disaster-recovery drill. Documentation is not evidence that credential rotation, database-read auditing, delivered alerts, or restore procedures are implemented. The submission states these gaps in the runbook and threat model.

For the brief's insecure-IaC/IAM/Dockerfile review requirement, the OIDC example is a restrictive IAM correctness bug and the logging example is an insecure application configuration. Neither is presented as an overly permissive IAM policy or insecure Dockerfile.

## References in this repository

- [CI workflow](.github/workflows/ci.yml)
- [State backend controls](infra/bootstrap/main.tf)
- [OIDC trust and deployment permissions](infra/staging/deployment.tf)
- [Application logging configuration](src/NovaPay.Api/Program.cs)
- [Container verification](scripts/verify_container.py)
- [Threat model](THREAT_MODEL.md) and [incident runbook](RUNBOOK.md)

This account was drafted with AI assistance from the conversation and repository evidence.
