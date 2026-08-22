using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Organization;

public class SubscriptionTierRequest
{
    [Required]
    [RegularExpression("^(Free|Pro|Business)$", ErrorMessage = "Tier must be 'Free', 'Pro', or 'Business'.")]
    public required string Tier { get; set; }
}
