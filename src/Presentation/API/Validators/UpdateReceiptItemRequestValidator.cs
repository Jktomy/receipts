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
	}
}
