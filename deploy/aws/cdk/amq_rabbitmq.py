from aws_cdk import (
    Stack,
    aws_ec2 as ec2,
    aws_amazonmq as amazonmq,
    aws_ssm as ssm,
    CfnOutput)

from constructs import Construct


class AmazonMQRabbitMQStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc: ec2.Vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        if (self.node.try_get_context("deploy_rabbitmq") == "True"):

            mq_group = ec2.SecurityGroup(self, 'mq_group', vpc=vpc)
            bastion_to_mq_group = ec2.SecurityGroup(
                self, 'bastion_to_mq_group', vpc=vpc)

            mq_group.add_ingress_rule(peer=ec2.Peer.ipv4(self.node.try_get_context(
                "vpc_cidr_mask_rabbitmq")),
                connection=ec2.Port.tcp(5671),
                description='allow AMQP 0-9-1 and 1.0 clients with TLS communication within VPC')
            mq_group.add_ingress_rule(peer=ec2.Peer.ipv4(self.node.try_get_context(
                "vpc_cidr_mask_rabbitmq")),
                connection=ec2.Port.tcp(5672),
                description='allow AMQP 0-9-1 and 1.0 clients with TLS communication without TLS within VPC')
            mq_group.add_ingress_rule(peer=ec2.Peer.ipv4(self.node.try_get_context(
                "vpc_cidr_mask_rabbitmq")),
                connection=ec2.Port.tcp(443),
                description='allow communication on RabbitMQ console port within VPC')
            mq_group.add_ingress_rule(peer=mq_group,
                                      connection=ec2.Port.all_tcp(),
                                      description='allow communication from nlb and other brokers')

            if self.node.try_get_context("vpc_only_public") == "True":
                mq_group.add_ingress_rule(peer=ec2.Peer.any_ipv4(),
                                          connection=ec2.Port.tcp(443),
                                          description='allow communication on RabbitMQ console port on https from public internet')

            # allow SSH to bastion from anywhere (for debugging)
            # bastion_to_mq_group.add_ingress_rule(connection=ec2.Port.all_tcp())

            CfnOutput(self, 'bastionToMQGroupSGID',
                      value=bastion_to_mq_group.security_group_id)

            mq_username = 'admin'
            mq_password = 'password1234'

            ssm.StringParameter(self, 'string_parameter',
                                parameter_name='MQBrokerUserPassword',
                                string_value='{username},{password}'.format(username=mq_username, password=mq_password))

            mq_master = amazonmq.CfnBroker.UserProperty(console_access=True,
                                                        username=mq_username,
                                                        password=mq_password)

            if (self.node.try_get_context("deploy_rabbitmq_as_cluster") == "True"):

                if self.node.try_get_context("vpc_only_public") == "False":
                    vpc_subnets = [vpc.private_subnets[1].subnet_id,
                                   vpc.private_subnets[2].subnet_id]
                else:
                    vpc_subnets = [vpc.public_subnets[1].subnet_id,
                                   vpc.public_subnets[2].subnet_id]

                    mq_instance = amazonmq.CfnBroker(self, 'mq_instance',
                                                     auto_minor_version_upgrade=False,
                                                     broker_name='eshopRabbitMQ',
                                                     deployment_mode='CLUSTER_MULTI_AZ',
                                                     engine_type='RABBITMQ',
                                                     engine_version='3.10.10',
                                                     host_instance_type='mq.m5.large',
                                                     publicly_accessible=False,
                                                     users=[mq_master],
                                                     subnet_ids=vpc_subnets,
                                                     security_groups=[
                                                         mq_group.security_group_id],
                                                     logs=amazonmq.CfnBroker.LogListProperty(
                                                         general=True
                                                     ))

            else:

                if self.node.try_get_context("vpc_only_public") == "False":
                    vpc_subnets = [vpc.private_subnets[1].subnet_id]
                else:
                    vpc_subnets = [vpc.public_subnets[1].subnet_id]

                    mq_instance = amazonmq.CfnBroker(self, 'mq_instance',
                                                     auto_minor_version_upgrade=False,
                                                     broker_name='eshopRabbitMQ',
                                                     deployment_mode='SINGLE_INSTANCE',
                                                     engine_type='RABBITMQ',
                                                     engine_version='3.10.10',
                                                     host_instance_type='mq.t3.micro',
                                                     publicly_accessible=False,
                                                     users=[mq_master],
                                                     subnet_ids=vpc_subnets,
                                                     security_groups=[
                                                         mq_group.security_group_id],
                                                     logs=amazonmq.CfnBroker.LogListProperty(
                                                         general=True
                                                     ))
