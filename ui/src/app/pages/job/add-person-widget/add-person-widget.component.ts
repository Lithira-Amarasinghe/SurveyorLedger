import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { Person, PersonService } from '../../../core/person.service';

type Mode = 'search' | 'create' | 'confirm';

@Component({
  selector: 'app-add-person-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="border border-neutral-200 rounded-md p-md">
      @if (mode() === 'search') {
        <input
          class="input-field"
          type="text"
          placeholder="Search by name or email…"
          [(ngModel)]="query"
          (ngModelChange)="onQueryChange($event)"
        />

        @if (searching()) {
          <p class="text-xs text-neutral-500 mt-sm">Searching…</p>
        } @else if (query.trim().length > 0) {
          <div class="mt-sm space-y-xs">
            @for (person of results(); track person.userId) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 flex items-center justify-between"
                (click)="choose(person)"
              >
                <span class="text-sm text-neutral-900">{{ person.name }}</span>
                <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ person.roleLabel }}</span>
              </button>
            }
            @if (results().length === 0) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 text-sm text-primary-600"
                (click)="startCreate()"
              >
                + Create "{{ query.trim() }}" as new client
              </button>
            }
          </div>
        }
      } @else if (mode() === 'create') {
        <div class="space-y-sm">
          <p class="text-sm font-medium text-neutral-900">New client</p>
          <input class="input-field" type="text" placeholder="First name" [(ngModel)]="newFirstName" />
          <input class="input-field" type="text" placeholder="Last name" [(ngModel)]="newLastName" />
          <input class="input-field" type="text" placeholder="Phone (optional)" [(ngModel)]="newPhone" />
          @if (error()) {
            <p class="text-xs text-primary-500">{{ error() }}</p>
          }
          <div class="flex justify-end gap-sm">
            <button type="button" class="btn-secondary" (click)="reset()">Cancel</button>
            <button
              type="button"
              class="btn-primary"
              [disabled]="!newFirstName.trim() || !newLastName.trim() || creatingClient()"
              (click)="createAndContinue()"
            >
              {{ creatingClient() ? 'Creating…' : 'Create & continue' }}
            </button>
          </div>
        </div>
      } @else {
        <div class="space-y-sm">
          <p class="text-sm text-neutral-900">
            Add <strong>{{ selected()!.name }}</strong> as:
          </p>
          <select class="input-field" [(ngModel)]="participantType">
            <option value="Client">Client</option>
            <option value="Surveyor">Surveyor</option>
            <option value="Assistant">Assistant</option>
            <option value="Other">Other</option>
          </select>
          @if (error()) {
            <p class="text-xs text-primary-500">{{ error() }}</p>
          }
          <div class="flex justify-end gap-sm">
            <button type="button" class="btn-secondary" (click)="reset()">Cancel</button>
            <button type="button" class="btn-primary" [disabled]="adding()" (click)="confirmAdd()">
              {{ adding() ? 'Adding…' : 'Add' }}
            </button>
          </div>
        </div>
      }
    </div>
  `
})
export class AddPersonWidgetComponent {
  @Input() workspaceId = '';
  @Output() added = new EventEmitter<{ person: Person; participantType: string }>();

  mode = signal<Mode>('search');
  query = '';
  results = signal<Person[]>([]);
  searching = signal(false);
  selected = signal<Person | null>(null);
  participantType = 'Client';
  newFirstName = '';
  newLastName = '';
  newPhone = '';
  creatingClient = signal(false);
  adding = signal(false);
  error = signal('');

  private queryChanged = new Subject<string>();

  constructor(private personService: PersonService) {
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

  onQueryChange(value: string): void {
    this.queryChanged.next(value);
  }

  choose(person: Person): void {
    this.selected.set(person);
    this.mode.set('confirm');
  }

  startCreate(): void {
    const parts = this.query.trim().split(/\s+/);
    this.newFirstName = parts[0] ?? '';
    this.newLastName = parts.slice(1).join(' ');
    this.mode.set('create');
  }

  createAndContinue(): void {
    if (!this.newFirstName.trim() || !this.newLastName.trim()) return;
    this.error.set('');
    this.creatingClient.set(true);
    this.personService
      .createClient(this.workspaceId, {
        firstName: this.newFirstName.trim(),
        lastName: this.newLastName.trim(),
        phone: this.newPhone.trim() || undefined
      })
      .subscribe({
        next: (person) => {
          this.creatingClient.set(false);
          this.selected.set(person);
          this.mode.set('confirm');
        },
        error: (err) => {
          this.creatingClient.set(false);
          this.error.set(err.error?.message ?? 'Could not create client.');
        }
      });
  }

  confirmAdd(): void {
    const person = this.selected();
    if (!person) return;
    this.error.set('');
    this.adding.set(true);
    this.added.emit({ person, participantType: this.participantType });
  }

  /** Call after successfully handling the `added` event - resets to the search state. */
  markAdded(): void {
    this.reset();
  }

  /** Call if handling the `added` event failed - shows the error, re-enables Add. */
  markFailed(message: string): void {
    this.adding.set(false);
    this.error.set(message);
  }

  reset(): void {
    this.mode.set('search');
    this.query = '';
    this.results.set([]);
    this.selected.set(null);
    this.newFirstName = '';
    this.newLastName = '';
    this.newPhone = '';
    this.participantType = 'Client';
    this.error.set('');
    this.adding.set(false);
  }
}
