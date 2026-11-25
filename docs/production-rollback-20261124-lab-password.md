# Production rollback — lab password window, 24 November 2026

Taken before the lab password dialog deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it brings back the password window that stays on screen after Continue and ignores Close.

| | |
| --- | --- |
| Date | 2026-11-24 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/5582593ead534614b3d72d6fe93f3835` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261124-pre-lab-password` |
| Image digest | `sha256:2289a97ce18cbf4cdd7281d34b3f2c0d922bcf463563a720441013fda3e473c9` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `0ce8339` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261124-pre-lab-password"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:2289a97ce18cbf4cdd7281d34b3f2c0d922bcf463563a720441013fda3e473c9`.

The feature commit message is "Close the lab password window after a valid password and when Close is clicked." To return the git branch to the code that was in production before this deploy, check out `8c4f729`. Leave the backup image in ECR either way. The older image `backup-20261124-pre-monitor-resize` is unchanged.
