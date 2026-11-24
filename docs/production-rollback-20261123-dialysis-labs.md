# Production rollback — dialysis labs, 23 November 2026

Taken before the dialysis lab section deploy. The image below was the healthy task on ECS before `:latest` moved. Restoring it removes the Dialysis lab group, the dialysis result columns, and the dialysis entry fields.

| | |
| --- | --- |
| Date | 2026-11-23 |
| Account / region | 041755786175 / us-east-1 |
| Cluster | medicalmanager |
| Service | medicalmanager-app |
| Task definition at backup | `medicalmanager-app:12` |
| Running task at backup | `arn:aws:ecs:us-east-1:041755786175:task/medicalmanager/e94fc0bd3f3845ceb903ad5ae1662cd9` |
| ECR repository | medicalmanager |
| Backup tag | `backup-20261123-pre-dialysis-labs` |
| Image digest | `sha256:304a1b168f50dc55aa406eda9ac10404a7b1c0b80245ff189feafac4b79fda46` |
| Git branch | `registration-update-video-instruction-added` |
| Feature commit | `a4d72b8` |

The task definition names `medicalmanager:latest`. After this deploy, `:latest` is a different image, so pointing the service at task definition revision 12 does not restore this build. Restore the backup tag instead. That registers a new task definition whose container image is the backup tag, then starts a new deployment. Other stack parameters stay at their previous values. Do not deregister task definitions or delete the backup image.

From the repo root, with the AWS CLI:

```powershell
$aws = "aws"
$image = "041755786175.dkr.ecr.us-east-1.amazonaws.com/medicalmanager:backup-20261123-pre-dialysis-labs"

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

Confirm the new running task is HEALTHY and its image digest is `sha256:304a1b168f50dc55aa406eda9ac10404a7b1c0b80245ff189feafac4b79fda46`.

The feature commit message is "Add a Dialysis section so hemodialysis labs can be recorded with their own goals." To return the git branch to the code that was in production before this deploy, check out `539a2b3`. Leave the backup image in ECR either way. The older image `backup-20261122-pre-calendar` is unchanged.
