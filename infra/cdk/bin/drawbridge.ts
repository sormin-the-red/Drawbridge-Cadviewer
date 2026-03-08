#!/usr/bin/env node
import 'source-map-support/register';
import * as cdk from 'aws-cdk-lib';
import { DrawbridgeStack } from '../lib/drawbridge-stack';

const app = new cdk.App();

new DrawbridgeStack(app, 'DrawbridgeStack', {
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: process.env.CDK_DEFAULT_REGION,
  },
});
