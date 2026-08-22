using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Organization;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrganizationController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public OrganizationController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrganizationInfo>>> CreateOrganization([FromBody] OrganizationRequest request)
    {
        var org = await _organizationService.CreateOrganizationAsync(CurrentUserId, request);
        return CreatedAtAction(nameof(GetOrganizationById), new { id = org.Id }, ApiResponse<OrganizationInfo>.Ok(org));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<OrganizationInfo>>>> ListOrganizations()
    {
        var orgs = await _organizationService.GetUserOrganizationsAsync(CurrentUserId);
        return Ok(ApiResponse<List<OrganizationInfo>>.Ok(orgs));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<OrganizationInfo>>> GetOrganizationById(Guid id)
    {
        var org = await _organizationService.GetOrganizationAsync(id, CurrentUserId);
        if (org == null)
            return NotFound(ApiResponse<object>.Fail("Organization not found"));
        return Ok(ApiResponse<OrganizationInfo>.Ok(org));
    }

    [HttpGet("{id}/members")]
    public async Task<ActionResult<ApiResponse<List<OrganizationMemberInfo>>>> GetMembers(Guid id)
    {
        var members = await _organizationService.GetMembersAsync(id, CurrentUserId);
        return Ok(ApiResponse<List<OrganizationMemberInfo>>.Ok(members));
    }

    [HttpPost("{id}/members/{targetUserId}")]
    public async Task<ActionResult<ApiResponse<object>>> AddMember(Guid id, Guid targetUserId)
    {
        await _organizationService.AddMemberAsync(id, targetUserId, CurrentUserId);
        return Ok(ApiResponse<object>.Ok(null!));
    }

    [HttpDelete("{id}/members/{targetUserId}")]
    public async Task<ActionResult<ApiResponse<object>>> RemoveMember(Guid id, Guid targetUserId)
    {
        await _organizationService.RemoveMemberAsync(id, targetUserId, CurrentUserId);
        return Ok(ApiResponse<object>.Ok(null!));
    }

    [HttpPut("{id}/subscription")]
    public async Task<ActionResult<ApiResponse<OrganizationInfo>>> UpdateSubscription(Guid id, [FromBody] SubscriptionTierRequest request)
    {
        var org = await _organizationService.UpdateSubscriptionTierAsync(id, CurrentUserId, request.Tier);
        return Ok(ApiResponse<OrganizationInfo>.Ok(org));
    }
}
