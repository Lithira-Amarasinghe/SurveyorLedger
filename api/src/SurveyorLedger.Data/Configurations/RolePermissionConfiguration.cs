using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();

        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        RolePermission Grant(Guid id, Guid roleId, Guid permissionId) =>
            new() { Id = id, RoleId = roleId, PermissionId = permissionId, CreatedAt = seededAt };

        builder.HasData(
            // Admin: full workspace control
            Grant(new Guid("00000000-0000-0000-0000-000000000201"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000202"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000203"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000204"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ManageMembersId),
            // Surveyor, Client: view only
            Grant(new Guid("00000000-0000-0000-0000-000000000206"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewWorkspaceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000207"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewWorkspaceId),
            // Land - Admin: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000208"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000209"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000210"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000211"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteLandId),
            // Land - Surveyor: view/create/edit, not delete (captures/updates land data in the field)
            Grant(new Guid("00000000-0000-0000-0000-000000000216"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000217"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000218"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditLandId),
            // Land - Client: view only
            Grant(new Guid("00000000-0000-0000-0000-000000000219"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewLandId),
            // Job - Admin: full access
            Grant(new Guid("00000000-0000-0000-0000-000000000220"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000221"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000222"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000223"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteJobId),
            // Job - Surveyor: view/edit only - job creation is an Admin decision, not
            // something a Surveyor can do on their own.
            Grant(new Guid("00000000-0000-0000-0000-000000000228"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000230"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditJobId),
            // Job - Client: view only (further scoped to their own jobs in JobService, not Casbin)
            Grant(new Guid("00000000-0000-0000-0000-000000000231"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewJobId),
            // Client contacts - Admin/Surveyor: view+create (whoever can field the
            // call and capture a client). The Client role gets nothing here - a client
            // doesn't manage other clients.
            Grant(new Guid("00000000-0000-0000-0000-000000000232"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000233"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000236"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewClientId),
            Grant(new Guid("00000000-0000-0000-0000-000000000237"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateClientId),
            // Job view-all - Admin sees every job in the workspace; Surveyor/Client
            // are scoped to jobs they've been explicitly assigned (job-scoped UserAccess).
            Grant(new Guid("00000000-0000-0000-0000-000000000238"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewAllJobId),
            // Land view-all - staff (Admin, Surveyor) see every land record in the
            // workspace; Client is scoped to land linked to a job they're assigned to
            // (see ScopedAccessService.EnsureLandAccessAsync / AccessibleLandIds).
            Grant(new Guid("00000000-0000-0000-0000-000000000239"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewAllLandId),
            Grant(new Guid("00000000-0000-0000-0000-000000000240"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewAllLandId),
            // Member: workspace membership only. No job/land access at workspace scope -
            // capability comes purely from job-scope grants (Surveyor or Client on a job).
            Grant(new Guid("00000000-0000-0000-0000-000000000241"), RoleConfiguration.MemberRoleId, PermissionConfiguration.ViewWorkspaceId),
            // Billing (quotation/invoice) - Admin: full CRUD.
            Grant(new Guid("00000000-0000-0000-0000-000000000246"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000247"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000248"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000249"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000250"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewInvoiceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000251"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateInvoiceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000252"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditInvoiceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000253"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteInvoiceId),
            // Billing - Surveyor: view/create/edit, no delete.
            Grant(new Guid("00000000-0000-0000-0000-000000000257"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000258"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000259"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000260"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewInvoiceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000261"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateInvoiceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000262"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditInvoiceId),
            // Billing - Client and Member: view only. Billing data is financial; phase 1
            // does not build a client-scoped billing portal.
            Grant(new Guid("00000000-0000-0000-0000-000000000264"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000265"), RoleConfiguration.ClientRoleId, PermissionConfiguration.ViewInvoiceId),
            Grant(new Guid("00000000-0000-0000-0000-000000000267"), RoleConfiguration.MemberRoleId, PermissionConfiguration.ViewQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000268"), RoleConfiguration.MemberRoleId, PermissionConfiguration.ViewInvoiceId),
            // Finance: job-scoped view of invoices/quotations only. Needs job.view too -
            // EnsureJobAccessAsync enforces against the "job" resource as the access gate for
            // anything hanging off a job (same mechanism Milestone/Document already use), not
            // against "invoice"/"quotation" directly - without job.view here, a Finance-role
            // caller would be rejected before InvoiceService's own permission check even runs.
            Grant(new Guid("00000000-0000-0000-0000-000000000284"), RoleConfiguration.FinanceRoleId, PermissionConfiguration.ViewJobId),
            Grant(new Guid("00000000-0000-0000-0000-000000000282"), RoleConfiguration.FinanceRoleId, PermissionConfiguration.ViewQuotationId),
            Grant(new Guid("00000000-0000-0000-0000-000000000283"), RoleConfiguration.FinanceRoleId, PermissionConfiguration.ViewInvoiceId),
            // WorkspaceMember: least-privilege membership granted automatically when a role
            // requires workspace-level presence. View workspace only, nothing else.
            Grant(new Guid("00000000-0000-0000-0000-000000000802"), RoleConfiguration.WorkspaceMemberRoleId, PermissionConfiguration.ViewWorkspaceId),
            // Expense - Admin: full CRUD. Surveyor: view/create/edit (field staff record
            // their own costs), no delete. Client: nothing (financial data).
            Grant(new Guid("00000000-0000-0000-0000-000000000269"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewExpenseId),
            Grant(new Guid("00000000-0000-0000-0000-000000000270"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateExpenseId),
            Grant(new Guid("00000000-0000-0000-0000-000000000271"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditExpenseId),
            Grant(new Guid("00000000-0000-0000-0000-000000000272"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteExpenseId),
            Grant(new Guid("00000000-0000-0000-0000-000000000273"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewExpenseId),
            Grant(new Guid("00000000-0000-0000-0000-000000000274"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.CreateExpenseId),
            Grant(new Guid("00000000-0000-0000-0000-000000000275"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.EditExpenseId),
            // StaffPayment - Admin: full CRUD + view_all (payroll is a stricter surface than
            // expenses). Surveyor: view only, and only their own (view_all withheld - the
            // service layer filters to UserId == callerUserId without it). Client: nothing.
            Grant(new Guid("00000000-0000-0000-0000-000000000276"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewStaffPaymentId),
            Grant(new Guid("00000000-0000-0000-0000-000000000277"), RoleConfiguration.AdminRoleId, PermissionConfiguration.CreateStaffPaymentId),
            Grant(new Guid("00000000-0000-0000-0000-000000000278"), RoleConfiguration.AdminRoleId, PermissionConfiguration.EditStaffPaymentId),
            Grant(new Guid("00000000-0000-0000-0000-000000000279"), RoleConfiguration.AdminRoleId, PermissionConfiguration.DeleteStaffPaymentId),
            Grant(new Guid("00000000-0000-0000-0000-000000000280"), RoleConfiguration.AdminRoleId, PermissionConfiguration.ViewAllStaffPaymentId),
            Grant(new Guid("00000000-0000-0000-0000-000000000281"), RoleConfiguration.SurveyorRoleId, PermissionConfiguration.ViewStaffPaymentId)
        );
    }
}
