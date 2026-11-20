using API.Generated.Dtos;
using API.Validators;

namespace Presentation.API.Tests.Validators;

public class CreateReceiptItemRequestValidatorTests
{
	private readonly CreateReceiptItemRequestValidator _validator = new();

	[Fact]
	public void Should_Pass_When_AllFieldsValid()
	{
		// Arrange
		CreateReceiptItemRequest request = new() { UnitPrice = 9.99, Description = "Test", Quantity = 1, Category = "Food" };

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);

		// Assert
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Should_Fail_When_UnitPriceIsZero()
	{
		// Arrange
		CreateReceiptItemRequest request = new() { UnitPrice = 0 };

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == CreateReceiptItemRequestValidator.UnitPriceMustBePositive);
	}

	[Fact]
	public void Should_Fail_When_UnitPriceIsNegative()
	{
		// Arrange
		CreateReceiptItemRequest request = new() { UnitPrice = -5.00 };

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == CreateReceiptItemRequestValidator.UnitPriceMustBePositive);
	}

	[Fact]
	public void Should_Fail_When_DescriptionIsEmpty()
	{
		// Arrange
		CreateReceiptItemRequest request = new() { UnitPrice = 9.99, Description = "", Quantity = 1, Category = "Food" };

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == CreateReceiptItemRequestValidator.DescriptionMustNotBeEmpty);
	}

	[Fact]
	public void Should_Fail_When_QuantityIsZero()
	{
		// Arrange
		CreateReceiptItemRequest request = new() { UnitPrice = 9.99, Description = "Test", Quantity = 0, Category = "Food" };

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == CreateReceiptItemRequestValidator.QuantityMustBePositive);
	}

	[Fact]
	public void Should_Fail_When_QuantityIsNegative()
	{
		// Arrange
		CreateReceiptItemRequest request = new() { UnitPrice = 9.99, Description = "Test", Quantity = -1, Category = "Food" };

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == CreateReceiptItemRequestValidator.QuantityMustBePositive);
	}

	[Fact]
	public void Should_Pass_When_CategoryIsMissing()
	{
		// Arrange: the retired legacy label is optional and ignored.
		CreateReceiptItemRequest request = new() { UnitPrice = 9.99, Description = "Test", Quantity = 1, Category = null!, Subcategory = null };

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);

		// Assert
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Should_Pass_When_CategoryIsEmpty()
	{
		CreateReceiptItemRequest request = new() { UnitPrice = 9.99, Description = "Uncertain item", Quantity = 1, Category = "" };
		Assert.True(_validator.Validate(request).IsValid);
	}

	[Fact]
	public void Should_Pass_When_PackageContentModelsACarton()
	{
		CreateReceiptItemRequest request = new() { UnitPrice = 3.00, Description = "Juice", Quantity = 1, Category = "Food", UnitOfMeasure = "each", PackageContentQuantity = 64, PackageContentUnit = "fl_oz" };
		Assert.True(_validator.Validate(request).IsValid);
	}

	[Fact]
	public void Should_Pass_When_PackageContentModelsACountMultipack()
	{
		// EGGS 12 CT $4.50: purchased quantity 1 carton, contents 12 count.
		CreateReceiptItemRequest request = new() { UnitPrice = 4.50, Description = "EGGS 12 CT", Quantity = 1, Category = "Food", UnitOfMeasure = "each", PackageContentQuantity = 12, PackageContentUnit = "count" };
		Assert.True(_validator.Validate(request).IsValid);
	}

	[Fact]
	public void Should_Fail_When_PackageContentUnitMissing()
	{
		CreateReceiptItemRequest request = new() { UnitPrice = 3.00, Description = "Juice", Quantity = 1, Category = "Food", UnitOfMeasure = "each", PackageContentQuantity = 64, PackageContentUnit = null };
		FluentValidation.Results.ValidationResult result = _validator.Validate(request);
		Assert.False(result.IsValid);
	}

	[Fact]
	public void Should_Fail_When_UnitIsMassWithPackageContent()
	{
		CreateReceiptItemRequest request = new() { UnitPrice = 3.00, Description = "Juice", Quantity = 1, Category = "Food", UnitOfMeasure = "fl_oz", PackageContentQuantity = 64, PackageContentUnit = "fl_oz" };
		Assert.False(_validator.Validate(request).IsValid);
	}
}
