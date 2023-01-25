from aws_cdk import (
    aws_dynamodb as dynamodb,
    Stack,
    RemovalPolicy
)


class DynamoDBStack(Stack):

    def __init__(self, scope: Construct, construct_id: str, vpc,
                 **kwargs) -> None:
        super().__init__(scope, construct_id, **kwargs)

        # Locations table
        
        locations_table = dynamodb.Table(self, "Locations",
                              partition_key=dynamodb.Attribute(name="LocationId", 
                                        type=dynamodb.Attribute_type.NUMBER),
                              billing_mode=dynamodb.BillingMode.PAY_PER_REQUEST
                              removal_policy=RemovalPolicy.DESTROY)

        locations_table.add_attribute(dynamodb.Attribute(name="Code", type=dynamodb.attribute_type.STRING))
        locations_table.add_attribute(dynamodb.Attribute(name="Description", type=dynamodb.attribute_type.STRING))
        locations_table.add_attribute(dynamodb.Attribute(name="Location", type=dynamodb.attribute_type.MAP))
        locations_table.add_attribute(dynamodb.Attribute(name="Parent_Location_Id", type=dynamodb.attribute_type.NUMBER))
        
        # User locations table
        user_locations_table = dynamodb.Table(self, "UserLocations",
                              partition_key=dynamodb.Attribute(name="UserId", 
                                        type=dynamodb.Attribute_type.NUMBER),
                              sort_key=dynamodb.attribute(name="LocationId", 
                                                           type=dynamodb.attribute_type.NUMBER)
                              billing_mode=dynamodb.BillingMode.PAY_PER_REQUEST
                              removal_policy=RemovalPolicy.DESTROY)

        user_locations_table.add_global_secondary_index(
            index_name="UserIdGSI",
            partition_key=dynamodb.attribute(name="UserId", type=dynamodb.attribute_type.NUMBER),
            sort_key=dynamodb.attribute(name="LocationId", type=dynamodb.attribute_type.NUMBER)
        )
        user_locations_table.add_attribute(dynamodb.Attribute(name="UpdateDate", type=dynamodb.attribute_type.STRING))
        
        # MarketingData table
        marketing_data_table = dynamodb.Table(self, "MarketingData",
                              partition_key=dynamodb.Attribute(name="UserId", 
                                        type=dynamodb.Attribute_type.NUMBER),
                              billing_mode=dynamodb.BillingMode.PAY_PER_REQUEST
                              removal_policy=RemovalPolicy.DESTROY)

        user_locations_table.add_attribute(dynamodb.Attribute(name="Locations", type=dynamodb.attribute_type.MAP))
        user_locations_table.add_attribute(dynamodb.Attribute(name="UpdateDate", type=dynamodb.attribute_type.STRING))