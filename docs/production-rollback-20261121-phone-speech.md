# Production rollback — phone speech, 21 November 2026

Taken before the dialysis phone-speech deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes phone playback of each picture’s sentence. The editable picture lines and the patient’s recorded voice stay in this image.

| | |
| --- | --- |
| Date | 2026-11-21 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/bb638d7c7d9946e186c7ff41403fd851` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261121-pre-phone-speech` |
| Image digest | `sha256:551d761c317be0479373a53785b3930163f966ca71d0126eafcc80f5b8aac900` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `21853b6` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261121-pre-phone-speech"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:551d761c317be0479373a53785b3930163f966ca71d0126eafcc80f5b8aac900`.

The feature commit message is "Speak each dialysis picture's sentence on phones as well as laptops." To return the git branch to the code that was in production before this deploy, check out `b0ca088`. Leave the backup image in ECR either way. The older images `backup-20261121-pre-voice-lines` and `backup-20261121-pre-accessibility` are unchanged.
