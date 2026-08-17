import { Component, OnInit, ViewChild, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { Observable, Subject, forkJoin } from 'rxjs';
import { Job, JobInvitation, JobParticipant, JobService } from '../../core/job.service';
import { Land, addressLine } from '../../core/land.service';
import { AuthService } from '../../core/auth.service';
import { InviteByEmail, PersonWithRole } from './add-job-person-modal/add-job-person-modal.component';
import { Milestone, MilestoneService } from '../../core/milestone.service';
import { Document, DocumentService } from '../../core/document.service';
import { DocumentRequest, DocumentRequestService } from '../../core/document-request.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { Invoice, InvoiceService, Quotation, QuotationService } from '../../core/billing.service';
import { AddJobPersonModalComponent } from './add-job-person-modal/add-job-person-modal.component';
import { AddLandWidgetComponent } from './add-land-widget/add-land-widget.component';
import { LandDetailPanelComponent } from '../land/land-detail-panel/land-detail-panel.component';
import { DocumentUploadWidgetComponent } from './document-upload-widget/document-upload-widget.component';
import { DocumentViewerModalComponent } from './document-viewer-modal/document-viewer-modal.component';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';

const STATUSES = ['Draft', 'Scheduled', 'InProgress', 'Completed', 'Cancelled'];
const MILESTONE_STATUSES = ['Pending', 'InProgress', 'Completed'];

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    DragDropModule,
    AddJobPersonModalComponent,
    AddLandWidgetComponent,
    LandDetailPanelComponent,
    DocumentUploadWidgetComponent,
    DocumentViewerModalComponent
  ],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (error()) {
      <div class="p-lg">
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      </div>
    } @else if (job(); as j) {
      <div class="p-lg max-w-3xl mx-auto space-y-lg">
        <div class="card">
          <div class="flex items-center justify-between">
            <span class="font-mono text-xs text-neutral-500">{{ j.jobNumber }}</span>
            <select class="input-field w-40 py-xs" [ngModel]="j.status" (ngModelChange)="onStatusChange($event)">
              @for (s of statuses; track s) {
                <option [value]="s">{{ s }}</option>
              }
            </select>
          </div>
          <input class="input-field mt-sm text-base font-semibold" [(ngModel)]="titleDraft" />
          <textarea
            class="input-field mt-sm text-sm"
            rows="2"
            placeholder="Description (optional)"
            [(ngModel)]="descriptionDraft"
          ></textarea>

          @if (headerDirty()) {
            <div class="flex items-center justify-end gap-sm mt-sm">
              @if (headerError()) {
                <span class="text-xs text-primary-500 mr-auto">{{ headerError() }}</span>
              } @else {
                <span class="text-xs text-amber-600 mr-auto">Unsaved changes</span>
              }
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" [disabled]="savingHeader()" (click)="discardHeader()">
                Discard
              </button>
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600 font-medium" [disabled]="savingHeader()" (click)="saveHeader()">
                {{ savingHeader() ? 'Saving…' : 'Save changes' }}
              </button>
            </div>
          }
        </div>

        <div class="card">
          <div class="flex items-center justify-between mb-md">
            <h2 class="text-sm font-semibold text-neutral-900">People</h2>
            @if (job()?.canManageParticipants) {
              <button type="button" class="btn-primary text-xs" (click)="addPersonModalOpen.set(true)">Add person</button>
            }
          </div>

          @if (jobPeopleRows().length === 0 && pendingInvitations().length === 0) {
            <p class="text-xs text-neutral-500">No one has access to this job yet.</p>
          }

          <div class="space-y-xs">
            @for (row of jobPeopleRows(); track row.userId) {
              <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
                <div class="flex items-center flex-wrap gap-xs">
                  <span class="text-sm text-neutral-900">{{ row.firstName }} {{ row.lastName }}</span>
                  @for (role of row.directRoles; track role) {
                    <span class="text-xs pl-sm pr-xs py-xs rounded bg-neutral-100 text-neutral-600 flex items-center gap-xs">
                      {{ role }}
                      @if (job()?.canManageParticipants) {
                        <button type="button" class="text-neutral-400 hover:text-primary-500" title="Remove this role" (click)="removeParticipant({ userId: row.userId, role })">
                          ×
                        </button>
                      }
                    </span>
                  }
                  @for (role of row.workspaceWideRoles; track role) {
                    <span
                      class="text-xs pl-sm pr-xs py-xs rounded bg-primary-50 text-primary-600"
                      [title]="'Holds ' + role + ' at the workspace level - can open every job, not tied to this one specifically'"
                    >
                      Workspace-wide via {{ role }}
                    </span>
                  }
                  @if (job()?.canManageParticipants && jobRoleOptions(row.directRoles).length > 0) {
                    <select class="input-field w-28 py-xs text-xs" [ngModel]="''" (ngModelChange)="addRoleToParticipant(row.userId, $event)">
                      <option value="" disabled selected>+ role</option>
                      @for (r of jobRoleOptions(row.directRoles); track r) {
                        <option [value]="r">{{ r }}</option>
                      }
                    </select>
                  }
                </div>
              </div>
            }
            @for (inv of pendingInvitations(); track inv.invitationId) {
              <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
                <span class="text-sm text-neutral-500">{{ inv.email }}</span>
                <span class="text-xs px-sm py-xs rounded bg-amber-100 text-amber-700">{{ inv.role }} · Pending</span>
              </div>
            }
          </div>

          @if (personMessage()) {
            <p class="text-xs text-primary-600 mt-sm">{{ personMessage() }}</p>
          }
        </div>

        @if (addPersonModalOpen()) {
          <app-add-job-person-modal
            #personModal
            [workspaceId]="workspaceId"
            (cancel)="addPersonModalOpen.set(false)"
            (added)="onPersonAdded($event)"
            (invited)="onPersonInvited($event)"
          />
        }

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Land</h2>
          @if (lands().length > 0) {
            <div class="space-y-xs mb-md">
              @for (l of lands(); track l.landId) {
                <div class="rounded bg-neutral-50">
                  <div class="flex items-center justify-between px-md py-sm cursor-pointer" (click)="toggleLand(l.landId)">
                    <div>
                      <span class="text-sm text-neutral-900">{{ addressLine(l) }}</span>
                      @if (l.latitude !== null) {
                        <a
                          class="text-xs text-primary-600 ml-xs"
                          [href]="'https://www.google.com/maps?q=' + l.latitude + ',' + l.longitude"
                          target="_blank"
                          rel="noopener"
                          title="Open in Google Maps"
                          (click)="$event.stopPropagation()"
                        >📍</a>
                      } @else {
                        <span class="text-xs text-neutral-300 ml-xs" title="Location not set">📍</span>
                      }
                      @if (l.ownerName) {
                        <span class="text-xs text-neutral-500 block">{{ l.ownerName }}</span>
                      }
                      @if (l.size) {
                        <span class="text-xs text-neutral-500 block">{{ l.size }} {{ l.sizeUnit }}</span>
                      }
                    </div>
                    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeLand(l); $event.stopPropagation()">
                      Remove
                    </button>
                  </div>
                  @if (expandedLandId() === l.landId) {
                    <div class="px-md pb-md pt-sm border-t border-neutral-200">
                      <app-land-detail-panel [workspaceId]="workspaceId" [landId]="l.landId" (deleted)="onLandDeleted(l.landId)" />
                    </div>
                  }
                </div>
              }
            </div>
          }
          <app-add-land-widget [workspaceId]="workspaceId" (added)="onLandAdded($event)" />
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Milestones</h2>
          @if (milestones().length > 0) {
            <div cdkDropList class="space-y-xs mb-md" (cdkDropListDropped)="onMilestoneDropped($event)">
              @for (m of milestones(); track m.milestoneId) {
                <div cdkDrag [cdkDragDisabled]="isClient()" class="flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50">
                  <div class="flex items-center gap-sm min-w-0">
                    @if (!isClient()) {
                      <span cdkDragHandle class="cursor-grab text-neutral-400 select-none flex-shrink-0">⠿</span>
                    }
                    <span class="flex-shrink-0">{{ milestoneStatusIcon(m.status) }}</span>
                    <div class="min-w-0">
                      <span class="text-sm text-neutral-900 truncate block">{{ m.title }}</span>
                      @if (m.description) {
                        <span class="text-xs text-neutral-500 truncate block">{{ m.description }}</span>
                      }
                    </div>
                  </div>
                  <div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">
                    @if (m.status === 'Completed') {
                      <span class="text-xs text-neutral-500">Completed {{ m.completedAt | date: 'mediumDate' }}</span>
                    } @else if (m.dueDate) {
                      <span class="text-xs text-neutral-500">Due: {{ m.dueDate | date: 'mediumDate' }}</span>
                    }
                    @if (isClient()) {
                      <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ m.status }}</span>
                    } @else {
                      <select class="input-field w-32 py-xs text-xs" [ngModel]="m.status" (ngModelChange)="onMilestoneStatusChange(m, $event)">
                        @for (s of milestoneStatuses; track s) {
                          <option [value]="s">{{ s }}</option>
                        }
                      </select>
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeMilestone(m)">
                        Remove
                      </button>
                    }
                  </div>
                </div>
              }
            </div>
          }
          @if (!isClient()) {
            @if (addingMilestone()) {
              <div class="rounded bg-neutral-50 p-md space-y-sm">
                <input class="input-field text-sm" placeholder="Title" [(ngModel)]="milestoneTitleDraft" />
                <textarea class="input-field text-sm" rows="2" placeholder="Description (optional)" [(ngModel)]="milestoneDescriptionDraft"></textarea>
                <input class="input-field text-sm" type="date" [(ngModel)]="milestoneDueDateDraft" />
                @if (milestoneError()) {
                  <p class="text-xs text-primary-500">{{ milestoneError() }}</p>
                }
                <div class="flex items-center justify-end gap-sm">
                  <button type="button" class="btn-secondary text-xs" (click)="cancelAddMilestone()">Cancel</button>
                  <button type="button" class="btn-primary text-xs" (click)="submitMilestone()">Add</button>
                </div>
              </div>
            } @else {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="addingMilestone.set(true)">
                + Add milestone
              </button>
            }
          }
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Billing</h2>

          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Invoices</h3>
          @if (jobInvoices().length === 0) {
            <p class="text-sm text-neutral-500 mb-md">No invoices linked to this job yet.</p>
          } @else {
            <div class="card p-0 overflow-x-auto mb-md">
              <table class="w-full text-sm">
                <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
                  <tr>
                    <th class="text-left px-lg py-sm font-medium">Number</th>
                    <th class="text-left px-lg py-sm font-medium">Total</th>
                    <th class="text-left px-lg py-sm font-medium">Status</th>
                  </tr>
                </thead>
                <tbody>
                  @for (invoice of jobInvoices(); track invoice.invoiceId) {
                    <tr class="border-t border-neutral-200">
                      <td class="px-lg py-sm text-neutral-900">{{ invoice.number }}</td>
                      <td class="px-lg py-sm text-neutral-600">{{ invoice.total | number: '1.2-2' }}</td>
                      <td class="px-lg py-sm text-neutral-600">{{ invoice.status }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
          <a class="text-xs text-primary-500 hover:text-primary-600" [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices']">
            Manage invoices →
          </a>

          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-xs mt-lg">Quotations</h3>
          @if (jobQuotations().length === 0) {
            <p class="text-sm text-neutral-500 mb-md">No quotations linked to this job yet.</p>
          } @else {
            <div class="card p-0 overflow-x-auto mb-md">
              <table class="w-full text-sm">
                <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
                  <tr>
                    <th class="text-left px-lg py-sm font-medium">Number</th>
                    <th class="text-left px-lg py-sm font-medium">Total</th>
                    <th class="text-left px-lg py-sm font-medium">Status</th>
                  </tr>
                </thead>
                <tbody>
                  @for (quotation of jobQuotations(); track quotation.quotationId) {
                    <tr class="border-t border-neutral-200">
                      <td class="px-lg py-sm text-neutral-900">{{ quotation.number }}</td>
                      <td class="px-lg py-sm text-neutral-600">{{ quotation.total | number: '1.2-2' }}</td>
                      <td class="px-lg py-sm text-neutral-600">{{ quotation.status }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
          <a class="text-xs text-primary-500 hover:text-primary-600" [routerLink]="['/app/workspace', workspaceId, 'billing', 'quotations']">
            Manage quotations →
          </a>
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Documents</h2>
          @if (documentRows().length > 0) {
            <div class="space-y-xs mb-md">
              @for (row of documentRows(); track (row.request?.requestId ?? row.document?.documentId)) {
                @if (row.kind === 'request' && (row.request!.status === 'Pending' || row.request!.status === 'Reopened')) {
                  <div class="rounded border border-dashed border-neutral-300">
                    <div class="flex items-center justify-between gap-sm px-md py-sm">
                      <div class="min-w-0">
                        <span class="text-sm text-neutral-900 truncate block">
                          {{ row.request!.status === 'Reopened' ? 'Needs re-upload: ' : 'Requested: ' }}{{ row.request!.title }}
                        </span>
                        @if (row.request!.description) {
                          <span class="text-xs text-neutral-500 block">{{ row.request!.description }}</span>
                        }
                        <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 mr-xs">{{ row.request!.category }}</span>
                        @if (row.request!.targetRole) {
                          <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">for {{ row.request!.targetRole }}</span>
                        } @else if (row.request!.targetUserName; as targetName) {
                          <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">for {{ targetName }}</span>
                        }
                      </div>
                      <div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">
                        <input #fulfillInput type="file" class="hidden" (change)="fulfillRequest(row.request!, fulfillInput.files); fulfillInput.value = ''" />
                        <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="fulfillInput.click()">Upload</button>
                        @if (!isClient()) {
                          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="startEditTarget(row.request!)">Edit target</button>
                          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="copyShareLink(row.request!)">Copy link</button>
                          @if (row.request!.hasActiveShareLink) {
                            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="revokeShareLink(row.request!)">Revoke link</button>
                          }
                          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="cancelRequest(row.request!)">Cancel</button>
                        }
                      </div>
                    </div>
                    @if (editingRequestTarget() === row.request!.requestId) {
                      <div class="px-md pb-md space-y-sm border-t border-neutral-200 pt-sm">
                        <select class="input-field text-sm" [(ngModel)]="requestTargetKind">
                          <option value="anyone">Anyone</option>
                          <option value="role">By role</option>
                          <option value="person">Specific person</option>
                        </select>
                        @if (requestTargetKind === 'role') {
                          <select class="input-field text-sm" [(ngModel)]="requestTargetRoleDraft">
                            <option value="Admin">Admin</option>
                            <option value="Surveyor">Surveyor</option>
                            <option value="Client">Client</option>
                          </select>
                        } @else if (requestTargetKind === 'person') {
                          <select class="input-field text-sm" [(ngModel)]="requestTargetUserIdDraft">
                            <option value="" disabled>Select a person</option>
                            @for (p of participants(); track p.userId) {
                              <option [value]="p.userId">{{ p.firstName }} {{ p.lastName }}</option>
                            }
                          </select>
                        }
                        <div class="flex items-center justify-end gap-sm">
                          <button type="button" class="btn-secondary text-xs" (click)="cancelEditTarget()">Cancel</button>
                          <button type="button" class="btn-primary text-xs" (click)="submitTargetEdit(row.request!)">Save</button>
                        </div>
                      </div>
                    }
                  </div>
                } @else if (row.document; as d) {
                  <div class="flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50">
                    <div class="min-w-0">
                      <span class="text-sm text-neutral-900 truncate block">{{ d.fileName }}</span>
                      <span class="text-xs text-neutral-500 block">
                        {{ d.uploadedByName }} · {{ d.createdAt | date: 'mediumDate' }} · {{ formatFileSize(d.fileSizeBytes) }}
                      </span>
                      <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 mr-xs">{{ documentIcon(d.contentType) }}</span>
                      <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 mr-xs">{{ d.category }}</span>
                      @if (!isClient()) {
                        <button
                          type="button"
                          class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 hover:bg-neutral-200 mr-xs"
                          (click)="toggleVisibility(d)"
                        >
                          {{ d.visibility }}
                        </button>
                      }
                      @if (row.request) {
                        <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">via request: {{ row.request!.title }}</span>
                      }
                    </div>
                    <div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="viewDocument(d)">View</button>
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="downloadDocument(d)">Download</button>
                      @if (!isClient() && row.request) {
                        <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="reopenRequest(row.request!)">Reopen</button>
                      }
                      @if (!isClient() && !row.request) {
                        <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDeleteDocument.set(d)">Remove</button>
                      }
                    </div>
                  </div>
                }
              }
            </div>
          }
          @if (documentError()) {
            <p class="text-xs text-primary-500 mb-sm">{{ documentError() }}</p>
          }
          <div class="flex flex-wrap items-center gap-md">
            <app-document-upload-widget
              [workspaceId]="workspaceId"
              [jobId]="jobId"
              [isClient]="isClient()"
              (added)="onDocumentAdded($event)"
            />
            @if (!isClient()) {
              @if (!requestingDocument()) {
                <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="requestingDocument.set(true)">
                  + Request document
                </button>
              }
            }
          </div>
          @if (requestError()) {
            <p class="text-xs text-primary-500 mt-sm">{{ requestError() }}</p>
          }
          @if (!isClient()) {
            @if (requestingDocument()) {
              <div class="rounded bg-neutral-50 p-md space-y-sm mt-sm">
                <input class="input-field text-sm" placeholder="What do you need? (e.g. Legal Deed)" [(ngModel)]="requestTitleDraft" />
                <textarea class="input-field text-sm" rows="2" placeholder="Description (optional)" [(ngModel)]="requestDescriptionDraft"></textarea>
                <select class="input-field text-sm" [(ngModel)]="requestCategoryDraft">
                  <option value="SurveyPlan">SurveyPlan</option>
                  <option value="LegalDocument">LegalDocument</option>
                  <option value="Photo">Photo</option>
                  <option value="Other">Other</option>
                </select>
                <select class="input-field text-sm" [(ngModel)]="requestTargetKind">
                  <option value="anyone">Anyone</option>
                  <option value="role">By role</option>
                  <option value="person">Specific person</option>
                </select>
                @if (requestTargetKind === 'role') {
                  <select class="input-field text-sm" [(ngModel)]="requestTargetRoleDraft">
                    <option value="Admin">Admin</option>
                    <option value="Surveyor">Surveyor</option>
                    <option value="Client">Client</option>
                  </select>
                } @else if (requestTargetKind === 'person') {
                  <select class="input-field text-sm" [(ngModel)]="requestTargetUserIdDraft">
                    <option value="" disabled>Select a person</option>
                    @for (p of uniqueParticipants(); track p.userId) {
                      <option [value]="p.userId">{{ p.firstName }} {{ p.lastName }}</option>
                    }
                  </select>
                }
                <div class="flex items-center justify-end gap-sm">
                  <button type="button" class="btn-secondary text-xs" (click)="cancelAddRequest()">Cancel</button>
                  <button type="button" class="btn-primary text-xs" (click)="submitRequest()">Request</button>
                </div>
              </div>
            }
          }
        </div>
      </div>
    }

    @if (viewingDocument(); as doc) {
      @if (viewingBlobUrl(); as url) {
        <app-document-viewer-modal [document]="doc" [blobUrl]="url" (closed)="closeViewer()" />
      }
    }

    @if (confirmingDeleteDocument(); as doc) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg">
        <div class="card w-full max-w-sm">
          <h2 class="text-base font-semibold text-neutral-900">Remove document?</h2>
          <p class="text-sm text-neutral-600 mt-xs">
            "{{ doc.fileName }}" will be removed. This can't be undone from here.
          </p>
          <div class="flex items-center justify-end gap-sm mt-lg">
            <button type="button" class="btn-secondary text-xs" (click)="confirmingDeleteDocument.set(null)">Cancel</button>
            <button type="button" class="btn-primary text-xs" (click)="deleteDocument(doc)">Remove</button>
          </div>
        </div>
      </div>
    }

    @if (confirmingLeave()) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg">
        <div class="card w-full max-w-sm">
          <h2 class="text-base font-semibold text-neutral-900">Unsaved changes</h2>
          <p class="text-sm text-neutral-600 mt-xs">
            You've edited the job title or description but haven't saved. What would you like to do?
          </p>
          <div class="flex flex-col gap-sm mt-lg">
            <button type="button" class="btn-primary" (click)="saveAndLeave()">Save and leave</button>
            <button type="button" class="btn-secondary" (click)="discardAndLeave()">Discard changes</button>
            <button type="button" class="btn-secondary" (click)="stayOnPage()">Keep editing</button>
          </div>
        </div>
      </div>
    }
  `
})
export class JobDetailComponent implements OnInit, HasUnsavedChanges {
  @ViewChild('personModal') personModal?: AddJobPersonModalComponent;

  workspaceId = '';
  jobId = '';
  job = signal<Job | null>(null);
  participants = signal<JobParticipant[]>([]);
  effectiveParticipants = signal<JobParticipant[]>([]);
  pendingInvitations = signal<JobInvitation[]>([]);
  addPersonModalOpen = signal(false);
  /** One entry per person, for pickers that target a person rather than a specific role-grant. */
  uniqueParticipants = computed(() => {
    const seen = new Set<string>();
    return this.participants().filter(p => (seen.has(p.userId) ? false : (seen.add(p.userId), true)));
  });
  /**
   * One row per person, merging their direct job roles (removable, per-role) and any
   * workspace-wide access (read-only badge, e.g. Admin) - the same person can hold both if
   * they were also explicitly assigned to this job on top of their blanket access.
   */
  jobPeopleRows = computed(() => {
    const byUser = new Map<string, { userId: string; firstName: string; lastName: string; directRoles: string[]; workspaceWideRoles: string[] }>();
    for (const p of this.effectiveParticipants()) {
      let row = byUser.get(p.userId);
      if (!row) {
        row = { userId: p.userId, firstName: p.firstName, lastName: p.lastName, directRoles: [], workspaceWideRoles: [] };
        byUser.set(p.userId, row);
      }
      if (p.accessType === 'WorkspaceWide') row.workspaceWideRoles.push(p.role);
      else row.directRoles.push(p.role);
    }
    return [...byUser.values()];
  });
  lands = signal<Land[]>([]);
  milestones = signal<Milestone[]>([]);
  milestoneStatuses = MILESTONE_STATUSES;
  documents = signal<Document[]>([]);
  viewingDocument = signal<Document | null>(null);
  viewingBlobUrl = signal<string | null>(null);
  confirmingDeleteDocument = signal<Document | null>(null);
  documentError = signal('');
  documentRequests = signal<DocumentRequest[]>([]);
  jobInvoices = signal<Invoice[]>([]);
  jobQuotations = signal<Quotation[]>([]);
  requestingDocument = signal(false);
  requestTitleDraft = '';
  requestDescriptionDraft = '';
  requestCategoryDraft = 'Other';
  requestTargetKind: 'anyone' | 'role' | 'person' = 'anyone';
  requestTargetRoleDraft = 'Client';
  requestTargetUserIdDraft = '';
  requestError = signal('');
  editingRequestTarget = signal<string | null>(null);
  shareLinkTokens = signal<Record<string, string>>({});

  documentRows = computed(() => {
    const requests = this.documentRequests();
    const linkedDocIds = new Set(requests.filter(r => r.fulfilledDocumentId).map(r => r.fulfilledDocumentId));

    const plainDocRows = this.documents()
      .filter(d => !linkedDocIds.has(d.documentId))
      .map(d => ({ kind: 'document' as const, document: d, request: null as DocumentRequest | null, createdAt: d.createdAt }));

    const requestRows = requests.map(r => ({
      kind: 'request' as const,
      document: r.fulfilledDocumentId ? this.documents().find(d => d.documentId === r.fulfilledDocumentId) ?? null : null,
      request: r,
      createdAt: r.createdAt
    }));

    return [...plainDocRows, ...requestRows].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  });
  addingMilestone = signal(false);
  milestoneTitleDraft = '';
  milestoneDescriptionDraft = '';
  milestoneDueDateDraft = '';
  milestoneError = signal('');
  loading = signal(true);
  savingHeader = signal(false);
  headerError = signal('');
  error = signal('');
  statuses = STATUSES;
  titleDraft = '';
  descriptionDraft = '';

  addressLine = addressLine;
  expandedLandId = signal<string | null>(null);
  confirmingLeave = signal(false);
  private leaveDecision: Subject<boolean> | null = null;

  toggleLand(landId: string): void {
    this.expandedLandId.update(current => (current === landId ? null : landId));
  }

  constructor(
    private jobService: JobService,
    private milestoneService: MilestoneService,
    private documentService: DocumentService,
    private documentRequestService: DocumentRequestService,
    private invoiceService: InvoiceService,
    private quotationService: QuotationService,
    private currentWorkspace: CurrentWorkspaceService,
    private authService: AuthService,
    private route: ActivatedRoute
  ) {}

  /**
   * Client is job-scoped now, not a workspace role - derive it from this job's own
   * participants list (already loaded) by finding the caller's own entry, rather than the
   * workspace-wide role which no longer applies at this level.
   */
  isClient(): boolean {
    const myId = this.authService.getCurrentUserId();
    const me = this.participants().find(p => p.userId === myId);
    return me?.role === 'Client';
  }

  milestoneStatusIcon(status: string): string {
    return status === 'Completed' ? '✓' : status === 'InProgress' ? '◐' : '○';
  }

  ngOnInit(): void {
    // Falls back to the route param for /app/job/:workspaceId/:jobId (job-only access, no
    // CurrentWorkspaceService set - see jobAccessGuard) - the normal workspace-shell route
    // still resolves via CurrentWorkspaceService as before.
    this.workspaceId = this.currentWorkspace.current()?.workspaceId
      ?? this.route.snapshot.paramMap.get('workspaceId') ?? '';
    this.jobId = this.route.snapshot.paramMap.get('jobId') ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      job: this.jobService.getById(this.workspaceId, this.jobId),
      participants: this.jobService.getParticipants(this.workspaceId, this.jobId),
      effectiveParticipants: this.jobService.getEffectiveParticipants(this.workspaceId, this.jobId),
      pendingInvitations: this.jobService.getPendingInvitations(this.workspaceId, this.jobId),
      lands: this.jobService.getLands(this.workspaceId, this.jobId),
      milestones: this.milestoneService.list(this.workspaceId, this.jobId),
      documents: this.documentService.list(this.workspaceId, this.jobId),
      documentRequests: this.documentRequestService.list(this.workspaceId, this.jobId),
      invoices: this.invoiceService.search(this.workspaceId),
      quotations: this.quotationService.search(this.workspaceId)
    }).subscribe({
      next: ({ job, participants, effectiveParticipants, pendingInvitations, lands, milestones, documents, documentRequests, invoices, quotations }) => {
        this.job.set(job);
        this.titleDraft = job.title;
        this.descriptionDraft = job.description ?? '';
        this.participants.set(participants);
        this.effectiveParticipants.set(effectiveParticipants);
        this.pendingInvitations.set(pendingInvitations);
        this.lands.set(lands);
        this.milestones.set(milestones);
        this.documents.set(documents);
        this.documentRequests.set(documentRequests);
        this.jobInvoices.set(invoices.filter(i => i.jobId === this.jobId));
        this.jobQuotations.set(quotations.filter(q => q.jobId === this.jobId));
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load job.');
        this.loading.set(false);
      }
    });
  }

  headerDirty(): boolean {
    const current = this.job();
    if (!current) return false;
    return this.titleDraft.trim() !== current.title || (this.descriptionDraft.trim() || null) !== current.description;
  }

  discardHeader(): void {
    const current = this.job();
    if (!current) return;
    this.titleDraft = current.title;
    this.descriptionDraft = current.description ?? '';
    this.headerError.set('');
  }

  saveHeader(onSaved?: () => void): void {
    const current = this.job();
    if (!current || !this.headerDirty()) return;
    if (!this.titleDraft.trim()) {
      this.headerError.set('Title is required.');
      return;
    }

    this.headerError.set('');
    this.savingHeader.set(true);
    this.jobService
      .update(this.workspaceId, this.jobId, { title: this.titleDraft.trim(), description: this.descriptionDraft.trim() || null })
      .subscribe({
        next: (job) => {
          this.job.set(job);
          this.titleDraft = job.title;
          this.descriptionDraft = job.description ?? '';
          this.savingHeader.set(false);
          onSaved?.();
        },
        error: (err) => {
          this.savingHeader.set(false);
          this.headerError.set(err.error?.message ?? 'Could not save changes.');
        }
      });
  }

  /** Router guard hook - pauses navigation until the user picks Save/Discard/Stay. */
  canDeactivate(): boolean | Observable<boolean> {
    if (!this.headerDirty()) return true;

    this.confirmingLeave.set(true);
    this.leaveDecision = new Subject<boolean>();
    return this.leaveDecision.asObservable();
  }

  saveAndLeave(): void {
    this.saveHeader(() => this.resolveLeave(true));
  }

  discardAndLeave(): void {
    this.discardHeader();
    this.resolveLeave(true);
  }

  stayOnPage(): void {
    this.resolveLeave(false);
  }

  private resolveLeave(allow: boolean): void {
    this.confirmingLeave.set(false);
    this.leaveDecision?.next(allow);
    this.leaveDecision?.complete();
    this.leaveDecision = null;
  }

  onStatusChange(status: string): void {
    const current = this.job();
    if (!current || current.status === status) return;
    const previous = current.status;
    this.job.set({ ...current, status });

    this.jobService.updateStatus(this.workspaceId, this.jobId, status).subscribe({
      error: (err) => {
        this.job.set({ ...current, status: previous });
        this.error.set(err.error?.message ?? 'Could not change status.');
      }
    });
  }

  personMessage = signal('');

  private refreshParticipants(): void {
    this.jobService.getParticipants(this.workspaceId, this.jobId).subscribe(p => this.participants.set(p));
    this.jobService.getEffectiveParticipants(this.workspaceId, this.jobId).subscribe(p => this.effectiveParticipants.set(p));
  }

  private refreshPendingInvitations(): void {
    this.jobService.getPendingInvitations(this.workspaceId, this.jobId).subscribe(inv => this.pendingInvitations.set(inv));
  }

  onPersonAdded({ person, role }: PersonWithRole): void {
    this.jobService.addParticipant(this.workspaceId, this.jobId, person.userId, role).subscribe({
      next: (result) => {
        this.personModal?.markAdded();
        this.addPersonModalOpen.set(false);
        if (result.status === 'invited') {
          this.personMessage.set(`Invitation sent to ${person.name} - pending acceptance.`);
          this.refreshPendingInvitations();
        } else {
          this.personMessage.set('');
          this.refreshParticipants();
        }
      },
      error: (err) => this.personModal?.markFailed(err.error?.message ?? 'Could not add person.')
    });
  }

  onPersonInvited({ email, firstName, lastName, phone, role }: InviteByEmail): void {
    this.jobService.inviteParticipant(this.workspaceId, this.jobId, role, email, firstName, lastName, phone).subscribe({
      next: () => {
        this.personModal?.markAdded();
        this.addPersonModalOpen.set(false);
        this.personMessage.set(`Invitation sent to ${email} - pending acceptance.`);
        this.refreshPendingInvitations();
      },
      error: (err) => this.personModal?.markFailed(err.error?.message ?? 'Could not send invitation.')
    });
  }

  removeParticipant(p: { userId: string; role: string }): void {
    this.jobService.removeParticipant(this.workspaceId, this.jobId, p.userId, p.role).subscribe({
      next: () => {
        this.participants.update(list => list.filter(x => !(x.userId === p.userId && x.role === p.role)));
        this.effectiveParticipants.update(list => list.filter(x => !(x.userId === p.userId && x.role === p.role && x.accessType === 'Direct')));
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not remove participant.')
    });
  }

  /** Roles this job scope allows that the person doesn't already hold - mirrors WorkspaceService.GetEligibleRoleNames('Job'). */
  private readonly allJobRoles = ['Surveyor', 'Client'];
  jobRoleOptions(heldRoles: string[]): string[] {
    return this.allJobRoles.filter(r => !heldRoles.includes(r));
  }

  addRoleToParticipant(userId: string, role: string): void {
    if (!role) return;
    this.jobService.addParticipant(this.workspaceId, this.jobId, userId, role).subscribe({
      next: () => this.refreshParticipants(),
      error: (err) => this.error.set(err.error?.message ?? 'Could not add role.')
    });
  }

  onLandAdded(land: Land): void {
    this.jobService.addLand(this.workspaceId, this.jobId, land.landId).subscribe({
      next: () => this.lands.update(list => (list.some(l => l.landId === land.landId) ? list : [...list, land])),
      error: (err) => this.error.set(err.error?.message ?? 'Could not attach land.')
    });
  }

  removeLand(land: Land): void {
    this.jobService.removeLand(this.workspaceId, this.jobId, land.landId).subscribe({
      next: () => this.lands.update(list => list.filter(l => l.landId !== land.landId)),
      error: (err) => this.error.set(err.error?.message ?? 'Could not remove land.')
    });
  }

  /** The land record itself was deleted (not just unlinked) - drop it locally, no separate unlink call needed. */
  onLandDeleted(landId: string): void {
    this.lands.update(list => list.filter(l => l.landId !== landId));
    this.expandedLandId.set(null);
  }

  cancelAddMilestone(): void {
    this.addingMilestone.set(false);
    this.milestoneTitleDraft = '';
    this.milestoneDescriptionDraft = '';
    this.milestoneDueDateDraft = '';
    this.milestoneError.set('');
  }

  submitMilestone(): void {
    if (!this.milestoneTitleDraft.trim()) {
      this.milestoneError.set('Title is required.');
      return;
    }
    this.milestoneService
      .create(this.workspaceId, this.jobId, {
        title: this.milestoneTitleDraft.trim(),
        description: this.milestoneDescriptionDraft.trim() || null,
        dueDate: this.milestoneDueDateDraft || null
      })
      .subscribe({
        next: (milestone) => {
          this.milestones.update(list => [...list, milestone]);
          this.cancelAddMilestone();
        },
        error: (err) => this.milestoneError.set(err.error?.message ?? 'Could not add milestone.')
      });
  }

  onMilestoneStatusChange(milestone: Milestone, status: string): void {
    if (milestone.status === status) return;
    const previous = milestone.status;
    this.milestones.update(list => list.map(m => (m.milestoneId === milestone.milestoneId ? { ...m, status } : m)));

    this.milestoneService.updateStatus(this.workspaceId, this.jobId, milestone.milestoneId, status).subscribe({
      next: (updated) => this.milestones.update(list => list.map(m => (m.milestoneId === updated.milestoneId ? updated : m))),
      error: (err) => {
        this.milestones.update(list => list.map(m => (m.milestoneId === milestone.milestoneId ? { ...m, status: previous } : m)));
        this.error.set(err.error?.message ?? 'Could not change milestone status.');
      }
    });
  }

  removeMilestone(milestone: Milestone): void {
    this.milestoneService.delete(this.workspaceId, this.jobId, milestone.milestoneId).subscribe({
      next: () => this.milestones.update(list => list.filter(m => m.milestoneId !== milestone.milestoneId)),
      error: (err) => this.error.set(err.error?.message ?? 'Could not remove milestone.')
    });
  }

  onMilestoneDropped(event: CdkDragDrop<Milestone[]>): void {
    if (event.previousIndex === event.currentIndex) return;
    const previous = this.milestones();
    const reordered = [...previous];
    moveItemInArray(reordered, event.previousIndex, event.currentIndex);
    this.milestones.set(reordered);

    this.milestoneService.reorder(this.workspaceId, this.jobId, reordered.map(m => m.milestoneId)).subscribe({
      next: (updated) => this.milestones.set(updated),
      error: (err) => {
        this.milestones.set(previous);
        this.error.set(err.error?.message ?? 'Could not reorder milestones.');
      }
    });
  }

  onDocumentAdded(doc: Document): void {
    this.documents.update(list => [doc, ...list]);
  }

  viewDocument(doc: Document): void {
    this.documentError.set('');
    this.documentService.getFileBlob(this.workspaceId, this.jobId, doc.documentId).subscribe({
      next: (blob) => {
        this.viewingDocument.set(doc);
        this.viewingBlobUrl.set(URL.createObjectURL(blob));
      },
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not open document.')
    });
  }

  closeViewer(): void {
    const url = this.viewingBlobUrl();
    if (url) URL.revokeObjectURL(url);
    this.viewingDocument.set(null);
    this.viewingBlobUrl.set(null);
  }

  downloadDocument(doc: Document): void {
    this.documentError.set('');
    this.documentService.getFileBlob(this.workspaceId, this.jobId, doc.documentId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = window.document.createElement('a');
        link.href = url;
        link.download = doc.fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not download document.')
    });
  }

  deleteDocument(doc: Document): void {
    this.documentService.delete(this.workspaceId, this.jobId, doc.documentId).subscribe({
      next: () => {
        this.documents.update(list => list.filter(d => d.documentId !== doc.documentId));
        this.confirmingDeleteDocument.set(null);
      },
      error: (err) => {
        this.documentError.set(err.error?.message ?? 'Could not remove document.');
        this.confirmingDeleteDocument.set(null);
      }
    });
  }

  submitRequest(): void {
    if (!this.requestTitleDraft.trim()) {
      this.requestError.set('Title is required.');
      return;
    }
    if (this.requestTargetKind === 'person' && !this.requestTargetUserIdDraft) {
      this.requestError.set('Select a person to target, or switch to Anyone/By role.');
      return;
    }
    this.requestError.set('');

    const targetRole = this.requestTargetKind === 'role' ? this.requestTargetRoleDraft : null;
    const targetUserId = this.requestTargetKind === 'person' ? this.requestTargetUserIdDraft : null;

    this.documentRequestService
      .create(this.workspaceId, this.jobId, this.requestTitleDraft.trim(), this.requestDescriptionDraft.trim() || null, this.requestCategoryDraft, targetRole, targetUserId)
      .subscribe({
        next: (request) => {
          this.documentRequests.update(list => [request, ...list]);
          this.cancelAddRequest();
        },
        error: (err) => this.requestError.set(err.error?.message ?? 'Could not create request.')
      });
  }

  cancelAddRequest(): void {
    this.requestingDocument.set(false);
    this.requestTitleDraft = '';
    this.requestDescriptionDraft = '';
    this.requestCategoryDraft = 'Other';
    this.requestTargetKind = 'anyone';
    this.requestTargetRoleDraft = 'Client';
    this.requestTargetUserIdDraft = '';
    this.requestError.set('');
  }

  copyShareLink(request: DocumentRequest): void {
    // Reuse the cached token if we already have one for this request - a second click on
    // "Copy link" should just re-copy the same URL, not silently mint a new one and
    // invalidate whatever was already shared. Only regenerate when we have no token in
    // hand (first click, or the page was reloaded since the last generate).
    const cached = this.shareLinkTokens()[request.requestId];
    if (cached) {
      navigator.clipboard.writeText(`${window.location.origin}/document-upload/${cached}`);
      return;
    }

    this.documentRequestService.generateShareLink(this.workspaceId, this.jobId, request.requestId).subscribe({
      next: ({ token }) => {
        this.shareLinkTokens.update(map => ({ ...map, [request.requestId]: token }));
        this.documentRequests.update(list => list.map(r => (r.requestId === request.requestId ? { ...r, hasActiveShareLink: true } : r)));
        navigator.clipboard.writeText(`${window.location.origin}/document-upload/${token}`);
        this.requestError.set('');
      },
      error: (err) => this.requestError.set(err.error?.message ?? 'Could not generate link.')
    });
  }

  revokeShareLink(request: DocumentRequest): void {
    this.documentRequestService.revokeShareLink(this.workspaceId, this.jobId, request.requestId).subscribe({
      next: () => {
        this.shareLinkTokens.update(map => {
          const { [request.requestId]: _removed, ...rest } = map;
          return rest;
        });
        this.documentRequests.update(list => list.map(r => (r.requestId === request.requestId ? { ...r, hasActiveShareLink: false } : r)));
      },
      error: (err) => this.requestError.set(err.error?.message ?? 'Could not revoke link.')
    });
  }

  startEditTarget(request: DocumentRequest): void {
    this.requestTargetKind = request.targetRole ? 'role' : request.targetUserId ? 'person' : 'anyone';
    this.requestTargetRoleDraft = request.targetRole ?? 'Client';
    this.requestTargetUserIdDraft = request.targetUserId ?? '';
    this.requestError.set('');
    this.editingRequestTarget.set(request.requestId);
  }

  cancelEditTarget(): void {
    this.editingRequestTarget.set(null);
    this.requestTargetKind = 'anyone';
    this.requestTargetRoleDraft = 'Client';
    this.requestTargetUserIdDraft = '';
    this.requestError.set('');
  }

  submitTargetEdit(request: DocumentRequest): void {
    if (this.requestTargetKind === 'person' && !this.requestTargetUserIdDraft) {
      this.requestError.set('Select a person to target, or switch to Anyone/By role.');
      return;
    }
    const targetRole = this.requestTargetKind === 'role' ? this.requestTargetRoleDraft : null;
    const targetUserId = this.requestTargetKind === 'person' ? this.requestTargetUserIdDraft : null;

    this.requestError.set('');
    this.documentRequestService.updateTarget(this.workspaceId, this.jobId, request.requestId, targetRole, targetUserId).subscribe({
      next: (updated) => {
        this.documentRequests.update(list => list.map(r => (r.requestId === updated.requestId ? updated : r)));
        this.cancelEditTarget();
      },
      error: (err) => this.requestError.set(err.error?.message ?? 'Could not update target.')
    });
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  documentIcon(contentType: string): string {
    if (contentType === 'application/pdf') return 'PDF';
    if (contentType.startsWith('image/')) return 'IMG';
    if (contentType.includes('word')) return 'DOC';
    if (contentType.includes('sheet') || contentType.includes('excel')) return 'XLS';
    return 'FILE';
  }

  toggleVisibility(doc: Document): void {
    const next = doc.visibility === 'Internal' ? 'ClientVisible' : 'Internal';
    this.documentError.set('');
    this.documentService.updateVisibility(this.workspaceId, this.jobId, doc.documentId, next).subscribe({
      next: (updated) => this.documents.update(list => list.map(d => (d.documentId === updated.documentId ? updated : d))),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not update visibility.')
    });
  }

  fulfillRequest(request: DocumentRequest, files: FileList | null): void {
    const file = files?.item(0);
    if (!file) return;
    const visibility = this.isClient() ? 'ClientVisible' : 'Internal';
    const displayFileName = prompt('File name', file.name) ?? undefined;

    this.documentError.set('');
    this.documentRequestService.fulfill(this.workspaceId, this.jobId, request.requestId, file, visibility, displayFileName).subscribe({
      next: (fulfilled) => {
        this.documentRequests.update(list => list.map(r => (r.requestId === fulfilled.requestId ? fulfilled : r)));
        this.documentService.list(this.workspaceId, this.jobId).subscribe(documents => this.documents.set(documents));
      },
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not upload document.')
    });
  }

  reopenRequest(request: DocumentRequest): void {
    const note = prompt('Note for the re-upload (optional)', request.description ?? '');
    if (note === null) return;

    this.documentRequestService.reopen(this.workspaceId, this.jobId, request.requestId, note).subscribe({
      next: (reopened) => this.documentRequests.update(list => list.map(r => (r.requestId === reopened.requestId ? reopened : r))),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not reopen request.')
    });
  }

  cancelRequest(request: DocumentRequest): void {
    this.documentRequestService.cancel(this.workspaceId, this.jobId, request.requestId).subscribe({
      next: () => this.documentRequests.update(list => list.filter(r => r.requestId !== request.requestId)),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not cancel request.')
    });
  }
}
