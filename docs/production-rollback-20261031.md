# Production rollback — 31 October 2026

Taken before the calling-dialog deploy. The image below was the healthy task on ECS before `:latest` moved.

| | |
| --- | --- |
| Date | 2026-10-31 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition | `medicalmanager-app:12` (`arn:aws:ecs:us-east-1:041755786175:task-definition/medicalmanager-app:12`) |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261031-pre-call-close` |
| Image digest | `sha256:61183412d3c9abfcca6a55db69ee340aff66d8f9be65680fc2e48a5f847c9bee` |

Revision 12 names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service back at revision 12 does not restore the old build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "C:\Users\navee\AppData\Local\Programs\Amazon\AWSCLIV2\aws.exe"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261031-pre-call-close"

& $aws cloudformation deploy `
  --region us-east-1 `
  --stack-name medicalmanager `
  --template-file aws\cloudformation.yml `
  --capabilities CAPABILITY_NAMED_IAM `
  --parameter-overrides "ImageUri=$image"

& $aws ecs update-service `
  --region us-east-1 `
  --cluster medicalmanager `
  --service medicalmanager-app `
  --force-new-deployment
```

Confirm the new running task is HEALTHY and its image digest is `sha256:61183412d3c9abfcca6a55db69ee340aff66d8f9be65680fc2e48a5f847c9bee`.
