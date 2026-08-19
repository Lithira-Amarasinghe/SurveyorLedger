import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';
import { Land, LandService, addressLine, formatArea } from '../../core/land.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';

interface LandRow {
  land: Land;
  deedCount: number;
  surveyCount: number;
}

@Component({
  selector: 'app-land-list',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Land</h1>
        <button class="btn-primary" (click)="createNew()">New land</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (rows().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No land records yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Address</th>
                <th class="text-left px-lg py-sm font-medium">Area</th>
                <th class="text-left px-lg py-sm font-medium">Deeds</th>
                <th class="text-left px-lg py-sm font-medium">Surveys</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.land.landId) {
                <tr class="border-t border-neutral-200 cursor-pointer hover:bg-neutral-50" (click)="open(row.land)">
                  <td class="px-lg py-sm text-neutral-900">
                    <span>{{ addressLine(row.land) }}</span>
                    @if (row.land.ownerName) {
                      <span class="text-xs text-neutral-500 block">{{ row.land.ownerName }}</span>
                    }
                  </td>
                  <td class="px-lg py-sm text-neutral-600">
                    {{ formatArea(row.land.area) }}
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.deedCount }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.surveyCount }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `
})
export class LandListComponent implements OnInit {
  workspaceId = '';
  rows = signal<LandRow[]>([]);
  loading = signal(true);
  error = signal('');

  addressLine = addressLine;
  formatArea = formatArea;

  constructor(
    private landService: LandService,
    private currentWorkspace: CurrentWorkspaceService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.landService.search(this.workspaceId).subscribe({
      next: (lands) => {
        if (lands.length === 0) {
          this.rows.set([]);
          this.loading.set(false);
          return;
        }
        forkJoin(
          lands.map(land =>
            forkJoin({
              deeds: this.landService.getDeeds(this.workspaceId, land.landId),
              surveys: this.landService.getSurveys(this.workspaceId, land.landId)
            }).pipe(map(({ deeds, surveys }) => ({ land, deedCount: deeds.length, surveyCount: surveys.length })))
          )
        ).subscribe({
          next: (rows) => {
            this.rows.set(rows);
            this.loading.set(false);
          },
          error: (err) => {
            this.error.set(err.error?.message ?? 'Could not load land records.');
            this.loading.set(false);
          }
        });
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load land records.');
        this.loading.set(false);
      }
    });
  }

  open(land: Land): void {
    this.router.navigate(['/app/workspace', this.workspaceId, 'lands', land.landId]);
  }

  createNew(): void {
    this.router.navigate(['/app/workspace', this.workspaceId, 'lands', 'new']);
  }
}
