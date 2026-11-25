# Production rollback — medical transcript, 25 November 2026

Taken before the medical transcript deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes the visit transcript and keeps the lab password window fix.

| | |
| --- | --- |
| Date | 2026-11-25 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/db97d340920d4971a98882c49a61e7c0` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261125-pre-medical-transcript` |
| Image digest | `sha256:063e2e3b8a0c8c1d341c4bcbf5758c1b6160642d52f57bd119dc65466495a6da` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `6855448` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261125-pre-medical-transcript"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:063e2e3b8a0c8c1d341c4bcbf5758c1b6160642d52f57bd119dc65466495a6da`.

The feature commit message is "Add a medical transcript the doctor can start during a video visit and share with the patient." To return the git branch to the code that was in production before this deploy, check out `111e1eb`. Leave the backup image in ECR either way. The older image `backup-20261124-pre-lab-password` is unchanged.
