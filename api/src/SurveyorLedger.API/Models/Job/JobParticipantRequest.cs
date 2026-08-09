using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Job;

public class JobParticipantRequest
{
    [Required(ErrorMessage = "ParticipantType is required.")]
    [RegularExpression("^(Client|Surveyor|Assistant|Other)$",
        ErrorMessage = "ParticipantType must be Client, Surveyor, Assistant, or Other.")]
    public required string ParticipantType { get; set; }
}
