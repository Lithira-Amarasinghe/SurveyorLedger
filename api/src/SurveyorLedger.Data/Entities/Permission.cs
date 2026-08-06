namespace SurveyorLedger.Data.Entities;

public class Permission
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Resource { get; set; }
    public string Action { get; set; }
    public string? Scope { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
