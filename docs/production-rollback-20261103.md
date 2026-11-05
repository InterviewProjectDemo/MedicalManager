# Production rollback — application voice reminder

The live production image below is the SMS opt-in build. It does not contain application voice reminders. Restore this tag to remove the voice feature and keep the rest of production.

| | |
| --- | --- |
| Date | 2026-11-04 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261104-pre-application-voice` |
| Image digest | `sha256:68c014d9bd5a69b6cea8e4dec8185a236c1175bb8ce03797809134688f687588` |

The task definition names `medicalmanager:latest`. After the voice deploy, `:latest` is a different image, so pointing the service at an older task definition revision does not restore this build. Restore the backup tag instead.

```powershell
$aws = "C:\Users\navee\AppData\Local\Programs\Amazon\AWSCLIV2\aws.exe"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261104-pre-application-voice"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:68c014d9bd5a69b6cea8e4dec8185a236c1175bb8ce03797809134688f687588`.

An older image, `backup-20261103-pre-application-reminder` (`sha256:959a6bca2da40f2c821638455b98a871294e305578d770ec2f96e7087008c056`), is production from 1 November 2026. Restoring that tag also removes the SMS opt-in and later profile work.

# Earlier rollback — 3 November 2026


Taken before the application-reminder deploy. The image below was the healthy task on ECS before `:latest` moved.

| | |
| --- | --- |
| Date | 2026-11-03 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition | `medicalmanager-app:12` (`arn:aws:ecs:us-east-1:041755786175:task-definition/medicalmanager-app:12`) |
| Running task | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/f8793541246643ed89dc9bc6eca6a4e0` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261103-pre-application-reminder` |
| Image digest | `sha256:959a6bca2da40f2c821638455b98a871294e305578d770ec2f96e7087008c056` |

Revision 12 names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service back at revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "C:\Users\navee\AppData\Local\Programs\Amazon\AWSCLIV2\aws.exe"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261103-pre-application-reminder"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:959a6bca2da40f2c821638455b98a871294e305578d770ec2f96e7087008c056`.

## Voice-fix rollback

Taken before the Polly reminder-voice deploy. This image is the application-reminder build that was healthy on ECS but could not speak, because the consumer neural socket never connected. Use it only to undo the Polly change. The earlier tag above is the build from before application reminders existed.

| | |
| --- | --- |
| Backup tag | `backup-20261103-pre-polly-voice` |
| Image digest | `sha256:72e9858d17ef91a42bc14ad8818e816909c6063da8a2ba65a6cc31eef2c668b0` |

```powershell
$aws = "C:\Users\navee\AppData\Local\Programs\Amazon\AWSCLIV2\aws.exe"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261103-pre-polly-voice"

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
