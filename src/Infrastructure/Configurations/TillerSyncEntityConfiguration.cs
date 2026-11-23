using Infrastructure.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public class TillerTransactionEntityConfiguration : IEntityTypeConfiguration<TillerTransactionEntity>
{
	public void Configure(EntityTypeBuilder<TillerTransactionEntity> builder)
	{
		builder.ToTable("TillerTransactions", "tiller");

		builder.HasKey(e => e.Id);

		builder.HasIndex(e => new { e.SpreadsheetId, e.ProviderTransactionId })
			.IsUnique();

		builder.Property(e => e.ProviderTransactionId).HasMaxLength(200);
		builder.Property(e => e.SpreadsheetId).HasMaxLength(200);
		builder.Property(e => e.Description).HasMaxLength(500);
		builder.Property(e => e.Amount).HasPrecision(19, 4);
		builder.Property(e => e.Category).HasMaxLength(200);
		builder.Property(e => e.Account).HasMaxLength(200);
		builder.Property(e => e.Institution).HasMaxLength(200);
		builder.Property(e => e.Status).HasMaxLength(40);
		builder.Property(e => e.SplitGroupId).HasMaxLength(100);
		builder.Property(e => e.SupersededBy).HasMaxLength(200);
	}
}

public class TillerKnownIdEntityConfiguration : IEntityTypeConfiguration<TillerKnownIdEntity>
{
	public void Configure(EntityTypeBuilder<TillerKnownIdEntity> builder)
	{
		builder.ToTable("TillerKnownIds", "tiller");

		builder.HasKey(e => e.Id);

		builder.HasIndex(e => new { e.SpreadsheetId, e.ProviderTransactionId })
			.IsUnique();

		builder.Property(e => e.ProviderTransactionId).HasMaxLength(200);
		builder.Property(e => e.SpreadsheetId).HasMaxLength(200);
		builder.Property(e => e.CanonicalProviderTransactionId).HasMaxLength(200);
	}
}

public class TillerCheckpointEntityConfiguration : IEntityTypeConfiguration<TillerCheckpointEntity>
{
	public void Configure(EntityTypeBuilder<TillerCheckpointEntity> builder)
	{
		builder.ToTable("TillerCheckpoints", "tiller");

		builder.HasKey(e => e.Id);

		builder.HasIndex(e => e.SpreadsheetId).IsUnique();

		builder.Property(e => e.SpreadsheetId).HasMaxLength(200);
	}
}

public class TillerSyncEventEntityConfiguration : IEntityTypeConfiguration<TillerSyncEventEntity>
{
	public void Configure(EntityTypeBuilder<TillerSyncEventEntity> builder)
	{
		builder.ToTable("TillerSyncEvents", "tiller");

		builder.HasKey(e => e.Id);

		builder.Property(e => e.SpreadsheetId).HasMaxLength(200);
		builder.Property(e => e.EventType).HasMaxLength(40);
		builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
	}
}

public class TillerReceiptLinkEntityConfiguration : IEntityTypeConfiguration<TillerReceiptLinkEntity>
{
	public void Configure(EntityTypeBuilder<TillerReceiptLinkEntity> builder)
	{
		builder.ToTable("TillerReceiptLinks", "tiller");

		builder.HasKey(e => e.Id);

		builder.HasIndex(e => new { e.ProviderTransactionId, e.ReceiptId });

		builder.Property(e => e.ProviderTransactionId).HasMaxLength(200);
		builder.Property(e => e.LinkKind).HasMaxLength(40);
		builder.Property(e => e.Reviewer).HasMaxLength(200);
	}
}

public class TillerLinkEventEntityConfiguration : IEntityTypeConfiguration<TillerLinkEventEntity>
{
	public void Configure(EntityTypeBuilder<TillerLinkEventEntity> builder)
	{
		builder.ToTable("TillerLinkEvents", "tiller");

		builder.HasKey(e => e.Id);

		builder.HasIndex(e => e.ProviderTransactionId);

		builder.Property(e => e.ProviderTransactionId).HasMaxLength(200);
		builder.Property(e => e.Action).HasMaxLength(40);
		builder.Property(e => e.Reviewer).HasMaxLength(200);
	}
}
