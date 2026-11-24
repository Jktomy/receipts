using Application.Interfaces.Services;
using Application.Models.Tiller;
using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

// Native Tiller account mappings: local receipt account -> Tiller account,
// bound to one spreadsheet. Update can change the Tiller side but never the
// spreadsheet binding; cross-spreadsheet moves require delete + recreate so
// synced history is never silently re-pointed.
public class TillerAccountMappingService(
	IDbContextFactory<ApplicationDbContext> contextFactory) : ITillerAccountMappingService
{
	public async Task<List<TillerAccountMappingDto>> GetBySpreadsheetIdAsync(
		string spreadsheetId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		return await context.Set<TillerAccountMappingEntity>()
			.AsNoTracking()
			.Where(e => e.SpreadsheetId == spreadsheetId)
			.Select(e => new TillerAccountMappingDto(
				e.Id, e.ReceiptsAccountId, e.TillerAccountId, e.TillerAccountName, e.SpreadsheetId))
			.ToListAsync(cancellationToken);
	}

	public async Task<TillerAccountMappingDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerAccountMappingEntity? entity = await context.Set<TillerAccountMappingEntity>()
			.AsNoTracking()
			.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
		return entity is null ? null : ToDto(entity);
	}

	public async Task<TillerAccountMappingDto> CreateAsync(
		Guid receiptsAccountId,
		string tillerAccountId,
		string tillerAccountName,
		string spreadsheetId,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tillerAccountId);
		ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);

		DateTimeOffset now = DateTimeOffset.UtcNow;
		TillerAccountMappingEntity entity = new()
		{
			Id = Guid.NewGuid(),
			ReceiptsAccountId = receiptsAccountId,
			TillerAccountId = tillerAccountId,
			TillerAccountName = tillerAccountName,
			SpreadsheetId = spreadsheetId,
			CreatedAt = now,
			UpdatedAt = now,
		};

		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		context.Set<TillerAccountMappingEntity>().Add(entity);
		await context.SaveChangesAsync(cancellationToken);
		return ToDto(entity);
	}

	public async Task UpdateAsync(
		Guid id,
		string tillerAccountId,
		string tillerAccountName,
		string spreadsheetId,
		CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerAccountMappingEntity? entity = await context.Set<TillerAccountMappingEntity>()
			.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

		if (entity is null)
		{
			return;
		}

		if (!string.Equals(entity.SpreadsheetId, spreadsheetId, StringComparison.Ordinal))
		{
			throw new ArgumentException(
				"A mapping cannot be moved between Tiller spreadsheets. Delete it and create a new one.",
				nameof(spreadsheetId));
		}

		entity.TillerAccountId = tillerAccountId;
		entity.TillerAccountName = tillerAccountName;
		entity.UpdatedAt = DateTimeOffset.UtcNow;
		await context.SaveChangesAsync(cancellationToken);
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerAccountMappingEntity? entity = await context.Set<TillerAccountMappingEntity>()
			.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

		if (entity is null)
		{
			return;
		}

		context.Set<TillerAccountMappingEntity>().Remove(entity);
		await context.SaveChangesAsync(cancellationToken);
	}

	private static TillerAccountMappingDto ToDto(TillerAccountMappingEntity entity) => new(
		entity.Id,
		entity.ReceiptsAccountId,
		entity.TillerAccountId,
		entity.TillerAccountName,
		entity.SpreadsheetId);
}
