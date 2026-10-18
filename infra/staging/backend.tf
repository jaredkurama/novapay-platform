terraform {
  backend "s3" {
    bucket              = "novapay-tfstate-008971632699-eu-west-1"
    key                 = "staging/terraform.tfstate"
    region              = "eu-west-1"
    encrypt             = true
    use_lockfile        = true
    allowed_account_ids = ["008971632699"]
  }
}
