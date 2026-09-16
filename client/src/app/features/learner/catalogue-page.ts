import { Component, inject, signal } from '@angular/core';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { PageState } from '../../shared/page-state';
import { BilingualPipe, LocaleDatePipe, PageControls, SearchBox, SubmitError } from '../../shared/ui';
import { CatalogueApi, ProgrammesApi } from '../admin/admin.api';
import { CatalogueEntry, DELIVERY_MODES, Programme } from '../admin/admin.models';

/** Catalogue browsing with filtering and self-enrolment (15.3 "Learner self-service"). Only Open sections are shown. */
@Component({
  imports: [TranslatePipe, BilingualPipe, LocaleDatePipe, PageControls, SearchBox, SubmitError],
  templateUrl: './catalogue-page.html',
})
export class CataloguePage {
  private readonly api = inject(CatalogueApi);
  private readonly programmesApi = inject(ProgrammesApi);

  protected readonly deliveryModes = DELIVERY_MODES;
  protected search = '';
  protected programmeId = '';
  protected deliveryMode = '';
  protected readonly programmes = signal<Programme[]>([]);
  protected readonly state = new PageState<CatalogueEntry>((page, pageSize) =>
    this.api.browse({ page, pageSize, search: this.search, programmeId: this.programmeId, deliveryMode: this.deliveryMode }),
  );

  protected readonly busyOn = signal<string | null>(null);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly enrolledIn = signal<string | null>(null);

  constructor() {
    this.state.pageSize.set(12);
    this.state.load();
    this.programmesApi.list({ page: 1, pageSize: 100, isActive: true, sort: 'code' }).subscribe((p) => this.programmes.set(p.items));
  }

  protected onSearch(term: string): void {
    this.search = term;
    this.state.load(1);
  }

  protected onProgramme(value: string): void {
    this.programmeId = value;
    this.state.load(1);
  }

  protected onMode(value: string): void {
    this.deliveryMode = value;
    this.state.load(1);
  }

  /** The server applies BR-01/BR-02/BR-03; a refusal is shown with its rule reference. */
  protected enrol(entry: CatalogueEntry): void {
    if (this.busyOn()) {
      return;
    }
    this.busyOn.set(entry.sectionId);
    this.error.set(null);
    this.enrolledIn.set(null);
    this.api.enrol(entry.sectionId).subscribe({
      next: () => {
        this.busyOn.set(null);
        this.enrolledIn.set(entry.sectionId);
        this.state.reload();
      },
      error: (failure: unknown) => {
        this.busyOn.set(null);
        this.error.set(toPresentableError(failure));
      },
    });
  }
}
