# nomad-node-term-handler
Nomad node termination handler for AWS

This docker image monitors an sqs queue for asg node termination events and calls `nomad node drain -enable` and once done completes the asg lifecycle hook

## Permissions Required

```terraform
  statement {
    sid    = "NodeTerm"
    effect = "Allow"

    actions = [
      "autoscaling:CompleteLifecycleAction",
      "ec2:DescribeInstances",
      "sqs:ReceiveMessage",
      "sqs:DeleteMessage",
      "sqs:GetQueueUrl",
    ]

    resources = ["*"]
  }
```
