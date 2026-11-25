using System.Text.Json;
using Application.Interfaces.Services;
using Application.Models.Tiller;
using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services;

// Native Tiller connection settings. The selected spreadsheet is the ONLY
// spreadsheet every Tiller read path may touch; selection validates against
// the live readable list so scope can never drift to an unknown sheet.
public class TillerSelectionService(
	IDbContextFactory<ApplicationDbContext> contextFactory,
	ITillerApiClient tillerApiClient,
	IConfiguration? configuration = null) : ITillerSelectionService
{
	public async Task<string?> GetSelectedSpreadsheetIdAsync(CancellationToken cancellationToken)
	{
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerSelectedSpreadsheetEntity? row = await context.Set<TillerSelectedSpreadsheetEntity>()
			.AsNoTracking()
			.SingleOrDefaultAsync(cancellationToken);
		return row?.SpreadsheetId;
	}

	public async Task<TillerExecutorMetadata?> GetExecutorMetadataAsync(CancellationToken cancellationToken)
	{
		string? approved = configuration?["TILLER_EXECUTOR_SPREADSHEET_ID"];
		if (string.IsNullOrWhiteSpace(approved))
		{
			return null;
		}
		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerSelectedSpreadsheetEntity? row = await context.Set<TillerSelectedSpreadsheetEntity>()
			.AsNoTracking().SingleOrDefaultAsync(e => e.SpreadsheetId == approved, cancellationToken);
		return row?.ExecutorMetadataJson is string json
			? JsonSerializer.Deserialize<TillerExecutorMetadata>(json)
			: null;
	}

	public async Task SelectSpreadsheetAsync(string spreadsheetId, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(configuration?["TILLER_EXECUTOR_SPREADSHEET_ID"]))
		{
			throw new ArgumentException("Spreadsheet selection is managed by the existing Executor synchronization.", nameof(spreadsheetId));
		}
		if (string.IsNullOrWhiteSpace(spreadsheetId))
		{
			throw new ArgumentException("spreadsheetId is required.", nameof(spreadsheetId));
		}

		IReadOnlyList<TillerSpreadsheet> sheets = await tillerApiClient.ListSpreadsheetsAsync(cancellationToken);
		TillerSpreadsheet? match = sheets.FirstOrDefault(s =>
			string.Equals(s.SpreadsheetId, spreadsheetId, StringComparison.Ordinal));

		if (match is null)
		{
			throw new ArgumentException(
				$"Spreadsheet '{spreadsheetId}' is not in the readable spreadsheet list. No selection was stored.",
				nameof(spreadsheetId));
		}

		if (!match.Editable)
		{
			// Editable = Direct Fill (the provider backend fills the sheet).
			// Per the connected interface's documented tool contract, tools
			// taking a spreadsheet_id work only on readable (editable) sheets
			// and otherwise return an error with no reads or changes; the
			// independent live handshake verified only editable:true sheets,
			// so selection stays conservative here. No selection was stored.
			throw new ArgumentException(
				$"Spreadsheet '{spreadsheetId}' is not readable (Direct Fill is off). No selection was stored.",
				nameof(spreadsheetId));
		}

		await using ApplicationDbContext context = contextFactory.CreateDbContext();
		TillerSelectedSpreadsheetEntity? row = await context.Set<TillerSelectedSpreadsheetEntity>()
			.SingleOrDefaultAsync(cancellationToken);

		if (row is null)
		{
			row = new TillerSelectedSpreadsheetEntity
			{
				Id = Guid.NewGuid(),
				SpreadsheetId = spreadsheetId,
				UpdatedAt = DateTimeOffset.UtcNow,
			};
			context.Set<TillerSelectedSpreadsheetEntity>().Add(row);
		}
		else
		{
			if (!string.Equals(row.SpreadsheetId, spreadsheetId, StringComparison.Ordinal))
			{
				row.ExecutorMetadataJson = null;
			}
			row.SpreadsheetId = spreadsheetId;
			row.UpdatedAt = DateTimeOffset.UtcNow;
		}

		await context.SaveChangesAsync(cancellationToken);
	}
}
