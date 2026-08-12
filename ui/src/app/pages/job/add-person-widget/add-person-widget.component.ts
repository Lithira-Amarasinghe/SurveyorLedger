import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { Person, PersonService } from '../../../core/person.service';
import { WorkspaceService } from '../../../core/workspace.service';

export interface PersonWithRole {
  person: Person;
  role: string;
}

@Component({
  selector: 'app-add-person-widget',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="border border-neutral-200 rounded-md p-md">
      <div class="flex gap-sm mb-sm">
        <input
          class="input-field flex-1"
          type="text"
          placeholder="Search by name or email…"
          [(ngModel)]="query"
          (ngModelChange)="onQueryChange($event)"
        />
        <select class="input-field w-32" [(ngModel)]="role">
          @for (r of eligibleRoles(); track r) {
            <option [value]="r">{{ r }}</option>
          }
        </select>
      </div>

      @if (searching()) {
        <p class="text-xs text-neutral-500 mt-sm">Searching…</p>
      } @else if (query.trim().length > 0) {
        <div class="mt-sm space-y-xs">
          @for (person of results(); track person.userId) {
            <button
              type="button"
              class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 flex items-center justify-between"
              [disabled]="adding()"
              (click)="choose(person)"
            >
              <span class="text-sm text-neutral-900">{{ person.name }}</span>
              <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ person.roleLabel }}</span>
            </button>
          }
          @if (results().length === 0) {
            <p class="text-xs text-neutral-500 px-md py-sm">
              Not found. <a [routerLink]="['/app/workspace', workspaceId, 'members']" class="text-primary-600">Add them as a member</a> first, then assign them here.
            </p>
          }
        </div>
      }
      @if (error()) {
        <p class="text-xs text-primary-500 mt-sm">{{ error() }}</p>
      }
    </div>
  `
})
export class AddPersonWidgetComponent implements OnInit {
  @Input() workspaceId = '';
  @Output() added = new EventEmitter<PersonWithRole>();

  query = '';
  role = 'Client';
  eligibleRoles = signal<string[]>(['Client', 'Surveyor']);
  results = signal<Person[]>([]);
  searching = signal(false);
  adding = signal(false);
  error = signal('');

  private queryChanged = new Subject<string>();

  constructor(private personService: PersonService, private workspaceService: WorkspaceService) {
    this.queryChanged
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          if (!q.trim()) {
            this.searching.set(false);
            return [];
          }
          this.searching.set(true);
          return this.personService.searchPeople(this.workspaceId, q.trim());
        })
      )
      .subscribe({
        next: (people) => {
          this.results.set(people);
          this.searching.set(false);
        },
        error: () => this.searching.set(false)
      });
  }

  ngOnInit(): void {
    this.workspaceService.getEligibleRoles(this.workspaceId, 'Job').subscribe(roles => {
      this.eligibleRoles.set(roles);
      if (!roles.includes(this.role)) this.role = roles[0] ?? this.role;
    });
  }

  onQueryChange(value: string): void {
    this.queryChanged.next(value);
  }

  choose(person: Person): void {
    this.error.set('');
    this.adding.set(true);
    this.added.emit({ person, role: this.role });
  }

  /** Call after successfully handling the `added` event - resets to the search state. */
  markAdded(): void {
    this.reset();
  }

  /** Call if handling the `added` event failed - shows the error, re-enables the picker. */
  markFailed(message: string): void {
    this.adding.set(false);
    this.error.set(message);
  }

  reset(): void {
    this.query = '';
    this.results.set([]);
    this.error.set('');
    this.adding.set(false);
  }
}
