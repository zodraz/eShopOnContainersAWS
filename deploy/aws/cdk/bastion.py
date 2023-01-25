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


class BastionStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        if self.node.try_get_context("deploy_bastion") == "True":
            # Create an Instance Profile for our Admin Role to assume w/EC2
            cluster_admin_role_instance_profile = iam.CfnInstanceProfile(
                self,
                "ClusterAdminRoleInstanceProfile",
                roles=[cluster_admin_role.role_name],
            )

            # Another way into our Bastion is via Systems Manager Session Manager
            if self.node.try_get_context(
                    "create_new_cluster_admin_role") == "True":
                cluster_admin_role.add_managed_policy(
                    iam.ManagedPolicy.from_aws_managed_policy_name(
                        "AmazonSSMManagedInstanceCore"))

            # Create Bastion
            # Get Latest Amazon Linux AMI
            amzn_linux = ec2.MachineImage.latest_amazon_linux(
                generation=ec2.AmazonLinuxGeneration.AMAZON_LINUX_2,
                edition=ec2.AmazonLinuxEdition.STANDARD,
                virtualization=ec2.AmazonLinuxVirt.HVM,
                storage=ec2.AmazonLinuxStorage.GENERAL_PURPOSE,
            )

            # Create SecurityGroup for bastion
            bastion_security_group = ec2.SecurityGroup(self,
                                                       "BastionSecurityGroup",
                                                       vpc=vpc,
                                                       allow_all_outbound=True)

            # Add a rule to allow our new SG to talk to the EKS control plane
            eks_cluster.cluster_security_group.add_ingress_rule(
                bastion_security_group, ec2.Port.all_traffic())

            # Create our EC2 instance for bastion
            bastion_instance = ec2.Instance(
                self,
                "BastionInstance",
                instance_type=ec2.InstanceType(
                    self.node.try_get_context("bastion_node_type")),
                machine_image=amzn_linux,
                role=cluster_admin_role,
                vpc=vpc,
                vpc_subnets=ec2.SubnetSelection(
                    subnet_type=ec2.SubnetType.PUBLIC),
                security_group=bastion_security_group,
                block_devices=[
                    ec2.BlockDevice(
                        device_name="/dev/xvda",
                        volume=ec2.BlockDeviceVolume.ebs(
                            self.node.try_get_context("bastion_disk_size")),
                    )
                ],
            )

            # Set up our kubectl and fluxctl
            bastion_instance.user_data.add_commands(
                "curl -o kubectl https://amazon-eks.s3.us-west-2.amazonaws.com/1.21.2/2021-07-05/bin/linux/amd64/kubectl"
            )
            bastion_instance.user_data.add_commands("chmod +x ./kubectl")
            bastion_instance.user_data.add_commands("mv ./kubectl /usr/bin")
            bastion_instance.user_data.add_commands(
                "curl -s https://raw.githubusercontent.com/helm/helm/master/scripts/get-helm-3 | bash -"
            )
            bastion_instance.user_data.add_commands(
                "curl -s https://fluxcd.io/install.sh | bash -")
            bastion_instance.user_data.add_commands(
                "curl --silent --location https://rpm.nodesource.com/setup_14.x | bash -"
            )
            bastion_instance.user_data.add_commands(
                "yum install nodejs git -y")
            bastion_instance.user_data.add_commands(
                'su -c "aws eks update-kubeconfig --name ' +
                eks_cluster.cluster_name + " --region " + self.region +
                '" ssm-user')

            # Wait to deploy Bastion until cluster is up and we're deploying manifests/charts to it
            # This could be any of the charts/manifests I just picked this one as almost everybody will want it
            bastion_instance.node.add_dependency(metricsserver_chart)

            CfnOutput(
                self,
                id="BastionPrivateIP",
                value=bastion_instance.instance_private_ip,
                description="BASTION Private IP",
                export_name=
                f"{self.region}:{self.account}:{self.stack_name}:bastion-private-ip"
            )

            CfnOutput(
                self,
                id="BastionPublicIP",
                value=bastion_instance.instance_public_ip,
                description="BASTION Public IP",
                export_name=
                f"{self.region}:{self.account}:{self.stack_name}:bastion-public-ip"
            )