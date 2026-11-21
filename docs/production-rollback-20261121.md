# Production rollback — 21 November 2026

Taken before the patient accessibility deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes accessibility, dialysis help calls, speech recognition, the affiliated-patient chart request, and the editable after-visit summary.

| | |
| --- | --- |
| Date | 2026-11-21 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/9185267c61064daa959efd5a81c0a206` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261121-pre-accessibility` |
| Image digest | `sha256:c330843f35eb96f94fac143a29da45ac3bf1d56d866ece82f8212598850ffd77` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `4041b9e84e2a6684635358b6fd23a0e5aa096a88` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261121-pre-accessibility"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:c330843f35eb96f94fac143a29da45ac3bf1d56d866ece82f8212598850ffd77`.

The feature commit message is "Add patient accessibility and the doctor chart workflow." To return the git branch to the code that was in production before this deploy, check out the parent of that commit, `7dc06c7`. Leave the backup image in ECR either way.
