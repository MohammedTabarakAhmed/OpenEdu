import { Component, input } from '@angular/core';

/**
 * The OpenCampus mark: a circle rising over the two-tone horizon. Inline SVG so it inherits the theme
 * tokens, needs no request, and mirrors correctly under RTL (it is symmetric).
 */
@Component({
  selector: 'app-mark',
  template: `
    <svg class="oc-mark" [attr.width]="size()" [attr.height]="size()" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <rect width="32" height="32" rx="6" fill="var(--oc-deep)" />
      <circle cx="16" cy="12.5" r="5.5" fill="var(--oc-sky)" />
      <rect x="6" y="19" width="20" height="2.5" rx="1.25" fill="var(--oc-sky)" />
      <rect x="6" y="22.5" width="20" height="2.5" rx="1.25" fill="var(--oc-paper)" opacity="0.55" />
    </svg>
  `,
})
export class Mark {
  readonly size = input(24);
}
