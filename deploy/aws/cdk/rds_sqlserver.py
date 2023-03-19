from aws_cdk import (
    Stack,
    aws_rds as rds,
    aws_ec2 as ec2,
    aws_cloudwatch as cloudwatch,
    aws_secretsmanager as secretsmanager,
    RemovalPolicy,
    Duration,
    CfnOutput)

from constructs import Construct


class RDSSQLServerStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc: ec2.Vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        db_password = secretsmanager.Secret(
            self,
            "sqlServerPwd",
            description="SQLServer auth",
            generate_secret_string=secretsmanager.SecretStringGenerator(
                exclude_characters='/"@'))

        db_user = "adminuser"

        db_security_group = ec2.SecurityGroup(self,
                                              "SQLServerSecurityGroup",
                                              vpc=vpc,
                                              allow_all_outbound=True)

        db_security_group.add_ingress_rule(
            peer=ec2.Peer.ipv4(self.node.try_get_context(
                "vpc_cidr_mask_sqlserver")),
            description="SQL Server",
            connection=ec2.Port.tcp(1433))

        # DB Subnet Group
        if self.node.try_get_context("vpc_only_public") == "False":
            vpc_subnet = ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.PRIVATE_WITH_EGRESS)
        else:
            vpc_subnet = ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.PUBLIC)

        db_subnet_group = rds.SubnetGroup(self, "DBSubnet",
                                          vpc=vpc,
                                          description="Subnet Group for RDS SQLServer",
                                          removal_policy=RemovalPolicy.DESTROY,
                                          subnet_group_name="db-subnet-sqlserver",
                                          vpc_subnets=vpc_subnet
                                          )

        if self.node.try_get_context("deploy_rds_sqlserver_as_cluster") == "False":

            rds_db = rds.DatabaseInstance(
                self,
                "RDSQLServer",
                engine=rds.DatabaseInstanceEngine.sql_server_ex(
                    version=rds.SqlServerEngineVersion.VER_15),
                vpc=vpc,
                credentials=rds.Credentials.from_password(
                    db_user, db_password.secret_value),
                instance_type=ec2.InstanceType.of(ec2.InstanceClass.BURSTABLE3,
                                                  ec2.InstanceSize.SMALL),
                security_groups=[db_security_group],
                subnet_group=db_subnet_group,
                multi_az=False,
                vpc_subnets=vpc_subnet,
                deletion_protection=False,
                delete_automated_backups=True,
                backup_retention=Duration.days(0),
                removal_policy=RemovalPolicy.DESTROY,
                cloudwatch_logs_exports=["error"],

            )

            # Add alarm for high CPU
            cloudwatch.Alarm(self,
                             id="HighCPU",
                             metric=rds_db.metric_cpu_utilization(),
                             threshold=90,
                             evaluation_periods=1)

            CfnOutput(
                self,
                id="RDSQLServer Instance ARN",
                value=rds_db.instance_arn,
                description="The ARN of RDS SQL Server",
                export_name="RDSSQLServerARN"
            )

        else:

            instance_props = rds.InstanceProps(
                vpc=vpc,
                security_groups=[db_security_group],
                vpc_subnets=vpc_subnet)

            rds_cluster = rds.DatabaseCluster(
                self,
                "RDSCluster",
                cluster_identifier="eshop-db-sqlserver-cluster",
                instance_props=instance_props,
                engine=rds.DatabaseInstanceEngine.sql_server_ee(
                    version=rds.SqlServerEngineVersion.VER_15),
                credentials=rds.Credentials.from_password(
                    db_user, db_password.secret_value),
                instances=self.node.try_get_context(
                    "rds_sqlserver_node_quantity"),
                subnet_group=db_subnet_group,
                removal_policy=RemovalPolicy.DESTROY,
                deletion_protection=False)

            CfnOutput(self,
                      "RDS Cluster Endpoint",
                      value=rds_cluster.cluster_endpoint.hostname)
