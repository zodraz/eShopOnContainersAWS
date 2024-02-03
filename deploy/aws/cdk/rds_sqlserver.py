from aws_cdk import (
    SecretValue,
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

        # db_password = secretsmanager.Secret(
        #     self,
        #     "sqlServerPwd",
        #     description="SQLServer auth",
        #     generate_secret_string=secretsmanager.SecretStringGenerator(
        #         exclude_characters='/\\`"@;:|=][],'))

        db_user = "adminuser"

        db_password = secretsmanager.Secret(self, "sqlserverCredentials",
                                            secret_string_value=SecretValue("PAssw0rd"))

        db_security_group = ec2.SecurityGroup(self,
                                              "SQLServerSecurityGroup",
                                              vpc=vpc,
                                              allow_all_outbound=True)

        # DB Subnet Group
        if self.node.try_get_context("vpc_only_public") == "False":
            vpc_subnet = ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.PRIVATE_WITH_EGRESS)

            list_subnets = self.node.try_get_context(
                "vpc_cidr_sqlserver_subnets").split(',')

            for subnet in list_subnets:
                db_security_group.add_ingress_rule(
                    peer=ec2.Peer.ipv4(subnet),
                    description="SQL Server",
                    connection=ec2.Port.tcp(1433))

            db_public_access = False
        else:
            vpc_subnet = ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.PUBLIC)

            db_security_group.add_ingress_rule(
                peer=ec2.Peer.any_ipv4(),
                description="SQL Server",
                connection=ec2.Port.tcp(1433))

            db_public_access = True

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
                    version=rds.SqlServerEngineVersion.VER_14_00_3465_1_V1),
                vpc=vpc,
                credentials=rds.Credentials.from_password(
                    db_user, db_password.secret_value),
                instance_type=ec2.InstanceType('t2.micro'),
                allocated_storage=20,  # Storage size in GB
                max_allocated_storage=100,  # Maximum storage size (optional)
                auto_minor_version_upgrade=False,
                security_groups=[db_security_group],
                subnet_group=db_subnet_group,
                multi_az=False,
                publicly_accessible=db_public_access,
                vpc_subnets=vpc_subnet,
                deletion_protection=False,
                delete_automated_backups=True,
                backup_retention=Duration.days(0),
                removal_policy=RemovalPolicy.DESTROY,
                cloudwatch_logs_exports=["error"]
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

            rds_db = rds.DatabaseInstance(
                self,
                "RDSQLServer",
                engine=rds.DatabaseInstanceEngine.sql_server_ee(
                    version=rds.SqlServerEngineVersion.VER_15),
                vpc=vpc,
                multi_az=True,
                publicly_accessible=db_public_access,
                credentials=rds.Credentials.from_password(
                    db_user, db_password.secret_value),
                instance_type=ec2.InstanceType.of(ec2.InstanceClass.M5,
                                                  ec2.InstanceSize.XLARGE),
                license_model=rds.LicenseModel.LICENSE_INCLUDED,
                security_groups=[db_security_group],
                subnet_group=db_subnet_group,
                vpc_subnets=vpc_subnet,
                deletion_protection=False,
                removal_policy=RemovalPolicy.DESTROY,
                cloudwatch_logs_exports=["error"]
            )

            # Add alarm for high CPU
            cloudwatch.Alarm(self,
                             id="HighCPU",
                             metric=rds_db.metric_cpu_utilization(),
                             threshold=90,
                             evaluation_periods=1)

            replica_db1 = rds.DatabaseInstanceReadReplica(
                self,
                "RDSReadReplica1",
                source_database_instance=rds_db,
                vpc=vpc,
                publicly_accessible=db_public_access,
                instance_type=ec2.InstanceType.of(ec2.InstanceClass.M5,
                                                  ec2.InstanceSize.XLARGE),
                security_groups=[db_security_group],
                subnet_group=db_subnet_group,
                vpc_subnets=vpc_subnet,
                deletion_protection=False,
                removal_policy=RemovalPolicy.DESTROY,
                cloudwatch_logs_exports=["error"]
            )

            replica_db2 = rds.DatabaseInstanceReadReplica(
                self,
                "RDSReadReplica2",
                source_database_instance=rds_db,
                vpc=vpc,
                publicly_accessible=db_public_access,
                instance_type=ec2.InstanceType.of(ec2.InstanceClass.M5,
                                                  ec2.InstanceSize.XLARGE),
                security_groups=[db_security_group],
                subnet_group=db_subnet_group,
                vpc_subnets=vpc_subnet,
                deletion_protection=False,
                removal_policy=RemovalPolicy.DESTROY,
                cloudwatch_logs_exports=["error"]
            )
