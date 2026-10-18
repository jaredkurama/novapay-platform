resource "aws_vpc" "staging" {
  cidr_block           = "10.42.0.0/16"
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = { Name = local.name }
}

resource "aws_internet_gateway" "staging" {
  vpc_id = aws_vpc.staging.id
}

resource "aws_subnet" "application" {
  vpc_id            = aws_vpc.staging.id
  cidr_block        = "10.42.0.0/24"
  availability_zone = data.aws_availability_zones.available.names[0]
  tags              = { Name = "${local.name}-application" }
}

resource "aws_route_table" "application" {
  vpc_id = aws_vpc.staging.id
  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.staging.id
  }
}

resource "aws_route_table_association" "application" {
  subnet_id      = aws_subnet.application.id
  route_table_id = aws_route_table.application.id
}

resource "aws_subnet" "database" {
  count             = 2
  vpc_id            = aws_vpc.staging.id
  cidr_block        = cidrsubnet("10.42.0.0/16", 8, count.index + 10)
  availability_zone = data.aws_availability_zones.available.names[count.index]
  tags              = { Name = "${local.name}-database-${count.index + 1}" }
}

# Database subnets have only the implicit VPC-local route, no internet route.
resource "aws_route_table" "database" {
  vpc_id = aws_vpc.staging.id
}

resource "aws_route_table_association" "database" {
  count          = 2
  subnet_id      = aws_subnet.database[count.index].id
  route_table_id = aws_route_table.database.id
}

resource "aws_security_group" "application" {
  name        = "${local.name}-application"
  description = "No inbound rules; access through Systems Manager"
  vpc_id      = aws_vpc.staging.id
}

resource "aws_security_group" "database" {
  name        = "${local.name}-database"
  description = "PostgreSQL from the application security group only"
  vpc_id      = aws_vpc.staging.id
}

resource "aws_vpc_security_group_egress_rule" "https" {
  security_group_id = aws_security_group.application.id
  description       = "AWS APIs, package repositories, and image layers over HTTPS"
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}

resource "aws_vpc_security_group_egress_rule" "postgres" {
  security_group_id            = aws_security_group.application.id
  referenced_security_group_id = aws_security_group.database.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}

resource "aws_vpc_security_group_ingress_rule" "postgres" {
  security_group_id            = aws_security_group.database.id
  referenced_security_group_id = aws_security_group.application.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}
