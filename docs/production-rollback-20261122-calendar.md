# Production rollback — calendar, 22 November 2026

Taken before the shared calendar deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes the calendar, seat holds, and the cancel flow.

| | |
| --- | --- |
| Date | 2026-11-22 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/7ec3a6586cb24eda8f33735ac8727ee5` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261122-pre-calendar` |
| Image digest | `sha256:54d04f6be933bdad6ca7601e476bb9688519d2b3d4971b97cc9f26e0804429e4` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `68c7917` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261122-pre-calendar"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:54d04f6be933bdad6ca7601e476bb9688519d2b3d4971b97cc9f26e0804429e4`.

The feature commit message is "Add a shared calendar so patients and doctors can hold, confirm, and cancel visit seats." To return the git branch to the code that was in production before this deploy, check out `a2a5908`. Leave the backup image in ECR either way.
