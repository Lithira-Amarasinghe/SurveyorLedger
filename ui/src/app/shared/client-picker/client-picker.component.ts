import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { Client, ClientService } from '../../core/billing.service';

@Component({
  selector: 'app-client-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Client</label>

      @if (selected(); as client) {
        <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
          <div>
            <span class="text-sm text-neutral-900">{{ client.name }}</span>
            @if (client.phone) {
              <span class="block text-xs text-neutral-500">{{ client.phone }}</span>
            }
          </div>
          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="clear()">Change</button>
        </div>
      } @else {
        <input
          class="input-field"
          placeholder="Search clients by name, phone, or email…"
          [ngModel]="query"
          (ngModelChange)="onQueryChange($event)"
          name="clientSearch"
        />

        @if (searching()) {
          <p class="text-xs text-neutral-500 mt-xs">Searching…</p>
        } @else if (results().length > 0) {
          <div class="mt-xs border border-neutral-200 rounded divide-y divide-neutral-200">
            @for (client of results(); track client.clientId) {
              <button type="button" class="w-full text-left px-md py-sm hover:bg-neutral-50" (click)="select(client)">
                <span class="text-sm text-neutral-900">{{ client.name }}</span>
                @if (client.phone) {
                  <span class="block text-xs text-neutral-500">{{ client.phone }}</span>
                }
              </button>
            }
          </div>
        } @else if (query.trim().length >= 2) {
          <p class="text-xs text-neutral-500 mt-xs">No match. Create the client first from the Clients tab.</p>
        }
      }
    </div>
  `
})
export class ClientPickerComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() value: string | null = null;
  @Input() initialClientLabel: string | null = null;
  @Output() valueChange = new EventEmitter<string | null>();

  query = '';
  results = signal<Client[]>([]);
  searching = signal(false);
  selected = signal<Client | null>(null);

  private queries = new Subject<string>();

  constructor(private clientService: ClientService) {}

  ngOnInit(): void {
    if (this.value && this.initialClientLabel) {
      this.selected.set({
        clientId: this.value,
        name: this.initialClientLabel,
        phone: null,
        email: null,
        address: { street: null, city: null, district: null, postalCode: null, country: null },
        createdAt: '',
        updatedAt: ''
      });
    }

    this.queries
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap(term => this.clientService.search(this.workspaceId, term))
      )
      .subscribe({
        next: clients => {
          this.results.set(clients);
          this.searching.set(false);
        },
        error: () => {
          this.results.set([]);
          this.searching.set(false);
        }
      });
  }

  onQueryChange(term: string): void {
    this.query = term;
    this.searching.set(term.trim().length >= 2);
    if (term.trim().length < 2) {
      this.results.set([]);
      return;
    }
    this.queries.next(term);
  }

  select(client: Client): void {
    this.selected.set(client);
    this.results.set([]);
    this.query = '';
    this.valueChange.emit(client.clientId);
  }

  clear(): void {
    this.selected.set(null);
    this.valueChange.emit(null);
  }
}
