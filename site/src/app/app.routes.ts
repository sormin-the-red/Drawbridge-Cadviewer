import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  {
    path: 'auth/callback',
    loadComponent: () =>
      import('./pages/auth-callback/auth-callback').then(m => m.AuthCallbackComponent),
  },
  {
    path: 'landing',
    loadComponent: () =>
      import('./pages/landing/landing').then(m => m.LandingComponent),
  },
  {
    path: '',
    loadComponent: () =>
      import('./pages/product-library/product-library').then(m => m.ProductLibraryComponent),
    canActivate: [authGuard],
  },
  {
    path: 'products/:pn',
    loadComponent: () =>
      import('./pages/product-detail/product-detail').then(m => m.ProductDetailComponent),
    canActivate: [authGuard],
  },
  {
    path: 'viewer/:pn/:v',
    loadComponent: () =>
      import('./pages/model-viewer/model-viewer').then(m => m.ModelViewerComponent),
    canActivate: [authGuard],
  },
  { path: '**', redirectTo: '' },
];
