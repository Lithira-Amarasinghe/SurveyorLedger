using Microsoft.AspNetCore.Http;
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

    Task<Land> SetLocationAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandLocationRequest request);
    Task<string> GenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<string> RegenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task RevokeLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<Land> GetByLocationShareTokenAsync(string token);
    Task<Land> SetLocationViaShareTokenAsync(string token, LandLocationRequest request);

    Task<LandSurvey> AddSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandSurveyRequest request);
    Task<List<LandSurvey>> GetSurveysAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<LandSurvey> UpdateSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid surveyId, LandSurveyRequest request);
    Task DeleteSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid surveyId);

    Task<LandDeed> AddDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandDeedRequest request);
    Task<List<LandDeed>> GetDeedsAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<LandDeed> UpdateDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid deedId, LandDeedRequest request);
    Task DeleteDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid deedId);

    Task<LandBoundary> AddBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandBoundaryRequest request);
    Task<List<LandBoundary>> GetBoundariesAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<LandBoundary> UpdateBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid boundaryId, LandBoundaryRequest request);
    Task DeleteBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid boundaryId);

    Task<LandPhoto> UploadPhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, IFormFile file);
    Task<List<LandPhoto>> GetPhotosAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<(LandPhoto photo, Stream content)> GetPhotoFileAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId);
    Task DeletePhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId);
}

