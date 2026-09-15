import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  imports: [RouterOutlet],
  template: `
    <h1 class="h4">Learner</h1>
    <router-outlet />
  `,
})
export class LearnerLayout {}
