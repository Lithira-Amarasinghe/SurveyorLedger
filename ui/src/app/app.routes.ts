import { Routes } from '@angular/router';
import { LandingComponent } from './pages/landing/landing.component';
import { LoginComponent } from './pages/auth/login.component';
import { RegisterComponent } from './pages/auth/register.component';
import { VerifyOtpComponent } from './pages/auth/verify-otp.component';
import { ForgotPasswordComponent } from './pages/auth/forgot-password.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { WorkspaceOverviewComponent } from './pages/workspace/overview.component';
import { MembersComponent } from './pages/workspace/members.component';
import { RolesComponent } from './pages/workspace/roles.component';
import { JobListComponent } from './pages/job/job-list.component';
import { JobDetailComponent } from './pages/job/job-detail.component';
import { LandListComponent } from './pages/land/land-list.component';
import { LandDetailComponent } from './pages/land/land-detail.component';
import { ClientListComponent } from './pages/billing/clients/client-list.component';
import { QuotationListComponent } from './pages/billing/quotations/quotation-list.component';
import { InvoiceListComponent } from './pages/billing/invoices/invoice-list.component';
import { AcceptInviteComponent } from './pages/invite/accept-invite.component';
import { PublicDocumentUploadComponent } from './pages/document-upload/public-document-upload.component';
import { PublicSetLocationComponent } from './pages/set-location/public-set-location.component';
import { LandPrintComponent } from './pages/land/land-print.component';
import { InvoicePrintComponent } from './pages/billing/print/invoice-print.component';
import { QuotationPrintComponent } from './pages/billing/print/quotation-print.component';
import { ReceiptPrintComponent } from './pages/billing/print/receipt-print.component';
import { InvitationsComponent } from './pages/invitations/invitations.component';
import { ProfileComponent } from './pages/profile/profile.component';
import { AppShellComponent } from './shell/app-shell.component';
import { authGuard } from './core/auth.guard';
import { unsavedChangesGuard } from './core/unsaved-changes.guard';
import { guestGuard } from './core/guest.guard';
import { workspaceResolveGuard } from './core/workspace-resolve.guard';

export const routes: Routes = [
  { path: '', component: LandingComponent },
  { path: 'invite/:token', component: AcceptInviteComponent },
  { path: 'document-upload/:token', component: PublicDocumentUploadComponent },
  { path: 'set-location/:token', component: PublicSetLocationComponent },
  // No AppShellComponent wrapper - print layout is intentionally chrome-free, but still
  // needs auth since it calls the authenticated LandService (not a public share token).
  { path: 'app/workspace/:id/lands/:landId/print', component: LandPrintComponent, canActivate: [authGuard] },
  { path: 'app/workspace/:id/billing/invoices/:invoiceId/print', component: InvoicePrintComponent, canActivate: [authGuard] },
  { path: 'app/workspace/:id/billing/quotations/:quotationId/print', component: QuotationPrintComponent, canActivate: [authGuard] },
  { path: 'app/workspace/:id/billing/invoices/:invoiceId/payments/:paymentId/print', component: ReceiptPrintComponent, canActivate: [authGuard] },
  {
    path: 'auth',
    children: [
      { path: 'login', component: LoginComponent, canActivate: [guestGuard] },
      { path: 'register', component: RegisterComponent, canActivate: [guestGuard] },
      { path: 'verify-otp', component: VerifyOtpComponent },
      { path: 'forgot-password', component: ForgotPasswordComponent, canActivate: [guestGuard] },
    ]
  },
  {
    path: 'app',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      { path: 'profile', component: ProfileComponent, canDeactivate: [unsavedChangesGuard] },
      { path: 'invitations', component: InvitationsComponent },
      {
        path: 'workspace/:id',
        canActivate: [workspaceResolveGuard],
        children: [
          { path: '', component: WorkspaceOverviewComponent },
          { path: 'jobs', component: JobListComponent },
          { path: 'jobs/:jobId', component: JobDetailComponent, canDeactivate: [unsavedChangesGuard] },
          { path: 'lands', component: LandListComponent },
          { path: 'lands/:landId', component: LandDetailComponent, canDeactivate: [unsavedChangesGuard] },
          { path: 'billing/clients', component: ClientListComponent },
          { path: 'billing/quotations', component: QuotationListComponent },
          { path: 'billing/invoices', component: InvoiceListComponent },
          { path: 'members', component: MembersComponent },
          { path: 'roles', component: RolesComponent },
        ]
      },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
    ]
  },
  { path: '**', redirectTo: '' },
];
