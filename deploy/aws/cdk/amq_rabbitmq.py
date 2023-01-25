from aws_cdk import (
    # Duration,
    Stack,
    aws_rds as rds,
    aws_ec2 as ec2,
    aws_iam as iam,
    aws_cloudwatch as cloudwatch,
    aws_secretsmanager as secretsmanager,
    aws_elasticache as elasticache,
    aws_amazonmq as amazonmq,
    RemovalPolicy,
    Duration,
    CfnOutput)

from constructs import Construct


class BastionStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)
        
        mq_group = ec2.SecurityGroup(self, 'mq_group', vpc=vpc)
        bastion_to_mq_group = ec2.SecurityGroup(self, 'bastion_to_mq_group', vpc=vpc)

        mq_group.add_ingress_rule(peer=ec2.Peer.ipv4(cidr),
                                  connection=ec2.Port.tcp(broker_port),
                                  description='allow OpenWire communication within VPC')
        mq_group.add_ingress_rule(peer=ec2.Peer.ipv4(cidr),
                                  connection=ec2.Port.tcp(mq_console_port),
                                  description='allow communication on ActiveMQ console port within VPC')
        mq_group.add_ingress_rule(peer=mq_group,
                                  connection=ec2.Port.all_tcp(),
                                  description='allow communication from nlb and other brokers')

        # allow SSH to bastion from anywhere (for debugging)
        # bastion_to_mq_group.add_ingress_rule(connection=ec2.Port.all_tcp())

        CfnOutput(self, 'bastionToMQGroupSGID', value=bastion_to_mq_group.security_group_id)

        
        mq_username = 'admin'
        mq_password = 'password1234'
        
        ssm.StringParameter(self, 'string_parameter',
                            parameter_name='MQBrokerUserPassword',
                            string_value='{username},{password}'.format(username=mq_username, password=mq_password))
        
        mq_master = amazonmq.CfnBroker.UserProperty(console_access=True,
                                              username=mq_username,
                                              password=mq_password
                                              console_access=True)

        mq_instance = amazonmq.CfnBroker(self, 'mq_instance',
                                   auto_minor_version_upgrade=False,
                                   broker_name='eshopRabbitMQ',
                                   deployment_mode='ACTIVE_STANDBY_MULTI_AZ',  #SINGLE_INSTANCE,CLUSTER_MULTI_AZ  
                                   engine_type='RABBITMQ',
                                   engine_version='3.10.10',
                                   host_instance_type='mq.t3.micro', 
                                #    host_instance_type='mq.m5.large',  #SINGLE_INSTANCE,CLUSTER_MULTI_AZ 
                                   publicly_accessible=False,
                                   users=[mq_master],
                                   subnet_ids=vpc.select_subnets(subnet_type=ec2.SubnetType.PRIVATE).subnet_ids,
                                   security_groups=[mq_group.security_group_id]
                                   encryption_options=amazonmq.CfnBroker.EncryptionOptionsProperty(
                                        use_aws_owned_key=True),
                                   logs=amazonmq.CfnBroker.LogListProperty(
                                        audit=False,
                                        general=True
                                   ),
                                   storage_type=ebs
                                   tags=[amazonmq.CfnBroker.TagsEntryProperty(
                                        key="key",
                                        value="value"
                                   )]
                                   
        
        nlb_target_group = elb.NetworkTargetGroup(self, 'nlb_target',
                                                  vpc=vpc,
                                                  port=broker_port,
                                                  target_type=elb.TargetType.IP,
                                                  protocol=elb.Protocol.TLS,
                                                  health_check=elb.HealthCheck(enabled=True,
                                                                               port=str(mq_console_port),
                                                                               protocol=elb.Protocol.TCP))

        # For now there is no way to retrieve private ip addresses of MQ broker instances from aws-amazonmq module.
        mq_described = cr.AwsCustomResource(self, 'function',
                                            policy=cr.AwsCustomResourcePolicy.from_sdk_calls(
                                                resources=cr.AwsCustomResourcePolicy.ANY_RESOURCE),
                                            on_create=cr.AwsSdkCall(
                                                physical_resource_id=cr.PhysicalResourceId.of('function'),
                                                service='MQ',
                                                action='describeBroker',
                                                parameters={'BrokerId': mq_instance.broker_name}))

        mq_described.node.add_dependency(mq_instance)

        # Adding private ip addresses of broker instances to target group one by one
        for az in range(len(vpc.availability_zones)):
            ip = mq_described.get_response_field('BrokerInstances.{az}.IpAddress'.format(az=az))
            nlb_target_group.add_target(elb_targets.IpTarget(ip_address=ip))

        mq_nlb = elb.NetworkLoadBalancer(self, 'mq_nlb',
                                         vpc=vpc,
                                         internet_facing=True)

        mq_nlb.add_listener('listener',
                            port=broker_port,
                            protocol=elb.Protocol.TLS,
                            certificates=[elb.ListenerCertificate(certificate_arn=cert.certificate_arn)],
                            default_target_groups=[nlb_target_group])
        
        
        CfnOutput(self, 'nlb dns', value=mq_nlb.loadBalancerDnsName)