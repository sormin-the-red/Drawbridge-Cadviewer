import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-navbar',
  imports: [RouterLink],
  templateUrl: './navbar.html',
  styleUrl: './navbar.scss',
})
export class NavbarComponent {
  protected auth  = inject(AuthService);
  protected email = this.auth.getCurrentUser()?.email ?? '';

  protected signOut(): void {
    this.auth.logout();
  }
}
