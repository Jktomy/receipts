using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public class TillerSelectedSpreadsheetEntityConfiguration : IEntityTypeConfiguration<TillerSelectedSpreadsheetEntity>
{
	public void Configure(EntityTypeBuilder<TillerSelectedSpreadsheetEntity> builder)
	{
		builder.ToTable("TillerSelectedSpreadsheet", "tiller");

		builder.HasKey(e => e.Id);

		builder.Property(e => e.SpreadsheetId)
			.HasMaxLength(200);
	}
}

public class TillerAccountMappingEntityConfiguration : IEntityTypeConfiguration<TillerAccountMappingEntity>
{
	public void Configure(EntityTypeBuilder<TillerAccountMappingEntity> builder)
	{
		builder.ToTable("TillerAccountMappings", "tiller");

		builder.HasKey(e => e.Id);

		builder.Property(e => e.TillerAccountId)
			.HasMaxLength(200);

		builder.Property(e => e.TillerAccountName)
			.HasMaxLength(200);

		builder.Property(e => e.SpreadsheetId)
			.HasMaxLength(200);

		builder.HasOne(e => e.Account)
			.WithMany()
			.HasForeignKey(e => e.ReceiptsAccountId)
			.OnDelete(DeleteBehavior.Restrict);
	}
}
