using API.Generated.Dtos;
using Common;
using Domain;
using Domain.Core;
using Presentation.API.Contracts.Common;
using Presentation.API.Contracts.Receipts;
using Riok.Mapperly.Abstractions;

namespace Presentation.API.Mapping.Core;

[Mapper]
public static partial class ReceiptItemMapper
{
	[MapperIgnoreTarget(nameof(ReceiptItem.CurrentClassification))]
	[MapperIgnoreSource(nameof(CreateReceiptItemRequest.UnitOfMeasure))]
	[MapperIgnoreTarget(nameof(ReceiptItem.UnitOfMeasure))]
	private static partial ReceiptItem ToDomainPartial(CreateReceiptItemRequest request);

	public static ReceiptItem ToDomain(CreateReceiptItemRequest request)
	{
		ReceiptItem item = ToDomainPartial(request);
		if (!string.IsNullOrWhiteSpace(request.UnitOfMeasure))
		{
			item.SetUnitOfMeasure(request.UnitOfMeasure);
		}

		return item;
	}

	[MapperIgnoreTarget(nameof(ReceiptItem.CurrentClassification))]
	[MapperIgnoreSource(nameof(ReceiptItem.NormalizedDescriptionId))]
	[MapperIgnoreSource(nameof(ReceiptItem.NormalizedDescriptionName))]
	[MapperIgnoreSource(nameof(UpdateReceiptItemRequest.UnitOfMeasure))]
	[MapperIgnoreTarget(nameof(ReceiptItem.UnitOfMeasure))]
	private static partial ReceiptItem ToDomainPartial(UpdateReceiptItemRequest request, ReceiptItem existing);

	public static ReceiptItem ToDomain(UpdateReceiptItemRequest request, ReceiptItem existing)
	{
		ReceiptItem item = ToDomainPartial(request, existing);
		if (request.UnitOfMeasure is null)
		{
			return item;
		}

		if (string.IsNullOrWhiteSpace(request.UnitOfMeasure))
		{
			item.ClearUnitOfMeasure();
		}
		else
		{
			item.SetUnitOfMeasure(request.UnitOfMeasure);
		}

		return item;
	}

	[MapProperty(nameof(ReceiptItem.UnitPrice), nameof(ReceiptItemResponse.UnitPrice))]
	[MapProperty(nameof(ReceiptItem.TotalAmount), nameof(ReceiptItemResponse.TotalAmount))]
	[MapperIgnoreSource(nameof(ReceiptItem.CurrentClassification))]
	public static partial ReceiptItemResponse ToResponse(ReceiptItem item);

	private static MoneyResponse MapMoney(Money money) => new(money.Amount, money.Currency.ToString());
}
