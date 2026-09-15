# Deploys MedicalManager to AWS: ECR image, encrypted RDS PostgreSQL, ECS Fargate, ALB.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Get-Aws {
    $fallback = Join-Path $env:ProgramFiles "Amazon\AWSCLIV2\aws.exe"
    if (Test-Path $fallback) { return $fallback }
    $cmd = Get-Command aws -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "AWS CLI is not installed. Install it, run 'aws configure', then rerun this script."
}

$aws = Get-Aws
$region = $env:AWS_REGION
if ([string]::IsNullOrWhiteSpace($region)) { $region = "us-east-1" }

Write-Host "Checking AWS identity in $region..."
$identity = & $aws sts get-caller-identity --region $region --output json | ConvertFrom-Json
$account = $identity.Account
Write-Host "Account $account  ($($identity.Arn))"

$envPath = Join-Path $root ".env"
if (-not (Test-Path $envPath)) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root "scripts\New-DockerEnv.ps1")
}
$secrets = @{}
Get-Content $envPath | ForEach-Object {
    if ($_ -match "^\s*#" -or $_ -notmatch "=") { return }
    $parts = $_.Split("=", 2)
    $secrets[$parts[0].Trim()] = $parts[1].Trim()
}
foreach ($required in @("POSTGRES_USER", "POSTGRES_PASSWORD", "POSTGRES_DB", "MEDICALMANAGER_PHI_KEY")) {
    if (-not $secrets.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($secrets[$required])) {
        throw ".env is missing $required"
    }
}

$repo = "medicalmanager"
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& $aws ecr describe-repositories --repository-names $repo --region $region 2>$null | Out-Null
$repoMissing = $LASTEXITCODE -ne 0
$ErrorActionPreference = $prevEap
if ($repoMissing) {
    Write-Host "Creating ECR repository $repo..."
    & $aws ecr create-repository --repository-name $repo --region $region --image-scanning-configuration scanOnPush=true | Out-Null
}

$registry = "$account.dkr.ecr.$region.amazonaws.com"
$imageUri = "${registry}/${repo}:latest"
Write-Host "Logging in to ECR..."
& $aws ecr get-login-password --region $region | docker login --username AWS --password-stdin $registry
if ($LASTEXITCODE -ne 0) { throw "ECR login failed. Is Docker Desktop running?" }

Write-Host "Building and pushing $imageUri ..."
docker build -t $repo`:latest -t $imageUri $root
if ($LASTEXITCODE -ne 0) { throw "docker build failed" }
docker push $imageUri
if ($LASTEXITCODE -ne 0) { throw "docker push failed" }

$vpc = & $aws ec2 describe-vpcs --region $region --filters Name=isDefault,Values=true --query "Vpcs[0].VpcId" --output text
if ([string]::IsNullOrWhiteSpace($vpc) -or $vpc -eq "None") {
    throw "No default VPC in $region. Create one or pass a VPC in the CloudFormation console."
}
$subnets = & $aws ec2 describe-subnets --region $region --filters Name=vpc-id,Values=$vpc --query "Subnets[?MapPublicIpOnLaunch==``true``].SubnetId" --output text
$subnetList = @($subnets -split "\s+" | Where-Object { $_ })
if ($subnetList.Count -lt 2) {
    throw "Need at least two public subnets in $vpc for the load balancer."
}

$stack = "medicalmanager"
$template = Join-Path $root "aws\cloudformation.yml"
$params = @(
    "VpcId=$vpc",
    "PublicSubnet1=$($subnetList[0])",
    "PublicSubnet2=$($subnetList[1])",
    "ImageUri=$imageUri",
    "DbUsername=$($secrets.POSTGRES_USER)",
    "DbPassword=$($secrets.POSTGRES_PASSWORD)",
    "PhiKey=$($secrets.MEDICALMANAGER_PHI_KEY)",
    "DbName=$($secrets.POSTGRES_DB)"
)

Write-Host "Deploying CloudFormation stack $stack (RDS can take 10+ minutes)..."
& $aws cloudformation deploy `
    --region $region `
    --stack-name $stack `
    --template-file $template `
    --capabilities CAPABILITY_NAMED_IAM `
    --parameter-overrides @params

if ($LASTEXITCODE -ne 0) { throw "CloudFormation deploy failed" }

Write-Host "Forcing ECS to pull the image that was just pushed..."
& $aws ecs update-service --region $region --cluster medicalmanager --service medicalmanager-app --force-new-deployment | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ECS force-new-deployment failed" }

$url = & $aws cloudformation describe-stacks --region $region --stack-name $stack --query "Stacks[0].Outputs[?OutputKey=='ApplicationUrl'].OutputValue" --output text
Write-Host ""
Write-Host "MedicalManager is deploying behind:"
Write-Host "  $url"
Write-Host "Give ECS a minute to pass the ALB health check, then open that URL."
Write-Host "The RDS instance is private. Only the app security group can reach port 5432 over TLS."
