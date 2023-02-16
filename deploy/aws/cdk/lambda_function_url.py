from aws_cdk import (
    Stack,
    aws_iam as iam,
    aws_lambda as fn,
    aws_ecr as ecr,
    CfnOutput)

from constructs import Construct


class LambdaFunctionUrlStack(Stack):

    def __init__(self, scope: Construct, construct_id: str,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        repository = ecr.Repository.from_repository_name(
            self, id="eshop-aws-repo", repository_name=self.node.try_get_context(
                "ecr_repo_name"))

        docker_image_function = fn.DockerImageFunction(
            self,
            "eshop-aws-marketing-function",
            code=fn.DockerImageCode.from_ecr(repository=repository,
                                             tag_or_digest=self.node.try_get_context(
                                            "ecr_image_tag")))
            
        fn_url_role = iam.Role(
                self,
                "EshopMarketingFunctionRole",
                assumed_by=iam.ServicePrincipal("lambda.amazonaws.com")
            )
        
        fn_url_role_policy_statement_json_1 = {
                "Effect":"Allow",
                "Action": "logs:CreateLogGroup",
                "Resource":"*",
            }
        
        fn_url_role_policy_statement_json_2 = {
                "Effect":"Allow",
                "Action": [
                        "logs:CreateLogStream",
                        "logs:PutLogEvents"
                ],
                "Resource":"*",
            }
        
        fn_url_role.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    fn_url_role_policy_statement_json_1))
        
        fn_url_role.add_to_principal_policy(
                iam.PolicyStatement.from_json(
                    fn_url_role_policy_statement_json_2))

        fn_url = docker_image_function.add_function_url()
        fn_url.grant_invoke_url(fn_url_role)

        CfnOutput(
            self,
            "TheUrl",
            # The .url attributes will return the unique Function URL
            value=fn_url.url)
