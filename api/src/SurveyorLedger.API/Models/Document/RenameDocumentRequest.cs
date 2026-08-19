using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Document;

/// <summary>Shared rename payload - one DTO for every document/photo rename endpoint (Job documents, Land documents, Land photos, Survey/Deed attachments), not a copy per owner type.</summary>
public class RenameDocumentRequest
{
    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;
}
