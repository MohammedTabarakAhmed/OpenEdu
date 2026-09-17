/**
 * NFR-09 assertion shared by component specs: every rendered form control has an accessible name (an associated
 * <label>, aria-label or aria-labelledby) and every button has text or an aria-label. Called on a rendered state.
 */
export function expectAccessibleControls(root: HTMLElement): void {
  const controls = Array.from(root.querySelectorAll<HTMLElement>('input, select, textarea')).filter(
    (c) => (c as HTMLInputElement).type !== 'hidden',
  );
  for (const control of controls) {
    const id = control.getAttribute('id');
    const labelled =
      (id && root.querySelector(`label[for="${id}"]`) !== null) ||
      control.closest('label') !== null ||
      !!control.getAttribute('aria-label')?.trim() ||
      !!control.getAttribute('aria-labelledby');
    expect(labelled, `control without an accessible name: ${control.outerHTML.slice(0, 120)}`).toBe(true);
  }
  for (const button of Array.from(root.querySelectorAll<HTMLElement>('button, a[routerLink], a[href]'))) {
    const named = !!button.textContent?.trim() || !!button.getAttribute('aria-label')?.trim();
    expect(named, `button without a name: ${button.outerHTML.slice(0, 120)}`).toBe(true);
  }
  // Nothing interactive hides behind a click handler on a non-focusable element.
  for (const el of Array.from(root.querySelectorAll<HTMLElement>('div, span, td, tr, li'))) {
    if (el.onclick) {
      expect(el.getAttribute('tabindex'), `click-only element without tabindex: ${el.outerHTML.slice(0, 80)}`).not.toBeNull();
    }
  }
}