public class LandService : ILandService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<LandService> _logger;

    public LandService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, ILogger<LandService> logger)
    {
        _context = context;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Land> CreateAsync(Guid workspaceId, Guid callerUserId, LandRequest request)
    {
        // No record exists yet, so only the workspace-level create right applies here.
        await _access.EnsureAllowedAsync(callerUserId, "land", "create", workspaceId);
        await ValidateOwnerAsync(request);

        var land = new Land
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Address = ToAddress(request.Address),
            Size = request.Size,
            SizeUnit = request.SizeUnit,
            GpsCoordinates = request.GpsCoordinates,
            Notes = request.Notes,
            OwnerId = request.OwnerId,
            OwnerName = request.OwnerId == null ? request.OwnerName?.Trim() : null,
            OwnerPhone = request.OwnerId == null ? request.OwnerPhone?.Trim() : null,
            OwnerEmail = request.OwnerId == null ? request.OwnerEmail?.Trim() : null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Lands.AddAsync(land);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Land {LandId} created in workspace {WorkspaceId} by {UserId}", land.Id, workspaceId, callerUserId);
        return await FindLandAsync(workspaceId, land.Id);
    }

    /// <summary>
    /// Matches address fields (street/city/district/postal code) to let a job creator
    /// find and reuse an existing Land instead of re-entering it. Empty/null query
    /// returns the workspace's full Land list.
    /// </summary>
    public async Task<List<Land>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var lands = _context.Lands.Include(l => l.Owner).Where(l => l.WorkspaceId == workspaceId);

        // Without land.view_all (Admin/Surveyor), land is only visible through a job the
        // caller is assigned to - otherwise a Client with zero job assignments could list
        // every land record in the workspace, including other clients' properties.
        if (!await _access.HasViewAllAsync(callerUserId, "land", workspaceId))
        {
            var accessibleLandIds = _access.AccessibleLandIds(callerUserId);
            lands = lands.Where(l => accessibleLandIds.Contains(l.Id));
        }

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
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        return await FindLandAsync(workspaceId, landId);
    }

    public async Task<Land> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await ValidateOwnerAsync(request);
        var land = await FindLandAsync(workspaceId, landId);

        land.Address = ToAddress(request.Address);
        land.Size = request.Size;
        land.SizeUnit = request.SizeUnit;
        land.GpsCoordinates = request.GpsCoordinates;
        land.Notes = request.Notes;
        land.OwnerId = request.OwnerId;
        land.OwnerName = request.OwnerId == null ? request.OwnerName?.Trim() : null;
        land.OwnerPhone = request.OwnerId == null ? request.OwnerPhone?.Trim() : null;
        land.OwnerEmail = request.OwnerId == null ? request.OwnerEmail?.Trim() : null;
        land.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return await FindLandAsync(workspaceId, landId);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "delete");
        var land = await FindLandAsync(workspaceId, landId);

        land.IsActive = false;
        land.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Land> SetLocationAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandLocationRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        land.Latitude = request.Latitude;
        land.Longitude = request.Longitude;
        land.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return land;
    }

    public async Task<string> GenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        // Idempotent: an existing active token is returned as-is, not overwritten -
        // regenerating is a distinct, explicit action (see RegenerateLocationShareLinkAsync).
        if (land.LocationShareToken != null)
            return land.LocationShareToken;

        land.LocationShareToken = Guid.NewGuid().ToString("N");
        await _context.SaveChangesAsync();
        return land.LocationShareToken;
    }

    public async Task<string> RegenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        land.LocationShareToken = Guid.NewGuid().ToString("N");
        await _context.SaveChangesAsync();
        return land.LocationShareToken;
    }

    public async Task RevokeLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        land.LocationShareToken = null;
        await _context.SaveChangesAsync();
    }

    public async Task<Land> GetByLocationShareTokenAsync(string token)
    {
        return await _context.Lands.FirstOrDefaultAsync(l => l.LocationShareToken == token && l.IsActive)
            ?? throw new NotFoundException("Link not found");
    }

    public async Task<Land> SetLocationViaShareTokenAsync(string token, LandLocationRequest request)
    {
        var land = await GetByLocationShareTokenAsync(token);

        land.Latitude = request.Latitude;
        land.Longitude = request.Longitude;
        land.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return land;
    }

    public async Task<LandSurvey> AddSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandSurveyRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
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
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        await FindLandAsync(workspaceId, landId);

        return await _context.LandSurveys
            .Where(s => s.LandId == landId)
            .OrderByDescending(s => s.SurveyDate)
            .ToListAsync();
    }

    public async Task<LandSurvey> UpdateSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid surveyId, LandSurveyRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);
        var survey = await FindSurveyAsync(landId, surveyId);

        survey.SurveyPlanNumber = request.SurveyPlanNumber.Trim();
        survey.SurveyDate = request.SurveyDate;
        survey.SurveyedByName = request.SurveyedByName?.Trim();
        survey.Notes = request.Notes;

        await _context.SaveChangesAsync();
        return survey;
    }

    /// <summary>Hard delete - corrects a mis-entered record, not meaningful history to preserve once wrong.</summary>
    public async Task DeleteSurveyAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid surveyId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);
        var survey = await FindSurveyAsync(landId, surveyId);

        _context.LandSurveys.Remove(survey);
        await _context.SaveChangesAsync();
    }

    public async Task<LandDeed> AddDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandDeedRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
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
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        await FindLandAsync(workspaceId, landId);

        return await _context.LandDeeds
            .Where(d => d.LandId == landId)
            .OrderByDescending(d => d.IssuedDate)
            .ToListAsync();
    }

    public async Task<LandDeed> UpdateDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid deedId, LandDeedRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);
        var deed = await FindDeedAsync(landId, deedId);

        if (request.IsCurrent && !deed.IsCurrent)
        {
            var currentDeeds = await _context.LandDeeds
                .Where(d => d.LandId == landId && d.IsCurrent && d.Id != deedId)
                .ToListAsync();
            foreach (var old in currentDeeds)
                old.IsCurrent = false;
        }

        deed.DeedNumber = request.DeedNumber.Trim();
        deed.IssuedDate = request.IssuedDate;
        deed.IsCurrent = request.IsCurrent;
        deed.Notes = request.Notes;

        await _context.SaveChangesAsync();
        return deed;
    }

    /// <summary>Hard delete - corrects a mis-entered record, not meaningful history to preserve once wrong.</summary>
    public async Task DeleteDeedAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid deedId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);
        var deed = await FindDeedAsync(landId, deedId);

        _context.LandDeeds.Remove(deed);
        await _context.SaveChangesAsync();
    }

    public async Task<LandBoundary> AddBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandBoundaryRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
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
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        await FindLandAsync(workspaceId, landId);

        return await _context.LandBoundaries
            .Where(b => b.LandId == landId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();
    }

    public async Task<LandBoundary> UpdateBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid boundaryId, LandBoundaryRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);
        var boundary = await FindBoundaryAsync(landId, boundaryId);

        boundary.Label = request.Label.Trim();
        boundary.Description = request.Description;

        await _context.SaveChangesAsync();
        return boundary;
    }

    /// <summary>Hard delete - corrects a mis-entered record, not meaningful history to preserve once wrong.</summary>
    public async Task DeleteBoundaryAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid boundaryId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);
        var boundary = await FindBoundaryAsync(landId, boundaryId);

        _context.LandBoundaries.Remove(boundary);
        await _context.SaveChangesAsync();
    }

    private static readonly HashSet<string> AllowedPhotoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public async Task<LandPhoto> UploadPhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, IFormFile file)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedPhotoExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedPhotoExtensions)}.");
        if (file.Length > DocumentService.MaxFileSizeBytes)
            throw new ValidationException($"File exceeds the {DocumentService.MaxFileSizeBytes / (1024 * 1024)}MB size limit.");

        var storedFileName = $"{Guid.NewGuid():N}_{file.FileName}";
        var relativePath = $"{workspaceId}/land/{landId}/{storedFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorage.SaveAsync(stream, relativePath, CancellationToken.None);
        }

        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var photo = new LandPhoto
        {
            Id = Guid.NewGuid(),
            LandId = landId,
            FileName = file.FileName,
            StoredPath = relativePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedBy = callerPersonId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.LandPhotos.AddAsync(photo);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Photo {PhotoId} uploaded to land {LandId} by {UserId}", photo.Id, landId, callerUserId);
        return await _context.LandPhotos.Include(p => p.UploadedByUser).FirstAsync(p => p.Id == photo.Id);
    }

    public async Task<List<LandPhoto>> GetPhotosAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        await FindLandAsync(workspaceId, landId);

        return await _context.LandPhotos.Include(p => p.UploadedByUser)
            .Where(p => p.LandId == landId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<(LandPhoto photo, Stream content)> GetPhotoFileAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        var photo = await FindPhotoAsync(landId, photoId);
        var content = await _fileStorage.OpenAsync(photo.StoredPath, CancellationToken.None);
        return (photo, content);
    }

    public async Task DeletePhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var photo = await FindPhotoAsync(landId, photoId);

        await _fileStorage.DeleteAsync(photo.StoredPath, CancellationToken.None);
        _context.LandPhotos.Remove(photo);
        await _context.SaveChangesAsync();
    }

    private async Task<LandPhoto> FindPhotoAsync(Guid landId, Guid photoId)
    {
        return await _context.LandPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.LandId == landId)
            ?? throw new NotFoundException("Photo not found");
    }

    private async Task<Land> FindLandAsync(Guid workspaceId, Guid landId)
    {
        return await _context.Lands.Include(l => l.Owner)
            .FirstOrDefaultAsync(l => l.Id == landId && l.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Land not found");
    }

    /// <summary>
    /// Enforces "exactly one owner form" - either an existing account (OwnerId) or plain
    /// contact info (OwnerName), never both. OwnerId isn't restricted to this workspace's
    /// members - land ownership is a data-tracking concern, decoupled from workspace access.
    /// </summary>
    private async Task ValidateOwnerAsync(LandRequest request)
    {
        var hasAccountOwner = request.OwnerId.HasValue;
        var hasPlainOwner = !string.IsNullOrWhiteSpace(request.OwnerName);

        if (hasAccountOwner && hasPlainOwner)
            throw new ValidationException("Set either OwnerId or OwnerName, not both.");

        if (hasAccountOwner)
        {
            var ownerExists = await _context.People.AnyAsync(p => p.Id == request.OwnerId!.Value && p.IsActive);
            if (!ownerExists)
                throw new ValidationException("OwnerId does not match an existing account.");
        }
    }

    private async Task<LandSurvey> FindSurveyAsync(Guid landId, Guid surveyId)
    {
        return await _context.LandSurveys.FirstOrDefaultAsync(s => s.Id == surveyId && s.LandId == landId)
            ?? throw new NotFoundException("Survey record not found");
    }

    private async Task<LandDeed> FindDeedAsync(Guid landId, Guid deedId)
    {
        return await _context.LandDeeds.FirstOrDefaultAsync(d => d.Id == deedId && d.LandId == landId)
            ?? throw new NotFoundException("Deed record not found");
    }

    private async Task<LandBoundary> FindBoundaryAsync(Guid landId, Guid boundaryId)
    {
        return await _context.LandBoundaries.FirstOrDefaultAsync(b => b.Id == boundaryId && b.LandId == landId)
            ?? throw new NotFoundException("Boundary record not found");
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
