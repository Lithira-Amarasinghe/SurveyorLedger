using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public static readonly Guid ViewWorkspaceId = new("00000000-0000-0000-0000-000000000101");
    public static readonly Guid EditWorkspaceId = new("00000000-0000-0000-0000-000000000102");
    public static readonly Guid DeleteWorkspaceId = new("00000000-0000-0000-0000-000000000103");
    public static readonly Guid ManageMembersId = new("00000000-0000-0000-0000-000000000104");
    public static readonly Guid ViewLandId = new("00000000-0000-0000-0000-000000000105");
    public static readonly Guid CreateLandId = new("00000000-0000-0000-0000-000000000106");
    public static readonly Guid EditLandId = new("00000000-0000-0000-0000-000000000107");
    public static readonly Guid DeleteLandId = new("00000000-0000-0000-0000-000000000108");
    public static readonly Guid ViewJobId = new("00000000-0000-0000-0000-000000000109");
    public static readonly Guid CreateJobId = new("00000000-0000-0000-0000-000000000110");
    public static readonly Guid EditJobId = new("00000000-0000-0000-0000-000000000111");
    public static readonly Guid DeleteJobId = new("00000000-0000-0000-0000-000000000112");
    public static readonly Guid ViewClientId = new("00000000-0000-0000-0000-000000000113");
    public static readonly Guid CreateClientId = new("00000000-0000-0000-0000-000000000114");
    public static readonly Guid ViewAllJobId = new("00000000-0000-0000-0000-000000000115");
    public static readonly Guid ViewAllLandId = new("00000000-0000-0000-0000-000000000116");
    public static readonly Guid ManageJobParticipantsId = new("00000000-0000-0000-0000-000000000138");
    public static readonly Guid ViewQuotationId = new("00000000-0000-0000-0000-000000000121");
    public static readonly Guid CreateQuotationId = new("00000000-0000-0000-0000-000000000122");
    public static readonly Guid EditQuotationId = new("00000000-0000-0000-0000-000000000123");
    public static readonly Guid DeleteQuotationId = new("00000000-0000-0000-0000-000000000124");
    public static readonly Guid ViewInvoiceId = new("00000000-0000-0000-0000-000000000125");
    public static readonly Guid CreateInvoiceId = new("00000000-0000-0000-0000-000000000126");
    public static readonly Guid EditInvoiceId = new("00000000-0000-0000-0000-000000000127");
    public static readonly Guid DeleteInvoiceId = new("00000000-0000-0000-0000-000000000128");
    public static readonly Guid ViewExpenseId = new("00000000-0000-0000-0000-000000000129");
    public static readonly Guid CreateExpenseId = new("00000000-0000-0000-0000-000000000130");
    public static readonly Guid EditExpenseId = new("00000000-0000-0000-0000-000000000131");
    public static readonly Guid DeleteExpenseId = new("00000000-0000-0000-0000-000000000132");
    public static readonly Guid ViewBudgetId = new("00000000-0000-0000-0000-000000000139");
    public static readonly Guid CreateBudgetId = new("00000000-0000-0000-0000-000000000140");
    public static readonly Guid EditBudgetId = new("00000000-0000-0000-0000-000000000141");
    public static readonly Guid DeleteBudgetId = new("00000000-0000-0000-0000-000000000142");
    public static readonly Guid ViewAllExpenseId = new("00000000-0000-0000-0000-000000000143");
    public static readonly Guid ViewReportId = new("00000000-0000-0000-0000-000000000144");
    public static readonly Guid ViewOrganizationId = new("00000000-0000-0000-0000-000000000145");
    public static readonly Guid ManageOrgMembersId = new("00000000-0000-0000-0000-000000000146");
    public static readonly Guid ManageSubscriptionId = new("00000000-0000-0000-0000-000000000147");
    public static readonly Guid CreateWorkspaceInOrgId = new("00000000-0000-0000-0000-000000000148");

    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Resource).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => new { x.Resource, x.Action, x.Scope }).IsUnique();

        builder.HasMany(x => x.RolePermissions).WithOne(x => x.Permission).HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);

        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new Permission { Id = ViewWorkspaceId, Name = "workspace.view", Description = "View workspace details.", Resource = "workspace", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditWorkspaceId, Name = "workspace.edit", Description = "Edit workspace settings.", Resource = "workspace", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteWorkspaceId, Name = "workspace.delete", Description = "Delete a workspace.", Resource = "workspace", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ManageMembersId, Name = "workspace.manage_members", Description = "Invite, remove, and change roles of workspace members.", Resource = "workspace", Action = "manage_members", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewLandId, Name = "land.view", Description = "View land records.", Resource = "land", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateLandId, Name = "land.create", Description = "Create land records.", Resource = "land", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditLandId, Name = "land.edit", Description = "Edit land records, surveys, deeds, and boundaries.", Resource = "land", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteLandId, Name = "land.delete", Description = "Delete land records.", Resource = "land", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewJobId, Name = "job.view", Description = "View jobs.", Resource = "job", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateJobId, Name = "job.create", Description = "Create jobs.", Resource = "job", Action = "create", Scope = null, CreatedAt = seededAt },
            // "edit" covers the job's own fields and land links only - who's assigned to
            // the job is a separate, narrower permission (see ManageJobParticipantsId) so a
            // Surveyor who can edit a job they're staffed on can't also add/remove other
            // people from it.
            new Permission { Id = EditJobId, Name = "job.edit", Description = "Edit jobs and land links.", Resource = "job", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteJobId, Name = "job.delete", Description = "Delete jobs.", Resource = "job", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ManageJobParticipantsId, Name = "job.manage_participants", Description = "Add, invite, and remove people assigned to a job.", Resource = "job", Action = "manage_participants", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewClientId, Name = "client.view", Description = "Search/view client contact records.", Resource = "client", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateClientId, Name = "client.create", Description = "Create a bare client contact record.", Resource = "client", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewAllJobId, Name = "job.view_all", Description = "View every job in the workspace, not just assigned ones.", Resource = "job", Action = "view_all", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewAllLandId, Name = "land.view_all", Description = "View every land record in the workspace, not just those linked to assigned jobs.", Resource = "land", Action = "view_all", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewQuotationId, Name = "quotation.view", Description = "View quotations.", Resource = "quotation", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateQuotationId, Name = "quotation.create", Description = "Create quotations.", Resource = "quotation", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditQuotationId, Name = "quotation.edit", Description = "Edit quotations.", Resource = "quotation", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteQuotationId, Name = "quotation.delete", Description = "Delete quotations.", Resource = "quotation", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewInvoiceId, Name = "invoice.view", Description = "View invoices and payments.", Resource = "invoice", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateInvoiceId, Name = "invoice.create", Description = "Create invoices and record payments.", Resource = "invoice", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditInvoiceId, Name = "invoice.edit", Description = "Edit invoices.", Resource = "invoice", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteInvoiceId, Name = "invoice.delete", Description = "Delete/cancel invoices.", Resource = "invoice", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewExpenseId, Name = "expense.view", Description = "View job expenses.", Resource = "expense", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateExpenseId, Name = "expense.create", Description = "Record job expenses.", Resource = "expense", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditExpenseId, Name = "expense.edit", Description = "Edit job expenses.", Resource = "expense", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteExpenseId, Name = "expense.delete", Description = "Delete job expenses.", Resource = "expense", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewBudgetId, Name = "budget.view", Description = "View a job's estimated fee/cost budget.", Resource = "budget", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateBudgetId, Name = "budget.create", Description = "Set a job's budget for the first time.", Resource = "budget", Action = "create", Scope = null, CreatedAt = seededAt },
            new Permission { Id = EditBudgetId, Name = "budget.edit", Description = "Edit a job's existing budget.", Resource = "budget", Action = "edit", Scope = null, CreatedAt = seededAt },
            new Permission { Id = DeleteBudgetId, Name = "budget.delete", Description = "Clear a job's budget.", Resource = "budget", Action = "delete", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewAllExpenseId, Name = "expense.view_all", Description = "View every StaffCost expense on a job, not just the caller's own payee rows.", Resource = "expense", Action = "view_all", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewReportId, Name = "report.view", Description = "View workspace-wide financial reports.", Resource = "report", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ViewOrganizationId, Name = "organization.view", Description = "View organization details and its workspaces.", Resource = "organization", Action = "view", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ManageOrgMembersId, Name = "organization.manage_members", Description = "Add and remove organization members.", Resource = "organization", Action = "manage_members", Scope = null, CreatedAt = seededAt },
            new Permission { Id = ManageSubscriptionId, Name = "organization.manage_subscription", Description = "Change the organization's subscription tier.", Resource = "organization", Action = "manage_subscription", Scope = null, CreatedAt = seededAt },
            new Permission { Id = CreateWorkspaceInOrgId, Name = "organization.create_workspace", Description = "Create a new workspace under the organization, subject to its tier's workspace limit.", Resource = "organization", Action = "create_workspace", Scope = null, CreatedAt = seededAt }
        );
    }
}
