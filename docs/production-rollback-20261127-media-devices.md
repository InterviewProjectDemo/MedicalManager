# Production rollback — audio and video devices, 27 November 2026

Taken before the Profile audio and video device deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes microphone, speaker, and camera selection, and keeps the floating-window controls and one-page calendar.

| | |
| --- | --- |
| Date | 2026-11-27 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/b2a209e186cc42bb9872f39d2dfaa264` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261127-pre-media-devices` |
| Image digest | `sha256:27ef3b496e68d9b3a2624e240ee1fd3323b7c7c3074db0a536f0448569b57a38` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `417cd1d` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261127-pre-media-devices"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:27ef3b496e68d9b3a2624e240ee1fd3323b7c7c3074db0a536f0448569b57a38`.

The feature commit message is "Let Profile choose the microphone, speaker, and camera this browser uses." To return the git branch to the code that was in production before this deploy, check out `96163c2`. Leave the backup image in ECR either way. The older image `backup-20261126-pre-window-controls` is unchanged.
