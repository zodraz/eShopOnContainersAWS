using Xunit;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.APIGatewayEvents;


namespace Marketing.Functions.Tests;

public class FunctionTest
{
    public FunctionTest()
    {
    }

    [Fact(Skip ="Needs to be reviewed because request and context should be filled up.")]
    public async Task TetGetMethod()
    {
        TestLambdaContext context;
        APIGatewayHttpApiV2ProxyRequest request;
        APIGatewayProxyResponse response;

        Function function = new Function();


        request = new APIGatewayHttpApiV2ProxyRequest();
        context = new TestLambdaContext();
        response = await function.FunctionHandler(request, context);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("Hello AWS Serverless", response.Body);
    }
}