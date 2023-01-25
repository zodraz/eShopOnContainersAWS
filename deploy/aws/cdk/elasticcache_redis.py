from aws_cdk import (
    # Duration,
    Stack,
    aws_rds as rds,
    aws_ec2 as ec2,
    aws_iam as iam,
    aws_elasticache as elasticache,
    RemovalPolicy,
    CfnOutput)

from constructs import Construct


class ElasticacheDemoCdkAppStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        # VPC
        vpc = ec2.Vpc(self,
                      "VPC",
                      nat_gateways=1,
                      cidr="10.0.0.0/16",
                      subnet_configuration=[
                          ec2.SubnetConfiguration(
                              name="public",
                              subnet_type=ec2.SubnetType.PUBLIC,
                              cidr_mask=24),
                          ec2.SubnetConfiguration(
                              name="private",
                              subnet_type=ec2.SubnetType.PRIVATE_WITH_NAT,
                              cidr_mask=24)
                      ])

        # Security Groups
        db_sec_group = ec2.SecurityGroup(
            self,
            "db-sec-group",
            security_group_name="db-sec-group",
            vpc=vpc,
            allow_all_outbound=True,
        )
        webserver_sec_group = ec2.SecurityGroup(
            self,
            "webserver_sec_group",
            security_group_name="webserver_sec_group",
            vpc=vpc,
            allow_all_outbound=True,
        )
        redis_sec_group = ec2.SecurityGroup(
            self,
            "redis-sec-group",
            security_group_name="redis-sec-group",
            vpc=vpc,
            allow_all_outbound=True,
        )

        private_subnets_ids = [ps.subnet_id for ps in vpc.private_subnets]

        redis_subnet_group = elasticache.CfnSubnetGroup(
            scope=self,
            id="redis_subnet_group",
            subnet_ids=private_subnets_ids,  # todo: add list of subnet ids here
            description="subnet group for redis")

        # Add ingress rules to security group
        webserver_sec_group.add_ingress_rule(
            peer=ec2.Peer.ipv4("0.0.0.0/0"),
            description="Flask Application",
            connection=ec2.Port.tcp(app_port),
        )

        db_sec_group.add_ingress_rule(
            peer=webserver_sec_group,
            description="Allow MySQL connection",
            connection=ec2.Port.tcp(3306),
        )

        redis_sec_group.add_ingress_rule(
            peer=webserver_sec_group,
            description="Allow Redis connection",
            connection=ec2.Port.tcp(6379),
        )