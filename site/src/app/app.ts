import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { NavbarComponent } from './components/navbar/navbar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, NavbarComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private router = inject(Router);

  private routeEvents = toSignal(
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)),
  );

  protected showNavbar = computed(() => {
    this.routeEvents();
    const url = this.router.url;
    return url !== '/landing' && !url.startsWith('/auth/callback');
  });
}
