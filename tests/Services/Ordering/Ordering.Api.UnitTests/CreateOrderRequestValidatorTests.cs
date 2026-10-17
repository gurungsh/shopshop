using Ordering.Api.DTOs;
using Ordering.Api.Validators;

namespace Ordering.Api.UnitTests;

public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    private static CreateOrderRequest CreateRequest(int itemCount)
        => new(
            "1 Main St",
            Enumerable.Range(0, itemCount).Select(_ => new CreateOrderItemRequest(Guid.NewGuid(), 1)).ToArray(),
            "pm_card_visa");

    #region Validate Tests

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Validate_WithItemCount_ShouldAllowAtMostMaxItems(int itemCount, bool expectedValid)
    {
        // Act
        var result = _validator.Validate(CreateRequest(itemCount));

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
        Assert.Equal(!expectedValid, result.Errors.Any(e => e.PropertyName == nameof(CreateOrderRequest.Items)));
    }

    #endregion
}
