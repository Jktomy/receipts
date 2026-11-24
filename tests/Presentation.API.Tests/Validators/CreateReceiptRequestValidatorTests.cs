using API.Generated.Dtos;
using API.Validators;

namespace Presentation.API.Tests.Validators;

public class CreateReceiptRequestValidatorTests
{
	private readonly CreateReceiptRequestValidator _validator = new(ValidatorTestClock.Policy);

	[Fact]
	public void Should_Pass_When_ValidReceipt()
	{
		// Arrange
		CreateReceiptRequest receipt = new()
		{
			Location = "Valid Location",
			Date = ValidatorTestClock.Today
		};

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(receipt);

		// Assert
		Assert.True(result.IsValid);
	}

	[Theory]
	[InlineData("photo-observed")]
	[InlineData("user-reported")]
	public void Should_Pass_When_SourceKindAdmitted(string sourceKind)
	{
		// Arrange
		CreateReceiptRequest receipt = new()
		{
			Location = "Valid Location",
			Date = ValidatorTestClock.Today,
			SourceKind = sourceKind
		};

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(receipt);

		// Assert
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Should_Pass_When_PaperlessVerifiedWithDocumentId()
	{
		// Arrange
		CreateReceiptRequest receipt = new()
		{
			Location = "Valid Location",
			Date = ValidatorTestClock.Today,
			SourceKind = "paperless-verified",
			SourcePaperlessDocumentId = 267
		};

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(receipt);

		// Assert
		Assert.True(result.IsValid);
	}

	[Fact]
	public void Should_Fail_When_SourceKindNotAdmitted()
	{
		// Arrange
		CreateReceiptRequest receipt = new()
		{
			Location = "Valid Location",
			Date = ValidatorTestClock.Today,
			SourceKind = "paperless-verified-by-me"
		};

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(receipt);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == Domain.Core.Receipt.SourceKindNotAdmitted);
	}

	[Fact]
	public void Should_Fail_When_PaperlessVerifiedWithoutDocumentId()
	{
		// Arrange: the native caller must not self-assert Paperless verification.
		CreateReceiptRequest receipt = new()
		{
			Location = "Valid Location",
			Date = ValidatorTestClock.Today,
			SourceKind = "paperless-verified"
		};

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(receipt);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == Domain.Core.Receipt.PaperlessVerifiedRequiresDocumentId);
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(200, true)]
	[InlineData(201, false)]
	public void GeneratedContract_EnforcesCanonicalLocationLength(int length, bool valid)
	{
		CreateReceiptRequest receipt = new()
		{
			Location = new string('a', length),
			Date = new DateOnly(2025, 1, 1),
		};
		List<System.ComponentModel.DataAnnotations.ValidationResult> errors = [];

		bool actual = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(receipt,
			new System.ComponentModel.DataAnnotations.ValidationContext(receipt), errors, validateAllProperties: true);

		Assert.Equal(valid, actual);
		if (!valid)
		{
			Assert.Contains(errors, error => error.MemberNames.Contains("Location"));
		}
	}

	[Fact]
	public void Should_Fail_When_DateIsInTheFuture()
	{
		// Arrange
		CreateReceiptRequest receipt = new()
		{
			Location = "Valid Location",
			Date = ValidatorTestClock.Today.AddDays(1)
		};

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(receipt);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == CreateReceiptRequestValidator.DateMustBePriorToCurrentDate);
	}

	[Fact]
	public void Should_Fail_When_LocationIsEmpty()
	{
		// Arrange
		CreateReceiptRequest receipt = new()
		{
			Location = "",
			Date = ValidatorTestClock.Today
		};

		// Act
		FluentValidation.Results.ValidationResult result = _validator.Validate(receipt);

		// Assert
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.ErrorMessage == CreateReceiptRequestValidator.LocationMustNotBeEmpty);
	}
}
