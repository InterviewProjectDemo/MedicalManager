# Production rollback — window controls, 26 November 2026

Taken before the floating-window and Accessibility deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes full screen and reduce on the floating windows, the Accessibility help-call fixes, and the one-page calendar, and keeps the medical transcript.

| | |
| --- | --- |
| Date | 2026-11-26 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/8633ed1d2eb2410eb383f7b2341bbd55` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261126-pre-window-controls` |
| Image digest | `sha256:92c3ef401cd32bc78488bb3c000b0d14d744a3ef5ebdf854cb49aa15716e33cf` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `6eeb744` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261126-pre-window-controls"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:92c3ef401cd32bc78488bb3c000b0d14d744a3ef5ebdf854cb49aa15716e33cf`.

The feature commit message is "Keep floating windows usable while a dialysis help call is speaking, and fit the calendar on one page." To return the git branch to the code that was in production before this deploy, check out `ce846c1`. Leave the backup image in ECR either way. The older image `backup-20261125-pre-medical-transcript` is unchanged.
