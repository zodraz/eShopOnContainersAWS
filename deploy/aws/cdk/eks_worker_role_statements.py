from aws_cdk import (
    aws_iam as iam
)


class EksWorkerRoleStatements(object):

    @staticmethod
    def admin_statement():
        policy_statement = iam.PolicyStatement(
            effect=iam.Effect.ALLOW,
            actions=['*'],
            resources=['*']
        )
        return policy_statement

    @staticmethod
    def eks_cni():
        policy_statement = iam.PolicyStatement(
            effect=iam.Effect.ALLOW,
            actions=[
                "ec2:AssignPrivateIpAddresses",
                "ec2:AttachNetworkInterface",
                "ec2:CreateNetworkInterface",
                "ec2:DeleteNetworkInterface",
                "ec2:DescribeInstances",
                "ec2:DescribeTags",
                "ec2:DescribeNetworkInterfaces",
                "ec2:DescribeInstanceTypes",
                "ec2:DetachNetworkInterface",
                "ec2:ModifyNetworkInterfaceAttribute",
                "ec2:UnassignPrivateIpAddresses",
                "ec2:CreateTags"
            ],
            resources=['*']
        )
        return policy_statement
