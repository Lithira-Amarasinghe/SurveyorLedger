import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { LandDetailPanelComponent } from './land-detail-panel/land-detail-panel.component';

@Component({
  selector: 'app-land-detail',
  standalone: true,
  imports: [CommonModule, LandDetailPanelComponent],
  template: `
    <div class="p-lg max-w-3xl mx-auto">
      <div class="card">
        <app-land-detail-panel [workspaceId]="workspaceId" [landId]="landId" />
      </div>
    </div>
  `
})
export class LandDetailComponent implements OnInit {
  workspaceId = '';
  landId = '';

  constructor(private currentWorkspace: CurrentWorkspaceService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.landId = this.route.snapshot.paramMap.get('landId') ?? '';
  }
}
