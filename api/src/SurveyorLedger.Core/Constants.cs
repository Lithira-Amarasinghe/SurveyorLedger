namespace SurveyorLedger.Core;

public static class Constants
{
    public static class ClaimNames
    {
        public const string UserId = "user_id";
        public const string Email = "email";
        public const string WorkspaceId = "workspace_id";
        public const string Role = "role";
    }

    public static class ErrorCodes
    {
        public const string InvalidCredentials = "INVALID_CREDENTIALS";
        public const string InvalidOtp = "INVALID_OTP";
        public const string UserNotFound = "USER_NOT_FOUND";
        public const string UserAlreadyExists = "USER_ALREADY_EXISTS";
        public const string WorkspaceNotFound = "WORKSPACE_NOT_FOUND";
        public const string Unauthorized = "UNAUTHORIZED";
        public const string Forbidden = "FORBIDDEN";
        public const string TokenExpired = "TOKEN_EXPIRED";
        public const string InvalidToken = "INVALID_TOKEN";
        public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
        public const string InternalServerError = "INTERNAL_SERVER_ERROR";
        public const string ValidationFailed = "VALIDATION_FAILED";
        public const string EmailSendFailed = "EMAIL_SEND_FAILED";
        public const string AuthorizationSetupFailed = "AUTHORIZATION_SETUP_FAILED";
        public const string InvitationNotFound = "INVITATION_NOT_FOUND";
        public const string InvitationExpired = "INVITATION_EXPIRED";
        public const string InvitationEmailMismatch = "INVITATION_EMAIL_MISMATCH";
        public const string AlreadyMember = "ALREADY_MEMBER";
        public const string LastAdminRequired = "LAST_ADMIN_REQUIRED";
        public const string CannotModifyOwner = "CANNOT_MODIFY_OWNER";
        public const string RegistrationExpired = "REGISTRATION_EXPIRED";
        public const string ResendCooldown = "RESEND_COOLDOWN";
        public const string EmailAlreadySet = "EMAIL_ALREADY_SET";
        public const string AccountLocked = "ACCOUNT_LOCKED";
        public const string TooManyRequests = "TOO_MANY_REQUESTS";
        public const string WorkspaceLimitReached = "WORKSPACE_LIMIT_REACHED";
    }

    public static class Permissions
    {
        public const string CreateWorkspace = "workspace.create";
        public const string ViewWorkspace = "workspace.view";
        public const string EditWorkspace = "workspace.edit";
        public const string DeleteWorkspace = "workspace.delete";
        public const string ManageWorkspaceMembers = "workspace.manage_members";
    }

    public static class OrganizationPermissions
    {
        public const string ViewOrganization = "organization.view";
        public const string ManageOrganizationMembers = "organization.manage_members";
        public const string ManageSubscription = "organization.manage_subscription";
        public const string CreateWorkspaceInOrg = "organization.create_workspace";
    }

    public static class SystemRoles
    {
        public const string Admin = "Admin";
        public const string Surveyor = "Surveyor";
        public const string Client = "Client";
        public const string Member = "Member";
        public const string Finance = "Finance";
        public const string OrgOwner = "OrgOwner";
        public const string OrgMember = "OrgMember";
    }

    public static class ScopeTypes
    {
        public const string Workspace = "Workspace";
        public const string Job = "Job";
        public const string Organization = "Organization";
    }

    /// <summary>Root-to-leaf order of the access hierarchy - the one place this order is
    /// declared. GetAccessibleJobsAsync (ScopedAccessService) walks it broadest-first.</summary>
    public static readonly string[] ScopeHierarchy =
        { ScopeTypes.Organization, ScopeTypes.Workspace, ScopeTypes.Job };

    /// <summary>Hardcoded subscription tiers - max active Workspaces an Organization on
    /// that tier may hold. int.MaxValue stands in for "unlimited".</summary>
    public static class OrganizationTiers
    {
        public const string Free = "Free";
        public const string Pro = "Pro";
        public const string Business = "Business";

        public static readonly IReadOnlyDictionary<string, int> MaxWorkspaces = new Dictionary<string, int>
        {
            [Free] = 1,
            [Pro] = 5,
            [Business] = int.MaxValue
        };
    }
}
