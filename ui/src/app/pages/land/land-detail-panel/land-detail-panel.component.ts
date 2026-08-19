import { Component, EventEmitter, Input, OnInit, Output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { Land, LandAddress, LandAreaValue, LandBoundary, LandDeed, LandMapPoint, LandPhoto, LandService, LandSurvey, OwnedDocument, telHref, toAreaRequest, whatsAppHref } from '../../../core/land.service';
import { LandDocumentRequest, LandDocumentRequestService } from '../../../core/land-document-request.service';
import { OwnerPickerComponent, OwnerValue } from '../owner-picker/owner-picker.component';
import { LandLocationPickerComponent } from '../../../shared/land-location-picker/land-location-picker.component';
import { LandLocationQrComponent } from '../../../shared/land-location-qr/land-location-qr.component';
import { LandAreaInputComponent } from '../../../shared/land-area-input/land-area-input.component';
import { DocumentListComponent, DocRow } from '../../../shared/document-list/document-list.component';
import { DocumentUploadButtonComponent } from '../../../shared/document-upload-button/document-upload-button.component';
import { DocumentRequestFormComponent, DocumentRequestFormValue } from '../../../shared/document-request-form/document-request-form.component';
import { DocumentViewerModalComponent } from '../../../shared/document-viewer-modal/document-viewer-modal.component';
import { IconComponent } from '../../../shared/icon/icon.component';
import { PROVINCES, DISTRICTS_BY_PROVINCE, provinceForDistrict } from '../../../shared/sri-lanka-locations';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-land-detail-panel',
  standalone: true,
  imports: [
    CommonModule, FormsModule, OwnerPickerComponent, LandLocationPickerComponent, LandLocationQrComponent,
    LandAreaInputComponent, DocumentListComponent, DocumentUploadButtonComponent, DocumentRequestFormComponent, DocumentViewerModalComponent, IconComponent, RouterLink
  ],
  template: `
    @if (loading()) {
      <p class="text-sm text-neutral-500">Loading…</p>
    } @else if (error()) {
      <div class="text-sm text-primary-500">
        {{ error() }}
        <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
      </div>
    } @else {
      <div class="space-y-lg">
        <div>
          <div class="flex items-center justify-between mb-sm gap-sm">
            <div class="flex items-center gap-sm">
              <h3 class="text-xs font-semibold text-neutral-500 uppercase">{{ isCreateMode ? 'New land' : 'Details' }}</h3>
              @if (!isCreateMode && detailsDirty()) {
                <span class="text-xs text-amber-600">Unsaved changes</span>
              }
            </div>
            @if (isCreateMode) {
              <!-- No dirty/delete/print controls yet - the record doesn't exist until Create land below succeeds. -->
            } @else if (detailsDirty()) {
              <span class="flex items-center gap-sm">
                @if (detailsError()) {
                  <span class="text-xs text-primary-500">{{ detailsError() }}</span>
                }
                <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" [disabled]="savingDetails()" (click)="discardDetails()">
                  Discard
                </button>
                <button type="button" class="text-xs text-primary-500 hover:text-primary-600 font-medium" [disabled]="savingDetails()" (click)="saveDetails()">
                  {{ savingDetails() ? 'Saving…' : 'Save changes' }}
                </button>
              </span>
            } @else if (confirmingDelete()) {
              <span class="text-xs text-neutral-600">
                Delete this land record?
                <button type="button" class="text-primary-500 font-medium ml-xs" [disabled]="deleting()" (click)="confirmDelete()">
                  {{ deleting() ? 'Deleting…' : 'Yes' }}
                </button>
                <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDelete.set(false)">No</button>
              </span>
            } @else {
              <span class="flex items-center gap-sm">
                <a
                  class="text-xs text-neutral-500 hover:text-neutral-700"
                  [routerLink]="['/app/workspace', workspaceId, 'lands', landId, 'print']"
                >
                  Print summary
                </a>
                <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDelete.set(true)">
                  Delete
                </button>
              </span>
            }
          </div>
          <div class="grid grid-cols-2 gap-sm">
            <input class="input-field" placeholder="Village" [(ngModel)]="village" />
            <input class="input-field" placeholder="Grama Niladhari Division" [(ngModel)]="gramaNiladhariDivision" />
            <input class="input-field" placeholder="Divisional Secretariat" [(ngModel)]="divisionalSecretariat" />
            <input class="input-field" placeholder="Pradeshiya Sabha" [(ngModel)]="pradeshiyaSabha" />
            <input class="input-field" placeholder="Korale" [(ngModel)]="korale" />
            <input class="input-field" placeholder="Hatpattu" [(ngModel)]="hatpattu" />
            <select class="input-field" [ngModel]="district" (ngModelChange)="onDistrictChange($event)">
              <option value="">—</option>
              @for (d of districtOptions; track d) {
                <option [value]="d">{{ d }}</option>
              }
            </select>
            <select class="input-field" [ngModel]="province" (ngModelChange)="onProvinceChange($event)">
              <option value="">—</option>
              @for (p of provinces; track p) {
                <option [value]="p">{{ p }}</option>
              }
            </select>
          </div>
          <div class="mt-sm">
            <app-land-area-input [value]="area" (valueChange)="onAreaChange($event)" />
          </div>
          <textarea class="input-field mt-sm" rows="2" placeholder="Notes" [(ngModel)]="notes"></textarea>

          <div class="mt-sm">
            <app-owner-picker
              [value]="owner"
              [initialAccountLabel]="ownerLabel"
              (valueChange)="onOwnerChange($event)"
            />
          </div>
          @if (land()?.ownerPhone) {
            <div class="flex gap-md mt-xs text-xs">
              <a [href]="telHref(land()!.ownerPhone!)" class="text-primary-600 hover:text-primary-700">Call {{ land()!.ownerPhone }}</a>
              <a [href]="whatsAppHref(land()!.ownerPhone!)" target="_blank" rel="noopener" class="text-primary-600 hover:text-primary-700">WhatsApp</a>
            </div>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Location</h3>
          @if (isCreateMode) {
            @if (pendingLat !== null) {
              <p class="text-sm text-neutral-900 mb-sm">{{ pendingLat }}, {{ pendingLng }}</p>
            }
            <app-land-location-picker
              [initialLat]="pendingLat"
              [initialLng]="pendingLng"
              heightClass="h-56"
              (pointAdded)="onPendingLocationChosen($event)"
            />
            <p class="text-xs text-neutral-500 mt-xs">
              {{ pendingLat !== null ? 'Location set - it will be saved with the land record. Add named points once the record is created.' : 'Optional - click the map to place a pin, or set it later.' }}
            </p>
          } @else {
            <app-land-location-picker
              heightClass="h-64"
              [markers]="mapPointMarkers()"
              [pendingPoint]="pendingNewPoint()"
              (pointAdded)="onMapClicked($event)"
              (pointMoved)="onMapPointMoved($event)"
            />
            @if (pendingNewPoint()) {
              <div class="flex gap-sm mt-sm">
                <input class="input-field flex-1" placeholder="Name this point (e.g. North gate)" [(ngModel)]="pendingNewPointName" (keydown.enter)="saveNewPoint()" />
                <button type="button" class="btn-primary" [disabled]="!pendingNewPointName.trim()" (click)="saveNewPoint()">Add</button>
                <button type="button" class="btn-secondary" (click)="pendingNewPoint.set(null)">Cancel</button>
              </div>
            }
            @if (mapPoints().length > 0) {
              <div class="space-y-xs mt-sm">
                @for (p of mapPoints(); track p.id) {
                  <div class="px-md py-sm rounded bg-neutral-50 text-sm">
                    <div class="flex items-center justify-between gap-sm">
                      <div class="min-w-0 cursor-pointer" (click)="toggleExpandedMapPoint(p.id)">
                        @if (renamingMapPointId() === p.id) {
                          <input class="input-field text-xs px-xs py-xs" [(ngModel)]="renameMapPointValue" (keydown.enter)="confirmRenameMapPoint(p.id)" (click)="$event.stopPropagation()" />
                        } @else {
                          <span class="text-neutral-900">{{ p.name }}</span>
                          <span class="text-neutral-500 block text-xs">{{ p.latitude | number: '1.6-6' }}, {{ p.longitude | number: '1.6-6' }}</span>
                        }
                      </div>
                      @if (confirmingDeleteMapPointId() === p.id) {
                        <span class="text-xs text-neutral-600 whitespace-nowrap">
                          Delete?
                          <button type="button" class="text-primary-500 font-medium ml-xs" (click)="deleteMapPoint(p.id)">Yes</button>
                          <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteMapPointId.set(null)">No</button>
                        </span>
                      } @else if (renamingMapPointId() === p.id) {
                        <span class="whitespace-nowrap">
                          <button type="button" class="text-xs text-primary-600 font-medium" (click)="confirmRenameMapPoint(p.id)">Save</button>
                          <button type="button" class="text-xs text-neutral-500 ml-sm" (click)="renamingMapPointId.set(null)">Cancel</button>
                        </span>
                      } @else {
                        <span class="flex items-center gap-xs flex-shrink-0">
                          <a [href]="googleMapsUrl(p.latitude, p.longitude)" target="_blank" rel="noopener" class="icon-btn" title="Open in Google Maps" (click)="$event.stopPropagation()">
                            <app-icon name="link" />
                          </a>
                          <button type="button" class="icon-btn" [title]="copiedMapPointId() === p.id ? 'Copied!' : 'Copy Google Maps link'" (click)="copyMapPointLink(p); $event.stopPropagation()">
                            <app-icon name="copy" />
                          </button>
                          <button type="button" class="icon-btn" title="Show QR code" (click)="toggleExpandedMapPoint(p.id)">
                            <app-icon name="qr" />
                          </button>
                          <button type="button" class="icon-btn" title="Rename" (click)="startRenameMapPoint(p); $event.stopPropagation()">
                            <app-icon name="rename" />
                          </button>
                          <button type="button" class="icon-btn text-primary-500" title="Delete" (click)="confirmingDeleteMapPointId.set(p.id); $event.stopPropagation()">
                            <app-icon name="delete" />
                          </button>
                        </span>
                      }
                    </div>
                    @if (expandedMapPointId() === p.id) {
                      <div class="mt-sm pt-sm border-t border-neutral-200">
                        <app-land-location-qr [lat]="p.latitude" [lng]="p.longitude" [sizePx]="120" />
                      </div>
                    }
                  </div>
                }
              </div>
            }
            <div class="flex flex-wrap gap-md mt-sm">
              <div class="flex items-center gap-sm">
                <span class="text-xs text-neutral-500">Add-a-point link:</span>
                @if (land()?.hasActiveLocationShareLink) {
                  <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyShareLink()">
                    {{ shareLinkCopied() ? 'Copied!' : 'Copy link' }}
                  </button>
                  <button type="button" class="icon-btn" title="Regenerate link" (click)="regenerateShareLink()"><app-icon name="reopen" /></button>
                  <button type="button" class="icon-btn text-primary-500" title="Revoke link" (click)="revokeShareLink()"><app-icon name="delete" /></button>
                } @else {
                  <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyShareLink()">Create link</button>
                }
              </div>
              <div class="flex items-center gap-sm">
                <span class="text-xs text-neutral-500">View-map link:</span>
                @if (land()?.hasActiveMapViewShareLink) {
                  <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyMapViewShareLink()">
                    {{ mapViewShareLinkCopied() ? 'Copied!' : 'Copy link' }}
                  </button>
                  <button type="button" class="icon-btn" title="Regenerate link" (click)="regenerateMapViewShareLink()"><app-icon name="reopen" /></button>
                  <button type="button" class="icon-btn text-primary-500" title="Revoke link" (click)="revokeMapViewShareLink()"><app-icon name="delete" /></button>
                } @else {
                  <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyMapViewShareLink()">Create link</button>
                }
              </div>
            </div>
          }
          @if (locationError()) {
            <p class="text-xs text-primary-500 mt-xs">{{ locationError() }}</p>
          }
          @if (mapPointError()) {
            <p class="text-xs text-primary-500 mt-xs">{{ mapPointError() }}</p>
          }
        </div>

        @if (isCreateMode) {
          <div>
            @if (createError()) {
              <p class="text-sm text-primary-500 mb-sm">{{ createError() }}</p>
            }
            <button type="button" class="btn-primary" [disabled]="!village.trim() || creating()" (click)="createLand()">
              {{ creating() ? 'Creating…' : 'Create land' }}
            </button>
          </div>
        }

        @if (!isCreateMode) {
        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Surveys</h3>
          @if (surveys().length > 0) {
            <div class="space-y-xs mb-sm">
              @for (s of surveys(); track s.id) {
                <div class="px-md py-sm rounded bg-neutral-50 text-sm">
                  <div class="flex items-center justify-between">
                    <div>
                      <span class="text-neutral-900">{{ s.surveyPlanNumber }}</span>
                      <span class="text-neutral-500"> · {{ s.surveyDate | date: 'mediumDate' }}</span>
                      @if (s.surveyedByName) {
                        <span class="text-neutral-500"> · {{ s.surveyedByName }}</span>
                      }
                    </div>
                    @if (confirmingDeleteSurveyId() === s.id) {
                      <span class="text-xs text-neutral-600 whitespace-nowrap">
                        Delete?
                        <button type="button" class="text-primary-500 font-medium ml-xs" (click)="deleteSurvey(s.id)">Yes</button>
                        <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteSurveyId.set(null)">No</button>
                      </span>
                    } @else {
                      <span class="whitespace-nowrap">
                        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="startEditSurvey(s)">Edit</button>
                        <button type="button" class="text-xs text-primary-500 hover:text-primary-600 ml-sm" (click)="confirmingDeleteSurveyId.set(s.id)">
                          Delete
                        </button>
                      </span>
                    }
                  </div>
                  <div class="mt-sm">
                    <app-document-list
                      [rows]="ownerRows(surveyDocuments()[s.id] ?? [], 'landSurvey', s.id)"
                      [previewUrls]="previewUrls()"
                      (view)="onOwnedDocView($event)"
                      (download)="onOwnedDocDownload($event)"
                      (remove)="onOwnedDocRemove($event)"
                      (removeGroup)="onRemoveGroup($event)"
                      (rename)="onOwnedDocRename($event)"
                      (requestFulfill)="onFulfillDocRequest($event)"
                      (requestReopen)="reopenDocRequestRow($event)"
                      (requestCancel)="cancelDocRequestRow($event)"
                      (requestCopyShareLink)="copyDocRequestShareLinkRow($event)"
                    />
                    <div class="flex gap-md mt-xs">
                      <app-document-upload-button label="Add file(s)" (filesSelected)="onSurveyDocUpload(s.id, $event)" />
                      <button type="button" class="text-sm text-primary-600" (click)="startOwnerRequest('landSurvey', s.id)">+ Request document</button>
                    </div>
                    @if (isRequestFormTarget('landSurvey', s.id)) {
                      <div class="mt-sm">
                        <app-document-request-form (submitted)="submitDocRequest($event)" (cancelled)="requestFormTarget.set(null)" />
                      </div>
                    }
                  </div>
                </div>
              }
            </div>
          }
          @if (addingSurvey()) {
            <div class="border border-neutral-200 rounded-md p-md space-y-sm">
              <input class="input-field" placeholder="Survey plan number" [(ngModel)]="newSurveyPlanNumber" />
              <input class="input-field" type="date" [(ngModel)]="newSurveyDate" />
              <input class="input-field" placeholder="Surveyed by (optional)" [(ngModel)]="newSurveyedByName" />
              <div class="flex justify-end gap-sm">
                <button type="button" class="btn-secondary" (click)="cancelSurveyForm()">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newSurveyPlanNumber.trim() || !newSurveyDate" (click)="submitSurvey()">
                  {{ editingSurveyId() ? 'Save' : 'Add' }}
                </button>
              </div>
            </div>
          } @else {
            <button type="button" class="text-sm text-primary-600" (click)="addingSurvey.set(true)">+ Add survey</button>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Deeds</h3>
          @if (deeds().length > 0) {
            <div class="space-y-xs mb-sm">
              @for (d of deeds(); track d.id) {
                <div class="px-md py-sm rounded bg-neutral-50 text-sm">
                  <div class="flex items-center justify-between">
                    <div>
                      <span class="text-neutral-900">{{ d.deedNumber }}</span>
                      <span class="text-neutral-500"> · {{ d.issuedDate | date: 'mediumDate' }}</span>
                      @if (d.isCurrent) {
                        <span class="text-xs px-sm py-xs rounded bg-green-100 text-green-700 ml-sm">Current</span>
                      }
                    </div>
                    @if (confirmingDeleteDeedId() === d.id) {
                      <span class="text-xs text-neutral-600 whitespace-nowrap">
                        Delete?
                        <button type="button" class="text-primary-500 font-medium ml-xs" (click)="deleteDeed(d.id)">Yes</button>
                        <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteDeedId.set(null)">No</button>
                      </span>
                    } @else {
                      <span class="whitespace-nowrap">
                        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="startEditDeed(d)">Edit</button>
                        <button type="button" class="text-xs text-primary-500 hover:text-primary-600 ml-sm" (click)="confirmingDeleteDeedId.set(d.id)">
                          Delete
                        </button>
                      </span>
                    }
                  </div>
                  <div class="mt-sm">
                    <app-document-list
                      [rows]="ownerRows(deedDocuments()[d.id] ?? [], 'landDeed', d.id)"
                      [previewUrls]="previewUrls()"
                      (view)="onOwnedDocView($event)"
                      (download)="onOwnedDocDownload($event)"
                      (remove)="onOwnedDocRemove($event)"
                      (removeGroup)="onRemoveGroup($event)"
                      (rename)="onOwnedDocRename($event)"
                      (requestFulfill)="onFulfillDocRequest($event)"
                      (requestReopen)="reopenDocRequestRow($event)"
                      (requestCancel)="cancelDocRequestRow($event)"
                      (requestCopyShareLink)="copyDocRequestShareLinkRow($event)"
                    />
                    <div class="flex gap-md mt-xs">
                      <app-document-upload-button label="Add file(s)" (filesSelected)="onDeedDocUpload(d.id, $event)" />
                      <button type="button" class="text-sm text-primary-600" (click)="startOwnerRequest('landDeed', d.id)">+ Request document</button>
                    </div>
                    @if (isRequestFormTarget('landDeed', d.id)) {
                      <div class="mt-sm">
                        <app-document-request-form (submitted)="submitDocRequest($event)" (cancelled)="requestFormTarget.set(null)" />
                      </div>
                    }
                  </div>
                </div>
              }
            </div>
          }
          @if (addingDeed()) {
            <div class="border border-neutral-200 rounded-md p-md space-y-sm">
              <input class="input-field" placeholder="Deed number" [(ngModel)]="newDeedNumber" />
              <input class="input-field" type="date" [(ngModel)]="newDeedIssuedDate" />
              <label class="flex items-center gap-sm text-sm text-neutral-700">
                <input type="checkbox" [(ngModel)]="newDeedIsCurrent" />
                This is the current deed
              </label>
              <div class="flex justify-end gap-sm">
                <button type="button" class="btn-secondary" (click)="cancelDeedForm()">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newDeedNumber.trim() || !newDeedIssuedDate" (click)="submitDeed()">
                  {{ editingDeedId() ? 'Save' : 'Add' }}
                </button>
              </div>
            </div>
          } @else {
            <button type="button" class="text-sm text-primary-600" (click)="addingDeed.set(true)">+ Add deed</button>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Boundaries</h3>
          @if (boundaries().length > 0) {
            <div class="space-y-xs mb-sm">
              @for (b of boundaries(); track b.id) {
                <div class="px-md py-sm rounded bg-neutral-50 text-sm flex items-center justify-between">
                  <div>
                    <span class="text-neutral-900">{{ b.label }}</span>
                    @if (b.description) {
                      <span class="text-neutral-500"> · {{ b.description }}</span>
                    }
                  </div>
                  @if (confirmingDeleteBoundaryId() === b.id) {
                    <span class="text-xs text-neutral-600 whitespace-nowrap">
                      Delete?
                      <button type="button" class="text-primary-500 font-medium ml-xs" (click)="deleteBoundary(b.id)">Yes</button>
                      <button type="button" class="text-neutral-500 ml-xs" (click)="confirmingDeleteBoundaryId.set(null)">No</button>
                    </span>
                  } @else {
                    <span class="whitespace-nowrap">
                      <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="startEditBoundary(b)">Edit</button>
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600 ml-sm" (click)="confirmingDeleteBoundaryId.set(b.id)">
                        Delete
                      </button>
                    </span>
                  }
                </div>
              }
            </div>
          }
          @if (addingBoundary()) {
            <div class="border border-neutral-200 rounded-md p-md space-y-sm">
              <input class="input-field" placeholder="Label (e.g. North, River side)" [(ngModel)]="newBoundaryLabel" />
              <input class="input-field" placeholder="Description (optional)" [(ngModel)]="newBoundaryDescription" />
              <div class="flex justify-end gap-sm">
                <button type="button" class="btn-secondary" (click)="cancelBoundaryForm()">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="!newBoundaryLabel.trim()" (click)="submitBoundary()">
                  {{ editingBoundaryId() ? 'Save' : 'Add' }}
                </button>
              </div>
            </div>
          } @else {
            <button type="button" class="text-sm text-primary-600" (click)="addingBoundary.set(true)">+ Add boundary</button>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Documents</h3>
          <p class="text-xs text-neutral-500 mb-sm">Deeds/surveys attachments live in their own sections above; photos have their own Photos section below.</p>
          <app-document-list
            [rows]="documentRows()"
            [previewUrls]="previewUrls()"
            (view)="onOwnedDocView($event)"
            (download)="onOwnedDocDownload($event)"
            (remove)="onOwnedDocRemove($event)"
            (removeGroup)="onRemoveGroup($event)"
            (rename)="onOwnedDocRename($event)"
            (requestFulfill)="onFulfillDocRequest($event)"
            (requestReopen)="reopenDocRequestRow($event)"
            (requestCancel)="cancelDocRequestRow($event)"
            (requestCopyShareLink)="copyDocRequestShareLinkRow($event)"
          />
          @if (isRequestFormTarget('land', landId)) {
            <app-document-request-form (submitted)="submitDocRequest($event)" (cancelled)="requestFormTarget.set(null)" />
          } @else {
            <div class="flex gap-md mt-sm">
              <app-document-upload-button (filesSelected)="onDocumentFilesSelected($event)" />
              <button type="button" class="text-sm text-primary-600" (click)="startOwnerRequest('land', landId)">+ Request document</button>
            </div>
          }
          @if (documentError()) {
            <p class="text-xs text-primary-500 mt-xs">{{ documentError() }}</p>
          }
        </div>

        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Photos</h3>
          <app-document-list
            [rows]="photoRows()"
            [previewUrls]="previewUrls()"
            (view)="onOwnedDocView($event)"
            (download)="onOwnedDocDownload($event)"
            (remove)="onOwnedDocRemove($event)"
            (removeGroup)="onRemoveGroup($event)"
            (rename)="onOwnedDocRename($event)"
            (requestFulfill)="onFulfillDocRequest($event)"
            (requestReopen)="reopenDocRequestRow($event)"
            (requestCancel)="cancelDocRequestRow($event)"
            (requestCopyShareLink)="copyDocRequestShareLinkRow($event)"
          />
          @if (isRequestFormTarget('landPhoto', landId)) {
            <app-document-request-form (submitted)="submitDocRequest($event)" (cancelled)="requestFormTarget.set(null)" />
          } @else {
            <div class="flex gap-md mt-sm">
              <app-document-upload-button label="+ Add photo" accept="image/*" (filesSelected)="onPhotoFilesSelected($event)" />
              <button type="button" class="text-sm text-primary-600" (click)="startOwnerRequest('landPhoto', landId)">+ Request photo</button>
            </div>
          }
          @if (documentError()) {
            <p class="text-xs text-primary-500 mt-xs">{{ documentError() }}</p>
          }
        </div>
        }
      </div>
    }
    @if (viewingDocument()) {
      <app-document-viewer-modal [document]="viewingDocument()!" [blobUrl]="viewingBlobUrl()!" (closed)="closeViewer()" />
    }
  `,
  styles: [`.icon-btn { display: flex; align-items: center; justify-content: center; width: 1.75rem; height: 1.75rem; border-radius: 0.25rem; color: var(--color-neutral-500, #737373); } .icon-btn:hover { background: var(--color-neutral-100, #f5f5f5); color: var(--color-primary-600, #0284c7); }`]
})
export class LandDetailPanelComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() landId = '';
  @Output() deleted = new EventEmitter<void>();
  /** Emitted once, right after a create-mode submit succeeds - the parent owns navigating to the new land's own URL. */
  @Output() created = new EventEmitter<Land>();

  /** True until a landId is routed to - drives which sections/controls render (see template). */
  get isCreateMode(): boolean {
    return !this.landId;
  }
  creating = signal(false);
  createError = signal('');
  pendingLat: number | null = null;
  pendingLng: number | null = null;

  loading = signal(true);
  error = signal('');
  land = signal<Land | null>(null);
  confirmingDelete = signal(false);
  deleting = signal(false);
  savingDetails = signal(false);
  detailsError = signal('');
  surveys = signal<LandSurvey[]>([]);
  deeds = signal<LandDeed[]>([]);
  surveyDocuments = signal<Record<string, OwnedDocument[]>>({});
  deedDocuments = signal<Record<string, OwnedDocument[]>>({});
  boundaries = signal<LandBoundary[]>([]);
  locationError = signal('');
  shareLinkCopied = signal(false);
  mapViewShareLinkCopied = signal(false);
  photos = signal<LandPhoto[]>([]);
  /** Inline preview object-URLs for every image row across every section (photos, general docs, survey/deed docs) - keyed by documentId, fed to every <app-document-list> instance. */
  previewUrls = signal<Record<string, string>>({});

  mapPoints = signal<LandMapPoint[]>([]);
  mapPointError = signal('');
  confirmingDeleteMapPointId = signal<string | null>(null);
  renamingMapPointId = signal<string | null>(null);
  renameMapPointValue = '';
  copiedMapPointId = signal<string | null>(null);
  expandedMapPointId = signal<string | null>(null);
  pendingNewPoint = signal<{ lat: number; lng: number } | null>(null);
  pendingNewPointName = '';

  mapPointMarkers = computed(() => this.mapPoints().map(p => ({ id: p.id, name: p.name, lat: p.latitude, lng: p.longitude })));
  googleMapsUrl = (lat: number, lng: number) => this.landService.googleMapsUrl(lat, lng);

  toggleExpandedMapPoint(pointId: string): void {
    this.expandedMapPointId.update(current => (current === pointId ? null : pointId));
  }

  documents = signal<OwnedDocument[]>([]);
  documentRequests = signal<LandDocumentRequest[]>([]);
  documentError = signal('');
  /** Which owner's "+ Request document" form is open, if any - one signal shared by the general Documents section and every Survey/Deed row, since only one can be open at a time. */
  requestFormTarget = signal<{ ownerType: 'Land' | 'LandSurvey' | 'LandDeed' | 'LandPhoto'; ownerId: string } | null>(null);
  viewingDocument = signal<{ fileName: string; contentType: string } | null>(null);
  viewingBlobUrl = signal<string | null>(null);

  private readonly ownerTypeApi: Record<'land' | 'landSurvey' | 'landDeed' | 'landPhoto', 'Land' | 'LandSurvey' | 'LandDeed' | 'LandPhoto'> = {
    land: 'Land', landSurvey: 'LandSurvey', landDeed: 'LandDeed', landPhoto: 'LandPhoto'
  };

  startOwnerRequest(ownerType: 'land' | 'landSurvey' | 'landDeed' | 'landPhoto', ownerId: string): void {
    this.requestFormTarget.set({ ownerType: this.ownerTypeApi[ownerType], ownerId });
  }

  isRequestFormTarget(ownerType: 'land' | 'landSurvey' | 'landDeed' | 'landPhoto', ownerId: string): boolean {
    const target = this.requestFormTarget();
    return !!target && target.ownerType === this.ownerTypeApi[ownerType] && target.ownerId === ownerId;
  }

  /** Photos merged into the same unified list as every other document - a fulfilled photo request row absorbs into its photo row exactly like any other request. Adapts LandPhoto's shape into OwnedDocument so buildOwnerRows' request-merging/batch-grouping logic applies identically to photos, not a second hand-rolled code path. */
  photoRows = computed<DocRow[]>(() =>
    this.buildOwnerRows(
      this.photos().map(p => ({
        documentId: p.photoId, fileName: p.fileName, contentType: p.contentType, fileSizeBytes: p.fileSizeBytes,
        uploadedByName: p.uploadedByName, createdAt: p.createdAt, uploadBatchId: p.batchId
      })),
      'landPhoto', 'LandPhoto', this.landId
    )
  );

  /**
   * Merges an owner's plain uploaded documents with its pending/fulfilled document requests
   * into one DocRow list - the one place every owner kind (general land, survey, deed, photo)
   * builds its rows, so request-fulfillment shows correctly everywhere. A doc joins a
   * request's group when its batch id matches the request's fulfilledBatchId - every file
   * uploaded via that request's fulfill action, first time or after a reopen, shares it.
   */
  private buildOwnerRows(docs: OwnedDocument[], ownerKind: DocRow['ownerKind'], apiOwnerType: string, ownerId: string, subId?: string): DocRow[] {
    const requests = this.documentRequests().filter(r => r.ownerType === apiOwnerType && r.ownerId === ownerId);
    const rows: DocRow[] = [];

    for (const doc of docs) {
      const request = doc.uploadBatchId ? requests.find(r => r.fulfilledBatchId === doc.uploadBatchId) ?? null : null;
      rows.push({
        key: doc.documentId, ownerKind, ownerId, subId, documentId: doc.documentId,
        fileName: doc.fileName, contentType: doc.contentType, uploadedByName: doc.uploadedByName, createdAt: doc.createdAt,
        batchId: doc.uploadBatchId ?? null,
        requestId: request?.requestId ?? null, requestTitle: request?.title ?? null, requestStatus: request?.status ?? null
      });
    }
    for (const request of requests) {
      // Still-pending (never fulfilled) requests have no batch yet - render as the existing bare placeholder row.
      if (!request.fulfilledBatchId) {
        rows.push({
          key: request.requestId, ownerKind, ownerId, subId, documentId: null,
          fileName: null, contentType: null, uploadedByName: null, createdAt: null,
          requestId: request.requestId, requestTitle: request.title, requestStatus: request.status,
          requestDescription: request.description, hasActiveShareLink: request.hasActiveShareLink
        });
      }
    }
    return rows;
  }

  ownerRows(docs: OwnedDocument[], ownerKind: 'landSurvey' | 'landDeed', subId: string): DocRow[] {
    return this.buildOwnerRows(docs, ownerKind, this.ownerTypeApi[ownerKind], subId, subId);
  }

  documentRows = computed<DocRow[]>(() => this.buildOwnerRows(this.documents(), 'land', 'Land', this.landId));

  telHref = telHref;
  whatsAppHref = whatsAppHref;

  owner: OwnerValue = {};
  /** Display name for an existing account owner, so the picker can render it without a re-fetch. */
  ownerLabel: string | null = null;
  village = '';
  gramaNiladhariDivision = '';
  divisionalSecretariat = '';
  pradeshiyaSabha = '';
  korale = '';
  hatpattu = '';
  district = '';
  province = '';
  provinces = PROVINCES;

  /** Only the selected province's districts once one is chosen - otherwise (or if the loaded record's province is legacy free text that doesn't match our canonical list) every district, so a mismatched/empty province never hides every option. */
  get districtOptions(): string[] {
    if (this.province && DISTRICTS_BY_PROVINCE[this.province]) return DISTRICTS_BY_PROVINCE[this.province];
    return Object.values(DISTRICTS_BY_PROVINCE).flat();
  }

  onProvinceChange(newProvince: string): void {
    this.province = newProvince;
    if (this.district && !DISTRICTS_BY_PROVINCE[newProvince]?.includes(this.district)) {
      this.district = '';
    }
  }

  onDistrictChange(newDistrict: string): void {
    this.district = newDistrict;
    const owningProvince = provinceForDistrict(newDistrict);
    if (owningProvince && owningProvince !== this.province) {
      this.province = owningProvince;
    }
  }
  area: LandAreaValue = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
  notes = '';

  addingSurvey = signal(false);
  editingSurveyId = signal<string | null>(null);
  confirmingDeleteSurveyId = signal<string | null>(null);
  newSurveyPlanNumber = '';
  newSurveyDate = '';
  newSurveyedByName = '';

  addingDeed = signal(false);
  editingDeedId = signal<string | null>(null);
  confirmingDeleteDeedId = signal<string | null>(null);
  newDeedNumber = '';
  newDeedIssuedDate = '';
  newDeedIsCurrent = true;

  addingBoundary = signal(false);
  editingBoundaryId = signal<string | null>(null);
  confirmingDeleteBoundaryId = signal<string | null>(null);
  newBoundaryLabel = '';
  newBoundaryDescription = '';

  constructor(private landService: LandService, private documentRequestService: LandDocumentRequestService) {}

  ngOnInit(): void {
    if (this.isCreateMode) {
      // Nothing to load - blank Details/Owner/Location form, storedDetails snapshotted
      // now so detailsDirty() correctly reflects typing into a fresh create form.
      this.loading.set(false);
      this.storedDetails = this.snapshotDetails();
      return;
    }
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      land: this.landService.getById(this.workspaceId, this.landId),
      surveys: this.landService.getSurveys(this.workspaceId, this.landId),
      deeds: this.landService.getDeeds(this.workspaceId, this.landId),
      boundaries: this.landService.getBoundaries(this.workspaceId, this.landId),
      photos: this.landService.listPhotos(this.workspaceId, this.landId),
      mapPoints: this.landService.getMapPoints(this.workspaceId, this.landId),
      documents: this.landService.getDocuments(this.workspaceId, this.landId),
      documentRequests: this.documentRequestService.list(this.workspaceId, this.landId)
    }).subscribe({
      next: ({ land, surveys, deeds, boundaries, photos, mapPoints, documents, documentRequests }) => {
        this.land.set(land);
        this.village = land.address.village ?? '';
        this.gramaNiladhariDivision = land.address.gramaNiladhariDivision ?? '';
        this.divisionalSecretariat = land.address.divisionalSecretariat ?? '';
        this.pradeshiyaSabha = land.address.pradeshiyaSabha ?? '';
        this.korale = land.address.korale ?? '';
        this.hatpattu = land.address.hatpattu ?? '';
        this.district = land.address.district ?? '';
        this.province = land.address.province ?? '';
        this.area = land.area;
        this.notes = land.notes ?? '';
        this.owner = land.ownerId
          ? { ownerId: land.ownerId, ownerEmail: land.ownerEmail ?? undefined }
          : land.ownerName
            ? { ownerName: land.ownerName, ownerPhone: land.ownerPhone ?? undefined, ownerEmail: land.ownerEmail ?? undefined }
            : {};
        this.ownerLabel = land.ownerId ? land.ownerName : null;
        this.storedDetails = this.snapshotDetails();
        this.surveys.set(surveys);
        this.deeds.set(deeds);
        this.loadSurveyDocuments(surveys);
        this.loadDeedDocuments(deeds);
        this.boundaries.set(boundaries);
        this.photos.set(photos);
        this.mapPoints.set(mapPoints);
        this.documents.set(documents);
        this.documentRequests.set(documentRequests);
        this.loadPreviews(this.documentRows());
        this.loadPreviews(this.photoRows());
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load land record.');
        this.loading.set(false);
      }
    });
  }

  confirmDelete(): void {
    this.deleting.set(true);
    this.landService.delete(this.workspaceId, this.landId).subscribe({
      next: () => this.deleted.emit(),
      error: (err) => {
        this.deleting.set(false);
        this.confirmingDelete.set(false);
        this.error.set(err.error?.message ?? 'Could not delete land record.');
      }
    });
  }

  /** Snapshot of Details as last loaded/saved, so edits (including the owner) can be detected and discarded. */
  private storedDetails = '';

  private snapshotDetails(): string {
    return JSON.stringify({
      village: this.village, gramaNiladhariDivision: this.gramaNiladhariDivision,
      divisionalSecretariat: this.divisionalSecretariat, pradeshiyaSabha: this.pradeshiyaSabha,
      korale: this.korale, hatpattu: this.hatpattu, district: this.district, province: this.province,
      area: this.area, notes: this.notes, owner: this.owner
    });
  }

  detailsDirty(): boolean {
    return this.snapshotDetails() !== this.storedDetails;
  }

  /** The picker has no blur of its own - it just marks Details dirty like every other field. */
  onOwnerChange(value: OwnerValue): void {
    this.owner = value;
  }

  onAreaChange(value: Partial<LandAreaValue>): void {
    this.area = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null, ...value };
  }

  discardDetails(): void {
    const current = this.land();
    if (!current) return;
    this.village = current.address.village ?? '';
    this.gramaNiladhariDivision = current.address.gramaNiladhariDivision ?? '';
    this.divisionalSecretariat = current.address.divisionalSecretariat ?? '';
    this.pradeshiyaSabha = current.address.pradeshiyaSabha ?? '';
    this.korale = current.address.korale ?? '';
    this.hatpattu = current.address.hatpattu ?? '';
    this.district = current.address.district ?? '';
    this.province = current.address.province ?? '';
    this.area = current.area;
    this.notes = current.notes ?? '';
    this.owner = current.ownerId
      ? { ownerId: current.ownerId, ownerEmail: current.ownerEmail ?? undefined }
      : current.ownerName
        ? { ownerName: current.ownerName, ownerPhone: current.ownerPhone ?? undefined, ownerEmail: current.ownerEmail ?? undefined }
        : {};
    this.ownerLabel = current.ownerId ? current.ownerName : null;
    this.detailsError.set('');
  }

  saveDetails(onSaved?: () => void): void {
    const current = this.land();
    if (!current || !this.detailsDirty()) return;

    this.detailsError.set('');

    const address: Partial<LandAddress> = {
      village: this.village.trim() || null,
      gramaNiladhariDivision: this.gramaNiladhariDivision.trim() || null,
      divisionalSecretariat: this.divisionalSecretariat.trim() || null,
      pradeshiyaSabha: this.pradeshiyaSabha.trim() || null,
      korale: this.korale.trim() || null,
      hatpattu: this.hatpattu.trim() || null,
      district: this.district.trim() || null,
      province: this.province.trim() || null
    };

    this.savingDetails.set(true);
    this.landService
      .update(this.workspaceId, this.landId, {
        address,
        area: toAreaRequest(this.area),
        notes: this.notes.trim() || undefined,
        ...this.owner
      })
      .subscribe({
        next: (land) => {
          this.land.set(land);
          this.ownerLabel = land.ownerId ? land.ownerName : null;
          this.storedDetails = this.snapshotDetails();
          this.savingDetails.set(false);
          onSaved?.();
        },
        error: (err) => {
          this.savingDetails.set(false);
          this.detailsError.set(err.error?.message ?? 'Could not save changes.');
        }
      });
  }

  onPendingLocationChosen(location: { lat: number; lng: number }): void {
    this.pendingLat = location.lat;
    this.pendingLng = location.lng;
  }

  createLand(): void {
    if (!this.village.trim() || this.creating()) return;
    this.createError.set('');
    this.creating.set(true);

    const address: Partial<LandAddress> = {
      village: this.village.trim(),
      gramaNiladhariDivision: this.gramaNiladhariDivision.trim() || null,
      divisionalSecretariat: this.divisionalSecretariat.trim() || null,
      pradeshiyaSabha: this.pradeshiyaSabha.trim() || null,
      korale: this.korale.trim() || null,
      hatpattu: this.hatpattu.trim() || null,
      district: this.district.trim() || null,
      province: this.province.trim() || null
    };

    this.landService
      .create(this.workspaceId, {
        address,
        area: toAreaRequest(this.area),
        notes: this.notes.trim() || undefined,
        ...this.owner
      })
      .subscribe({
        next: (land) => {
          if (this.pendingLat !== null && this.pendingLng !== null) {
            // Map points aren't part of LandRequest - one follow-up call on the new id,
            // reusing the same endpoint the edit-mode map uses. Named "Location" for now;
            // the user can rename it from the point list once the record exists.
            this.landService.addMapPoint(this.workspaceId, land.landId, { name: 'Location', latitude: this.pendingLat, longitude: this.pendingLng }).subscribe({
              next: () => {
                this.creating.set(false);
                this.created.emit(land);
              },
              error: (err) => {
                // Land exists even though the point save failed - still navigate,
                // rather than strand the user on a form for a record that now exists.
                this.creating.set(false);
                this.createError.set(err.error?.message ?? 'Land created, but the location could not be saved.');
                this.created.emit(land);
              }
            });
          } else {
            this.creating.set(false);
            this.created.emit(land);
          }
        },
        error: (err) => {
          this.creating.set(false);
          this.createError.set(err.error?.message ?? 'Could not create land record.');
        }
      });
  }

  onMapClicked(location: { lat: number; lng: number }): void {
    this.mapPointError.set('');
    this.pendingNewPoint.set(location);
    this.pendingNewPointName = '';
  }

  saveNewPoint(): void {
    const pending = this.pendingNewPoint();
    if (!pending || !this.pendingNewPointName.trim()) return;
    this.mapPointError.set('');

    this.landService.addMapPoint(this.workspaceId, this.landId, { name: this.pendingNewPointName.trim(), latitude: pending.lat, longitude: pending.lng }).subscribe({
      next: (point) => {
        this.mapPoints.update(list => [...list, point]);
        this.pendingNewPoint.set(null);
      },
      error: (err) => this.mapPointError.set(err.error?.message ?? 'Could not add point.')
    });
  }

  onMapPointMoved(event: { id: string; lat: number; lng: number }): void {
    const point = this.mapPoints().find(p => p.id === event.id);
    if (!point) return;
    this.mapPointError.set('');

    this.landService.updateMapPoint(this.workspaceId, this.landId, point.id, { name: point.name, latitude: event.lat, longitude: event.lng }).subscribe({
      next: (updated) => this.mapPoints.update(list => list.map(p => (p.id === updated.id ? updated : p))),
      error: (err) => this.mapPointError.set(err.error?.message ?? 'Could not move point.')
    });
  }

  startRenameMapPoint(point: LandMapPoint): void {
    this.renameMapPointValue = point.name;
    this.renamingMapPointId.set(point.id);
  }

  confirmRenameMapPoint(pointId: string): void {
    if (!this.renameMapPointValue.trim()) return;
    const point = this.mapPoints().find(p => p.id === pointId);
    if (!point) return;
    this.mapPointError.set('');

    this.landService.updateMapPoint(this.workspaceId, this.landId, pointId, { name: this.renameMapPointValue.trim(), latitude: point.latitude, longitude: point.longitude }).subscribe({
      next: (updated) => {
        this.mapPoints.update(list => list.map(p => (p.id === updated.id ? updated : p)));
        this.renamingMapPointId.set(null);
      },
      error: (err) => this.mapPointError.set(err.error?.message ?? 'Could not rename point.')
    });
  }

  copyMapPointLink(point: LandMapPoint): void {
    navigator.clipboard.writeText(this.landService.googleMapsUrl(point.latitude, point.longitude));
    this.copiedMapPointId.set(point.id);
    setTimeout(() => this.copiedMapPointId.set(null), 2000);
  }

  copyShareLink(): void {
    this.locationError.set('');
    this.landService.generateLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/set-location/${token}`);
        this.shareLinkCopied.set(true);
        setTimeout(() => this.shareLinkCopied.set(false), 2000);
        this.land.update(l => (l ? { ...l, hasActiveLocationShareLink: true } : l));
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not create share link.')
    });
  }

  regenerateShareLink(): void {
    this.locationError.set('');
    this.landService.regenerateLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/set-location/${token}`);
        this.shareLinkCopied.set(true);
        setTimeout(() => this.shareLinkCopied.set(false), 2000);
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not regenerate share link.')
    });
  }

  revokeShareLink(): void {
    this.locationError.set('');
    this.landService.revokeLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: () => this.land.update(l => (l ? { ...l, hasActiveLocationShareLink: false } : l)),
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not revoke share link.')
    });
  }

  copyMapViewShareLink(): void {
    this.locationError.set('');
    this.landService.generateMapViewShareLink(this.workspaceId, this.landId).subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/land-map-view/${token}`);
        this.mapViewShareLinkCopied.set(true);
        setTimeout(() => this.mapViewShareLinkCopied.set(false), 2000);
        this.land.update(l => (l ? { ...l, hasActiveMapViewShareLink: true } : l));
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not create share link.')
    });
  }

  regenerateMapViewShareLink(): void {
    this.locationError.set('');
    this.landService.regenerateMapViewShareLink(this.workspaceId, this.landId).subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/land-map-view/${token}`);
        this.mapViewShareLinkCopied.set(true);
        setTimeout(() => this.mapViewShareLinkCopied.set(false), 2000);
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not regenerate share link.')
    });
  }

  revokeMapViewShareLink(): void {
    this.locationError.set('');
    this.landService.revokeMapViewShareLink(this.workspaceId, this.landId).subscribe({
      next: () => this.land.update(l => (l ? { ...l, hasActiveMapViewShareLink: false } : l)),
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not revoke share link.')
    });
  }

  startEditSurvey(s: LandSurvey): void {
    this.editingSurveyId.set(s.id);
    this.newSurveyPlanNumber = s.surveyPlanNumber;
    this.newSurveyDate = s.surveyDate.slice(0, 10);
    this.newSurveyedByName = s.surveyedByName ?? '';
    this.addingSurvey.set(true);
  }

  cancelSurveyForm(): void {
    this.addingSurvey.set(false);
    this.editingSurveyId.set(null);
    this.newSurveyPlanNumber = '';
    this.newSurveyDate = '';
    this.newSurveyedByName = '';
  }

  submitSurvey(): void {
    if (!this.newSurveyPlanNumber.trim() || !this.newSurveyDate) return;
    const request = {
      surveyPlanNumber: this.newSurveyPlanNumber.trim(),
      surveyDate: this.newSurveyDate,
      surveyedByName: this.newSurveyedByName.trim() || undefined
    };
    const editingId = this.editingSurveyId();

    const result$ = editingId
      ? this.landService.updateSurvey(this.workspaceId, this.landId, editingId, request)
      : this.landService.addSurvey(this.workspaceId, this.landId, request);

    result$.subscribe({
      next: (survey) => {
        this.surveys.update(list => (editingId ? list.map(s => (s.id === editingId ? survey : s)) : [survey, ...list]));
        this.cancelSurveyForm();
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not save survey.')
    });
  }

  deleteSurvey(surveyId: string): void {
    this.landService.deleteSurvey(this.workspaceId, this.landId, surveyId).subscribe({
      next: () => {
        this.surveys.update(list => list.filter(s => s.id !== surveyId));
        this.confirmingDeleteSurveyId.set(null);
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not delete survey.')
    });
  }

  startEditDeed(d: LandDeed): void {
    this.editingDeedId.set(d.id);
    this.newDeedNumber = d.deedNumber;
    this.newDeedIssuedDate = d.issuedDate.slice(0, 10);
    this.newDeedIsCurrent = d.isCurrent;
    this.addingDeed.set(true);
  }

  cancelDeedForm(): void {
    this.addingDeed.set(false);
    this.editingDeedId.set(null);
    this.newDeedNumber = '';
    this.newDeedIssuedDate = '';
    this.newDeedIsCurrent = true;
  }

  submitDeed(): void {
    if (!this.newDeedNumber.trim() || !this.newDeedIssuedDate) return;
    const request = { deedNumber: this.newDeedNumber.trim(), issuedDate: this.newDeedIssuedDate, isCurrent: this.newDeedIsCurrent };
    const editingId = this.editingDeedId();

    const result$ = editingId
      ? this.landService.updateDeed(this.workspaceId, this.landId, editingId, request)
      : this.landService.addDeed(this.workspaceId, this.landId, request);

    result$.subscribe({
      next: () => {
        // A current deed supersedes any other current deed server-side - refetch the
        // list rather than patch it locally, so every affected badge updates too.
        this.landService.getDeeds(this.workspaceId, this.landId).subscribe(deeds => {
          this.deeds.set(deeds);
          this.loadDeedDocuments(deeds);
        });
        this.cancelDeedForm();
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not save deed.')
    });
  }

  deleteDeed(deedId: string): void {
    this.landService.deleteDeed(this.workspaceId, this.landId, deedId).subscribe({
      next: () => {
        this.deeds.update(list => list.filter(d => d.id !== deedId));
        this.confirmingDeleteDeedId.set(null);
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not delete deed.')
    });
  }

  startEditBoundary(b: LandBoundary): void {
    this.editingBoundaryId.set(b.id);
    this.newBoundaryLabel = b.label;
    this.newBoundaryDescription = b.description ?? '';
    this.addingBoundary.set(true);
  }

  cancelBoundaryForm(): void {
    this.addingBoundary.set(false);
    this.editingBoundaryId.set(null);
    this.newBoundaryLabel = '';
    this.newBoundaryDescription = '';
  }

  submitBoundary(): void {
    if (!this.newBoundaryLabel.trim()) return;
    const request = { label: this.newBoundaryLabel.trim(), description: this.newBoundaryDescription.trim() || undefined };
    const editingId = this.editingBoundaryId();

    const result$ = editingId
      ? this.landService.updateBoundary(this.workspaceId, this.landId, editingId, request)
      : this.landService.addBoundary(this.workspaceId, this.landId, request);

    result$.subscribe({
      next: (boundary) => {
        this.boundaries.update(list => (editingId ? list.map(b => (b.id === editingId ? boundary : b)) : [...list, boundary]));
        this.cancelBoundaryForm();
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not save boundary.')
    });
  }

  deleteBoundary(boundaryId: string): void {
    this.landService.deleteBoundary(this.workspaceId, this.landId, boundaryId).subscribe({
      next: () => {
        this.boundaries.update(list => list.filter(b => b.id !== boundaryId));
        this.confirmingDeleteBoundaryId.set(null);
      },
      error: (err) => this.error.set(err.error?.message ?? 'Could not delete boundary.')
    });
  }

  private loadSurveyDocuments(surveys: LandSurvey[]): void {
    surveys.forEach(s => {
      this.landService.getSurveyDocuments(this.workspaceId, this.landId, s.id).subscribe(docs => {
        this.surveyDocuments.update(map => ({ ...map, [s.id]: docs }));
        this.loadPreviews(this.ownerRows(docs, 'landSurvey', s.id));
      });
    });
  }

  private loadDeedDocuments(deeds: LandDeed[]): void {
    deeds.forEach(d => {
      this.landService.getDeedDocuments(this.workspaceId, this.landId, d.id).subscribe(docs => {
        this.deedDocuments.update(map => ({ ...map, [d.id]: docs }));
        this.loadPreviews(this.ownerRows(docs, 'landDeed', d.id));
      });
    });
  }

  /** Fetches and caches an inline-preview object-URL for every image row not already cached - the one place every section (photos, general docs, survey/deed docs) goes through so a click-to-preview modal thumbnail is never the only preview. */
  private loadPreviews(rows: DocRow[]): void {
    for (const row of rows) {
      if (!row.documentId || !row.contentType?.startsWith('image/') || this.previewUrls()[row.documentId]) continue;
      this.ownedDocBlob(row).subscribe(blob => {
        this.previewUrls.update(urls => ({ ...urls, [row.documentId!]: URL.createObjectURL(blob) }));
      });
    }
  }

  private triggerDownload(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }

  onSurveyDocUpload(surveyId: string, files: File[]): void {
    this.error.set('');
    const batchId = files.length > 1 ? crypto.randomUUID() : undefined;
    files.forEach(file =>
      this.landService.uploadSurveyDocument(this.workspaceId, this.landId, surveyId, file, batchId).subscribe({
        next: (doc) => this.surveyDocuments.update(map => ({ ...map, [surveyId]: [doc, ...(map[surveyId] ?? [])] })),
        error: (err) => this.error.set(err.error?.message ?? 'Could not upload document.')
      })
    );
  }

  onDeedDocUpload(deedId: string, files: File[]): void {
    this.error.set('');
    const batchId = files.length > 1 ? crypto.randomUUID() : undefined;
    files.forEach(file =>
      this.landService.uploadDeedDocument(this.workspaceId, this.landId, deedId, file, batchId).subscribe({
        next: (doc) => this.deedDocuments.update(map => ({ ...map, [deedId]: [doc, ...(map[deedId] ?? [])] })),
        error: (err) => this.error.set(err.error?.message ?? 'Could not upload document.')
      })
    );
  }

  private ownedDocBlob(row: DocRow) {
    switch (row.ownerKind) {
      case 'landSurvey':
        return this.landService.getSurveyDocumentBlob(this.workspaceId, this.landId, row.subId!, row.documentId!);
      case 'landDeed':
        return this.landService.getDeedDocumentBlob(this.workspaceId, this.landId, row.subId!, row.documentId!);
      case 'landPhoto':
        return this.landService.getPhotoBlob(this.workspaceId, this.landId, row.documentId!);
      default:
        return this.landService.getDocumentBlob(this.workspaceId, this.landId, row.documentId!);
    }
  }

  onOwnedDocView(row: DocRow): void {
    this.ownedDocBlob(row).subscribe(blob => this.openViewer({ fileName: row.fileName!, contentType: row.contentType! }, blob));
  }

  onOwnedDocDownload(row: DocRow): void {
    this.ownedDocBlob(row).subscribe(blob => this.triggerDownload(blob, row.fileName!));
  }

  onRemoveGroup(rows: DocRow[]): void {
    rows.forEach(row => this.onOwnedDocRemove(row));
  }

  onOwnedDocRemove(row: DocRow): void {
    this.documentError.set('');
    this.error.set('');
    const documentId = row.documentId!;
    switch (row.ownerKind) {
      case 'landSurvey':
        this.landService.deleteSurveyDocument(this.workspaceId, this.landId, row.subId!, documentId).subscribe({
          next: () => this.surveyDocuments.update(map => ({ ...map, [row.subId!]: (map[row.subId!] ?? []).filter(d => d.documentId !== documentId) })),
          error: (err) => this.error.set(err.error?.message ?? 'Could not delete document.')
        });
        break;
      case 'landDeed':
        this.landService.deleteDeedDocument(this.workspaceId, this.landId, row.subId!, documentId).subscribe({
          next: () => this.deedDocuments.update(map => ({ ...map, [row.subId!]: (map[row.subId!] ?? []).filter(d => d.documentId !== documentId) })),
          error: (err) => this.error.set(err.error?.message ?? 'Could not delete document.')
        });
        break;
      case 'landPhoto':
        this.landService.deletePhoto(this.workspaceId, this.landId, documentId).subscribe({
          next: () => this.photos.update(list => list.filter(p => p.photoId !== documentId)),
          error: (err) => this.documentError.set(err.error?.message ?? 'Could not delete photo.')
        });
        break;
      default:
        this.landService.deleteDocument(this.workspaceId, this.landId, documentId).subscribe({
          next: () => this.documents.update(list => list.filter(d => d.documentId !== documentId)),
          error: (err) => this.documentError.set(err.error?.message ?? 'Could not delete document.')
        });
    }
  }

  onOwnedDocRename(event: { row: DocRow; fileName: string }): void {
    const { row, fileName } = event;
    const documentId = row.documentId!;
    this.documentError.set('');
    this.error.set('');
    switch (row.ownerKind) {
      case 'landSurvey':
        this.landService.renameSurveyDocument(this.workspaceId, this.landId, row.subId!, documentId, fileName).subscribe({
          next: (updated) => this.surveyDocuments.update(map => ({ ...map, [row.subId!]: (map[row.subId!] ?? []).map(d => (d.documentId === updated.documentId ? updated : d)) })),
          error: (err) => this.error.set(err.error?.message ?? 'Could not rename document.')
        });
        break;
      case 'landDeed':
        this.landService.renameDeedDocument(this.workspaceId, this.landId, row.subId!, documentId, fileName).subscribe({
          next: (updated) => this.deedDocuments.update(map => ({ ...map, [row.subId!]: (map[row.subId!] ?? []).map(d => (d.documentId === updated.documentId ? updated : d)) })),
          error: (err) => this.error.set(err.error?.message ?? 'Could not rename document.')
        });
        break;
      case 'landPhoto':
        this.landService.renamePhoto(this.workspaceId, this.landId, documentId, fileName).subscribe({
          next: (updated) => this.photos.update(list => list.map(p => (p.photoId === updated.photoId ? updated : p))),
          error: (err) => this.documentError.set(err.error?.message ?? 'Could not rename photo.')
        });
        break;
      default:
        this.landService.renameDocument(this.workspaceId, this.landId, documentId, fileName).subscribe({
          next: (updated) => this.documents.update(list => list.map(d => (d.documentId === updated.documentId ? updated : d))),
          error: (err) => this.documentError.set(err.error?.message ?? 'Could not rename document.')
        });
    }
  }

  deleteMapPoint(pointId: string): void {
    this.mapPointError.set('');
    this.landService.deleteMapPoint(this.workspaceId, this.landId, pointId).subscribe({
      next: () => {
        this.mapPoints.update(list => list.filter(p => p.id !== pointId));
        this.confirmingDeleteMapPointId.set(null);
      },
      error: (err) => this.mapPointError.set(err.error?.message ?? 'Could not delete point.')
    });
  }

  /** General document upload - photos have their own card/upload path again (see onPhotoFilesSelected), so this always uploads as Category=Other. */
  onDocumentFilesSelected(files: File[]): void {
    this.documentError.set('');
    const batchId = files.length > 1 ? crypto.randomUUID() : undefined;
    files.forEach(file => {
      this.landService.uploadDocument(this.workspaceId, this.landId, file, 'Other', batchId).subscribe({
        next: (doc) => this.documents.update(list => [doc, ...list]),
        error: (err) => this.documentError.set(err.error?.message ?? 'Could not upload document.')
      });
    });
  }

  onPhotoFilesSelected(files: File[]): void {
    this.documentError.set('');
    const batchId = files.length > 1 ? crypto.randomUUID() : undefined;
    files.forEach(file => {
      this.landService.uploadPhoto(this.workspaceId, this.landId, file, batchId).subscribe({
        next: (photo) => {
          this.photos.update(list => [photo, ...list]);
          this.loadPreviews(this.photoRows());
        },
        error: (err) => this.documentError.set(err.error?.message ?? 'Could not upload photo.')
      });
    });
  }

  submitDocRequest(value: DocumentRequestFormValue): void {
    const target = this.requestFormTarget();
    if (!target) return;
    this.documentError.set('');
    this.documentRequestService
      .create(this.workspaceId, this.landId, value.title, value.description, value.category, value.targetRole, target.ownerType, target.ownerId)
      .subscribe({
        next: (request) => {
          this.documentRequests.update(list => [request, ...list]);
          this.requestFormTarget.set(null);
        },
        error: (err) => this.documentError.set(err.error?.message ?? 'Could not create request.')
      });
  }

  onFulfillDocRequest(event: { row: DocRow; files: File[] }): void {
    this.documentError.set('');
    const batchId = event.row.batchId ?? crypto.randomUUID();
    this.documentRequestService.fulfill(this.workspaceId, this.landId, event.row.requestId!, event.files, batchId).subscribe({
      next: (updated) => {
        this.documentRequests.update(list => list.map(r => (r.requestId === updated.requestId ? updated : r)));
        this.landService.getDocuments(this.workspaceId, this.landId).subscribe(docs => this.documents.set(docs));
        this.landService.listPhotos(this.workspaceId, this.landId).subscribe(photos => {
          this.photos.set(photos);
          this.loadPreviews(this.photoRows());
        });
      },
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not fulfill request.')
    });
  }

  reopenDocRequestRow(row: DocRow): void {
    this.documentError.set('');
    this.documentRequestService.reopen(this.workspaceId, this.landId, row.requestId!).subscribe({
      next: (updated) => this.documentRequests.update(list => list.map(r => (r.requestId === updated.requestId ? updated : r))),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not reopen request.')
    });
  }

  cancelDocRequestRow(row: DocRow): void {
    this.documentError.set('');
    this.documentRequestService.cancel(this.workspaceId, this.landId, row.requestId!).subscribe({
      next: () => this.documentRequests.update(list => list.filter(r => r.requestId !== row.requestId)),
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not cancel request.')
    });
  }

  copyDocRequestShareLinkRow(row: DocRow): void {
    this.documentError.set('');
    this.documentRequestService.generateShareLink(this.workspaceId, this.landId, row.requestId!).subscribe({
      next: ({ token }) => {
        navigator.clipboard.writeText(`${location.origin}/land-document-upload/${token}`);
        this.documentRequests.update(list => list.map(r => (r.requestId === row.requestId ? { ...r, hasActiveShareLink: true } : r)));
      },
      error: (err) => this.documentError.set(err.error?.message ?? 'Could not create share link.')
    });
  }

  private openViewer(doc: { fileName: string; contentType: string }, blob: Blob): void {
    this.viewingDocument.set(doc);
    this.viewingBlobUrl.set(URL.createObjectURL(blob));
  }

  closeViewer(): void {
    const url = this.viewingBlobUrl();
    if (url) URL.revokeObjectURL(url);
    this.viewingDocument.set(null);
    this.viewingBlobUrl.set(null);
  }
}
