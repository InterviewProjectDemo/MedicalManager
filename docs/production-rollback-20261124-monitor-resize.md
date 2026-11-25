# Production rollback — monitor resize, 24 November 2026

Taken before the monitor resize and hide deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes the resizable monitor window, the Hide control, and the Monitor entry in the side menu.

| | |
| --- | --- |
| Date | 2026-11-24 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/3eaf727a3c5f414fa34a610f5788ff32` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261124-pre-monitor-resize` |
| Image digest | `sha256:f11f52eb371ea75295da8efd8b3898acd66488e4a813f74b78ca5e4a549c55f4` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `704c1fa` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261124-pre-monitor-resize"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:f11f52eb371ea75295da8efd8b3898acd66488e4a813f74b78ca5e4a549c55f4`.

The feature commit message is "Let the family monitor window be resized and hidden while the camera stays on." To return the git branch to the code that was in production before this deploy, check out `53d391a`. Leave the backup image in ECR either way. The older image `backup-20261123-pre-dialysis-labs` is unchanged.
