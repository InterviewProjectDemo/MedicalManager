# Production rollback — voice lines, 21 November 2026

Taken before the dialysis voice-line deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes the editable picture lines, the dialysis-only Help / Family / Pain / Breathing / Blood logos, and the patient's own recorded voice on those calls. The earlier accessibility and chart workflow stay in this image.

| | |
| --- | --- |
| Date | 2026-11-21 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/7af21d0ee8d94c5d934357da122f08bb` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261121-pre-voice-lines` |
| Image digest | `sha256:d20a6535ed19609622ac7de23f01ae9c399b952c28430b029e85731ea30afedf` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `c78b6c6` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261121-pre-voice-lines"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:d20a6535ed19609622ac7de23f01ae9c399b952c28430b029e85731ea30afedf`.

The feature commit message is "Let each dialysis picture speak a saved line in the patient's own voice." To return the git branch to the code that was in production before this deploy, check out `adca653`. Leave the backup image in ECR either way. The older image `backup-20261121-pre-accessibility` is unchanged and still restores the build from before accessibility.
