import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';

export class DrawbridgeStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Resources defined here as the project is built out:
    //
    // - S3: DrawingsBucket (drawings + markup assets), SpaBucket (Angular SPA)
    // - CloudFront: SPA default origin + DrawingsBucket /products/*/2d/* origin
    // - SQS: JobsQueue + JobsDLQ
    // - DynamoDB: ProductsTable, VersionsTable, JobsTable, AnnotationsTable, MarkupsTable
    // - Lambda: API (minimal ASP.NET Core)
    // - API Gateway: HTTP API → Lambda
    // - SSM: APS client ID + secret (SecureString)
    // - IAM: ConversionWorker user with scoped S3/DynamoDB/SQS permissions
  }
}
