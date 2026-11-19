using API.Generated.Dtos;
using FluentValidation;

namespace API.Validators;

public class UpdateReceiptItemRequestValidator : AbstractValidator<UpdateReceiptItemRequest>
{
	public const string UnitPriceMustBePositive = "Unit price must be positive.";
	public const string IdMustNotBeEmpty = "ID must not be empty.";
	public const string DescriptionMustNotBeEmpty = "Description must not be empty.";
	public const string QuantityMustBePositive = "Quantity must be positive.";
	public const string UnitOfMeasureNotInCatalog = "Unit of measure is not a known unit.";
	public const string PackageContentQuantityMustBePositive = "Package content quantity must be positive.";
	public const string PackageContentUnitNotInCatalog = "Package content unit is not a known mass or volume.";
	public const string PackageUnitMustCountPackages = "Unit must count packages when package content is set.";

	public UpdateReceiptItemRequestValidator()
	{
		RuleFor(x => x.Id)
			.NotEqual(Guid.Empty)
			.WithMessage(IdMustNotBeEmpty);

		RuleFor(x => x.UnitPrice)
			.GreaterThan(0)
			.WithMessage(UnitPriceMustBePositive);

		RuleFor(x => x.Description)
			.NotEmpty()
			.WithMessage(DescriptionMustNotBeEmpty);

		RuleFor(x => x.Quantity)
			.GreaterThan(0)
			.WithMessage(QuantityMustBePositive);

		RuleFor(x => x.UnitOfMeasure)
			.Must(unit => unit is null || Domain.Core.UnitCatalog.IsKnown(unit))
			.WithMessage(UnitOfMeasureNotInCatalog);

		RuleFor(x => x.PackageContentQuantity)
			.Must((request, qty) => request.PackageContentQuantity is null && request.PackageContentUnit is null || qty > 0)
			.WithMessage(PackageContentQuantityMustBePositive);

		RuleFor(x => x.PackageContentUnit)
			.Must((request, unit) => request.PackageContentQuantity is null && request.PackageContentUnit is null || IsMassOrVolume(unit))
			.WithMessage(PackageContentUnitNotInCatalog);

		RuleFor(x => x.UnitOfMeasure)
			.Must((request, unit) => request.PackageContentQuantity is null && request.PackageContentUnit is null || unit is null || unit == "each" || unit == "count")
			.WithMessage(PackageUnitMustCountPackages);
	}

	private static bool IsMassOrVolume(string? unit)
	{
		if (unit is null || !Domain.Core.UnitCatalog.IsKnown(unit))
		{
			return false;
		}
		return Domain.Core.UnitCatalog.TryGetDimension(unit, out var dimension)
			&& (dimension == Domain.Core.UnitCatalog.Dimension.Mass || dimension == Domain.Core.UnitCatalog.Dimension.Volume);
	}
}
