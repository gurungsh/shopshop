using Ordering.Api.DTOs;
using Ordering.Api.Validators;

namespace Ordering.Api.UnitTests
{
    public class CreateOrderRequestValidatorTests
    {
        private readonly CreateOrderRequestValidator _validator = new();

        private static CreateOrderRequest CreateRequest(int itemCount)
            => new(
                "1 Main St",
                Enumerable.Range(0, itemCount).Select(_ => new CreateOrderItemRequest(Guid.NewGuid(), 1)).ToArray(),
                "pm_card_visa");

        #region Validate Tests

        [Fact]
        public void Validate_WithMaxItems_ShouldBeValid()
        {
            // Act
            var result = _validator.Validate(CreateRequest(100));

            // Assert
            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithTooManyItems_ShouldBeInvalid()
        {
            // Act
            var result = _validator.Validate(CreateRequest(101));

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOrderRequest.Items));
        }

        #endregion
    }
}
