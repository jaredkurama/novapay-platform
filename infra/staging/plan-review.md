# Historical staging foundation plan

This records the initial plan, not the current deployment state. The foundation was subsequently applied successfully; see [deployment evidence](deployment-review.md). Do not reuse the old saved plan.

Authenticated through the `novapay` Identity Center profile I created for this project (assumed administrator role, not root). Region: `eu-west-1`.

Terraform originally produced `staging.tfplan`: **25 create, 0 update, 0 delete**. Saved plans are ignored by Git. Always generate and review a fresh plan for new changes.

The resources are: one VPC, one internet gateway, three subnets, two route tables, three route associations, two security groups, three security-group rules, one ECR repository and lifecycle policy, one RDS instance/subnet group/parameter group, one application secret, one EC2 instance, and its IAM role/policy/instance profile.

The RDS orderable-options API confirms PostgreSQL 17 versions with `db.t4g.micro` and gp3 in this region. This is an availability check, not a guarantee of capacity or successful apply. Terraform plans do not validate every service-side constraint or detect unmanaged name collisions.

## Boundaries at that review

- No inbound EC2 ports; application access was designed to use Systems Manager port forwarding and now does.
- Private RDS, TLS required, and port 5432 allowed only from the application security group.
- Runtime IAM can read the application secret but not the administrator secret.
- At this point the application secret had no value and bootstrap/deployment were pending. Initialization and deployment have since completed; rollback has mocked test coverage.
- Remote S3 state and native locking were subsequently configured before the infrastructure apply.
- RDS deletion protection and a final snapshot are enabled; teardown is deliberately not a single unattended destroy.
