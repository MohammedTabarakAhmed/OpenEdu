import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { HealthService } from './core/health.service';

@Component({
  imports: [RouterOutlet, RouterLink],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly health = inject(HealthService);

  protected readonly apiStatus = signal<'checking' | 'healthy' | 'unreachable'>('checking');

  constructor() {
    this.health.status().subscribe({
      next: (s) => this.apiStatus.set(s === 'Healthy' ? 'healthy' : 'unreachable'),
      error: () => this.apiStatus.set('unreachable'),
    });
  }
}
