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
import { QuotationListComponent } from './pages/billing/quotations/quotation-list.component';
import { InvoiceListComponent } from './pages/billing/invoices/invoice-list.component';
import { BillingDocumentFormPageComponent } from './pages/billing/document-form/billing-document-form-page.component';
import { WorkspaceExpenseListComponent } from './pages/billing/expenses/workspace-expense-list.component';
import { AcceptInviteComponent } from './pages/invite/accept-invite.component';
import { PublicDocumentUploadComponent } from './pages/document-upload/public-document-upload.component';
import { PublicSetLocationComponent } from './pages/set-location/public-set-location.component';
import { PublicLandDocumentUploadComponent } from './pages/land-document-upload/public-land-document-upload.component';
import { PublicLandMapViewComponent } from './pages/land-map-view/public-land-map-view.component';
import { LandPrintComponent } from './pages/land/land-print.component';
import { InvoicePrintComponent } from './pages/billing/print/invoice-print.component';
import { QuotationPrintComponent } from './pages/billing/print/quotation-print.component';
import { ReceiptPrintComponent } from './pages/billing/print/receipt-print.component';
import { InvitationsComponent } from './pages/invitations/invitations.component';
import { ReportsComponent } from './pages/workspace/reports.component';
import { ProfileComponent } from './pages/profile/profile.component';
import { AppShellComponent } from './shell/app-shell.component';
import { authGuard } from './core/auth.guard';
import { unsavedChangesGuard } from './core/unsaved-changes.guard';
import { jobAccessGuard } from './core/job-access.guard';
import { guestGuard } from './core/guest.guard';
import { workspaceResolveGuard } from './core/workspace-resolve.guard';

export const routes: Routes = [
  { path: '', component: LandingComponent },
  { path: 'invite/:token', component: AcceptInviteComponent },
  { path: 'document-upload/:token', component: PublicDocumentUploadComponent },
  { path: 'set-location/:token', component: PublicSetLocationComponent },
  { path: 'land-document-upload/:token', component: PublicLandDocumentUploadComponent },
  { path: 'land-map-view/:token', component: PublicLandMapViewComponent },
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
      { path: 'job/:workspaceId/:jobId', component: JobDetailComponent, canActivate: [jobAccessGuard], canDeactivate: [unsavedChangesGuard] },
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
          // Order matters: 'new' must be matched before ':landId' or it'd be swallowed as a param.
          // No canDeactivate here - an abandoned create form has nothing saved yet to warn about.
          { path: 'lands/new', component: LandDetailComponent },
          { path: 'lands/:landId', component: LandDetailComponent, canDeactivate: [unsavedChangesGuard] },
          { path: 'billing/quotations', component: QuotationListComponent },
          { path: 'billing/quotations/new', component: BillingDocumentFormPageComponent, data: { documentType: 'quotation' } },
          { path: 'billing/quotations/:quotationId/edit', component: BillingDocumentFormPageComponent, data: { documentType: 'quotation' } },
          { path: 'billing/invoices', component: InvoiceListComponent },
          { path: 'billing/invoices/new', component: BillingDocumentFormPageComponent, data: { documentType: 'invoice' } },
          { path: 'billing/invoices/:invoiceId/edit', component: BillingDocumentFormPageComponent, data: { documentType: 'invoice' } },
          { path: 'billing/expenses', component: WorkspaceExpenseListComponent },
          { path: 'members', component: MembersComponent },
          { path: 'roles', component: RolesComponent },
          { path: 'reports', component: ReportsComponent },
        ]
      },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
    ]
  },
  { path: '**', redirectTo: '' },
];
