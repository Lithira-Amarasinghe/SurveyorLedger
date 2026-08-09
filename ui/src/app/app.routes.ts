import { Routes } from '@angular/router';
import { LandingComponent } from './pages/landing/landing.component';
import { LoginComponent } from './pages/auth/login.component';
import { RegisterComponent } from './pages/auth/register.component';
import { VerifyOtpComponent } from './pages/auth/verify-otp.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { WorkspaceOverviewComponent } from './pages/workspace/overview.component';
import { MembersComponent } from './pages/workspace/members.component';
import { RolesComponent } from './pages/workspace/roles.component';
import { JobListComponent } from './pages/job/job-list.component';
import { JobDetailComponent } from './pages/job/job-detail.component';
import { AcceptInviteComponent } from './pages/invite/accept-invite.component';
import { ProfileComponent } from './pages/profile/profile.component';
import { AppShellComponent } from './shell/app-shell.component';
import { authGuard } from './core/auth.guard';
import { guestGuard } from './core/guest.guard';
import { workspaceResolveGuard } from './core/workspace-resolve.guard';

export const routes: Routes = [
  { path: '', component: LandingComponent },
  { path: 'invite/:token', component: AcceptInviteComponent },
  {
    path: 'auth',
    children: [
      { path: 'login', component: LoginComponent, canActivate: [guestGuard] },
      { path: 'register', component: RegisterComponent, canActivate: [guestGuard] },
      { path: 'verify-otp', component: VerifyOtpComponent },
    ]
  },
  {
    path: 'app',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      { path: 'profile', component: ProfileComponent },
      {
        path: 'workspace/:id',
        canActivate: [workspaceResolveGuard],
        children: [
          { path: '', component: WorkspaceOverviewComponent },
          { path: 'jobs', component: JobListComponent },
          { path: 'jobs/:jobId', component: JobDetailComponent },
          { path: 'members', component: MembersComponent },
          { path: 'roles', component: RolesComponent },
        ]
      },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
    ]
  },
  { path: '**', redirectTo: '' },
];
