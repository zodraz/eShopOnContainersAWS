from aws_cdk import (
    # Duration,
    Stack,
    aws_rds as rds,
    aws_ec2 as ec2,
    aws_iam as iam,
    aws_cloudwatch as cloudwatch,
    aws_secretsmanager as secretsmanager,
    aws_elasticache as elasticache,
    RemovalPolicy,
    Duration,
    CfnOutput)

from constructs import Construct


class RDSSQLServerStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc,
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
            peer=ec2.Peer.ipv4("XXX.XXX.XXX.XXX/32"),
            description="SQL Server",
            connection=ec2.Port.tcp(1433))

        rds.DatabaseInstance(
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
            multi_az=True,
            vpc_subnets=ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.ISOLATED),
            deletion_protection=False,
            delete_automated_backups=True,
            backup_retention=Duration.Days(0),
            removal_policy=RemovalPolicy.DESTROY,
            cloudwatch_logs_exports=["audit", "error", "general", "slowquery"],
        )

        # Add alarm for high CPU
        cloudwatch.Alarm(self,
                         id="HighCPU",
                         metric=db.metric_cpu_utilization(),
                         threshold=90,
                         evaluation_periods=1)

        CfnOutput(
            self,
            "EKSClusterName",
            value=eks_cluster.cluster_name,
            description="The name of the EKS Cluster",
            export_name="EKSClusterName",
        )

        instance_props = rds.InstanceProps(
            vpc=vpc,
            security_groups=security_groups,
            vpc_subnets=private_subnet_selections)
        rds_cluster = rds.DatabaseCluster(
            self,
            "RDS-cluster",
            cluster_identifier="ecs-sample-db-cluster",
            instance_props=instance_props,
            engine=rds.DatabaseClusterEngine.aurora_mysql(
                version=rds.AuroraMysqlEngineVersion.VER_2_07_1),
            credentials=credential,
            default_database_name="sample",
            instances=1,
            subnet_group=subnet_group,
            removal_policy=core.RemovalPolicy.DESTROY,
            deletion_protection=False)
        core.CfnOutput(self,
                       "RDS_cluster_endpoint",
                       value=rds_cluster.cluster_endpoint.hostname)
