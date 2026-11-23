# Production rollback — custom controls close, 22 November 2026

Taken before the Custom Controls close-button deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes the per-logo close button and the refill that follows a removal.

| | |
| --- | --- |
| Date | 2026-11-22 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/6f9aebf645934b71b025e5edcad73002` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261122-pre-controls-close` |
| Image digest | `sha256:d1f503dcb051f5c85beca9c46ebfc6b8b1bd006cf65d372f2331d7cd62fb8e21` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `9bad530` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261122-pre-controls-close"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:d1f503dcb051f5c85beca9c46ebfc6b8b1bd006cf65d372f2331d7cd62fb8e21`.

The feature commit message is "Let each Custom Controls logo be removed, then resize the rest to fill the window." To return the git branch to the code that was in production before this deploy, check out `3c2243c`. Leave the backup image in ECR either way. The older image `backup-20261122-pre-custom-controls` is unchanged.
