from constructs import Construct

from aws_cdk import (
    aws_dynamodb as dynamodb,
    Stack,
    RemovalPolicy
)


class DynamoDBStack(Stack):

    def __init__(self, scope: Construct, construct_id: str,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        if (self.node.try_get_context("deploy_documentdb") == "True"):  
            # Locations table

            locations_table = dynamodb.Table(self, "Locations",
                                            table_name="Locations",
                                            partition_key=dynamodb.Attribute(name="LocationId",
                                                                            type=dynamodb.AttributeType.NUMBER),
                                            billing_mode=dynamodb.BillingMode.PAY_PER_REQUEST,
                                            removal_policy=RemovalPolicy.DESTROY)

            # locations_table.(dynamodb.Attribute(
            #     name="Code", type=dynamodb.AttributeType.STRING))
            # locations_table.add_attribute(dynamodb.Attribute(
            #     name="Description", type=dynamodb.AttributeType.STRING))
            # # locations_table.add_attribute(dynamodb.Attribute(
            # #     name="Location", type=dynamodb.AttributeType.MAP))
            # locations_table.add_attribute(dynamodb.Attribute(
            #     name="Parent_Location_Id", type=dynamodb.AttributeType.NUMBER))

            # User locations table
            user_locations_table = dynamodb.Table(self, "UserLocations",
                                                table_name="UserLocations",
                                                partition_key=dynamodb.Attribute(name="UserId",
                                                                                type=dynamodb.AttributeType.NUMBER),
                                                sort_key=dynamodb.Attribute(name="LocationId",
                                                                            type=dynamodb.AttributeType.NUMBER),
                                                billing_mode=dynamodb.BillingMode.PAY_PER_REQUEST,
                                                removal_policy=RemovalPolicy.DESTROY)

            # user_locations_table.add_global_secondary_index(
            #     index_name="UserIdGSI",
            #     partition_key=dynamodb.attribute(
            #         name="UserId", type=dynamodb.AttributeType.NUMBER),
            #     sort_key=dynamodb.attribute(
            #         name="LocationId", type=dynamodb.AttributeType.NUMBER)
            # )
            # user_locations_table.(dynamodb.Attribute(
            #     name="UpdateDate", type=dynamodb.AttributeType.STRING))

            # MarketingData table
            marketing_data_table = dynamodb.Table(self, "MarketingData",
                                                table_name="MarketingData",
                                                partition_key=dynamodb.Attribute(name="UserId",
                                                                                type=dynamodb.AttributeType.NUMBER),
                                                billing_mode=dynamodb.BillingMode.PAY_PER_REQUEST,
                                                removal_policy=RemovalPolicy.DESTROY)

            # marketing_data_table.add_attribute(dynamodb.Attribute(
            #     name="Locations", type=dynamodb.AttributeType.MAP))
            # marketing_data_table.add_attribute(dynamodb.Attribute(
            #     name="UpdateDate", type=dynamodb.AttributeType.STRING))
            
            
            # Create a DynamoDB table using a CloudFormation template
            # table = dynamodb.CfnTable(
            #     self, "MyTable",
            #     table_name="MyTable",
            #     key_schema=[
            #         {
            #             "attribute_name": "id",
            #             "key_type": "HASH"
            #         },
            #         {
            #             "attribute_name": "sortKey",
            #             "key_type": "RANGE"
            #         }
            #     ],
            #     attribute_definitions=[
            #         {
            #             "attribute_name": "id",
            #             "attribute_type": "S"
            #         },
            #         {
            #             "attribute_name": "sortKey",
            #             "attribute_type": "S"
            #         },
            #         {
            #             "attribute_name": "gsiId",
            #             "attribute_type": "S"
            #         },
            #         {
            #             "attribute_name": "gsiSortKey",
            #             "attribute_type": "S"
            #         },
            #         {
            #             "attribute_name": "data",
            #             "attribute_type": "S"
            #         }
            #     ],
            #     global_secondary_indexes=[
            #         {
            #             "index_name": "MyGSI",
            #             "key_schema": [
            #                 {
            #                     "attribute_name": "gsiId",
            #                     "key_type": "HASH"
            #                 },
            #                 {
            #                     "attribute_name": "gsiSortKey",
            #                     "key_type": "RANGE"
            #                 }
            #             ],
            #             "projection": {
            #                 "projection_type": "ALL"
            #             }
            #         }
            #     ],
            #     provisioned_throughput={
            #         "read_capacity_units": 5,
            #         "write_capacity_units": 5
            #     }
            # )
