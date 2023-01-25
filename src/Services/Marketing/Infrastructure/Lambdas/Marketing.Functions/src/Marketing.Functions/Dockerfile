#FROM public.ecr.aws/lambda/dotnet:6
#
#WORKDIR /var/task
#
## This COPY command copies the .NET Lambda project's build artifacts from the host machine into the image. 
## The source of the COPY should match where the .NET Lambda project publishes its build artifacts. If the Lambda function is being built 
## with the AWS .NET Lambda Tooling, the `--docker-host-build-output-dir` switch controls where the .NET Lambda project
## will be built. The .NET Lambda project templates default to having `--docker-host-build-output-dir`
## set in the aws-lambda-tools-defaults.json file to "bin/Release/lambda-publish".
##
## Alternatively Docker multi-stage build could be used to build the .NET Lambda project inside the image.
## For more information on this approach checkout the project's README.md file.
#COPY "bin/Release/lambda-publish"  .


FROM public.ecr.aws/lambda/dotnet:6 AS base

FROM mcr.microsoft.com/dotnet/sdk:6.0-bullseye-slim as build
WORKDIR /src
COPY ["Marketing.Functions.csproj", "Marketing.Functions/"]
RUN dotnet restore "Marketing.Functions/Marketing.Functions.csproj"

WORKDIR "/src/Marketing.Functions"
COPY . .
RUN dotnet build "Marketing.Functions.csproj" --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "Marketing.Functions.csproj" \
            --configuration Release \ 
            --runtime linux-x64 \
            --self-contained false \ 
            --output /app/publish \
            -p:PublishReadyToRun=true  

FROM base AS final
WORKDIR /var/task
COPY --from=publish /app/publish .
CMD ["Marketing.Functions::Marketing.Functions.Function::FunctionHandler"]
