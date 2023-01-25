from aws_cdk import (
    # Duration,
    Stack,
    aws_rds as rds,
    aws_ec2 as ec2,
    aws_iam as iam,
    aws_cloudwatch as cloudwatch,
    aws_secretsmanager as secretsmanager,
    aws_elasticache as elasticache,
    aws_docdb as docdb,
    RemovalPolicy,
    Duration,
    CfnOutput)

from constructs import Construct


class DocumentDbStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)
        
        # my_user_secret = docdb.DatabaseSecret(self, "MyUserSecret",
        #     username="myuser",
        #     master_secret=cluster.secret
        # )
        # my_user_secret_attached = my_user_secret.attach(cluster) # Adds DB connections information in the secret

        # cluster.add_rotation_multi_user("MyUser",  # Add rotation using the multi user scheme
        #     secret=my_user_secret_attached)
        
        cluster = docdb.DatabaseCluster(self, "Database",
            master_user=docdb.Login(
                username="myuser",  # NOTE: 'admin' is reserved by DocumentDB
                exclude_characters=""@/:",  # optional, defaults to the set ""@/" and is also used for eventually created rotations
                secret_name="/myapp/mydocdb/masteruser"
            ),
            instance_type=ec2.InstanceType.of(ec2.InstanceClass.MEMORY5, ec2.InstanceSize.LARGE),
            vpc_subnets=ec2.SubnetSelection(
                subnet_type=ec2.SubnetType.PUBLIC
            ),
            vpc=vpc
            instances=3
            deletion_protection=False
            export_profiler_logs_to_cloud_watch=True,  # Enable sending profiler logs
            export_audit_logs_to_cloud_watch=True,  # Enable sending audit logs
            cloud_watch_logs_retention=logs.RetentionDays.THREE_MONTHS,  # Optional - default is to never expire logs
            cloud_watch_logs_retention_role=my_logs_publishing_role
        )
            
        cluster.connections.allow_default_port_from_any_ipv4("Open to the world")
        
        
        # cluster: docdb.DatabaseCluster

        write_address = cluster.cluster_endpoint.socket_address
        
        security_group = ec2.SecurityGroup(self, "SecurityGroup",
            vpc=vpc
        )
        cluster.add_security_groups(security_group)