import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { Roles } from './core/auth/auth.models';
import { SessionService } from './core/auth/session.service';
import { HealthService } from './core/health.service';
import { I18nService, TranslatePipe } from './core/i18n/i18n.service';
import { Mark } from './shared/brand';

@Component({
  imports: [RouterOutlet, RouterLink, TranslatePipe, Mark],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly health = inject(HealthService);

  protected readonly session = inject(SessionService);
  protected readonly i18n = inject(I18nService);
  protected readonly roles = Roles;
  protected readonly apiStatus = signal<'checking' | 'healthy' | 'unreachable'>('checking');

  constructor() {
    this.health.status().subscribe({
      next: (s) => this.apiStatus.set(s === 'Healthy' ? 'healthy' : 'unreachable'),
      error: () => this.apiStatus.set('unreachable'),
    });
  }

  protected logout(): void {
    this.session.logout().subscribe();
  }
}
