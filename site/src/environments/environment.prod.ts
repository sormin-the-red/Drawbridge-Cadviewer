export const environment = {
  production: true,
  apiBaseUrl: 'https://API_GATEWAY_ID.execute-api.us-east-2.amazonaws.com/Prod',
  cognito: {
    domain: 'YOUR_COGNITO_DOMAIN.auth.us-east-2.amazoncognito.com',
    clientId: 'YOUR_COGNITO_CLIENT_ID',
    redirectUri: 'https://YOUR_CLOUDFRONT_DOMAIN/auth/callback',
  },
};
