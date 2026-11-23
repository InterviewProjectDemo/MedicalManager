# Production rollback — custom controls, 22 November 2026

Taken before the Custom Controls deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes the Custom Controls page.

| | |
| --- | --- |
| Date | 2026-11-22 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/d0df45914b6b4073a4b509360ffb6987` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261122-pre-custom-controls` |
| Image digest | `sha256:8d8aacdd412bb1f863f6449760cbc08c9aa815daab82192ce28b04fdd68b4da1` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `cf114b9` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261122-pre-custom-controls"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:8d8aacdd412bb1f863f6449760cbc08c9aa815daab82192ce28b04fdd68b4da1`.

The feature commit message is "Add a Custom Controls page so a few logos can be chosen, arranged, and kept." To return the git branch to the code that was in production before this deploy, check out `0f189c3`. Leave the backup image in ECR either way. The older images `backup-20261121-pre-phone-speech`, `backup-20261121-pre-voice-lines`, and `backup-20261121-pre-accessibility` are unchanged.
