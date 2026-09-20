# Deploy MedicalManager to AWS

This deploys the same two-tier design as Docker Compose:

| Piece | AWS service | Notes |
|-------|-------------|--------|
| Application | ECS Fargate + ECR + ALB | One task, port 8080 behind HTTP :80 |
| Database | RDS PostgreSQL 16 | Encrypted storage, not public, TLS required |

PHI still uses the app AES-256-GCM key from `.env` (`MEDICALMANAGER_PHI_KEY`). The connection string is stored in Secrets Manager and injected into the task.

Welcome and password-reset emails are sent from `medmgr.us@gmail.com` via Gmail SMTP when `EMAIL_SMTP_PASSWORD` is set in `.env` (a [Google App Password](https://support.google.com/accounts/answer/185833) for that account). The deploy script passes it into the `medicalmanager/runtime` secret as `emailSmtpPassword`, which ECS maps to `Email__Smtp__Password`. Do not commit the app password.

## What you need

1. An AWS account and an IAM user or role that can create ECR, ECS, RDS, VPC security groups, ALB, IAM roles, CloudWatch Logs, and Secrets Manager.
2. [AWS CLI v2](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html) and `aws configure` (access key, secret, region).
3. Docker Desktop running (Linux containers).
4. A local `.env` from `scripts\New-DockerEnv.ps1`.

Expected monthly cost in `us-east-1` for this small stack is roughly one `db.t3.micro`, one `0.5 vCPU` Fargate task, and one ALB. Stop the stack when you do not need it.

## Deploy

```powershell
cd C:\Users\navee\MedicalManager
aws configure
powershell -ExecutionPolicy Bypass -File .\scripts\Deploy-Aws.ps1
```

Optional: `$env:AWS_REGION = "us-east-1"` before the script.

The script creates the ECR repo, builds and pushes the image, then deploys `aws/cloudformation.yml`. RDS creation often takes 10–15 minutes. The last line prints the ALB URL (`http://...elb.amazonaws.com`).

Demo logins after seed:

- `patient@medicalmanager.local` / `Patient123!`
- `doctor@medicalmanager.local` / `Doctor123!`

## HTTPS

CloudFormation requests an ACM certificate for `www.medicalmgr.com` and `medicalmgr.com` (DNS validation) and adds an HTTPS :443 listener. HTTP :80 stays in place so both work.

After you start a stack update, add the ACM CNAME records from the AWS Console (Certificate Manager → the pending cert → Domains) into GoDaddy. The update stays in progress until those records validate.

```powershell
aws acm list-certificates --certificate-statuses PENDING_VALIDATION ISSUED
```

## Tear down

```powershell
aws ecs update-service --cluster medicalmanager --service medicalmanager-app --desired-count 0
aws cloudformation delete-stack --stack-name medicalmanager
```

RDS is set to snapshot on delete. Delete unused snapshots so they do not keep billing. Do not delete the stack until you have confirmed you do not need the data.
