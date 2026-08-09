using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface ILandService
{
    Task<Land> CreateAsync(Guid workspaceId, Guid callerUserId, LandRequest request);
    Task<List<Land>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query);
    Task<Land> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<Land> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid landId);

    Task<LandSurvey> AddSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandSurveyRequest request);
    Task<List<LandSurvey>> GetSurveysAsync(Guid workspaceId, Guid callerUserId, Guid landId);

    Task<LandDeed> AddDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandDeedRequest request);
    Task<List<LandDeed>> GetDeedsAsync(Guid workspaceId, Guid callerUserId, Guid landId);

    Task<LandBoundary> AddBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandBoundaryRequest request);
    Task<List<LandBoundary>> GetBoundariesAsync(Guid workspaceId, Guid callerUserId, Guid landId);
}

public class LandService : ILandService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly ILogger<LandService> _logger;

    public LandService(ApplicationDbContext context, ICasbinService casbinService, ILogger<LandService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _logger = logger;
    }

    public async Task<Land> CreateAsync(Guid workspaceId, Guid callerUserId, LandRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "create", workspaceId);

        var land = new Land
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Address = ToAddress(request.Address),
            Size = request.Size,
            SizeUnit = request.SizeUnit,
            GpsCoordinates = request.GpsCoordinates,
            Notes = request.Notes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Lands.AddAsync(land);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Land {LandId} created in workspace {WorkspaceId} by {UserId}", land.Id, workspaceId, callerUserId);
        return land;
    }

    /// <summary>
    /// Matches address fields (street/city/district/postal code) to let a job creator
    /// find and reuse an existing Land instead of re-entering it. Empty/null query
    /// returns the workspace's full Land list.
    /// </summary>
    public async Task<List<Land>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query)
    {
        await EnsureAllowedAsync(callerUserId, "view", workspaceId);

        var lands = _context.Lands.Where(l => l.WorkspaceId == workspaceId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            lands = lands.Where(l =>
                EF.Functions.Like(l.Address.Street, $"%{term}%") ||
                EF.Functions.Like(l.Address.City, $"%{term}%") ||
                EF.Functions.Like(l.Address.District, $"%{term}%") ||
                l.Deeds.Any(d => EF.Functions.Like(d.DeedNumber, $"%{term}%")) ||
                l.Surveys.Any(s => EF.Functions.Like(s.SurveyPlanNumber, $"%{term}%")));
        }

        return await lands.OrderByDescending(l => l.CreatedAt).ToListAsync();
    }

    public async Task<Land> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await EnsureAllowedAsync(callerUserId, "view", workspaceId);
        return await FindLandAsync(workspaceId, landId);
    }

    public async Task<Land> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        var land = await FindLandAsync(workspaceId, landId);

        land.Address = ToAddress(request.Address);
        land.Size = request.Size;
        land.SizeUnit = request.SizeUnit;
        land.GpsCoordinates = request.GpsCoordinates;
        land.Notes = request.Notes;
        land.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return land;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await EnsureAllowedAsync(callerUserId, "delete", workspaceId);
        var land = await FindLandAsync(workspaceId, landId);

        land.IsActive = false;
        land.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<LandSurvey> AddSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandSurveyRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        await FindLandAsync(workspaceId, landId);

        var survey = new LandSurvey
        {
            Id = Guid.NewGuid(),
            LandId = landId,
            SurveyPlanNumber = request.SurveyPlanNumber.Trim(),
            SurveyDate = request.SurveyDate,
            SurveyedByName = request.SurveyedByName?.Trim(),
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        await _context.LandSurveys.AddAsync(survey);
        await _context.SaveChangesAsync();
        return survey;
    }

    public async Task<List<LandSurvey>> GetSurveysAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await EnsureAllowedAsync(callerUserId, "view", workspaceId);
        await FindLandAsync(workspaceId, landId);

        return await _context.LandSurveys
            .Where(s => s.LandId == landId)
            .OrderByDescending(s => s.SurveyDate)
            .ToListAsync();
    }

    public async Task<LandDeed> AddDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandDeedRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        await FindLandAsync(workspaceId, landId);

        // A new current deed supersedes whichever one was current before - old deeds
        // stay in place (IsCurrent=false), never overwritten or deleted, so the
        // government-reissue history is always visible.
        if (request.IsCurrent)
        {
            var currentDeeds = await _context.LandDeeds
                .Where(d => d.LandId == landId && d.IsCurrent)
                .ToListAsync();
            foreach (var old in currentDeeds)
                old.IsCurrent = false;
        }

        var deed = new LandDeed
        {
            Id = Guid.NewGuid(),
            LandId = landId,
            DeedNumber = request.DeedNumber.Trim(),
            IssuedDate = request.IssuedDate,
            IsCurrent = request.IsCurrent,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        await _context.LandDeeds.AddAsync(deed);
        await _context.SaveChangesAsync();
        return deed;
    }

    public async Task<List<LandDeed>> GetDeedsAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await EnsureAllowedAsync(callerUserId, "view", workspaceId);
        await FindLandAsync(workspaceId, landId);

        return await _context.LandDeeds
            .Where(d => d.LandId == landId)
            .OrderByDescending(d => d.IssuedDate)
            .ToListAsync();
    }

    public async Task<LandBoundary> AddBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandBoundaryRequest request)
    {
        await EnsureAllowedAsync(callerUserId, "edit", workspaceId);
        await FindLandAsync(workspaceId, landId);

        var boundary = new LandBoundary
        {
            Id = Guid.NewGuid(),
            LandId = landId,
            Label = request.Label.Trim(),
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        await _context.LandBoundaries.AddAsync(boundary);
        await _context.SaveChangesAsync();
        return boundary;
    }

    public async Task<List<LandBoundary>> GetBoundariesAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await EnsureAllowedAsync(callerUserId, "view", workspaceId);
        await FindLandAsync(workspaceId, landId);

        return await _context.LandBoundaries
            .Where(b => b.LandId == landId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();
    }

    private async Task<Land> FindLandAsync(Guid workspaceId, Guid landId)
    {
        return await _context.Lands.FirstOrDefaultAsync(l => l.Id == landId && l.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Land not found");
    }

    private async Task EnsureAllowedAsync(Guid callerUserId, string action, Guid workspaceId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "land", action, workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException($"You do not have permission to {action} land records in this workspace.");
    }

    private static Address ToAddress(AddressDto? dto) => new()
    {
        Street = dto?.Street,
        City = dto?.City,
        District = dto?.District,
        PostalCode = dto?.PostalCode,
        Country = dto?.Country
    };
}
